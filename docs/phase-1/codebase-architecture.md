# Code Print Manager — Codebase Architecture

> This document describes the implemented code layout, the reasoning behind every structural decision, and how the architecture supports planned future features. It is intended as a ramp-up guide for new engineers and AI agents working on the project.

---

## 1. Solution Overview

The application lives in `application/CodePrintManager.sln` — a multi-project .NET 8 solution organized into five solution folders that mirror the disk layout.

```
application/
  CodePrintManager.sln
  src/
    Core/
      CodePrintManager.Domain/              # Pure domain — zero NuGet dependencies
      CodePrintManager.Application/         # Business logic / orchestration
    Infrastructure/
      CodePrintManager.Data/                # EF Core + SQLite persistence
    Printers/
      CodePrintManager.Printer.Savema/      # Savema TTO adapter (SPPL protocol)
    Hosts/
      CodePrintManager.Desktop/             # WPF shell (thin host)
  tests/
    CodePrintManager.Domain.Tests/
    CodePrintManager.Data.Tests/
    CodePrintManager.Printer.Savema.Tests/
    CodePrintManager.Application.Tests/
  tools/
    PrinterTestHarness/                     # Interactive console for adapter engineers
```

**Total:** 10 projects (5 source, 4 test, 1 tool), ~50 source files.

---

## 2. Dependency Graph

```
   Domain  (zero deps)
   ↗      ↖
Data    Printer.Savema      ← both only reference Domain
  ↖      ↗
Application                 ← orchestrates both; no UI dependencies
     ↑
  Desktop                   ← thin WPF host; wires DI, adapts events to UI
```

| Project | References | NuGet Packages |
|---------|-----------|----------------|
| **Domain** | (none) | (none) |
| **Data** | Domain | EF Core SQLite, EF Core Design |
| **Printer.Savema** | Domain | Logging.Abstractions |
| **Application** | Domain, Data | DI Abstractions, Logging Abstractions, Serilog |
| **Desktop** | Application, Printer.Savema | CommunityToolkit.Mvvm, Hosting, Serilog.Extensions.Hosting |
| **PrinterTestHarness** | Printer.Savema | Logging, Logging.Console |

Key rules enforced by the project references:

- **Domain has zero dependencies.** All interfaces, entities, enums, and event DTOs live here. Any project in the solution can reference Domain without pulling in EF Core, Serilog, or WPF.
- **Application has zero UI dependencies.** No `ObservableCollection`, no `Dispatcher`, no `IProgress<T>`. Services communicate via plain C# events with DTO payloads. This is critical — see §7.
- **Printer projects only reference Domain.** A printer engineer never needs to open or build the Application, Data, or Desktop projects.

---

## 3. Project-by-Project Breakdown

### 3.1 CodePrintManager.Domain

The heart of the system. Contains everything that every other project needs to agree on, and nothing else.

```
Domain/
  Entities/
    ProductNode.cs      # Tree node: folders (IsLeaf=false) and products (IsLeaf=true)
    Code.cs             # A single unique code. Status: Available → Reserved → Printed/Returned/Burned
    Printer.cs          # Printer config record: name, IP, port, adapter type string
    PrintJob.cs         # Job lifecycle: Preparing → Ready → Printing → Completed/Cancelled/Error
    AuditEntry.cs       # Persistent event log row
  Enums/
    CodeStatus.cs       # Available, Reserved, Printed, Returned, Burned
    JobStatus.cs        # Preparing, Ready, Printing, Completed, Cancelled, Error
    PrinterStatus.cs    # Offline, Init, Idle, Printing, Error, Blocked
    AlertSeverity.cs    # Info, Warning, Error
  Interfaces/
    IPrinterAdapter.cs          # THE contract every printer implementation fulfills
    IPrinterAdapterFactory.cs   # Creates an adapter given an adapter type string
    IProductService.cs          # CRUD for product tree
    ICodePoolService.cs         # CSV import, reservation, status transitions, dedup
    IPrintJobService.cs         # Job lifecycle management
    IAlertService.cs            # Ephemeral alert events (not persistent)
    IAuditService.cs            # Persistent event logging
    ICurrentUser.cs             # Auth hook (returns "local" for now)
  Events/
    JobProgressChangedEvent.cs      # { JobId, Confirmed, Total }
    JobCompletedEvent.cs            # { JobId, Status }
    PrinterStatusChangedEvent.cs    # { PrinterId, OldStatus, NewStatus }
    AlertRaisedEvent.cs             # { Id, Timestamp, Severity, Source, Message, PrinterId?, JobId? }
```

