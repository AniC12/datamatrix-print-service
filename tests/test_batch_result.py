from __future__ import annotations

from csv_processor import InvalidRow
from main import BatchResult


class TestBatchResultReport:
    def test_all_success(self):
        r = BatchResult(
            csv_file="data.csv",
            total_rows=5,
            skipped_rows=0,
            codes_extracted=5,
            sent_ok=5,
        )
        report = r.format_report()
        assert "BATCH RESULT REPORT" in report
        assert "data.csv" in report
        assert "Total rows:        5" in report
        assert "Skipped rows:      0" in report
        assert "Codes extracted:   5" in report
        assert "Sent OK:           5" in report
        assert "Failed:            0" in report
        assert "INVALID CSV ROWS" not in report
        assert "PRINT FAILURES" not in report

    def test_with_failures(self):
        r = BatchResult(
            csv_file="codes.csv",
            total_rows=10,
            skipped_rows=2,
            codes_extracted=8,
            sent_ok=6,
            failed=[
                (3, "ABC123", "Connection refused"),
                (7, "DEF456", "Timeout"),
            ],
        )
        report = r.format_report()
        assert "Sent OK:           6" in report
        assert "Failed:            2" in report
        assert "PRINT FAILURES:" in report
        assert "Label 3: Connection refused" in report
        assert "code: ABC123" in report
        assert "Label 7: Timeout" in report
        assert "code: DEF456" in report

    def test_dry_run(self):
        r = BatchResult(
            csv_file="test.csv",
            total_rows=3,
            skipped_rows=0,
            codes_extracted=3,
            dry_run_generated=3,
        )
        report = r.format_report()
        assert "Dry-run generated: 3" in report
        assert "Sent OK" not in report
        assert "Failed:" not in report

    def test_with_skipped_rows(self):
        r = BatchResult(
            csv_file="partial.csv",
            total_rows=10,
            skipped_rows=4,
            codes_extracted=6,
            sent_ok=6,
        )
        report = r.format_report()
        assert "Total rows:        10" in report
        assert "Skipped rows:      4" in report
        assert "Codes extracted:   6" in report

    def test_empty_batch(self):
        r = BatchResult(
            csv_file="empty.csv",
            total_rows=0,
            skipped_rows=0,
            codes_extracted=0,
        )
        report = r.format_report()
        assert "Codes extracted:   0" in report
        assert "Sent OK:           0" in report

    def test_failed_count_property(self):
        r = BatchResult()
        assert r.failed_count == 0
        r.failed.append((1, "X", "err"))
        assert r.failed_count == 1

    def test_long_code_truncated_in_report(self):
        long_code = "A" * 100
        r = BatchResult(
            csv_file="test.csv",
            total_rows=1,
            codes_extracted=1,
            failed=[(1, long_code, "error")],
        )
        report = r.format_report()
        assert "A" * 50 in report
        assert "A" * 100 not in report

    def test_invalid_rows_in_report(self):
        r = BatchResult(
            csv_file="bad.csv",
            total_rows=5,
            skipped_rows=0,
            invalid_rows=[
                InvalidRow(3, "x,,", "empty code"),
                InvalidRow(5, "DUP", "duplicate code (first seen at row 2)"),
            ],
            codes_extracted=3,
            sent_ok=3,
        )
        report = r.format_report()
        assert "Invalid rows:      2" in report
        assert "INVALID CSV ROWS:" in report
        assert "Row 3: empty code" in report
        assert "Row 5: duplicate code" in report

    def test_csv_warnings_in_report(self):
        r = BatchResult(
            csv_file="warn.csv",
            total_rows=3,
            codes_extracted=3,
            sent_ok=3,
            csv_warnings=["Row 2: expected 3 columns, got 4"],
        )
        report = r.format_report()
        assert "CSV WARNINGS:" in report
        assert "expected 3 columns, got 4" in report

    def test_invalid_count_property(self):
        r = BatchResult()
        assert r.invalid_count == 0
        r.invalid_rows.append(InvalidRow(1, "x", "bad"))
        assert r.invalid_count == 1

    def test_full_report_all_sections(self):
        r = BatchResult(
            csv_file="full.csv",
            total_rows=10,
            skipped_rows=1,
            invalid_rows=[InvalidRow(4, "dup", "duplicate code (first seen at row 2)")],
            codes_extracted=8,
            sent_ok=7,
            failed=[(3, "CODE3", "Timeout")],
            csv_warnings=["Row 6: expected 2 columns, got 3"],
        )
        report = r.format_report()
        assert "Invalid rows:      1" in report
        assert "INVALID CSV ROWS:" in report
        assert "CSV WARNINGS:" in report
        assert "PRINT FAILURES:" in report
