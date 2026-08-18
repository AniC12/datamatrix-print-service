# Code Print Manager — Agent Reference

## Project Purpose

Windows desktop application for managing the printing of unique government-issued product codes (Data Matrix / QR) on Savema thermal printers. Each code is used exactly once. Core goals:

- Accurate tracking of every code state transition
- Duplicate prevention
- Waste minimization

## Key Documentation

- `docs/phase-1/client-overview.md` — Product overview, screens, safety guarantees
- `docs/phase-1/phase1-design.md` — Full technical design: data model, SPPL protocol, adapter interface, UI screens, architecture
- `docs/phase-1/multi-printer-concurrency.md` — Concurrency model: two-level locking, job execution, SQLite WAL, UI architecture
- `docs/phase-1/codebase-architecture.md` — **Code layout, project structure, data flow, future feature support** (start here for ramp-up)
- `docs/phase-1/implementation-plan.md` — **Task breakdown: 32 stories, dependencies, parallelization, story points**
- `docs/savema_language_-_rev12.md` — Full SPPL protocol reference (265 KB)

## Prescribed Tech Stack

From `docs/phase-1/phase1-design.md` §8:

- **Language:** C# / .NET 8
- **UI:** WPF + MVVM (CommunityToolkit.Mvvm)
- **Database:** SQLite via EF Core (WAL mode)
- **Logging:** Serilog → file
- **DI:** Microsoft.Extensions.DependencyInjection
- **Deployment:** Self-contained single-folder publish

## Core Architecture

- Product tree hierarchy; code pools with states: available → reserved → printed / returned / burned
- Savema TTO printers via TCP/IP (port 9100) using the SPPL protocol
- `IPrinterAdapter` interface with `SavemaTtoAdapter` implementation
- Two-level locking: per-printer service lock (`SemaphoreSlim`) + per-adapter lock
- `JobExecutor` per running job with a 500 ms polling loop
- `PrinterConnectionManager` singleton with exponential-backoff reconnection
- SQLite partial unique indexes enforce one active job per printer/product
- `AlertService` (ephemeral UI) + `AuditService` (persistent DB)

## Phase 1 Scope

- Import CSV codes
- Organize products in a tree
- Manage multiple printers
- Print jobs with progress tracking

### Out of Scope for Phase 1

Scanner / verification, recycler, E-Mark API, aggregation, authentication, cloud.

## Project Layout

- `application/` — Application source code (multi-project .NET solution)
- `demo/` — Savema simulator, sample data, dummy templates for development
- `docs/` — Project documentation

### Solution Structure (`application/CodePrintManager.sln`)

```
src/
  Core/
    CodePrintManager.Domain          — Pure domain: entities, enums, interfaces, events. Zero dependencies.
    CodePrintManager.Application     — Business logic / orchestration. References Domain + Data.
  Infrastructure/
    CodePrintManager.Data            — EF Core + SQLite. References Domain.
  Printers/
    CodePrintManager.Printer.Savema  — Savema TTO adapter. Only references Domain.
    CodePrintManager.Printer.Mock    — In-memory mock adapter for testing without hardware.
  Hosts/
    CodePrintManager.Desktop         — WPF app. References Application + Printer.Savema.
    CodePrintManager.TestHost        — ASP.NET Core minimal API host for integration tests.
tests/
  CodePrintManager.Domain.Tests
  CodePrintManager.Data.Tests
  CodePrintManager.Printer.Savema.Tests
  CodePrintManager.Application.Tests
  CodePrintManager.Integration.Tests — End-to-end tests via TestHost + MockPrinterAdapter.
tools/
  PrinterTestHarness                 — Interactive console for adapter engineers.
```

### Dependency Graph

```
Domain (zero deps)  ←  Data           ←  Application  ←  Desktop
                    ←  Printer.Savema                  ←  PrinterTestHarness
                    ←  Printer.Mock                    ←  TestHost
```

## Running the Application

**In Windsurf / VS Code:**
1. Open `application/` folder (or the `.sln` file)
2. Press `F5` or click Run → Desktop app launches automatically