**Why Events/ as DTOs in Domain?** Application services raise events; hosts consume them. WPF dispatches to the UI thread. A future web host would push them over SignalR. The Application layer never knows which host is listening. Events are plain `record` types — no delegates, no framework types.

**Why ICurrentUser?** It's a no-op placeholder today (returns "local operator"). When authentication arrives, each host implements it differently: WPF could use Windows identity or a login dialog; a web host would use ASP.NET Core `ClaimsPrincipal`. Services call `ICurrentUser.HasPermission(...)` uniformly.

### 3.2 CodePrintManager.Data

EF Core persistence. Owns the `AppDbContext`, entity configurations, and migrations.

```
Data/
  AppDbContext.cs                  # DbContext with DbSet<T> properties
  DbInitializer.cs                # PRAGMA journal_mode=WAL; busy_timeout=5000
  Configurations/
    ProductNodeConfiguration.cs    # Self-referencing parent/child relationship
    CodeConfiguration.cs           # UNIQUE(code_text), index on (product_id, status)
    PrinterConfiguration.cs
    PrintJobConfiguration.cs       # Partial unique indexes (see below)
    AuditEntryConfiguration.cs
  Migrations/                      # EF Core auto-generated
```

**Critical constraint — partial unique indexes** (in `PrintJobConfiguration.cs`):

```csharp
// Only one active job per printer at a time
entity.HasIndex(j => j.PrinterId)
    .HasFilter("[Status] IN ('Preparing','Ready','Printing')")
    .IsUnique();

// Only one active job per product at a time
entity.HasIndex(j => j.ProductId)
    .HasFilter("[Status] IN ('Preparing','Ready','Printing')")
    .IsUnique();
```

These are the database-level safety net. Even if application-level locking has a bug, the database prevents two jobs from running on the same printer or reserving codes from the same product simultaneously.

**WAL mode** is set in `DbInitializer` because multiple `JobExecutor` instances poll and write concurrently. WAL allows readers to not block writers.

### 3.3 CodePrintManager.Printer.Savema

The Savema TTO printer adapter. Implements `IPrinterAdapter` over TCP/IP using the SPPL protocol.

```
Printer.Savema/
  SavemaTtoAdapter.cs       # Full IPrinterAdapter implementation: TCP + SemaphoreSlim lock
  SavemaAdapterFactory.cs   # IPrinterAdapterFactory: maps "savema*" → SavemaTtoAdapter
  Protocol/
    SpplCommandBuilder.cs   # Builds SPPL command strings (e.g., ~SPPSAP^, ~SPLCDF{file~gt~data}^)
    SpplResponseParser.cs   # Parses ~SPGRES{...}^ responses into SpplResponse objects
    SpplConstants.cs        # Delimiters (~, ^, ~gt~, ~sc~), forbidden sequences, timeouts
    SpplResponse.cs         # Parsed response value object: IsOk, IsFail, Payload, AsInt(), AsList()
```

**Why Protocol/ is separated from the adapter:**

- `SpplCommandBuilder` and `SpplResponseParser` are pure functions — input in, output out, no side effects. They are independently testable without any TCP mock.
- `SavemaTtoAdapter` handles the TCP lifecycle and the per-adapter `SemaphoreSlim` lock. Every `SendCommandAsync` call acquires the lock, sends bytes, reads the response, and releases. This serializes all communication to a single printer and prevents interleaved SPPL commands.

**Adapter locking pattern:**

```
SendCommandAsync(cmd, ct):
    await _lock.WaitAsync(ct)       ← blocks if another command is in-flight
    try:
        write cmd bytes to TCP stream
        read response until '^' terminator
        return parsed SpplResponse
    finally:
        _lock.Release()
```

