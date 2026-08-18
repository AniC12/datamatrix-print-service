# Connection Recovery & Post-Reconnection State Reconciliation — Deep Dive

> **Purpose:** Exhaustive analysis of every scenario that can occur when the connection between the app and a Savema printer is lost and then re-established. Covers: what caused the disconnect, what the printer might have done while we were gone, what information we can extract after reconnecting, and what the correct action is in every case.
>
> This is the most safety-critical area of the system. Getting it wrong means either **duplicate codes** (illegal, compliance violation) or **unnecessary waste** (burned codes that didn't need to be burned). Every decision must err on the side of safety: waste is acceptable, duplicates are not.

---

## Table of Contents

1. [What We Know Before Disconnection](#1-what-we-know-before-disconnection)
2. [What the Printer Remembers (Persistence Model)](#2-what-the-printer-remembers-persistence-model)
3. [What We Can Read After Reconnection (Observable State)](#3-what-we-can-read-after-reconnection-observable-state)
4. [What We Cannot Know (Blind Spots)](#4-what-we-cannot-know-blind-spots)
5. [The Quarantined Status — Replacing Blind Burns with Operator Decision](#5-the-quarantined-status--replacing-blind-burns-with-operator-decision)
6. [Causes of Disconnection](#6-causes-of-disconnection)
7. [Post-Reconnection Inspection Procedure](#7-post-reconnection-inspection-procedure)
8. [Scenario Matrix — Every Possible State After Reconnection](#8-scenario-matrix--every-possible-state-after-reconnection)
9. [Decision Flowchart](#9-decision-flowchart)
10. [The Resume Procedure (Re-Upload Remaining Codes)](#10-the-resume-procedure-re-upload-remaining-codes)
11. [Edge Cases & Corner Cases](#11-edge-cases--corner-cases)
12. [Current Implementation vs Recommended Implementation](#12-current-implementation-vs-recommended-implementation)
13. [Summary: Safety Invariants](#13-summary-safety-invariants)

---

## 1. What We Know Before Disconnection

At any point during a print job, the app has the following information persisted in SQLite:

| Field | Source | Persistence |
|-------|--------|-------------|
| `job.TotalBaseline` | `SPGGTP` read at job start (Step 5 of Print Flow) | Saved to DB before printing begins |
| `job.CodesConfirmed` | Running count of codes confirmed printed by poll loop | Updated every 500ms when counter advances |
| `job.Quantity` | Total codes requested for this job | Set at job creation |
| `job.Status` | Current lifecycle state (Printing, Paused, etc.) | Updated on every state transition |
| `codes[].Status` | Per-code status (Available, Reserved, Printed, Burned, Returned, Quarantined) | Updated in batches by `MarkCodesPrintedAsync` |
| `job.PrinterId` | Which printer this job targets | Set at job creation |
| Product template name | The `.rox` file assigned to the product | In `product_nodes.template_file` |
| Product CSV name | The filename used on the printer | In `product_nodes.printer_csv_name` |

**The app's database is the source of truth for code state.** The printer is a peripheral — we query it for verification, but the DB decides what's printed and what's not.

**Key timing gap:** Between the last successful poll and the disconnection, the printer may have printed additional codes that the app hasn't recorded yet. This is the primary source of ambiguity.

```
Timeline:
  ...poll reads SPGGCP=342...500ms passes...printer prints 343,344,345...DISCONNECT
  
  App knows:  342 confirmed
  Printer did: 345 actually printed
  Gap:         3 codes printed but not yet recorded by app
```

---

## 2. What the Printer Remembers (Persistence Model)

Understanding what survives a printer power cycle vs. what is volatile is critical for recovery. Based on the SPPL Rev.12 documentation and empirical testing:

### Survives power cycle (non-volatile, stored on printer's flash/disk)

| Item | SPPL Command to Read | Notes |
|------|---------------------|-------|
| **Total print count (SPGGTP)** | `~SPGGTP^` | Lifetime counter. Never resets. This is our most reliable anchor. |
| **Stored template files (.rox)** | `~SPLGST^` | Template files uploaded via `SPLRTF` persist in printer storage. |
| **Stored data files (.csv)** | `~SPLGSD^` | CSV files uploaded via `SPLCDF` persist in printer storage. |
| **Printer serial number** | `~SPGGSN^` | Useful for detecting hardware swap. |

### Lost on power cycle (volatile, RAM-only)

| Item | SPPL Command to Read | Notes |
|------|---------------------|-------|
| **Current print count (SPGGCP)** | `~SPGGCP^` | Resets to 0 when ANY template is loaded (`SPLLTF`). Also resets on power cycle. From SPPL docs: "This counter resets when load any template." |
| **Active data buffer** | N/A (no read command) | When a template with a CSV field is loaded, the CSV data is read into a runtime buffer. This buffer is volatile. Lost on power cycle. |
| **CSV row pointer** | N/A (no read command) | The internal pointer tracking which CSV row to print next. Behavior after power cycle is undocumented — assume lost. |
| **Active template selection** | `~SPLGAT^` | Which template is currently loaded. After power cycle, the printer may auto-load the last template (firmware-dependent) or show INIT state with no template. |
| **Limited print quantity** | `~SPPGLQ^` | The remaining count set by `SPPSLQ`. Volatile — lost on power cycle. |
| **Print/Stop state** | `~SPPSTA^` | After power cycle, printer starts in INIT, then transitions to WAITING once a template is auto-loaded. |
| **Interface lock** | `~SPGGLI^` | The lock set by `SPGSLI{1}`. Volatile — lost on power cycle. |

### Critical implication: CSV data buffer

The data buffer is the most important volatile item. Here's why:

```
Normal flow:
  1. SPLCDF uploads CSV file to storage (persistent)
  2. SPLLTF loads template, which reads CSV from storage into data buffer (volatile)
  3. Printer prints from data buffer, advancing the row pointer
  4. SPGGCP counts how many rows have been printed

After power cycle:
  - CSV file is still in storage (SPLGSD confirms)
  - Data buffer is EMPTY (or re-initialized from CSV file start)
  - Row pointer is UNKNOWN (reset to beginning? random? depends on firmware)
  - SPGGCP is 0
```

**This means: after a power cycle, even if the CSV file is still on the printer, we CANNOT simply restart printing. The row pointer is at the beginning, so the printer would re-print codes that were already printed. This would create DUPLICATES.**

The correct recovery procedure after power cycle is:
1. Read `SPGGTP` to determine how many codes were actually printed
2. Delete the old CSV from the printer
3. Upload a NEW CSV containing ONLY the remaining unprinted codes
4. Reload the template (which re-initializes the buffer from the new CSV)
5. Set the print quantity to the remaining count
6. Start printing

---

## 3. What We Can Read After Reconnection (Observable State)

After re-establishing the TCP connection, we run a **state inspection** using these SPPL commands:

| # | Command | What It Returns | Why We Need It |
|---|---------|----------------|----------------|
| 1 | `SPPSTA` | Printer status: INIT, WAITING, RUNNING, ERROR, BLOCKED | Is the printer doing anything? Is it in an error state? |
| 2 | `SPGGTP` | Lifetime counter (integer) | **THE anchor.** `SPGGTP_now - job.TotalBaseline` = total codes physically printed since our job started. Survives power cycle. |
| 3 | `SPGGCP` | Current counter since last template load (integer) | If no power cycle, this should match our expectations. If 0 with SPGGTP delta > 0, the printer was power-cycled (or someone reloaded the template). |
| 4 | `SPLGAT` | Active template name (string) | Is OUR template still loaded? Or did someone load a different one? |
| 5 | `SPLGSD` | List of stored CSV filenames | Is our CSV file still on the printer? |
| 6 | `SPLGST` | List of stored template filenames | Is our template file still in storage? |
| 7 | `SPPGLQ` | Remaining prints in limited quantity | If the limited-quantity job is still active, how many are left? |
| 8 | `SPGGSN` | Printer serial number | Sanity check: is this the same physical printer? |

### Derived values

From the raw readings, we compute:

```
lifetime_delta       = SPGGTP_now - job.TotalBaseline
unrecorded_prints    = lifetime_delta - job.CodesConfirmed
remaining            = job.Quantity - lifetime_delta
power_cycled         = (SPGGCP == 0 && lifetime_delta > job.CodesConfirmed)
                       OR (SPGGCP == 0 && SPPSTA == WAITING && lifetime_delta > 0)
template_match       = (SPLGAT == expected_template_name)
csv_present          = (expected_csv_name IN SPLGSD_list)
```

---

## 4. What We Cannot Know (Blind Spots)

These are things the SPPL protocol does not expose:

| Blind Spot | Why It Matters | Mitigation |
|-----------|---------------|------------|
| **CSV data buffer contents** | We can't read back what data is in the active print buffer. We can only check if the CSV *file* exists in storage (`SPLGSD`), not what's in the runtime buffer. | We re-upload the CSV and reload the template on resume. |
| **CSV row pointer position** | We don't know which row the printer will print next. After a power cycle, the row pointer may be at position 0 (start), which would re-print already-printed codes. | We ALWAYS re-upload a NEW CSV with only remaining codes on resume. |
| **Whether prints used OUR codes** | If someone else loaded a different template/CSV and printed, SPGGTP still advances. We can't tell if the prints were ours or someone else's. | We check `SPLGAT` (active template). If it doesn't match, we flag the entire delta as suspicious. |
| **Physical print quality** | The counter advances even if the print is illegible (faded ribbon, misaligned, etc.). | Phase 2: scanner verification. |
| **What happened to products** | The printer may have printed codes but products didn't flow past it (production line jam). | Phase 2: scanner verification. |

---

## 5. The Quarantined Status — Replacing Blind Burns with Operator Decision

### The Problem with "Burned"

The original design uses a **Burned** status for codes whose print state is ambiguous: "we're not sure if this code was physically printed, so we mark it as permanently consumed to prevent any risk of duplicate usage." This is the safe default, but it has a cost:

- Each burned code is a code the client paid money for that can never be used.
- In scenarios with large ambiguity windows (long disconnections, template mismatches), potentially dozens or hundreds of codes get burned — most of which were probably never printed.
- The operator has no way to recover these codes, even if they can physically verify (by inspecting the production line or products) that the codes were never printed.

### The Solution: Quarantined

We introduce a new code status: **Quarantined**.

```
Code Status Lifecycle (updated):

Available ──→ Reserved ──→ Printed
                │
                ├──→ Returned ──→ Available (back to pool)
                │
                ├──→ Burned (certain waste — e.g., operator-confirmed loss)
                │
                └──→ Quarantined (uncertain — needs human verification)
```

**Definition:** A Quarantined code is one where the system cannot determine with certainty whether it was physically printed onto a product. It is **frozen** — it cannot be automatically reused, and it does not count as "available" for new jobs. It exists in a holding state awaiting human decision.

### Key Properties of Quarantined

| Property | Behavior |
|----------|----------|
| **Can be auto-reused?** | NO. Never enters the available pool automatically. |
| **Blocks new jobs?** | No. Quarantined codes are excluded from availability counts. They don't prevent the operator from creating jobs with remaining Available codes. |
| **Visible to operator?** | Yes. Shown in a dedicated "Quarantined" section in the Admin page with full context (which job, which printer, what scenario caused it). |
| **Operator can move to Available?** | Yes, via Admin page. After physical verification that the code was NOT printed. |
| **Operator can move to Printed?** | Yes, via Admin page. After physical verification that the code WAS printed. |
| **Operator can move to Burned?** | Yes, via Admin page. If they want to permanently discard it without verification. |
| **Expires automatically?** | No. Stays Quarantined forever until a human decides. |

### When to Quarantine vs When to Burn

| Situation | Old behavior (Burn) | New behavior (Quarantine) | Rationale |
|-----------|--------------------|-----------------------------|-----------|
| Cancel a printing job: the boundary code at position `counter` (might be mid-print) | Burn +1 | **Quarantine +1** | Operator can check the production line. If the last product has no code, move it back to Available. |
| Power cycle: the code at position `delta` (might have printed but counter didn't increment) | Burn +1 | **Quarantine +1** | Same reasoning. Physical inspection can confirm. |
| Template mismatch: codes `[confirmed .. delta-1]` where we don't know if OUR codes or someone else's data was printed | Burn all | **Quarantine all** | Operator investigates what happened. Codes that weren't ours can be recovered. |
| Counter went backward: entire unconfirmed range | Burn all unprinted | **Quarantine all unprinted** | Manual investigation required anyway. Don't permanently destroy codes before the operator even looks. |
| App crash + printer offline (can't read SPGGTP): boundary code | Burn +1 | **Quarantine +1** | Once printer comes online, operator can verify. |

### When to STILL use Burned (not Quarantine)

Burned remains appropriate when:
- The operator **explicitly confirms** (via Admin page) that a quarantined code should be permanently discarded.
- A code is rejected at import time (validation failure) — though this is a different flow entirely.
- The operator manually decides to sacrifice codes from the Admin page for business reasons.

**The key distinction:** Quarantined = system marks automatically (recoverable). Burned = human decides explicitly (permanent).

### Admin Page Integration

> **Note:** The Admin page is being developed in parallel. This section describes the interface contract that the recovery system relies on.

The Admin page provides:

1. **Quarantine Dashboard** — A list of all Quarantined codes, grouped by job/incident, showing:
   - Job # and product name
   - Timestamp of the incident
   - Reason for quarantine (e.g., "Power cycle boundary", "Template mismatch", "Cancel boundary")
   - The code value itself
   - Action buttons: [Move to Available] [Move to Printed] [Move to Burned]

2. **Batch operations** — Select multiple quarantined codes and move them all at once (e.g., "all 15 codes from Job #47's template mismatch incident → move to Available").

3. **Audit trail** — Every manual status change is logged: who did it, when, from what status to what status, and optionally a reason/note.

4. **Code search** — Find any code by value and see its full history (all status transitions, which job, which printer, timestamps).

### Impact on Code Pool Statistics

The Products page code pool stats should display:

```
Code Pool:
  Available:    8,300
  Printed:      1,700
  Quarantined:      7  ← NEW (shown in amber/yellow)
  Burned:           3
  Total:       10,010
```

Quarantined codes are NOT counted as Available. They are in limbo. The operator sees them and knows action is needed.

### Impact on Safety Guarantees

The safety invariant changes from:
- OLD: "Ambiguous codes are burned (permanently consumed) to prevent duplicates."
- NEW: "Ambiguous codes are quarantined (frozen, cannot be auto-reused) until a human verifies their physical state. They can only return to Available via explicit operator action in the Admin page."

**The safety guarantee is preserved.** A Quarantined code can never accidentally end up in a print job. The only path back to Available is through deliberate human decision via the Admin page. The difference is that we give the operator a chance to recover codes instead of automatically destroying them.

---

## 6. Causes of Disconnection

Different causes lead to different printer states upon reconnection. Here's every way we can lose the connection:

### 5A. Network Failure (Cable/Switch/Infrastructure)

**What happens to the printer:** Nothing. The printer continues whatever it was doing — if it was printing, it keeps printing. The TCP connection drops on our side, but the printer doesn't even know we disconnected (TCP has no "the other side went away" notification unless keepalive is configured).

**State on reconnect:** The printer might be RUNNING (still printing) or WAITING (finished while we were gone).

**SPGGCP:** Still valid (no template reload, no power cycle).

**Data buffer:** Intact. Row pointer has advanced normally.

### 5B. Printer Power Cycle (Power Outage, Manual Off/On)

**What happens to the printer:** Full reboot. SPGGCP resets to 0. Data buffer is lost. Active template may be auto-reloaded by firmware (going from INIT to WAITING), but the data buffer associated with the CSV is lost.

**State on reconnect:** INIT (still booting) or WAITING (booted, auto-loaded last template).

**SPGGCP:** 0 (or a small number if the printer started a new print after rebooting).

**SPGGTP:** Reflects all prints that happened before the power cycle. This is our lifeline.

**Data buffer:** GONE. Even if the CSV file is still in storage, the data buffer must be reloaded via `SPLLTF`.

### 5C. App Crash / Force Close

**What happens to the printer:** Nothing — same as network failure from the printer's perspective. The TCP connection eventually times out on the printer's side (if it even notices).

**State on reconnect:** Same as 5A (printer keeps doing whatever it was doing).

**App state:** The critical difference is that the app loses its in-memory state (active `JobExecutor`, in-flight counter readings). On restart, it must reconstruct state from the database only.

### 5D. App Intentional Shutdown (User Closes App)

**What happens:** Same as crash, but the app has a chance to clean up. Currently, jobs in "Printing" status stay in the DB — the app does NOT pause or cancel them on shutdown.

**State on reconnect:** Same as 5A. The printer may have finished the entire job while the app was closed.

### 5E. Printer Firmware Crash / Hang

**What happens:** The printer's internal software crashes or hangs. It may reboot automatically (same as 5B) or stay in a non-responsive state. The TCP connection may or may not drop depending on whether the network stack is affected.

**State on reconnect:** Unpredictable. Could be INIT (rebooted), WAITING (recovered), or ERROR. SPGGTP should be reliable (it's in non-volatile storage).

### 5F. Printer Enters BLOCKED State

**What happens:** The operator navigates away from the main screen on the printer's touchscreen. All SPPL commands except SPPSTA return FAIL. The printer may still be physically printing (RUNNING + BLOCKED).

**State on reconnect:** Not really a disconnection — the TCP connection is alive, but commands fail. SPPSTA reveals BLOCKED status. The poll loop sees FAIL responses and logs warnings.

**Note:** This is NOT a connection loss. It's a protocol-level issue handled in the poll loop. Included here for completeness.

---

## 7. Post-Reconnection Inspection Procedure

When the connection is re-established (either after a network recovery or on app startup finding stale jobs), we run this inspection sequence. Every step is mandatory.

```
RECONNECTION INSPECTION PROCEDURE
==================================

Step 1: Read printer status
  → SPPSTA
  → Determine: INIT / WAITING / RUNNING / ERROR / BLOCKED
  → If INIT: printer is still booting. Wait and retry.
  → If BLOCKED: alert operator to return to main screen. Wait and retry.

Step 2: Read lifetime counter
  → SPGGTP
  → Compute: lifetime_delta = SPGGTP_now - job.TotalBaseline
  → This tells us: EXACTLY how many codes were physically printed 
    since our job started, regardless of power cycles or template reloads.

Step 3: Compare with app records
  → unrecorded_prints = lifetime_delta - job.CodesConfirmed
  → If unrecorded_prints == 0: no new prints since last poll. Clean state.
  → If unrecorded_prints > 0: some prints happened that we haven't recorded.
  → If unrecorded_prints < 0: ANOMALY. Counter went backward. Critical alert.

Step 4: Read current counter (if printer is RUNNING or WAITING)
  → SPGGCP
  → If SPGGCP == 0 and lifetime_delta > 0:
      The printer was power-cycled (or template was reloaded) since our job started.
      Data buffer is LOST. Row pointer is LOST.
  → If SPGGCP > 0 and SPGGCP == lifetime_delta:
      No power cycle. Normal state. Data buffer is intact.
  → If SPGGCP > 0 and SPGGCP != lifetime_delta:
      Someone reloaded a template after some prints. Complex state.

Step 5: Read active template
  → SPLGAT
  → Does it match our expected template?
  → If yes: our job context is likely still active.
  → If no (or empty): someone changed the template. Our data buffer is GONE.

Step 6: Read stored files
  → SPLGSD (CSV files)
  → SPLGST (template files)
  → Is our CSV still on the printer? Is our template still stored?

Step 7: Read remaining quantity (if RUNNING)
  → SPPGLQ
  → Does it make sense? (quantity - SPPGLQ should ≈ SPGGCP)
  → If it doesn't match, the limited-quantity context was lost (power cycle).

Step 8: Classify the scenario (see Section 7)
```

---

## 8. Scenario Matrix — Every Possible State After Reconnection

Each scenario is defined by the combination of answers from the inspection procedure. For every scenario, we specify the **recommended action** and the **safety justification**.

### Legend

- **delta** = `SPGGTP_now - job.TotalBaseline` (total prints since job start)
- **confirmed** = `job.CodesConfirmed` (what app recorded before disconnect)
- **quantity** = `job.Quantity` (total codes in this job)
- **unrecorded** = `delta - confirmed` (prints we missed)

---

### Scenario 1: Network Blip — Printer Still Printing Our Job

**Trigger:** Brief network interruption. Printer was unaffected.

**Inspection results:**
- SPPSTA: **RUNNING**
- SPLGAT: **matches** our template
- SPGGCP: **> 0**, advancing
- SPGGTP delta: **> confirmed** (some prints happened during disconnect)
- SPGGCP **== delta** (no power cycle)

**Action: Resume polling immediately.**
1. Mark codes `[confirmed .. delta-1]` as Printed (catch up on missed prints).
2. Update `job.CodesConfirmed = delta`.
3. Resume the poll loop from the current counter value.
4. If `delta >= quantity`: job is complete. Mark all remaining reserved codes as Printed. Finalize.

**Safety:** No ambiguity. SPGGTP confirms exactly how many prints happened. Template match confirms they were our codes. SPGGCP consistency confirms no power cycle.

**This is the happy path.** The most common reconnection scenario.

---

### Scenario 2: Network Blip — Printer Finished Our Job While Disconnected

**Trigger:** Longer network outage. Printer completed all prints and returned to idle.

**Inspection results:**
- SPPSTA: **WAITING** (idle)
- SPLGAT: **matches** our template
- SPGGTP delta: **== quantity** (exactly the right number of prints)
- SPGGCP: **== quantity** (or **== delta**, no power cycle)

**Action: Complete the job.**
1. Mark ALL remaining reserved codes as Printed.
2. Set `job.CodesConfirmed = quantity`.
3. Set `job.Status = Completed`.
4. Log success.

**Safety:** Perfect match. SPGGTP confirms exactly `quantity` prints. No ambiguity.

---

### Scenario 3: Network Blip — Printer Ran Into Error During Our Job

**Trigger:** Ribbon ran out, mechanical error, etc., while we were disconnected.

**Inspection results:**
- SPPSTA: **ERROR<message>** (e.g., "Ribbon not found")
- SPLGAT: **matches** our template
- SPGGTP delta: **> confirmed but < quantity** (partial progress)
- SPGGCP: **== delta** (no power cycle between job start and error)

**Action: Pause the job and alert the operator.**
1. Mark codes `[confirmed .. delta-1]` as Printed (catch up).
2. Update `job.CodesConfirmed = delta`.
3. Set `job.Status = Paused` (or Error).
4. Alert: "Printer error: {message}. {delta}/{quantity} codes printed. Resolve the error, then resume or abort."
5. Operator fixes the hardware issue.
6. On Resume: use the standard resume procedure (Section 9).
7. On Abort: burn code at position `delta`, return remaining to pool.

**Safety:** SPGGTP gives us the exact count. Template match confirms our codes. Error state means the printer stopped cleanly at a known position.

---

### Scenario 4: Printer Power Cycled — Was Printing Our Job

**Trigger:** Power outage affecting the printer (not necessarily the app).

**Inspection results:**
- SPPSTA: **WAITING** (rebooted, auto-loaded template or sitting at main screen)
- SPGGTP delta: **> confirmed** (prints happened before power loss)
- **SPGGCP: 0** (reset by power cycle)
- SPLGAT: may or may not match (firmware-dependent auto-load behavior)
- SPLGSD: CSV file **likely still present** (stored on flash)

**Action: Reconcile and offer Resume/Abort to operator.**
1. Mark codes `[confirmed .. delta-1]` as Printed.
2. Update `job.CodesConfirmed = delta`.
3. **Quarantine** the code at position `delta` (the boundary code — may have been mid-print when power was lost).
4. Alert: "Printer was power-cycled. {delta} codes printed (app had recorded {confirmed}). 1 code quarantined (boundary). {quantity - delta - 1} remaining."
5. **DO NOT** attempt to simply restart printing — the data buffer and row pointer are lost.
6. Offer operator:
   - **Resume:** Execute the full Resume Procedure (Section 10) — re-upload new CSV with only remaining codes (excludes the quarantined code).
   - **Abort:** Return remaining (non-quarantined) codes to pool. The quarantined code stays in quarantine for operator review.

**Safety:** SPGGTP is the only reliable counter after power cycle. We trust it completely. The critical step is that we MUST re-upload a new CSV with only remaining codes — never resume from the existing CSV/buffer, which would re-print already-printed codes.

**Why quarantine +1 (not burn)?** During the power failure, the printer may have been mid-print on code `delta+1`. It's possible that code was partially printed (physically applied to a product but not counted because the counter didn't increment before power was lost). We quarantine it because the operator can physically check the production line. If they find the code on a product → move to Printed via Admin. If not → move back to Available. This saves one code versus the old burn approach, without compromising safety.

---

### Scenario 5: Printer Power Cycled — No Additional Prints

**Trigger:** Power outage hit exactly between prints, or during idle after a pause.

**Inspection results:**
- SPPSTA: **WAITING**
- SPGGTP delta: **== confirmed** (no new prints since last poll)
- **SPGGCP: 0**

**Action: Offer Resume/Abort to operator (same as Scenario 4, but cleaner).**
1. No new codes to mark — `confirmed` is accurate.
2. **Quarantine** the code at position `confirmed` (the next-to-print boundary code).
3. Alert: "Printer was power-cycled. No additional prints detected. 1 code quarantined (boundary). {quantity - confirmed - 1} remaining."
4. Resume: full Resume Procedure (Section 10), excluding the quarantined code.
5. Abort: return remaining (non-quarantined) codes to pool.

**Safety:** Clean state. But we still must re-upload the CSV on resume because the data buffer is lost.

**Why quarantine even when delta == confirmed?** There is a subtle edge case: the printer physically printed code `confirmed+1` but the SPGGTP counter didn't increment because the counter update happens after the print cycle completes. In this case, the code IS physically on a product but we don't know about it. Quarantining it (instead of burning) lets the operator verify: if the production line was stopped and no product with a partial print exists, they can move it back to Available via Admin. If they find it was printed, they mark it Printed. Either way — no waste if unnecessary, no duplicate if it was printed.

**Recommendation:** Always quarantine +1 on power cycle, even if delta == confirmed. The operator resolves it in the Admin page when they have time.

---

### Scenario 6: Printer Power Cycled — More Prints Than Expected

**Trigger:** Power was interrupted well after the last poll, and many prints happened.

**Inspection results:**
- SPPSTA: **WAITING**
- SPGGTP delta: **> confirmed** (unrecorded prints > 0)
- SPGGTP delta: **< quantity** (job didn't finish)
- **SPGGCP: 0**

**Action: Same as Scenario 4.** Mark unrecorded prints, offer Resume/Abort.

The only difference from Scenario 4 is the number of unrecorded prints. The procedure is identical.

---

### Scenario 7: Printer Power Cycled — Job Had Completed Before Power Loss

**Trigger:** The printer finished all prints, then lost power before the app could record completion.

**Inspection results:**
- SPPSTA: **WAITING**
- SPGGTP delta: **== quantity** (all prints done)
- **SPGGCP: 0**

**Action: Complete the job.**
1. Mark ALL remaining reserved codes as Printed.
2. Set `job.CodesConfirmed = quantity`.
3. Set `job.Status = Completed`.

**Safety:** SPGGTP confirms the full quantity was printed. Even though SPGGCP is 0 (power cycle), the lifetime counter tells the whole story.

---

### Scenario 8: Someone Loaded a Different Template

**Trigger:** While we were disconnected (or during a power cycle recovery), someone manually loaded a different template on the printer. This destroyed our data buffer and reset SPGGCP.

**Inspection results:**
- SPPSTA: **WAITING** or **RUNNING**
- SPLGAT: **does NOT match** our template
- SPGGTP delta: **> confirmed** (some prints happened)
- SPGGCP: could be anything (it's counting prints of the OTHER template)

**This is a dangerous scenario.** The SPGGTP delta includes prints from BOTH our job AND whatever the other person printed. We cannot distinguish them.

**Action: Conservative abort with quarantine.**
1. We know for certain that our job printed AT LEAST `confirmed` codes (these were recorded before the disconnect).
2. The `unrecorded_prints = delta - confirmed` could be:
   - (a) All from our job (the other template was loaded AFTER our job finished), or
   - (b) All from the other person (our job stopped when we disconnected, then they loaded their template), or
   - (c) A mix of both.
3. **We cannot tell which.** Therefore:
   - **Quarantine** codes `[confirmed .. delta-1]`. These codes MIGHT have been printed (if scenario (a) or (c)), or might NOT have been printed (if scenario (b)). The operator must investigate.
   - **Quarantine** code at position `delta` (+1 boundary safety).
4. Return remaining codes `[delta+1 .. quantity-1]` to Available (these were never in the data buffer after the template change, so they are certainly safe to reuse).
5. Set `job.Status = Cancelled`.
6. **Critical alert:** "Template mismatch detected. Expected '{our_template}', found '{other_template}'. {delta - confirmed} prints occurred on this printer from an unknown source. {delta - confirmed + 1} codes quarantined. Job #{id} has been aborted. Operator: investigate what was printed and resolve quarantined codes in the Admin page."

**Why quarantine instead of burn?**
In the old design, we'd burn all `delta - confirmed` codes — potentially dozens or hundreds, permanently destroyed. But in many real-world cases, scenario (b) is most common: our job stopped when we disconnected, and someone else loaded their own template to do their own unrelated work. In that case, NONE of our quarantined codes were actually printed, and the operator can move them all back to Available after confirming this. The savings can be significant.

**What does the operator do in the Admin page?**
- If they determine scenario (b) happened (the other person printed their own data, our codes were never used): move all quarantined codes back to Available.
- If they determine scenario (a) happened (our job actually printed these codes before the template switch): move them to Printed.
- If uncertain (scenario (c), or they can't investigate): move them to Burned (permanent). This is equivalent to the old behavior, but the operator made the call consciously.

---

### Scenario 9: SPGGTP Delta Exceeds Job Quantity

**Trigger:** More prints happened since our `TotalBaseline` than the number of codes in our job. This means external printing occurred — someone else printed something on this printer.

**Inspection results:**
- SPGGTP delta: **> quantity**
- SPGGCP: could be anything

**Sub-scenarios:**

**9A: Template matches, delta slightly > quantity:**
The printer over-printed (possible if `SPPSLQ` limited quantity didn't stop it, or someone hit print again from the touchscreen).
- Mark all `quantity` codes as Printed. Job is complete.
- Alert: "Printer over-printed by {delta - quantity}. Investigate if duplicate codes were printed."

**9B: Template doesn't match, delta >> quantity:**
Someone loaded a different template and did extensive printing.
- Same as Scenario 8 (conservative abort).
- The excess prints (beyond our quantity) are not our concern — they used someone else's data.

**Action for 9A:**
1. Mark all reserved codes `[0 .. quantity-1]` as Printed.
2. Complete the job.
3. Alert with over-print count for investigation.

**Safety:** Our `quantity` codes are accounted for. The excess prints may have used data beyond our CSV (if the data buffer wrapped) or used the other person's data. Either way, our codes are consumed.

---

### Scenario 10: SPGGTP Delta LESS Than CodesConfirmed (Counter Went Backward)

**Trigger:** This should be impossible under normal operation. Possible causes:
- Printer hardware was swapped (different physical printer at same IP)
- SPGGTP was manually reset (service menu)
- Database corruption (wrong TotalBaseline)
- Extreme firmware bug

**Inspection results:**
- SPGGTP delta: **< confirmed** (e.g., delta = 100, confirmed = 342)

**Action: CRITICAL ALERT — Quarantine all unconfirmed codes, require manual investigation.**
1. **Quarantine** ALL codes from `[confirmed .. quantity-1]` that are still in Reserved status. These codes are in an unknown state — we can't determine what was printed.
2. Do NOT resume or auto-abort.
3. Alert: "CRITICAL: Printer lifetime counter is LESS than what the app has recorded. Expected SPGGTP >= {TotalBaseline + confirmed} = {expected}, got {SPGGTP_now}. This indicates a hardware swap, counter reset, or data corruption. {quantity - confirmed} codes quarantined. Manual investigation required."
4. Set `job.Status = Error`.
5. Present all data to operator in the Admin page. They investigate and decide per-code:
   - If hardware was swapped: likely none of these codes were printed → move to Available, re-configure the printer.
   - If counter was reset: investigate what happened → move to Printed or Available based on findings.
   - If DB corruption: attempt to reconstruct from audit logs → resolve quarantined codes accordingly.

**Safety:** Never auto-resolve this. The data is contradictory. Human judgment is required. Quarantine ensures codes can't be accidentally reused while the investigation is ongoing.

### Scenario 10B: Detect hardware swap proactively

Read `SPGGSN` (serial number) on reconnect and compare with a stored value. If the serial number changed, we know the physical printer was replaced.

**Action:** Alert immediately. Do not attempt any job operations until the operator confirms the new hardware setup.

---

### Scenario 11: Printer is in INIT State

**Trigger:** We reconnected immediately after the printer booted. It's still loading its firmware/template.

**Inspection results:**
- SPPSTA: **INIT**
- All other commands will likely return FAIL

**Action: Wait and retry.**
1. Log: "Printer is still initializing. Waiting..."
2. Wait 2–5 seconds.
3. Re-run the inspection procedure.
4. Typical boot time is 5–15 seconds. After 60 seconds of INIT, escalate to a warning alert.

---

### Scenario 12: Printer is in BLOCKED State

**Trigger:** Operator is in the printer's settings menu.

**Inspection results:**
- SPPSTA: **WAITING<BLOCKED** or **RUNNING<BLOCKED**
- All other commands return FAIL

**Action: Alert operator, wait.**
1. Alert: "Printer BLOCKED — operator not in main window. Return to main screen to allow recovery."
2. Retry SPPSTA every 2 seconds until BLOCKED clears.
3. Then run the full inspection procedure.

**Note:** If RUNNING + BLOCKED, the printer IS printing but we can't read counters. When the block clears, we'll catch up.

---

### Scenario 13: Printer in ERROR State

**Trigger:** Hardware error (ribbon, mechanical, thermal, etc.)

**Inspection results:**
- SPPSTA: **ERROR<message>**
- SPGGTP: readable (ERROR doesn't prevent counter reads in most firmware versions)

**Action: Same as Scenario 3.** Reconcile counters, pause job, alert with error message. Wait for operator to fix hardware.

---

### Scenario 14: App Crash Recovery (Startup with Stale Jobs)

**Trigger:** App restarts and finds jobs in Preparing/Ready/Printing/Paused status in the DB.

**This is a composite scenario** — for each stale job, we classify it using the scenarios above.

**Preparing or Ready jobs:**
- These never started printing. No codes were sent to the printer's data buffer (Preparing) or the template was loaded but printing never began (Ready).
- **Action:** Auto-cancel. Return all reserved codes to Available. No burn needed.
- **Why no burn?** Nothing was printed. The codes never left the app's control.

**Printing jobs:**
1. Attempt to connect to the printer.
2. If connected: run the full inspection procedure (Section 6), classify into scenarios 1–13.
3. If NOT connected: mark for manual recovery. Show in Recovery Dialog as "Offline — connect printer to continue."
4. Present Recovery Dialog with per-job Resume/Abort options.

**Paused jobs:**
- Pausing already reconciled the counter (the printer was stopped, SPGGCP was read, codes were committed). `CodesConfirmed` is accurate.
- **Action:** Present in Recovery Dialog. Resume (re-upload remaining codes) or Abort (return remaining to pool, no burn needed since pause reconciled cleanly).

---

### Scenario 15: Connection Lost During Prepare Step

**Trigger:** TCP drops while uploading CSV or template.

**Inspection results (on reconnect):**
- Job status in DB: **Preparing**
- Printer state: unknown (CSV may be partially uploaded, template may be partially uploaded)

**Action:** The current implementation handles this correctly:
1. On reconnect, Prepare is not auto-resumed. The job stays in Preparing status.
2. If the app crashes during Prepare, startup recovery auto-cancels Preparing jobs.
3. The operator can retry Prepare manually — it will delete the old CSV (SPLDDF, ignore FAIL) and re-upload fresh.

**Safety:** Idempotent by design. Partial uploads are overwritten on retry.

---

## 9. Decision Flowchart

```
                    CONNECTION RE-ESTABLISHED
                            │
                            ▼
                     ┌──────────────┐
                     │ Read SPPSTA  │
                     └──────┬───────┘
                            │
              ┌─────────────┼─────────────┐──────────────┐
              ▼             ▼             ▼              ▼
           INIT          BLOCKED      ERROR         WAITING/RUNNING
              │             │             │              │
         Wait & retry  Alert operator  Go to §7.3      Continue
              │         Wait & retry       │              │
              └──────►──────┘              │              │
                                           ▼              ▼
                                    ┌──────────────┐
                                    │ Read SPGGTP  │
                                    │ Compute delta│
                                    └──────┬───────┘
                                           │
                              ┌────────────┼────────────┐
                              ▼            ▼            ▼
                       delta < confirmed  delta == confirmed  delta > confirmed
                              │            │            │
                        CRITICAL ALERT  Clean state   Unrecorded prints
                        (Scenario 10)      │            │
                        Manual review      ▼            ▼
                                    ┌──────────────┐
                                    │ Read SPGGCP  │
                                    │ Read SPLGAT  │
                                    └──────┬───────┘
                                           │
                               ┌───────────┼───────────┐
                               ▼                       ▼
                        Template MATCHES         Template MISMATCH
                               │                       │
                    ┌──────────┼──────────┐      Scenario 8:
                    ▼                     ▼      Conservative abort
              SPGGCP consistent    SPGGCP == 0   Burn unrecorded codes
              with delta           (power cycle)
                    │                     │
              No power cycle        Power cycle
                    │                     │
              ┌─────┼─────┐         ┌─────┼─────┐
              ▼           ▼         ▼           ▼
          RUNNING      WAITING   delta<qty   delta==qty
              │           │         │           │
         Scenario 1  Scenario 2  Scenario 4  Scenario 7
         Resume poll  Complete   Offer       Complete
                      the job    Resume/     the job
                                 Abort
```

---

## 10. The Resume Procedure (Re-Upload Remaining Codes)

When the operator chooses to Resume a job after any disconnection scenario, the procedure must be airtight. This is essentially "re-preparing the remaining codes."

```
RESUME PROCEDURE
================

Prerequisites:
  - job.CodesConfirmed has been updated to reflect all prints (from SPGGTP reconciliation)
  - Printer is in WAITING state (not RUNNING, not ERROR)
  - remaining = job.Quantity - job.CodesConfirmed > 0

Step 1: Determine remaining codes
  - Query DB: all codes for this job still in Reserved status
  - These are the codes that haven't been printed yet
  - Count should == remaining (validation check)

Step 2: Delete old CSV from printer
  - SPLDDF{csv_filename}
  - Ignore FAIL (file may not exist after power cycle)

Step 3: Upload NEW CSV with ONLY remaining codes
  - Build CSV from the remaining Reserved codes (in order)
  - SPLCDF{csv_filename~gt~code1\ncode2\n...}
  - Require OK response
  - THIS IS CRITICAL: we upload ONLY unprinted codes. 
    If we uploaded the original full CSV, the printer would start from row 1
    and re-print already-printed codes = DUPLICATES.

Step 4: Verify upload
  - SPLGSD — confirm csv_filename appears in list

Step 5: Check template is still in storage
  - SPLGST — confirm template file is present
  - If missing: re-upload from disk (SPLRTF)

Step 6: Reload template
  - SPLLTF{template_name}
  - This resets SPGGCP to 0, reloads the data buffer from the new CSV
  - Row pointer starts at row 1 (which is now the first UNPRINTED code)

Step 7: Record new lifetime baseline
  - Read SPGGTP again AFTER template reload
  - Store as new TotalBaseline
  - This gives us a fresh anchor point for the resumed segment

Step 8: Set print quantity
  - SPPSLQ{remaining}

Step 9: Start printing
  - SPPSAP

Step 10: Spawn new JobExecutor
  - Poll loop resumes from counter 0 (SPGGCP was reset by SPLLTF)
  - Codes are marked printed in order from the remaining set
  - ProgressChanged events fire with (confirmed + current_counter, quantity)
```

**Why a fresh TotalBaseline?** After the resume, we have a new "session." The old baseline is stale (it covers prints from the original session plus possibly external prints). A fresh baseline after `SPLLTF` gives us a clean anchor for cross-checking the resumed segment.

**Why update CodesConfirmed from SPGGTP before resume?** Consider this timeline:

```
Original job: 500 codes
App confirmed: 342 (last poll before disconnect)
SPGGTP says:   348 (6 more printed after last poll)

If we resume without updating:
  - We'd re-upload codes 343-500 (158 codes)
  - But codes 343-348 were ALREADY PRINTED by the printer
  - The printer would print them again from the new CSV = DUPLICATES!

Correct approach:
  - Update confirmed to 348 (from SPGGTP)
  - Mark codes 343-348 as Printed in DB
  - Re-upload codes 349-500 (152 codes)
  - No duplicates possible
```

---

## 11. Edge Cases & Corner Cases

### 11.1 Rapid Reconnect/Disconnect Cycles

**Scenario:** Network is flapping — connecting and disconnecting every few seconds.

**Risk:** Each reconnect triggers the inspection procedure. If the inspection is interrupted by another disconnect mid-way, we might have partially updated state.

**Mitigation:**
- The inspection procedure should be atomic within the per-printer service lock.
- If any inspection command fails (IOException), abort the entire inspection and wait for the next reconnect cycle.
- Do NOT commit any state changes (code status updates) until the full inspection completes successfully.

### 11.2 Multiple Disconnections During the Same Job

**Scenario:** Job starts, disconnects, reconnects (resumes), disconnects again, reconnects again.

**Risk:** Each resume creates a new TotalBaseline. If the baselines overlap or are recorded incorrectly, counter math becomes wrong.

**Mitigation:**
- Each resume is a clean break. The old TotalBaseline is replaced with the new one.
- `CodesConfirmed` is cumulative and always reflects the true total of printed codes across all sessions.
- The poll loop always starts from 0 (SPGGCP reset by SPLLTF on resume).
- Math: `total_printed = old_confirmed + new_SPGGCP`

### 11.3 Very Long Disconnection (Hours/Days)

**Scenario:** The app is closed for days. The printer may have been used for completely different work by other systems/operators.

**Risk:** SPGGTP delta could be enormous, reflecting prints from many different jobs by different people.

**Detection:** SPLGAT (active template) will likely NOT match our template. This triggers Scenario 8 (template mismatch — conservative abort).

**Additional check:** If SPGGTP delta > quantity * 2, it's almost certainly external printing. Flag explicitly.

### 11.4 Printer Replaced at Same IP

**Scenario:** The physical printer is swapped out (different hardware, different serial number) but configured at the same IP address.

**Risk:** SPGGTP on the new printer is completely unrelated to our TotalBaseline. The delta computation produces garbage.

**Detection:**
- `SPGGSN` (serial number) will be different.
- SPGGTP delta will likely be < 0 (the new printer's total is probably lower) or wildly different.

**Mitigation:**
- Store the printer's serial number in the DB on first connect.
- On reconnect, compare serial numbers.
- If mismatch: CRITICAL ALERT — "Physical printer hardware has changed. Old serial: {X}, New serial: {Y}. All active jobs on this printer must be manually reviewed."

### 11.5 Power Failure Hits Both App AND Printer Simultaneously

**Scenario:** Factory-wide power outage. Both the computer and all printers lose power at the same time.

**Recovery sequence on power restore:**
1. Computer boots, app starts (or is manually started).
2. Printers boot — INIT state for 5-15 seconds, then WAITING.
3. App runs startup recovery: finds stale jobs in DB.
4. App connects to printers (may need to wait for INIT → WAITING).
5. For each stale Printing job: reads SPGGTP, computes delta.
6. Proceeds as Scenario 4/5/6/7 depending on the delta.

**This is the worst-case scenario and it's fully handled by SPGGTP.** The lifetime counter is the single source of truth that bridges the gap across simultaneous failures.

### 11.6 Code at the Boundary of a Disconnect

**The "Schrodinger's Code" problem:**

```
Poll reads SPGGCP = 342 → App confirms codes 1-342 as Printed
...printer physically prints code 343...
...SPGGTP increments to TotalBaseline + 343...
...SPGGCP increments to 343...
...but the next poll never runs because: DISCONNECT

Question: Was code 343 printed?

Answer from SPGGTP: YES (delta = 343)
Answer from App DB: NO (confirmed = 342)
```

**Resolution:** On reconnect, SPGGTP tells us code 343 was printed. We mark it as Printed.

**But what if the disconnect was a power failure and SPGGTP didn't increment?**

The printer's counter update cycle is:
1. Print head fires (code is physically on the product)
2. Printer firmware increments SPGGCP (RAM)
3. Printer firmware increments SPGGTP (flash/EEPROM)

If power is lost between steps 1 and 3, the code is physically printed but SPGGTP doesn't reflect it. This is the "Schrodinger's Code" — it exists on a physical product but isn't counted.

**This is why we quarantine +1 on any power cycle scenario.** The code at position `delta` *might* have been physically printed even though SPGGTP doesn't count it. Quarantining it ensures we never auto-reuse it — but unlike the old "burn" approach, the operator can recover it if they verify it wasn't actually printed.

```
Safe approach:
  - SPGGTP delta = 342 after power cycle
  - Mark codes 1-342 as Printed (certain)
  - Quarantine code 343 (uncertain — might be on a product)
  - Resume from code 344
  - Operator later checks production line:
    - Code 343 on a product? → Admin: move to Printed
    - Code 343 NOT on a product? → Admin: move to Available (recovered!)
    - Can't tell? → Admin: move to Burned (permanent, same as old behavior)
```

### 11.7 App Crash During Resume Procedure

**Scenario:** The app crashes while executing the Resume Procedure (Section 10) — e.g., after uploading the new CSV but before starting to print.

**State:**
- Job is still in Printing or Paused status (we didn't update it yet, or it was updated to Printing)
- A new CSV with remaining codes has been uploaded to the printer
- Template may or may not have been reloaded

**Recovery:** On next startup, this is just another stale Printing/Paused job. The startup recovery runs the inspection procedure again. SPGGTP is still valid. The new CSV may be on the printer (harmless — it will be re-uploaded on the next resume attempt).

**Key insight:** The Resume Procedure is idempotent. Running it twice is safe because:
- SPLDDF deletes the old CSV (even if it's the one we just uploaded)
- SPLCDF creates a fresh one
- SPLLTF reloads and resets everything
- New TotalBaseline is recorded

### 11.8 Counter Overflow

**Scenario:** SPGGTP is a 32-bit integer. At 458,200 prints (from the SPPL docs example), overflow is not imminent. But a high-volume printer running continuously for years could theoretically reach max value.

**Detection:** If `SPGGTP_now < job.TotalBaseline`, and the delta is negative but `TotalBaseline` was very close to INT_MAX, this might be overflow rather than a hardware swap.

**Likelihood:** Extremely low. A printer doing 10,000 prints/day would take 588 years to reach 2^31 = 2,147,483,648. Not a practical concern.

---

## 12. Current Implementation vs Recommended Implementation

### What the current codebase does

| Area | Current Implementation | Reference |
|------|----------------------|-----------|
| **Poll loop disconnect** | `IOException` caught, alert raised, waits 2s, retries. But just retries the same poll command — doesn't run a full inspection procedure. | `JobExecutor.PollLoopAsync` |
| **Reconnection** | `PrinterConnectionManager` detects `IsConnected == false`, runs exponential backoff reconnect. On success, raises `PrinterStatusChanged` event. | `PrinterConnectionManager.StartReconnectLoop` |
| **Startup recovery** | Finds stale jobs, auto-cancels Preparing/Ready, reads SPGGTP for Printing jobs, shows Recovery Dialog with Resume/Abort. | `App.xaml.cs:RunStartupRecoveryAsync` |
| **Resume** | Reads remaining count, sets quantity, starts printer, spawns new executor. **Does NOT re-upload CSV or reload template.** | `PrintJobService.ResumeJobAsync` |
| **Template match check** | Not done on reconnect. Only done during Verify flow (manual action). | `PrintersViewModel.VerifyPrinterAsync` |
| **Serial number tracking** | Not implemented. No hardware swap detection. | — |
| **Burn on power cycle** | Burn +1 is done on Cancel, but the recovery flow lets the operator choose Resume or Abort — it doesn't force a burn on the boundary code for Resume. | `PrintJobService.CancelJobAsync` |
| **Quarantine status** | **Implemented.** `CodeStatus.Quarantined` added to the enum. The Codes tab on the Products page allows operators to inspect quarantined codes and resolve them (move to Available, Printed, or Burned) individually or in bulk. Quarantined codes are excluded from availability counts and cannot be auto-reused. | `Domain/Enums/CodeStatus.cs`, `CodesTabViewModel.cs` |

### Gaps requiring attention

**Gap 1: Resume does not re-upload CSV**

This is the most critical gap. The current `ResumeJobAsync` calls `adapter.SetPrintQuantityAsync(remaining)` and `adapter.StartPrintAsync()`, but does NOT:
- Delete the old CSV
- Upload a new CSV with only remaining codes
- Reload the template (to reset the data buffer and row pointer)

**If the printer was power-cycled, the data buffer is lost.** Resuming without re-uploading the CSV means the printer will either:
- Print from whatever data is in the buffer (undefined behavior), or
- Use the stored CSV from the beginning (row pointer reset → DUPLICATES)

**Fix:** `ResumeJobAsync` must follow the full Resume Procedure (Section 9).

**Gap 2: No post-reconnect inspection during live jobs**

When the poll loop reconnects after an `IOException`, it simply retries `GetCurrentCounterAsync`. It doesn't:
- Check SPPSTA for errors
- Check SPLGAT for template mismatch
- Check if a power cycle occurred (SPGGCP == 0 check)
- Read SPGGTP for reconciliation

**Fix:** After any `IOException` + successful reconnect, the poll loop should run a mini-inspection:
1. Read SPPSTA
2. Read SPGGTP, compute delta
3. Read SPGGCP — if 0 and delta > confirmed, flag power cycle
4. Read SPLGAT — if mismatch, escalate

**Gap 3: No serial number tracking**

Hardware swaps at the same IP are undetectable. SPGGTP delta will produce meaningless results.

**Fix:** Store serial number (`SPGGSN`) on first connect. Compare on every reconnect.

**Gap 4: Recovery dialog doesn't show enough detail**

The current Recovery Dialog shows: Job #, Product, Printer, App Says, Printer Says, Delta. But it doesn't show:
- Whether the printer was power-cycled
- Whether the template still matches
- Whether the CSV is still present
- Recommended action explanation

**Fix:** Add inspection results to each `RecoveryItem`.

---

## 13. Summary: Safety Invariants

These rules must NEVER be violated, regardless of the scenario:

| # | Invariant | Why |
|---|-----------|-----|
| 1 | **A code marked Printed never returns to Available.** | Prevents duplicates. Period. Only an operator can mark a code Printed (via Admin), and that's irreversible. |
| 2 | **A Quarantined code never auto-returns to Available.** | Only explicit operator action in the Admin page can move it. The system cannot automatically reuse quarantined codes. |
| 3 | **SPGGTP is the single source of truth for print count across power cycles.** | It's the only counter that survives everything. |
| 4 | **After ANY power cycle or template reload, the CSV must be re-uploaded with ONLY remaining codes before resuming.** | The data buffer and row pointer are lost. Re-using the old CSV starts from row 1 = duplicates. |
| 5 | **Quarantine +1 on any ambiguous boundary.** | The code at position `delta` might have been physically printed but not counted. Quarantine lets the operator recover it if verified safe. |
| 6 | **Never auto-resume printing after a disconnect.** | Always reconcile state first. Operator confirmation is required for power-cycle scenarios. |
| 7 | **Template mismatch means conservative abort + quarantine.** | If someone loaded a different template, we can't trust that the SPGGTP delta represents our codes. Quarantine all uncertain codes for operator review. |
| 8 | **Counter going backward means stop everything, quarantine, and alert.** | This indicates hardware swap, counter reset, or corruption. No automated response is safe. |
| 9 | **The inspection procedure must complete atomically.** | Don't commit partial state changes. If any inspection command fails, abort and retry from scratch. |
| 10 | **Quarantined codes are excluded from the Available pool count.** | When creating new jobs, the system counts only Available codes. Quarantined codes are invisible to job creation. |
