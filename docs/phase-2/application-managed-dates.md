# Application-Managed Production & Expiry Dates

> **Purpose:** Design for moving production date and expiry date selection from the printer touchscreen into the application. The operator sets dates in the app UI before each print job; the app pushes them to the printer via SPPL. No touchscreen interaction required.
>
> Based on SPPL Rev.12 specification (`docs/savema_language_-_rev12.md`), §4 Modification Commands.

---

## Table of Contents

1. [Problem Statement](#1-problem-statement)
2. [How It Works](#2-how-it-works)
3. [Template Design Requirement](#3-template-design-requirement)
4. [SPPL Commands](#4-sppl-commands)
5. [Workflow](#5-workflow)
6. [Error Handling](#6-error-handling)
7. [Implementation Checklist](#7-implementation-checklist)

---

## 1. Problem Statement

Templates can include production date and expiry date fields. There are two ways these can be configured:

- **Automatic dates:** The template uses Date objects with `Type=Actual`. The printer uses its system clock. The operator does nothing — today's date is printed automatically, and offsets (e.g., +1 year for expiry) are baked into the template.
- **Operator-selected dates:** The template is designed so the operator must choose the dates before each print run. Currently this requires interaction on the printer touchscreen.

**Goal:** For the operator-selected case, move the date entry into our application. The operator sees two date fields in the app (pre-filled with sensible defaults), adjusts if needed, hits Prepare, and the app sends the dates to the printer. The printer prints them as-is. No touchscreen interaction.

---

## 2. How It Works

The SPPL Modification Commands (`SPMCTV`, `SPMCSV`) can change the value of **Text objects with `Source=External`** in a loaded template. The key insight:

> SPPL Rev.12 §4: "Modification commands allows to change Text, Barcode and 2D barcode in a template. Source option of changable object must be **External** for modification."

If the production date and expiry date are designed as **Text objects** (not Date objects) with `Source=External`, the application can set their values to any formatted date string via Ethernet before printing starts.

This is the standard SPPL pattern for externally-controlled variable data. It is the same mechanism already used for External text, barcodes, and 2D barcodes throughout the protocol.

---

## 3. Template Design Requirement

The template (`.rox` file) must be designed in Sayasis S20 with the following structure for each date field:

| Property | Value | Notes |
|----------|-------|-------|
| ObjectType | `Text` | NOT `Date` — must be a Text object |
| Source | `External` | Required for modification commands to work |
| Name | e.g., `prod_date`, `exp_date` | These names are used in SPPL commands and must match the application config |

Example template XML for a production date field:

```xml
<Object>
    <ObjectType>Text</ObjectType>
    <NameID>Text03</NameID>
    <Name>prod_date</Name>
    <X>10</X>
    <Y>63</Y>
    <W>200</W>
    <H>33</H>
    <ZIndex>0</ZIndex>
    <Rotate>0</Rotate>
    <Hidden>False</Hidden>
    <Content>
        <Data></Data>
        <Source>External</Source>
        <PromptMessage></PromptMessage>
        <AllowedCharacters>Any</AllowedCharacters>
        <MagnificationRatio>100</MagnificationRatio>
        <Inverted>False</Inverted>
        <Mirror>False</Mirror>
    </Content>
    <Font>
        <Name>Tahoma</Name>
        <Size>24</Size>
        <OriginalSize>8</OriginalSize>
        <Style>Regular</Style>
    </Font>
</Object>
```

A complete template for this feature would typically contain:

1. **A Database-sourced 2D barcode** — the unique code from CSV (DataMatrix/QR)
2. **An External Text object `prod_date`** — production date, set by the app
3. **An External Text object `exp_date`** — expiry date, set by the app
4. Optionally: other static text objects (product name, logo, etc.)

### What the operator needs to know

When creating or modifying templates in Sayasis S20:

- The date fields must be created as **Text** objects, not **Date** objects.
- The Source must be set to **External**.
- The Name must match exactly what is configured in the application (case-sensitive).
- The field width (`W`) must be large enough to fit the longest formatted date string.

---

## 4. SPPL Commands

### SPMCTV — Change Text Value (one field at a time)

Sets the value of a single Text object with `Source=External` in the active template.

```
~SPMCTV{<field_name>~gt~<value>}^
```

| Parameter | Description |
|-----------|-------------|
| `field_name` | Name of the Text object as defined in the template |
| `value` | The text string to set (e.g., a formatted date) |

Responses:
```
~ SPGRES{SPMCTV:OK}^                         -- success
~ SPGRES{SPMCTV:FAIL}^                       -- failure
~ SPGRES{SPMCTV:<field_name> not found}^      -- field doesn't exist in template
```

Example:
```
~SPMCTV{prod_date~gt~30.08.2026}^
~SPMCTV{exp_date~gt~30.08.2027}^
```

> SPPL Rev.12 §4.1

### SPMCSV — Change Selected Values (multiple fields at once)

Sets values of multiple Text/Barcode/2D Barcode objects in a single command.

```
~SPMCSV{<name1>~gt~<value1>~gt~<name2>~gt~<value2>~gt~...}^
```

Responses:
```
~ SPGRES{SPMCSV:OK}^                         -- success
~ SPGRES{SPMCSV:FAIL}^                       -- failure
~ SPGRES{SPMCSV:<field_name> not found}^      -- first missing field name
```

Example (set both dates in one command):
```
~SPMCSV{prod_date~gt~30.08.2026~gt~exp_date~gt~30.08.2027}^
```

> SPPL Rev.12 §4.6

### SPLGFV — Get Field Value (read back for verification)

Reads the current value of any field in the active template.

```
~SPLGFV{<field_name>}^
```

Response:
```
~ SPGRES{SPLGFV:<field_name><<value>}^
```

Example:
```
~SPLGFV{prod_date}^
~ SPGRES{SPLGFV:prod_date<30.08.2026}^
```

> SPPL Rev.12 §3.16. Note: `<` separates field name from value in the response.

### Timing constraint

> SPPL Rev.12 §4: "In order to these commands operating as intended and working properly, commands must be sended either machine is stop position or alternatively machine is print position and package is stop position."

The commands must be sent **after** the template is loaded (`SPLLTF`) and **before** printing starts (`SPPSAP`). This fits naturally into the existing Prepare step.

---

## 5. Workflow

### Sequence during job preparation

```
  Operator in App UI:
    - Selects product, printer, quantity
    - Sets Production Date:  [30.08.2026]    ← date picker, default = today
    - Sets Expiry Date:      [30.08.2027]    ← date picker, default = today + product shelf life
    - Clicks [Prepare]

  Application Prepare Step:
    1. Reserve codes from pool
    2. Upload CSV:           ~SPLCDF{product.csv~gt~code1\ncode2\n...}^
    3. Load template:        ~SPLLTF{template.rox}^
    4. Set dates:            ~SPMCSV{prod_date~gt~30.08.2026~gt~exp_date~gt~30.08.2027}^
    5. Verify (optional):    ~SPLGFV{prod_date}^  → confirm "30.08.2026"
                             ~SPLGFV{exp_date}^   → confirm "30.08.2027"
    6. Record TotalBaseline: ~SPGGTP^
    7. Set quantity:         ~SPPSLQ{500}^
    8. Job status → Ready

  Operator clicks [Start]:
    9. Start print:          ~SPPSAP^
    10. Job status → Printing
```

Step 4 is the new addition. It happens after template load (step 3) and before the job is marked Ready. The dates are locked in for the entire print run.

### On Resume (after pause/reconnect)

When resuming a paused job, the Prepare step re-runs: template is reloaded, CSV is re-uploaded with remaining codes. **The dates must also be re-sent** because `SPLLTF` reloads the template from the stored `.rox` file, which resets all External fields to their default (empty) values.

The application must persist the selected dates in the database and re-send them on every template reload.

---

## 6. Error Handling

| Scenario | Detection | Action |
|----------|-----------|--------|
| `SPMCSV` returns FAIL | Response parsing | Fail the Prepare step. Alert operator: "Failed to set dates on printer." |
| `SPMCSV` returns `<field_name> not found` | Response parsing | Fail the Prepare step. Alert operator: "Template does not have the expected date field '{name}'. Check template design." Template and product configuration are mismatched. |
| Verification mismatch (`SPLGFV` returns unexpected value) | Compare sent vs read | Log warning. Retry once. If still mismatched, fail Prepare. |
| Operator leaves date fields empty | UI validation | Block Prepare. Both dates are required when the product is configured with date fields. |
| Operator enters invalid date (e.g., 30.02.2026) | UI validation | Block Prepare. Validate date before formatting. |
| Template reload on resume, dates not re-sent | Bug — date fields would be empty | The Resume procedure must re-send dates. Persisted in DB with the job. |

---

## 7. Implementation Checklist

### Data Model Changes

- [ ] Add `ProductionDate` (DateTime, nullable) to the `PrintJob` entity — the production date chosen by the operator for this job. Null means the product doesn't use application-managed dates.
- [ ] Add `ExpiryDate` (DateTime, nullable) to the `PrintJob` entity.
- [ ] Add `ProductionDateFieldName` (string, nullable) to the `ProductNode` entity — the template Text object name for production date (e.g., `"prod_date"`). Null means this product doesn't use application-managed dates.
- [ ] Add `ExpiryDateFieldName` (string, nullable) to the `ProductNode` entity.
- [ ] Add `DefaultShelfLifeDays` (int, nullable) to the `ProductNode` entity — used to auto-compute the default expiry date from the production date (e.g., 365 for one year).
- [ ] Add `DateFormat` (string, default `"dd.MM.yyyy"`) to the `ProductNode` entity — the date format string used when sending to the printer. Must match what the template label layout expects.
- [ ] Add EF Core migration for the new columns.

### IPrinterAdapter Changes

- [ ] Add `Task<bool> SetTextValueAsync(string fieldName, string value, CancellationToken ct)` — wraps `SPMCTV`.
- [ ] Add `Task<bool> SetSelectedValuesAsync(IDictionary<string, string> fieldValues, CancellationToken ct)` — wraps `SPMCSV` for setting multiple fields at once.
- [ ] Add `Task<string?> GetFieldValueAsync(string fieldName, CancellationToken ct)` — wraps `SPLGFV`.

Note: `GetFieldValueAsync` and `SetSelectedValuesAsync` are also needed by the per-product lifetime counter feature (see `savema-counters-and-csv-offset.md`). Implement once, use for both.

### SavemaTtoAdapter Changes

- [ ] Implement `SetTextValueAsync` using `SpplCommandBuilder.SetTextValue(name, value)`.
- [ ] Implement `SetSelectedValuesAsync` using `SpplCommandBuilder.SetSelectedValues(dict)`.
- [ ] Implement `GetFieldValueAsync` using `SpplCommandBuilder.GetFieldValue(name)`.
- [ ] Add response parsing for `SPLGFV` — the `<` character separates field name from value.

### SpplCommandBuilder Changes

- [ ] Add `SetTextValue(string name, string value)` → `~SPMCTV{name~gt~value}^`
- [ ] Add `SetSelectedValues(IDictionary<string, string> fields)` → `~SPMCSV{name1~gt~value1~gt~name2~gt~value2}^`
- [ ] Add `GetFieldValue(string name)` → `~SPLGFV{name}^`

### MockPrinterAdapter Changes

- [ ] Implement new methods with in-memory field value storage for testing.

### PrintJobService Changes (Prepare Step)

- [ ] After `ActivateTemplateAsync` (SPLLTF), if `product.ProductionDateFieldName` is not null:
  - Format `job.ProductionDate` using `product.DateFormat`.
  - Format `job.ExpiryDate` using `product.DateFormat`.
  - Call `SetSelectedValuesAsync` with both field name/value pairs.
  - If SPMCSV fails, fail the Prepare step with a clear error message.
  - Optionally verify with `GetFieldValueAsync` and log warning on mismatch.

### PrintJobService Changes (Resume Step)

- [ ] In `ResumeJobAsync`, after template reload, re-send the dates from the persisted `job.ProductionDate` and `job.ExpiryDate`. Same logic as Prepare.

### UI Changes (New Job Screen)

- [ ] When the selected product has `ProductionDateFieldName` set (non-null), show two date pickers:
  - **Production Date** — default: today.
  - **Expiry Date** — default: today + `product.DefaultShelfLifeDays` (or today + 365 if not set).
- [ ] When the product does NOT have `ProductionDateFieldName` set, hide the date pickers entirely. The product uses automatic dates (Date objects in the template) or no dates at all.
- [ ] Validate: both dates required, expiry must be after production, dates must be valid.
- [ ] Pass the selected dates to `PrintJobService.PrepareJobAsync`.

### UI Changes (Product Configuration)

- [ ] Add optional fields to product edit form:
  - Production Date Field Name (text input, e.g., `prod_date`)
  - Expiry Date Field Name (text input, e.g., `exp_date`)
  - Default Shelf Life (days, numeric input)
  - Date Format (text input, default `dd.MM.yyyy`)
- [ ] These are nullable. When empty, the feature is disabled for that product.

### Localization

- [ ] Add keys to `en.json` and `ru.json` (not `hy.json` per AGENTS.md rules):
  - `NewJob_ProductionDate`, `NewJob_ExpiryDate`
  - `Products_ProductionDateField`, `Products_ExpiryDateField`, `Products_DefaultShelfLife`, `Products_DateFormat`
  - `Error_DateSetFailed`, `Error_DateFieldNotFound`, `Error_DateVerificationFailed`
  - `Error_ExpiryBeforeProduction`, `Error_DatesRequired`
