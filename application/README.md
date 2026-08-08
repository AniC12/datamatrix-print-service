# Code Print Manager - Application

## Quick Start

### Running the Desktop Application

**In Visual Studio / Rider / VS Code:**
1. Open `CodePrintManager.sln`
2. Set `CodePrintManager.Desktop` as the startup project (right-click → Set as Startup Project)
3. Press F5 or click Run

**From Command Line:**
```bash
dotnet run --project src/Hosts/CodePrintManager.Desktop
```

The application will:
- Create `codeprintmanager.db` in the output directory on first run
- Apply EF Core migrations automatically
- Enable SQLite WAL mode for concurrent access
- Create a `logs/` directory with daily rolling logs

### Building

```bash
dotnet build
```

### Running Tests

```bash
dotnet test
```

All 4 test projects should pass (currently placeholder tests).

### Database Migrations

**Add a new migration:**
```bash
dotnet ef migrations add <MigrationName> -p src/Infrastructure/CodePrintManager.Data -s src/Hosts/CodePrintManager.Desktop
```

**Apply migrations:**
```bash
dotnet ef database update -p src/Infrastructure/CodePrintManager.Data -s src/Hosts/CodePrintManager.Desktop
```

**Generate SQL script:**
```bash
dotnet ef migrations script -p src/Infrastructure/CodePrintManager.Data -s src/Hosts/CodePrintManager.Desktop
```

## Project Structure

```
src/
  Core/
    CodePrintManager.Domain/          - Entities, interfaces, enums, events
    CodePrintManager.Application/     - Services, business logic
  Infrastructure/
    CodePrintManager.Data/            - EF Core DbContext, configurations, migrations
  Printers/
    CodePrintManager.Printer.Savema/  - Savema TTO adapter (SPPL protocol)
  Hosts/
    CodePrintManager.Desktop/         - WPF UI (MVVM)
tests/
  CodePrintManager.Domain.Tests/
  CodePrintManager.Application.Tests/
  CodePrintManager.Data.Tests/
  CodePrintManager.Printer.Savema.Tests/
tools/
  PrinterTestHarness/                 - Console tool for testing printer adapters
```

## Configuration

Edit `src/Hosts/CodePrintManager.Desktop/appsettings.json` to configure:
- Poll interval (default: 500ms)
- Reconnect settings
- Alert thresholds

## Documentation

See `../docs/phase-1/` for:
- `phase1-design.md` - Full technical design
- `client-overview.md` - Product overview
- `multi-printer-concurrency.md` - Concurrency model
- `implementation-plan.md` - Task breakdown
- `codebase-architecture.md` - Code layout and data flow

## Troubleshooting

**"Unable to create DbContext" during migrations:**
- Ensure `DesignTimeDbContextFactory.cs` exists in CodePrintManager.Data
- Ensure `Microsoft.EntityFrameworkCore.Design` package is installed in Desktop project

**Build errors about file locks:**
- Close any running instances of the Desktop app
- Clean and rebuild: `dotnet clean && dotnet build`

**App crashes on startup:**
- Check `logs/app-*.log` for details
- Verify `codeprintmanager.db` is not locked by another process
- Delete the DB file to start fresh (migrations will recreate it)