**In Visual Studio:**
1. Open `application/CodePrintManager.sln`
2. Right-click `CodePrintManager.Desktop` → "Set as Startup Project"
3. Press `F5`

**From command line:**
```bash
cd application/
dotnet run --project src/Hosts/CodePrintManager.Desktop
```

The app will create `codeprintmanager.db` and `logs/` in the output directory on first run.

**With mock printer (no hardware needed):**
```bash
cd application/
dotnet run --project src/Hosts/CodePrintManager.Desktop -- --mock
```

**With Savema simulator (tests full TCP path):**
```bash
# Terminal 1: start the simulator
python demo/savema_simulator.py --port 9100

# Terminal 2: start the app (add printer pointing to 127.0.0.1:9100)
cd application/
dotnet run --project src/Hosts/CodePrintManager.Desktop
```

## Build & Test Commands

```bash
# From application/ directory:
dotnet build                    # Build entire solution
dotnet test                     # Run all tests
dotnet test --filter "FullyQualifiedName~Savema"   # Run only Savema adapter tests

# Run the printer test harness:
dotnet run --project tools/PrinterTestHarness

# Publish self-contained:
dotnet publish src/Hosts/CodePrintManager.Desktop -c Release -r win-x64 --self-contained -o publish/
```

## Key Conventions

- **Namespace collision**: The `CodePrintManager.Printer.Savema` namespace conflicts with `CodePrintManager.Domain.Entities.Printer`. In Desktop ViewModels, use `using PrinterEntity = CodePrintManager.Domain.Entities.Printer;`
- **Events over UI bindings**: Application services raise plain C# events (in Domain/Events/). Hosts adapt them to their UI framework (WPF dispatches to UI thread, future web host pushes via SignalR).
- **IPrinterAdapterFactory**: Each printer brand registers its own factory via DI. Application never directly references printer-specific projects.
- **Printer engineers**: Only need `Domain` + their `Printer.X` project + `PrinterTestHarness`. No DB, no UI, no services.
- **Dispatcher.Invoke pitfall**: Never use `Dispatcher.Invoke(async () => ...)` — it creates `async void` and silently swallows exceptions. Use synchronous updates from event data inside Dispatcher callbacks.

## Localization

The application supports multi-language UI (English, Russian, Armenian). All user-facing text must be localized.

### How it works

- **Translation files**: `application/src/Hosts/CodePrintManager.Desktop/Localization/{en,ru,hy}.json`
- **Interface**: `ILocalizationService` (Domain layer) — injected into ViewModels and Application services
- **XAML markup**: `{loc:Loc KeyName}` using `LocExtension` + `TranslationSource` for live binding
- **ViewModel/service usage**: `_loc["KeyName"]` or `_loc.Format("KeyName", arg1, arg2)`
- **Language selector**: ComboBox at the bottom of the sidebar. Selection is persisted in `AppConfig` (same as zoom level).
- **Fallback**: If a key is missing in the current language, English is used. If missing in English too, the raw key name is shown.

### Rules for new UI components

1. **Never hardcode user-facing text.** Use localization keys for all labels, buttons, messages, dialog titles, error messages, and status text.
2. **Add keys to all 3 language files** (`en.json`, `ru.json`, `hy.json`). If you don't know the translation, add the English text as a placeholder — it will be corrected later.
3. **XAML**: Add `xmlns:loc="clr-namespace:CodePrintManager.Desktop.Localization"` and use `{loc:Loc KeyName}`.
4. **ViewModels/Services**: Inject `ILocalizationService` via constructor and use `_loc["Key"]` or `_loc.Format("Key", args)`.
5. **Do NOT translate**: user-entered data (product names, printer names, imported codes), diagnostic log messages, enum values, structural punctuation.
6. **Format placeholders**: Use `{0}`, `{1}`, etc. in translation values. Never concatenate translated fragments — grammar differs across languages.
7. **Key naming convention**: `Section_Description` (e.g., `Products_Title`, `Error_NotEnoughCodes`, `Dialog_ConfirmDelete`).
8. **Tests**: When testing with a mock `ILocalizationService`, the mock returns the key name as the value. Update test assertions accordingly.