This is the lower of the two-level locking described in `multi-printer-concurrency.md`. The upper level is the per-printer `SemaphoreSlim` in `PrintJobService`.

**Multiple Savema models:** If future Savema models (SVM 53, SVM 107) have firmware quirks, create subclasses of a shared `BaseSavemaAdapter` within this same project. They share the SPPL protocol; only specific command behavior differs. Truly different printer brands (Domino, Videojet) get their own `Printer.X` project.

### 3.4 CodePrintManager.Application

All business logic. Orchestrates Domain interfaces, Data access, and printer adapters.

```
Application/
  Services/
    ProductService.cs              # CRUD product tree, folder/product creation
    CodePoolService.cs             # CSV import, code reservation (FIFO), dedup, status transitions
    PrinterConnectionManager.cs    # Singleton: owns adapter instances, connect/reconnect with backoff
    PrintJobService.cs             # Job lifecycle, per-printer service locks, spawns JobExecutors
    JobExecutor.cs                 # Per-job: 500ms poll loop, counter tracking, anomaly detection
    AlertService.cs                # Event-based alerts, auto-dismiss info after 30s, bridges to audit
    AuditService.cs                # Persists events to audit_log table
  Models/
    PollSnapshot.cs                # Counter + optional lifetime delta (value object)
    RecoveryItem.cs                # Stale job + discrepancy info for recovery dialog
    CsvImportResult.cs             # Import outcome (count, duplicates, errors)
  ServiceCollectionExtensions.cs   # AddCodePrintManager(dbPath) — registers everything
```

**Key service patterns:**

| Service | Lifetime | Why |
|---------|----------|-----|
| `PrinterConnectionManager` | Singleton | Owns long-lived TCP connections and adapter instances across the app lifetime |
| `AlertService` | Singleton | Event hub — all services raise alerts through one instance; hosts subscribe once |
| `ProductService` | Scoped | One DbContext per operation; stateless |
| `CodePoolService` | Scoped | Same |
| `PrintJobService` | Scoped | Same, but also owns `ConcurrentDictionary<int, JobExecutor>` for active jobs |
| `AuditService` | Scoped | Writes to DB |

**PrinterConnectionManager** uses the factory pattern:

```csharp
// Never references Printer.Savema directly — uses IPrinterAdapterFactory via DI
var factory = _factories.FirstOrDefault(f => f.CanHandle(adapterType))
    ?? throw new InvalidOperationException($"No adapter factory for type '{adapterType}'");
return factory.Create(adapterType);
```

Multiple `IPrinterAdapterFactory` implementations can be registered in DI. The manager iterates through them to find one that `CanHandle` the adapter type string stored in the `Printer` entity.

**Reconnection:** When a connection fails, `PrinterConnectionManager.StartReconnectLoop` runs a background task with exponential backoff (1s, 2s, 4s, ... capped at 30s). On reconnect, it raises `PrinterStatusChanged`.

**JobExecutor polling loop:**

```
every 500ms:
    read SPGGCP (current counter)
    every 5th tick: cross-check SPGGTP (lifetime counter) delta
    if counter > confirmed: commit progress to DB, raise ProgressChanged
    if counter >= quantity: complete job, raise Completed
    if connection lost: raise alert, wait 2s, retry
```

**AlertService** is event-driven, not collection-driven. It raises `AlertRaised` events with DTO payloads. The WPF host subscribes to these events and manages its own `ObservableCollection` on the UI thread. A future web host would push them via SignalR. Info-severity alerts auto-dismiss after 30 seconds.

### 3.5 CodePrintManager.Desktop

The thin WPF host. Its only job is to wire DI, subscribe to Application events, and present the UI.

