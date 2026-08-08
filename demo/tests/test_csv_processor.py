from __future__ import annotations

import pytest

from csv_processor import CSVProcessorResult, InvalidRow, read_codes_from_csv


@pytest.fixture
def single_column_csv(tmp_path):
    p = tmp_path / "single.csv"
    p.write_text("code\nAAA\nBBB\nCCC\n", encoding="utf-8")
    return p


@pytest.fixture
def multi_column_csv(tmp_path):
    p = tmp_path / "multi.csv"
    p.write_text(
        "id,code,batch\n1,AAA,X\n2,BBB,Y\n3,CCC,Z\n",
        encoding="utf-8",
    )
    return p


@pytest.fixture
def csv_with_blanks(tmp_path):
    p = tmp_path / "blanks.csv"
    p.write_text("code\nAAA\n\nBBB\n   \nCCC\n", encoding="utf-8")
    return p


@pytest.fixture
def csv_no_header(tmp_path):
    p = tmp_path / "noheader.csv"
    p.write_text("AAA\nBBB\nCCC\n", encoding="utf-8")
    return p


@pytest.fixture
def csv_with_gs(tmp_path):
    p = tmp_path / "gs.csv"
    p.write_text("code\nABC\\x1dDEF\nGHI\\x1dJKL\n", encoding="utf-8")
    return p


class TestReadCodesFromCSV:
    def test_single_column(self, single_column_csv):
        result = read_codes_from_csv(single_column_csv)
        assert result.codes == ["AAA", "BBB", "CCC"]
        assert result.total_rows == 3
        assert result.skipped_rows == 0

    def test_multi_column_by_name(self, multi_column_csv):
        result = read_codes_from_csv(multi_column_csv, column="code")
        assert result.codes == ["AAA", "BBB", "CCC"]

    def test_multi_column_by_index(self, multi_column_csv):
        result = read_codes_from_csv(multi_column_csv, column=1)
        assert result.codes == ["AAA", "BBB", "CCC"]

    def test_multi_column_first_column(self, multi_column_csv):
        result = read_codes_from_csv(multi_column_csv, column="id")
        assert result.codes == ["1", "2", "3"]

    def test_blank_rows_skipped(self, csv_with_blanks):
        result = read_codes_from_csv(csv_with_blanks)
        assert result.codes == ["AAA", "BBB", "CCC"]
        assert result.skipped_rows == 2
        assert result.invalid_count == 0
        assert result.total_rows == 5

    def test_no_header(self, csv_no_header):
        result = read_codes_from_csv(csv_no_header, skip_header=False)
        assert result.codes == ["AAA", "BBB", "CCC"]

    def test_column_name_without_header_raises(self, csv_no_header):
        with pytest.raises(ValueError, match="Cannot use a column name"):
            read_codes_from_csv(csv_no_header, column="code", skip_header=False)

    def test_missing_column_name_raises(self, single_column_csv):
        with pytest.raises(ValueError, match="not found in header"):
            read_codes_from_csv(single_column_csv, column="missing")

    def test_file_not_found(self, tmp_path):
        with pytest.raises(FileNotFoundError):
            read_codes_from_csv(tmp_path / "nope.csv")

    def test_empty_file(self, tmp_path):
        p = tmp_path / "empty.csv"
        p.write_text("", encoding="utf-8")
        result = read_codes_from_csv(p)
        assert result.codes == []
        assert result.total_rows == 0

    def test_header_only(self, tmp_path):
        p = tmp_path / "headeronly.csv"
        p.write_text("code\n", encoding="utf-8")
        result = read_codes_from_csv(p)
        assert result.codes == []
        assert result.total_rows == 0

    def test_gs_escape_preserved_in_raw(self, csv_with_gs):
        result = read_codes_from_csv(csv_with_gs)
        assert result.codes == ["ABC\\x1dDEF", "GHI\\x1dJKL"]

    def test_semicolon_delimiter(self, tmp_path):
        p = tmp_path / "semi.csv"
        p.write_text("code;batch\nAAA;X\nBBB;Y\n", encoding="utf-8")
        result = read_codes_from_csv(p, column="code", delimiter=";")
        assert result.codes == ["AAA", "BBB"]


