"""Render ZPL files to PNG images using the Labelary API.

Usage:
    python render_label.py received_labels/label_0001.zpl
    python render_label.py received_labels/*.zpl
    python render_label.py received_labels/label_0001.zpl --dpmm 12 --width 4 --height 3
    python render_label.py received_labels/label_0001.zpl --config config.ini

Rendered PNGs are saved next to the ZPL files (e.g. label_0001.png).
Use --output-dir to save them elsewhere.
With --config, dpmm/width/height defaults are read from the [label] section.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.error import HTTPError, URLError


LABELARY_URL = "http://api.labelary.com/v1/printers/{dpmm}dpmm/labels/{width}x{height}/0/"


def render_zpl(
    zpl: str,
    dpmm: int = 8,
    width: float = 4,
    height: float = 6,
) -> bytes:
    """Send ZPL to the Labelary API and return the PNG bytes."""
    url = LABELARY_URL.format(dpmm=dpmm, width=width, height=height)
    req = Request(
        url,
        data=zpl.encode("utf-8"),
        headers={"Accept": "image/png"},
        method="POST",
    )
    try:
        with urlopen(req, timeout=15) as resp:
            return resp.read()
    except HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"Labelary API error {exc.code}: {body}") from exc
    except URLError as exc:
        raise RuntimeError(f"Failed to reach Labelary API: {exc.reason}") from exc


def render_file(
    zpl_path: Path,
    output_dir: Path | None = None,
    dpmm: int = 8,
    width: float = 4,
    height: float = 6,
) -> Path:
    """Render a single ZPL file and save the PNG."""
    zpl = zpl_path.read_text(encoding="ascii")
    png_data = render_zpl(zpl, dpmm=dpmm, width=width, height=height)

    dest_dir = output_dir or zpl_path.parent
    dest_dir.mkdir(parents=True, exist_ok=True)
    png_path = dest_dir / (zpl_path.stem + ".png")
    png_path.write_bytes(png_data)
    return png_path


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="render_label",
        description="Render ZPL files to PNG via the Labelary API",
    )
    parser.add_argument("files", nargs="+", help="ZPL file(s) to render")
    parser.add_argument("--dpmm", type=int, default=None, help="Dots per mm (default: 8 or from config)")
    parser.add_argument("--width", type=float, default=None, help="Label width in inches (default: 4 or from config)")
    parser.add_argument("--height", type=float, default=None, help="Label height in inches (default: 6 or from config)")
    parser.add_argument("--output-dir", default=None, help="Directory for rendered PNGs (default: same as ZPL)")
    parser.add_argument("--config", default=None, help="Path to config.ini to read label defaults")
    return parser


def main() -> None:
    args = build_parser().parse_args()
    output_dir = Path(args.output_dir) if args.output_dir else None

    # Resolve defaults: CLI > config > hardcoded
    dpmm = 8
    width = 4.0
    height = 6.0
    if args.config:
        from config import load_settings
        cfg = load_settings(args.config)
        dpmm = cfg.dpmm
        width = cfg.label_width
        height = cfg.label_height
    if args.dpmm is not None:
        dpmm = args.dpmm
    if args.width is not None:
        width = args.width
    if args.height is not None:
        height = args.height

    for file_arg in args.files:
        zpl_path = Path(file_arg)
        if not zpl_path.exists():
            print(f"SKIP: {zpl_path} not found")
            continue

        try:
            png_path = render_file(
                zpl_path,
                output_dir=output_dir,
                dpmm=dpmm,
                width=width,
                height=height,
            )
            print(f"OK: {zpl_path.name} -> {png_path}")
        except RuntimeError as exc:
            print(f"FAIL: {zpl_path.name} -> {exc}")


if __name__ == "__main__":
    main()
