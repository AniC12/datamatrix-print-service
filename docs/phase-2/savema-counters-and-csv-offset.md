# Savema Counters, CSV Offset & Per-Product Lifetime Counter

> **Purpose:** Reference document covering two related questions about the Savema TTO printer:
> 1. Can we start printing from a CSV offset (not row 0)?
> 2. Can we track a per-product lifetime print counter that persists across template switches?
>
> Based on exhaustive analysis of the SPPL Rev.12 specification (`docs/savema_language_-_rev12.md`), SVM 20i/20c service manuals (`docs/manuals/`), and the existing codebase.

---

## Table of Contents

1. [Background: The Savema Counter Landscape](#1-background-the-savema-counter-landscape)
2. [CSV Offset: Can We Start Printing from Row N?](#2-csv-offset-can-we-start-printing-from-row-n)
3. [Per-Product Lifetime Counter: Does It Exist Natively?](#3-per-product-lifetime-counter-does-it-exist-natively)
4. [Application-Managed Counter: How to Implement It](#4-application-managed-counter-how-to-implement-it)
5. [SPPL Commands Reference](#5-sppl-commands-reference)
6. [Implementation Checklist](#6-implementation-checklist)

---

## 1. Background: The Savema Counter Landscape

The Savema printer exposes several counters. Understanding each one is essential before designing any counter-based feature.

### 1.1 SPGGTP — Total Print Count (Printer Lifetime)

| Property | Value |
|----------|-------|
| SPPL command | `~SPGGTP^` |
| Scope | Entire printer — all templates, all products |
| Reset behavior | **Never resets.** Survives power cycles, template switches, everything. |
| Storage | Non-volatile (flash/disk) |

> SPPL Rev.12 §6.3: "Returns total print count of printer. Printer printed 458200 prints since it started working first."
>
> SVM 20i manual: "TOTAL PRINTING COUNT: Shows the number of prints from the first print. The value is never reset, neither the machine is reset nor the template is changed."

**Implication:** SPGGTP counts every print from every template. If you print 100 with template P, switch to template Q and print 50, SPGGTP shows 150. It cannot distinguish which template produced which prints. **Not usable for per-product tracking.**

### 1.2 SPGGCP — Current Print Count

| Property | Value |
|----------|-------|
| SPPL command | `~SPGGCP^` |
| Scope | Printer-wide, but scoped to current template session |
| Reset behavior | Per SPPL docs: "This counter resets when load any template." Per real hardware testing (serial 26050155): confirmed reset on SPLLTF. Other firmware may be cumulative. |
| Storage | Volatile (RAM) — lost on power cycle |

> SPPL Rev.12 §6.4: "Returns current print count of printer. This counter resets when load any template. This count is shown in main window."

**Implication:** Even if it didn't reset on template load, it's volatile and printer-wide. **Not usable for per-product tracking.**

### 1.3 PRINTING COUNT (Touchscreen Display)

| Property | Value |
|----------|-------|
| Access | Touchscreen only — no SPPL command to read it |
| Scope | Printer-wide |
| Reset behavior | Resets on machine restart. Does NOT reset on template change. |

> SVM 20i manual: "Shows the number of prints that printed since the machine was started. The value is reset in the area when the machine is restarted. Even if the template is changed, this value not reset."

**Implication:** Not accessible via SPPL. **Not usable programmatically.**

### 1.4 Template Counter Object (Visual Counter Printed on Label)

This is a field embedded in the template XML that **prints a visible, auto-incrementing number on each label.** It appears alongside other objects (text, QR codes, logos, etc.).

| Property | Value |
|----------|-------|
| XML element | `<ObjectType>Counter</ObjectType>` |
| Key properties | `Data` (current value), `NumericBegin`, `NumericEnd`, `NumericStep`, `NumericPeriod`, `NumericDigit`, `Restart` |
| Read via | `~SPLGFV{counterName}^` |
| Write via | `~SPMCCV{counterName~gt~value}^` |
| Source | `Internal` (auto-increments) |

Example template XML (SPPL Rev.12 §3.1.3.5):
```xml
<Object>
    <ObjectType>Counter</ObjectType>
    <NameID>Counter05</NameID>
    <Name>counter1</Name>
    <!-- position, size, rotation... -->
    <Content>
        <Data>0000</Data>                        <!-- starting value -->
        <Source>Internal</Source>
        <CounterType>Numeric</CounterType>
        <IncreasingDecreasing>Increasing</IncreasingDecreasing>
        <NumericBegin>0</NumericBegin>
        <NumericEnd>9999</NumericEnd>
        <NumericStep>1</NumericStep>
        <NumericPeriod>1</NumericPeriod>
        <NumericDigit>4</NumericDigit>
        <Restart>True</Restart>                  <!-- wrap around at end -->
    </Content>
    <Font>
        <Name>Tahoma</Name>
        <Size>36</Size>
        <Style>Regular</Style>
    </Font>
</Object>
```

**This is the counter that gets printed on the product label.** The critical question is whether its value persists across template switches.

### 1.5 Summary Table

| Counter | Scope | Persists across template switch? | Persists across power cycle? | Readable via SPPL? | Per-product? |
|---------|-------|----------------------------------|-----------------------------|--------------------|-------------|
| SPGGTP | Printer-wide | Yes | Yes | Yes (`~SPGGTP^`) | No |
| SPGGCP | Printer-wide | No (resets on load) | No | Yes (`~SPGGCP^`) | No |
| Touchscreen Printing Count | Printer-wide | Yes | No | No | No |
| Template Counter Object | Per-template | **No** | No | Yes (`~SPLGFV^` / `~SPMCCV^`) | No (natively) |

---

## 2. CSV Offset: Can We Start Printing from Row N?

### The Question

When a template with a Database-sourced field (QR code, barcode, text) is activated, the printer loads the attached CSV into a data buffer and starts printing from row 0. **Is there an SPPL command to start from row N instead?**

### The Answer: No

The SPPL protocol has **no command to set the CSV data buffer index.** The CSV row pointer is an internal, read-only, uncontrollable runtime state.

> SPPL Rev.12 §3.11: "When load template which have CSV database field, CSV datas and index of data (for start print) are loaded to data buffer."

The index is loaded together with the data at template-load time. There is no separate command to change it.

The connection recovery doc confirms the CSV row pointer is volatile and not controllable:
> "CSV row pointer: N/A (no read command). The internal pointer tracking which CSV row to print next. Behavior after power cycle is undocumented — assume lost."

### How the Application Handles This Today

The application already uses the correct workaround: **upload only the codes you need.** Instead of uploading a full CSV and trying to skip N rows, it uploads a CSV containing only the relevant codes.

- **Initial prepare:** Reserves codes from the pool and uploads only those reserved codes via `SPLCDF`.
- **Resume after pause/reconnect:** Deletes the old CSV and re-uploads a new CSV containing only the remaining unprinted codes.

This is the intended pattern. The CSV is the application's mechanism for controlling which codes the printer uses.

### Alternative: Queue System (SPLAQD)

The queue system (`SPLAQD`, `SPLCQD`) provides FIFO-based data feeding. You clear the queue, then append only the data you want. Each print consumes the front item. When the queue is empty, the printer stops.

This requires "Queue system must be enabled in Authorization settings" on the printer.

The current application does not use the queue system. The CSV upload approach is simpler and already implemented.

---

## 3. Per-Product Lifetime Counter: Does It Exist Natively?

### The Question

Can the Savema printer track a lifetime print count per product (i.e., per template) that persists across template switches, so that a visible counter on the label shows the total number of units of that product ever printed?

Example scenario:
1. Load template P, print 100 items (counter shows 1-100)
2. Switch to template Q, print 50 items
3. Switch back to template P, counter should resume at 101

### The Answer: No

The Savema printer **does not natively support per-product/per-template lifetime counters.** Here is the complete evidence:

#### Evidence 1: Template files are static

`SPLLTF` loads a stored `.rox` file. The `Data` field in the Counter object's XML sets the starting value. There is **no SPPL command to save the current in-memory template state (with its updated counter) back to storage.**

The complete set of template-related commands:

| Command | Direction | Purpose |
|---------|-----------|---------|
| `SPLTDS` | PC → Printer (storage + load) | Push template XML, store and load |
| `SPLKTD` | PC → Printer (storage only) | Push template XML, store but don't load |
| `SPLLTF` | Printer storage → Printer memory | Load stored .rox file into memory |
| `SPLRTF` | Printer storage → PC | Read stored .rox file back to PC |

None of these save the current in-memory state (with an updated counter) back to the `.rox` file. There is no "save active template" command.

#### Evidence 2: SPMCCV modifies only the in-memory template

> SPPL Rev.12 §4.4: "SPMCCV: This command changes selected Counter object value in template."

"In template" means the active, in-memory template. When you switch templates via `SPLLTF`, the `.rox` file is reloaded from storage, and the counter starts from whatever `Data` value is baked into that file.

#### Evidence 3: The persistence model confirms it

The connection recovery deep-dive (`docs/phase-1/connection-recovery-deep-dive.md`) categorizes all printer state:

**Non-volatile (survives power cycle):**
- SPGGTP, stored .rox files, stored .csv files, serial number

**Volatile (lost on power cycle):**
- SPGGCP, active data buffer, CSV row pointer, active template selection, limited print quantity, print/stop state, interface lock

The template counter object's current value is notably absent from the non-volatile list. It is part of the in-memory template state and is volatile.

#### Evidence 4: No auto-save mechanism

The Counter object XML has no `SaveOnChange`, `Persistent`, or `AutoSave` property. The only properties are: `Data`, `CounterType`, `IncreasingDecreasing`, `NumericBegin/End/Step/Period/Digit`, `AlphaBegin/End/Step/Period/Digit/Char`, `Restart`.

### Conclusion

**The printer has no concept of "per-product lifetime counter."** SPGGTP is printer-wide. SPGGCP is volatile and printer-wide. The template counter object resets to its initial `Data` value every time the template is loaded from storage.

---

## 4. Application-Managed Counter: How to Implement It

Although the printer can't do it natively, the application **can implement per-product lifetime counters** using two existing SPPL commands. This is not a workaround — it is the correct architectural approach for this class of printer.

### The Two Required SPPL Commands

#### SPMCCV — Set Counter Value

Sets the counter object's current value in the active (in-memory) template. Must be sent while the printer is in stop position.

```
~SPMCCV{<counter_name>~gt~<value>}^
```

| Parameter | Description |
|-----------|-------------|
| `counter_name` | Name of the counter object as defined in the template (e.g., `counter1`) |
| `value` | New numeric value. Must match the counter's digit format (e.g., `005001` for a 6-digit counter) |

Response:
```
~ SPGRES{SPMCCV:OK}^                          -- success
~ SPGRES{SPMCCV:FAIL}^                        -- failure
~ SPGRES{SPMCCV:< counter1 > not found}^      -- field doesn't exist
```

Example:
```
~SPMCCV{counter1~gt~005001}^    -- Set counter1 to 5001
```

> SPPL Rev.12 §4.4. Note: the Modification Commands section states that these commands must be sent "either machine is stop position or alternatively machine is print position and package is stop position."

#### SPLGFV — Get Field Value

Reads the current value of any field in the active template. Works for counter objects, text fields, barcodes, etc.

```
~SPLGFV{<field_name>}^
```

Response:
```
~ SPGRES{SPLGFV:<field_name><<field_value>}^           -- success
~ SPGRES{SPLGFV:<<field_name>> not found}^             -- field doesn't exist
```

Example:
```
~SPLGFV{counter1}^
~ SPGRES{SPLGFV:counter1<005237}^    -- counter1 is currently at 5237
```

> SPPL Rev.12 §3.16. Note: the `<` character separates field name from field value in the response.

### The Workflow

```
                              APPLICATION DB
                          ┌─────────────────────┐
                          │ Product "Milk 1L"    │
                          │   lifetime_count=5000│
                          │   template=P.rox     │
                          │   counter_field=ctr1  │
                          └─────────┬───────────┘
                                    │
    ┌───────────────────────────────┼───────────────────────────────┐
    │  START JOB for "Milk 1L"     │                               │
    │                              ▼                               │
    │  1. Upload CSV:    ~SPLCDF{milk.csv~gt~code1\ncode2\n...}^   │
    │  2. Load template: ~SPLLTF{P.rox}^                           │
    │     (counter resets to Data value from .rox, e.g., 000000)   │
    │  3. Set counter:   ~SPMCCV{ctr1~gt~005001}^    ◄── DB value + 1
    │  4. Set quantity:  ~SPPSLQ{200}^                             │
    │  5. Start print:   ~SPPSAP^                                  │
    │                                                              │
    │  ... printer prints labels with counter 5001, 5002, ..5200   │
    │  ... alongside the QR code from the CSV                      │
    │                                                              │
    │  6. On stop/pause/complete:                                  │
    │     Read counter: ~SPLGFV{ctr1}^  → 5200                    │
    │     Save to DB:   lifetime_count = 5200                      │
    └──────────────────────────────────────────────────────────────┘

    ┌───────────────────────────────────────────────────────────────┐
    │  SWITCH TO TEMPLATE Q for "Juice 2L"                         │
    │  ... print 300 items with template Q ...                     │
    │  (SPGGTP advances by 300, but that doesn't affect our DB)    │
    └──────────────────────────────────────────────────────────────┘

    ┌───────────────────────────────────────────────────────────────┐
    │  COME BACK TO "Milk 1L"                                      │
    │                                                              │
    │  1. Upload new CSV: ~SPLCDF{milk.csv~gt~code201\n...}^       │
    │  2. Load template:  ~SPLLTF{P.rox}^                          │
    │     (counter resets to 000000 again)                         │
    │  3. Set counter:    ~SPMCCV{ctr1~gt~005201}^   ◄── DB=5200+1│
    │  4. Resume printing ...                                      │
    │     Labels show: 5201, 5202, 5203...                         │
    └──────────────────────────────────────────────────────────────┘
```

### Error Handling & Edge Cases

| Scenario | What happens | Mitigation |
|----------|-------------|------------|
| Power cycle after SPLLTF but before SPMCCV | Counter is at its default `Data` value (e.g., 000000). If someone presses Print on the touchscreen, labels would have wrong counter values. | Always send SPMCCV immediately after SPLLTF, before transitioning the job to Ready state. Lock the interface (`SPGSLI{1}`) until SPMCCV is confirmed. |
| SPMCCV fails (field not found) | Template doesn't have the expected counter object. | Fail the prepare step. Alert the operator. Template and product configuration are mismatched. |
| SPLGFV returns unexpected value on stop | Counter value doesn't match expected (e.g., we printed 200 but counter shows 195). | Use SPGGTP delta as the authoritative count (as the app already does). Only use SPLGFV for the *display* counter value to persist. If SPLGFV disagrees with SPGGTP delta, log a warning but trust SPGGTP for code accounting. |
| App crashes mid-job, counter not saved | DB has stale `lifetime_count`. | On recovery, compute from SPGGTP delta: `lifetime_count = old_lifetime_count + (SPGGTP_now - job.TotalBaseline)`. This gives the correct value regardless of whether SPLGFV was read. |
| Counter wraps around (Restart=True, reaches NumericEnd) | Counter resets to NumericBegin, label shows small number. | Set `NumericEnd` high enough (e.g., 999999999) and `Restart=False` so the counter stops at the max. Or handle wrap-around in the application by tracking it. |

### What the Template Needs

The template (`.rox` file) designed in Sayasis S20 must include:

1. **A Database-sourced 2D barcode** (DataMatrix or QR) — gets its value from the CSV. This is the unique government code.
2. **An Internal-sourced Counter object** — auto-increments with each print. The application sets its starting value via `SPMCCV` before each print session.

Both objects coexist in the same template. The printer prints all visible objects on each label.

---

## 5. SPPL Commands Reference

Quick reference for all commands relevant to this feature:

### Template & CSV Management

| Command | Description | When to use |
|---------|-------------|------------|
| `~SPLCDF{name~gt~data}^` | Upload CSV file | Before loading template |
| `~SPLLTF{name.rox}^` | Load template from storage | Activates template, loads CSV into buffer, resets counter to `Data` value |
| `~SPLGAT^` | Get active template name | Verify correct template is loaded |
| `~SPLDDF{name}^` | Delete CSV file | Before uploading replacement CSV |
| `~SPLCDB^` | Clear data buffer | After deleting CSV, before reload |

### Counter Control

| Command | Description | When to use |
|---------|-------------|------------|
| `~SPMCCV{name~gt~value}^` | Set counter value | After SPLLTF, before SPPSAP — set to DB lifetime_count + 1 |
| `~SPLGFV{name}^` | Read field value | On stop/pause/complete — read final counter and save to DB |

### Print Control

| Command | Description | When to use |
|---------|-------------|------------|
| `~SPPSLQ{qty}^` | Set limited print quantity | Before starting print |
| `~SPPSAP^` | Start automatic printing | After all setup is complete |
| `~SPPSTP^` | Stop printing | On pause/cancel |

### Monitoring

| Command | Description | When to use |
|---------|-------------|------------|
| `~SPGGTP^` | Get lifetime counter (printer-wide) | Authoritative print count for code accounting |
| `~SPGGCP^` | Get current counter (volatile) | Poll loop monitoring |

---

## 6. Implementation Checklist

### Data Model Changes

- [ ] Add `LifetimePrintCount` (long) to the `ProductNode` entity — tracks total prints per product across all jobs.
- [ ] Add `CounterFieldName` (string, nullable) to the `ProductNode` entity — the name of the counter object in the template (e.g., `"counter1"`). Null means the template has no counter object / feature is disabled.
- [ ] Add EF Core migration for the new columns.

### IPrinterAdapter Changes

- [ ] Add `Task<bool> SetCounterValueAsync(string fieldName, string value, CancellationToken ct)` — wraps `SPMCCV`.
- [ ] Add `Task<string?> GetFieldValueAsync(string fieldName, CancellationToken ct)` — wraps `SPLGFV`.

### SavemaTtoAdapter Changes

- [ ] Implement `SetCounterValueAsync` using `SpplCommandBuilder.SetCounterValue(name, value)`.
- [ ] Implement `GetFieldValueAsync` using `SpplCommandBuilder.GetFieldValue(name)`.
- [ ] Add response parsing for `SPLGFV` — note the `<` separator between field name and value.

### MockPrinterAdapter Changes

- [ ] Implement both new methods with in-memory counter tracking for testing.

### SpplCommandBuilder Changes

- [ ] Add `SetCounterValue(string name, string value)` → `~SPMCCV{name~gt~value}^`
- [ ] Add `GetFieldValue(string name)` → `~SPLGFV{name}^`

### PrintJobService Changes (Prepare Step)

- [ ] After `ActivateTemplateAsync` (SPLLTF), if `product.CounterFieldName` is not null:
  - Read `product.LifetimePrintCount` from DB.
  - Call `SetCounterValueAsync(product.CounterFieldName, (lifetimeCount + 1).ToString("D" + digitCount))`.
  - If SPMCCV fails, fail the prepare step with a clear error.

### PrintJobService / JobExecutor Changes (Stop/Pause/Complete)

- [ ] On job completion/pause/cancel, if `product.CounterFieldName` is not null:
  - **Primary method:** Compute from SPGGTP delta: `newLifetimeCount = product.LifetimePrintCount + confirmedPrints`.
  - **Secondary verification:** Call `GetFieldValueAsync(product.CounterFieldName)`, parse it, and compare with the computed value. Log a warning if they disagree.
  - Save `newLifetimeCount` to `product.LifetimePrintCount` in the DB.

### UI Changes

- [ ] Product configuration: Add an optional "Counter Field Name" input and a read-only "Lifetime Print Count" display.
- [ ] Localization keys for new UI elements (en.json, ru.json only per AGENTS.md rules).

### Template Design Requirement

- [ ] Document for operators: When creating a template in Sayasis S20, add a Counter object (Source=Internal) with a high NumericEnd (e.g., 999999999) and Restart=False. Note the counter's Name — it must match the `CounterFieldName` configured in the application.