```
Desktop/
  App.xaml / App.xaml.cs           # DI/Host setup, Serilog config, DB init, startup
  appsettings.json                 # Configurable: poll interval, reconnect backoff, thresholds
  MainWindow.xaml / .xaml.cs       # Shell: sidebar nav + content area + alert bar
  ViewModels/
    MainViewModel.cs               # Navigation state, alert collection (subscribes to AlertRaised)
    DashboardViewModel.cs          # Printer cards, summary stats
    ProductsViewModel.cs           # Tree + detail + CSV import
    PrintersViewModel.cs           # Config + connect/disconnect
    JobsViewModel.cs               # Active jobs + history tabs
    NewJobViewModel.cs             # Create → Prepare → Start flow
    RecoveryViewModel.cs           # Startup recovery for stale jobs
    Components/
      PrinterCardViewModel.cs      # Per-printer dashboard card
  Views/
    DashboardView.xaml             # Summary cards + printer card grid
    ProductsView.xaml              # Tree view + detail pane
    PrintersView.xaml              # Add printer form + action buttons
    JobsView.xaml                  # DataGrid with active/history tabs
    NewJobView.xaml                # Product/printer selection, quantity, start
    RecoveryDialog.xaml            # Modal dialog for stale job resolution
  Converters/
    BoolToVisibilityConverter.cs   # + InverseBoolConverter, NullToVisibility, CountToVisibility
    StatusToColorConverter.cs      # Maps PrinterStatus/JobStatus → SolidColorBrush
  Assets/
    Styles/
      Theme.xaml                   # Colors, converter registrations, global TextBlock style
      Controls.xaml                # NavButton, LinkButton, SummaryCard, PrinterCard styles
```

**Event-to-UI bridging** happens directly in ViewModels:

```csharp
// MainViewModel subscribes to Application event, marshals to UI thread
_alertService.AlertRaised += (_, e) =>
    Application.Current.Dispatcher.Invoke(() => Alerts.Insert(0, new AlertItemViewModel(e)));
```

There is no separate "Bridge" layer — ViewModels subscribe in their constructors and update their own `[ObservableProperty]` fields. If multiple ViewModels needed the same complex wiring, a bridge class would be extracted, but that's not the case today.

**Known namespace issue:** The `CodePrintManager.Printer.Savema` namespace creates a collision with the `Printer` entity class. Desktop ViewModels use `using PrinterEntity = CodePrintManager.Domain.Entities.Printer;` to disambiguate.

### 3.6 PrinterTestHarness

An interactive console app for adapter engineers to test against real printer hardware without running the full application.

```
> connect 192.168.1.10 9100
Connected to 192.168.1.10:9100
> status
Printer status: Idle
> counters
Current counter (SPGGCP): 0
Lifetime counter (SPGGTP): 45200
> upload-csv test.csv CODE001,CODE002,CODE003
Uploaded test.csv (3 codes)
> start
Print started
> poll
Counter: 3
```

It only references `Domain` and `Printer.Savema`. An engineer can clone the repo, open only these three projects, and validate adapter behavior months before the full application is complete.

---

## 4. Data Flow: How a Print Job Executes

```
User clicks "Start Job" in UI
        │
        ▼
NewJobViewModel.StartJobAsync()
        │
        ▼
IPrintJobService.CreateJobAsync(productId, printerId, quantity)
    ├── Creates PrintJob entity (status: Preparing)
    ├── Acquires per-printer SemaphoreSlim
    └── Returns PrintJob
        │
        ▼
IPrintJobService.PrepareJobAsync(jobId)
    ├── ICodePoolService.ReserveCodesAsync() — reserves N codes (FIFO by import_order)
    ├── IPrinterAdapter.UploadCsvAsync() — sends codes to printer
    ├── IPrinterAdapter.ActivateTemplateAsync() — selects the template
    ├── IPrinterAdapter.SetPrintQuantityAsync() — sets count
    ├── IPrinterAdapter.GetTotalCounterAsync() — records SPGGTP baseline
    ├── Updates job status → Ready
    └── Saves to DB
        │
        ▼
IPrintJobService.StartJobAsync(jobId)
    ├── IPrinterAdapter.StartPrintAsync() — sends ~SPPSAP^
    ├── Updates job status → Printing
    ├── Spawns JobExecutor on a background Task
    └── Releases per-printer lock
        │
        ▼
JobExecutor.PollLoopAsync() (runs every 500ms)
    ├── IPrinterAdapter.GetCurrentCounterAsync() — reads SPGGCP
    ├── Every 5th tick: cross-check SPGGTP delta
    ├── DetectAnomalies() — counter jumps, lifetime mismatch
    ├── CommitProgressAsync() — marks codes as Printed in DB
    ├── Raises ProgressChanged event → UI updates progress bar
    └── When counter >= quantity: CompleteJobAsync() → status Completed
```

