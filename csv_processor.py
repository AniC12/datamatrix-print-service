from __future__ import annotations

import csv
import logging
from dataclasses import dataclass, field
from pathlib import Path

logger = logging.getLogger(__name__)


@dataclass
class InvalidRow:
    row_num: int
    raw: str
    reason: str


@dataclass
class CSVProcessorResult:
    codes: list[str] = field(default_factory=list)
    skipped_rows: int = 0
    total_rows: int = 0
    invalid_rows: list[InvalidRow] = field(default_factory=list)
    duplicate_codes: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    @property
    def valid_count(self) -> int:
        return len(self.codes)

    @property
    def invalid_count(self) -> int:
        return len(self.invalid_rows)


def read_codes_from_csv(
    file_path: str | Path,
    column: int | str | None = None,
    delimiter: str = ",",
    skip_header: bool = True,
) -> CSVProcessorResult:
    """Read serialization codes from an eMark CSV file.

    Args:
        file_path: Path to the CSV file.
        column: Column containing the code. Can be:
            - None: assumes single-column file (or first column).
            - int: zero-based column index.
            - str: column header name (requires skip_header=True).
        delimiter: CSV field delimiter.
        skip_header: Whether the first row is a header row.

    Returns:
        CSVProcessorResult with extracted codes and statistics.

    Row classification:
        - valid: code extracted and added to codes list.
        - skipped: empty or blank row (harmless).
        - invalid: malformed, missing column, duplicate, etc. (recorded with reason).

    Fail-early on bad file structure; skip-and-record on bad individual rows.
    """
    path = Path(file_path)
    if not path.exists():
        logger.error("CSV file not found: %s", path)
        raise FileNotFoundError(f"CSV file not found: {path}")

    logger.info("Reading CSV file: %s", path)
    result = CSVProcessorResult()
    expected_cols: int | None = None
    seen_codes: dict[str, int] = {}

    with path.open(newline="", encoding="utf-8") as fh:
        reader = csv.reader(fh, delimiter=delimiter)

        col_index: int = 0

        if skip_header:
            header = next(reader, None)
            if header is None:
                logger.warning("CSV file is empty: %s", path)
                return result
            logger.debug("CSV header: %s", header)
            expected_cols = len(header)
            if isinstance(column, str):
                try:
                    col_index = header.index(column)
                except ValueError:
                    raise ValueError(
                        f"Column '{column}' not found in header: {header}"
                    )
            elif isinstance(column, int):
                if column >= len(header):
                    raise ValueError(
                        f"Column index {column} out of range "
                        f"(header has {len(header)} columns: {header})"
                    )
                col_index = column
        elif isinstance(column, int):
            col_index = column
        elif isinstance(column, str):
            raise ValueError("Cannot use a column name when skip_header=False")

        for row in reader:
            result.total_rows += 1
            row_num = result.total_rows + (1 if skip_header else 0)
            raw_repr = delimiter.join(row)

            # --- empty / blank row ---
            if not row or all(cell.strip() == "" for cell in row):
                result.skipped_rows += 1
                logger.debug("Row %d: skipped (blank row)", row_num)
                continue

            # --- missing code column ---
            if col_index >= len(row):
                result.invalid_rows.append(
                    InvalidRow(row_num, raw_repr, "missing code column")
                )
                logger.warning("Row %d: missing code column (need index %d, got %d columns)",
                               row_num, col_index, len(row))
                continue

            # --- extra / fewer columns than header ---
            if expected_cols is not None and len(row) != expected_cols:
                result.warnings.append(
                    f"Row {row_num}: expected {expected_cols} columns, got {len(row)}"
                )
                logger.debug("Row %d: column count mismatch (%d vs %d expected)",
                             row_num, len(row), expected_cols)

            raw_code = row[col_index].strip()

            # --- empty code after stripping ---
            if not raw_code:
                result.invalid_rows.append(
                    InvalidRow(row_num, raw_repr, "empty code")
                )
                logger.debug("Row %d: invalid (empty code after stripping)", row_num)
                continue

            # --- malformed literal \x1d (broken escape) ---
            if "\\x" in raw_code:
                bad_escapes = [i for i in range(len(raw_code) - 3)
                               if raw_code[i:i+2] == "\\x"
                               and raw_code[i+2:i+4].lower() not in ("1d",)]
                if bad_escapes:
                    result.invalid_rows.append(
                        InvalidRow(row_num, raw_repr,
                                   f"malformed escape sequence in code: {raw_code[:50]}")
                    )
                    logger.warning("Row %d: malformed escape sequence: %s", row_num, raw_code[:50])
                    continue

            # --- duplicate code ---
            if raw_code in seen_codes:
                first_row = seen_codes[raw_code]
                result.invalid_rows.append(
                    InvalidRow(row_num, raw_repr,
                               f"duplicate code (first seen at row {first_row})")
                )
                if raw_code not in result.duplicate_codes:
                    result.duplicate_codes.append(raw_code)
                logger.warning("Row %d: duplicate code '%s' (first at row %d)",
                               row_num, raw_code[:40], first_row)
                continue

            # --- valid ---
            seen_codes[raw_code] = row_num
            result.codes.append(raw_code)

    logger.info(
        "CSV processing complete: %d rows, %d valid, %d skipped, %d invalid",
        result.total_rows, result.valid_count, result.skipped_rows, result.invalid_count,
    )
    if result.duplicate_codes:
        logger.warning("%d duplicate code(s) found", len(result.duplicate_codes))
    return result
