# Phase 2 — Scope

This document defines the Phase 2 scope for Code Print Manager. Phase 2 focuses exclusively on **new features and capabilities**. All safety-critical bugs and production-readiness gaps were addressed in Phase 1.

---

## New Features

### 1. Printer Interface Locking (`SPGSLI`)

Lock the Savema touchscreen during active print jobs so operators cannot accidentally start, stop, or modify the print queue from the printer itself.

- Send `SPGSLI` to lock the interface when a job transitions to `Printing`.
- Send the unlock command when the job completes, pauses, cancels, or errors.
- Handle lock/unlock failures gracefully (log + alert, do not block the job).
- Configurable per printer (some production lines may need touchscreen access).

### 2. Error-Job Recovery Workflows

Allow operators to recover jobs that ended in `Error` status without re-importing codes or creating a new job from scratch.

- "Retry" action on Error jobs: inspect the printer state, reconcile counters, and resume from the last confirmed position.
- "Force Complete" action: mark remaining reserved codes as quarantined and transition the job to `Completed` (for cases where the physical output was verified manually).
- Full audit trail for recovery actions.

### 3. Single-Instance Application Guard

Prevent multiple instances of the desktop application from running simultaneously on the same machine.

- Use a named mutex to detect an existing instance.
- If a second instance is launched, bring the existing window to the foreground and exit.
- Prevents concurrent database writes from two processes (which could corrupt the SQLite database despite WAL mode).

### 4. Template Validation

Validate that the printer's active template semantically matches the product configuration before starting a print job.

- After `SPLLTF`, query the template's variable fields and compare against expected CSV column structure.
- Alert the operator if the template expects different data than what the CSV provides.
- Requires investigation of Savema's template introspection commands (may need firmware-specific handling).

### 5. Scanner / Verification Module

Post-print verification by scanning printed codes and comparing against the database.

- Barcode scanner integration (USB HID or serial).
- Scan codes and verify they exist in the database with `Printed` status.
- Flag codes that scan as `Available` or `Reserved` (indicates a reuse or data issue).
- Generate verification reports (pass rate, mismatches, missing codes).
- UI screen for scan-and-verify workflow.

### 6. Reporting

Generate production reports for management and regulatory compliance.

- Daily/weekly/monthly print summaries per product and printer.
- Code utilization reports (printed vs. wasted vs. quarantined).
- Export to CSV/Excel.
- Print job history with filtering and search.
- Operator activity logs derived from audit records.

### 7. Extended Protocol Framing Tests

Expand test coverage for SPPL protocol edge cases that represent future capability rather than Phase 1 bugs.

- Multi-frame TCP responses (response split across multiple `Read` calls).
- Interleaved responses from concurrent commands.
- Response timeout and partial-read recovery.
- Large CSV upload chunking and verification.
- Adapter behavior under sustained high-frequency polling.

---

## Deferred Hardening (Non-Critical)

These items were identified during Phase 1 analysis but are not safety-critical bugs. They improve robustness or developer experience without affecting the core code-safety invariant.

### LOW-2: SQLite Status CHECK Constraints

Add `CHECK` constraints on the `Status` column in `codes` and `print_jobs` tables to prevent invalid enum values at the database level.

- Deferred because the application is the only database writer.
- EF Core's enum handling already prevents invalid values in normal operation.
- Adding `CHECK` constraints introduces migration complexity and can break if new statuses are added.

### LOW-4: Import Performance Optimization

Optimize bulk code import for large batches (>100,000 codes).

- Current implementation loads all codes into memory and inserts via EF Core change tracking.
- Could use `SqliteBulkInsert` or raw `INSERT` statements for large imports.
- Not a safety issue — imports work correctly, just slowly for very large batches.

### LOW-5: Printer Name Uniqueness

Add a unique constraint on printer `Name` to prevent operator confusion.

- Currently only `(IpAddress, Port)` is unique (added in Phase 1).
- Two printers can have the same display name but different endpoints.
- Low risk — confusing but not a data integrity issue.