---

## 5. Code Lifecycle State Machine

```
    ┌──────────────────────────────────────────────────────┐
    │                                                      │
    ▼                                                      │
Available ──→ Reserved ──→ Printed                         │
                 │                                         │
                 ├──→ Returned ──→ Available ───────────────┘
                 │
                 └──→ Burned (ambiguous / lost)
```

| Transition | When | Who |
|-----------|------|-----|
| Available → Reserved | Job preparation (FIFO by `import_order`) | `CodePoolService.ReserveCodesAsync` |
| Reserved → Printed | Counter confirms print | `CodePoolService.MarkCodesPrintedAsync` |
| Reserved → Returned | Job cancelled, codes not yet printed | `CodePoolService.ReturnCodesToPoolAsync` |
| Reserved → Burned | Ambiguous state (counter mismatch) | `CodePoolService.BurnCodeAsync` |
| Returned → Available | Automatic (returned codes re-enter the pool) | Same as above |

**Burn-on-ambiguity rule:** If a code's print status is uncertain (e.g., printer disconnected mid-print, counter doesn't match), the code is burned rather than returned. Government-issued codes must never be duplicated; wasting a code is acceptable, using it twice is not.

---

## 6. What Each Engineer Needs

| Role | Projects to check out | Can ignore |
|------|----------------------|------------|
| **Savema adapter engineer** | Domain + Printer.Savema + Printer.Savema.Tests + PrinterTestHarness | Data, Application, Desktop |
| **New printer brand engineer** | Domain + their new Printer.X project | Everything else |
| **Application/service developer** | Domain + Data + Application + their tests | Printer.Savema internals |
| **UI developer** | All of above + Desktop | Printer.Savema internals |

A Savema engineer's build:
```bash
dotnet build src/Core/CodePrintManager.Domain
dotnet build src/Printers/CodePrintManager.Printer.Savema
dotnet test tests/CodePrintManager.Printer.Savema.Tests
dotnet run --project tools/PrinterTestHarness
```

No EF Core, no WPF, no 30+ NuGet packages. Just Domain + TCP + protocol logic.

---

## 7. Future Feature Support

The architecture was explicitly designed for several planned additions. This section explains **what** is planned, **where** it would go, and **what existing code enables it**.

### 7.1 Authentication & Login

**What:** User login, role-based permissions, audit trail per user.

**Where it lands:**
- `ICurrentUser` in Domain already exists as the hook. Today it returns "local operator" unconditionally.
- Desktop host: implement `ICurrentUser` with a login dialog or Windows identity.
- Web host: implement `ICurrentUser` with ASP.NET Core `ClaimsPrincipal`.
- Services already have access to `ICurrentUser` via DI — just add permission checks.

**What to build:**
- A `LocalCurrentUser` implementation in Desktop (or a `LoginWindow` + stored session).
- Permission checks in service methods: `if (!_user.HasPermission("job:create")) throw ...`
- No schema changes needed if permissions are hard-coded by role. If stored in DB, add a `User` entity and migration.

### 7.2 E-Mark API Integration (Remote Code Download)

**What:** Instead of importing CSV files locally, download codes from the E-Mark government API.

**Where it lands:**
- `ICodePoolService.ImportCodesAsync(int productId, string batchName, IReadOnlyList<string> codes)` already accepts codes as a list of strings — it doesn't care where they came from.
- Add a new service (e.g., `EMarkApiClient`) in Application that fetches codes from the API and calls `ImportCodesAsync`.
- Alternatively, add a new project `CodePrintManager.Integration.EMark` if the API client is complex.

**What changes:**
- UI: Replace or augment the "Import CSV" button with "Download from E-Mark".
- New service: `EMarkApiClient` with credentials, HTTP calls, response parsing.
- `ImportCodesAsync` stays unchanged — it already handles dedup and validation.

### 7.3 Host Swap: Desktop → Service + Web UI

**What:** Replace the WPF desktop with a Windows Service backend + browser-based UI.