class TestCSVValidation:
    def test_duplicate_codes(self, tmp_path):
        p = tmp_path / "dup.csv"
        p.write_text("code\nAAA\nBBB\nAAA\nCCC\nBBB\n", encoding="utf-8")
        result = read_codes_from_csv(p)
        assert result.codes == ["AAA", "BBB", "CCC"]
        assert result.invalid_count == 2
        assert "duplicate" in result.invalid_rows[0].reason
        assert "duplicate" in result.invalid_rows[1].reason
        assert set(result.duplicate_codes) == {"AAA", "BBB"}

    def test_missing_code_column(self, tmp_path):
        p = tmp_path / "short.csv"
        p.write_text("id,code,batch\n1,AAA,X\n2\n3,CCC,Z\n", encoding="utf-8")
        result = read_codes_from_csv(p, column="code")
        assert result.codes == ["AAA", "CCC"]
        assert result.invalid_count == 1
        assert result.invalid_rows[0].reason == "missing code column"

    def test_empty_code_after_strip(self, tmp_path):
        p = tmp_path / "spaces.csv"
        p.write_text("id,code\n1,AAA\n2,   \n3,BBB\n", encoding="utf-8")
        result = read_codes_from_csv(p, column="code")
        assert result.codes == ["AAA", "BBB"]
        assert result.invalid_count == 1
        assert result.invalid_rows[0].reason == "empty code"

    def test_extra_columns_warning(self, tmp_path):
        p = tmp_path / "extra.csv"
        p.write_text("code,batch\nAAA,X\nBBB,Y,EXTRA\nCCC,Z\n", encoding="utf-8")
        result = read_codes_from_csv(p)
        assert result.codes == ["AAA", "BBB", "CCC"]
        assert len(result.warnings) == 1
        assert "expected 2 columns, got 3" in result.warnings[0]

    def test_fewer_columns_warning(self, tmp_path):
        p = tmp_path / "fewer.csv"
        p.write_text("id,code,batch\n1,AAA\n2,BBB,Y\n", encoding="utf-8")
        result = read_codes_from_csv(p, column="code")
        assert result.codes == ["AAA", "BBB"]
        assert len(result.warnings) == 1
        assert "expected 3 columns, got 2" in result.warnings[0]

    def test_column_index_out_of_range_fails_early(self, tmp_path):
        p = tmp_path / "small.csv"
        p.write_text("code\nAAA\n", encoding="utf-8")
        with pytest.raises(ValueError, match="out of range"):
            read_codes_from_csv(p, column=5)

    def test_malformed_escape_sequence(self, tmp_path):
        p = tmp_path / "bad_esc.csv"
        p.write_text("code\nABC\\x1dDEF\nGHI\\xZZJKL\n", encoding="utf-8")
        result = read_codes_from_csv(p)
        assert result.codes == ["ABC\\x1dDEF"]
        assert result.invalid_count == 1
        assert "malformed escape" in result.invalid_rows[0].reason

    def test_valid_x1d_escape_passes(self, tmp_path):
        p = tmp_path / "good_esc.csv"
        p.write_text("code\nABC\\x1dDEF\nGHI\\x1dJKL\n", encoding="utf-8")
        result = read_codes_from_csv(p)
        assert result.codes == ["ABC\\x1dDEF", "GHI\\x1dJKL"]
        assert result.invalid_count == 0

    def test_all_rows_blank(self, tmp_path):
        p = tmp_path / "allblank.csv"
        p.write_text("code\n   \n  \n\n", encoding="utf-8")
        result = read_codes_from_csv(p)
        assert result.codes == []
        assert result.total_rows == 3
        assert result.skipped_rows == 3
        assert result.invalid_count == 0

    def test_all_rows_invalid(self, tmp_path):
        p = tmp_path / "allinvalid.csv"
        p.write_text("id,code\n1,   \n2,  \n3,\n", encoding="utf-8")
        result = read_codes_from_csv(p, column="code")
        assert result.codes == []
        assert result.total_rows == 3
        assert result.invalid_count == 3

    def test_mixed_valid_skipped_invalid(self, tmp_path):
        p = tmp_path / "mixed.csv"
        p.write_text(
            "id,code\n1,AAA\n\n2,   \n3,BBB\n4,AAA\n5,CCC\n",
            encoding="utf-8",
        )
        result = read_codes_from_csv(p, column="code")
        assert result.codes == ["AAA", "BBB", "CCC"]
        assert result.skipped_rows == 1
        assert result.invalid_count == 2
        reasons = [r.reason for r in result.invalid_rows]
        assert "empty code" in reasons
        assert any("duplicate" in r for r in reasons)

    def test_valid_count_property(self):
        r = CSVProcessorResult(codes=["A", "B", "C"])
        assert r.valid_count == 3

    def test_invalid_count_property(self):
        r = CSVProcessorResult(
            invalid_rows=[InvalidRow(1, "x", "bad"), InvalidRow(2, "y", "bad")]
        )
        assert r.invalid_count == 2

    def test_invalid_row_fields(self):
        ir = InvalidRow(row_num=5, raw="a,b,c", reason="test reason")
        assert ir.row_num == 5
        assert ir.raw == "a,b,c"
        assert ir.reason == "test reason"