**Why the architecture supports it:** The Application layer has zero WPF dependencies. Services communicate via events, not `ObservableCollection` or `Dispatcher`. The entire business logic is hostable in any .NET process.

**What to build:**

```
src/
  Hosts/
    CodePrintManager.Desktop/          ← existing, keep for local use
    CodePrintManager.Host.Web/         ← NEW: ASP.NET Core + SignalR
      Program.cs                       ← services.AddCodePrintManager() + adapter factories
      Hubs/
        DashboardHub.cs                ← subscribes to Application events, pushes to clients
        JobHub.cs
      Controllers/
        ProductsController.cs
        PrintersController.cs
        JobsController.cs
      wwwroot/ or separate frontend
```

The key wiring in `Program.cs`:
```csharp
builder.Services.AddCodePrintManager(dbPath);                       // same call as Desktop
builder.Services.AddSingleton<IPrinterAdapterFactory, SavemaAdapterFactory>(); // same

// Subscribe to events → push to SignalR
var alertService = app.Services.GetRequiredService<IAlertService>();
alertService.AlertRaised += (_, e) => hubContext.Clients.All.SendAsync("alertRaised", e);
```

**Zero changes** to Domain, Data, Application, or Printer.Savema. Just a new host project.

### 7.4 Additional Printer Brands

**What:** Support Domino, Videojet, or other TTO printer brands.

**What to build:**

```
src/
  Printers/
    CodePrintManager.Printer.Domino/
      CodePrintManager.Printer.Domino.csproj    ← references Domain only
      DominoAdapter.cs                           ← implements IPrinterAdapter
      DominoAdapterFactory.cs                    ← implements IPrinterAdapterFactory
      Protocol/
        DominoProtocol.cs                        ← brand-specific protocol
```

**Registration:** One line in the host's DI setup:
```csharp
services.AddSingleton<IPrinterAdapterFactory, DominoAdapterFactory>();
```

`PrinterConnectionManager` iterates all registered `IPrinterAdapterFactory` instances and calls `CanHandle(adapterType)`. No switch statements, no Application-layer changes.

### 7.5 Scanner / Verification (Phase 2)

**What:** HikRobot camera reads printed codes to verify they match what was sent.

**Where it lands:**
- New project: `CodePrintManager.Scanner.HikRobot`
- New interface in Domain: `IScannerAdapter`
- New service in Application: `VerificationService` that correlates scanned codes with reserved codes
- New code status transition: Reserved/Printed → Verified

### 7.6 Aggregation (Later Phase)

**What:** Group printed codes into boxes/pallets with parent codes.

**Where it lands:**
- New entities in Domain: `AggregationLevel`, `AggregationUnit`
- New service in Application: `AggregationService`
- New UI views in Desktop or Web host

---

## 8. Build & Test Quick Reference

All commands run from `application/`:

```bash
# Build everything
dotnet build

# Run all tests
dotnet test

# Run specific test project
dotnet test tests/CodePrintManager.Printer.Savema.Tests

# Run the WPF app
dotnet run --project src/Hosts/CodePrintManager.Desktop

# Run the printer test harness
dotnet run --project tools/PrinterTestHarness

# Publish self-contained for deployment
dotnet publish src/Hosts/CodePrintManager.Desktop -c Release -r win-x64 --self-contained -o publish/
```

---

## 9. Known Gotchas

| Issue | Workaround |
|-------|------------|
| `Printer` entity vs `CodePrintManager.Printer` namespace collision | Use `using PrinterEntity = CodePrintManager.Domain.Entities.Printer;` in Desktop ViewModels |
| `CsvImportResult` exists in both Domain (interfaces file) and Application.Models | Domain's version is the canonical one; Application.Models copy is unused and can be removed |
| EF Core migrations not yet created | Run `dotnet ef migrations add InitialCreate -p src/Infrastructure/CodePrintManager.Data -s src/Hosts/CodePrintManager.Desktop` to generate the first migration |
| Test projects have placeholder `UnitTest1.cs` | Replace with real tests before implementation work begins |
| `RecoveryItem` model defined but unused | Will be used when the startup recovery flow is fully implemented |
