using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CodePrintManager.Application;
using CodePrintManager.Application.Models;
using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using CodePrintManager.Desktop.Localization;
using CodePrintManager.Desktop.ViewModels;
using CodePrintManager.Desktop.Views;
using CodePrintManager.Printer.Mock;
using CodePrintManager.Printer.Savema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace CodePrintManager.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Register global crash handlers before anything else
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var appDir = AppContext.BaseDirectory;
        var dbPath = Path.Combine(appDir, "codeprintmanager.db");

        var useMockArg = Environment.GetCommandLineArgs().Contains("--mock");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .Enrich.WithMachineName()
            .WriteTo.File(
                Path.Combine(appDir, "logs", "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [T{ThreadId}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // --- Startup Banner ---
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";
        Log.Information("============================================");
        Log.Information("  CODE PRINT MANAGER SESSION START");
        Log.Information("  Version:   {Version}", version);
        Log.Information("  Runtime:   {Runtime}", RuntimeInformation.FrameworkDescription);
        Log.Information("  OS:        {OS}", RuntimeInformation.OSDescription);
        Log.Information("  Machine:   {Machine}", Environment.MachineName);
        Log.Information("  AppDir:    {AppDir}", appDir);
        Log.Information("  DbPath:    {DbPath}", dbPath);
        Log.Information("  Mode:      {Mode}", useMockArg ? "MOCK PRINTER" : "REAL PRINTER");
        Log.Information("  StartTime: {StartTime}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        Log.Information("============================================");

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureAppConfiguration(config =>
            {
                config.SetBasePath(appDir);
                config.AddJsonFile("appsettings.json", optional: true);
            })
            .ConfigureServices((context, services) =>
            {
                // Localization — register before AddCodePrintManager so TryAddSingleton defers
                var locDir = Path.Combine(appDir, "Localization");
                services.AddSingleton<LocalizationService>(sp =>
                    new LocalizationService(locDir, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LocalizationService>>()));
                services.AddSingleton<ILocalizationService>(sp =>
                    sp.GetRequiredService<LocalizationService>());

                services.AddCodePrintManager(dbPath);

                // Register printer adapter factory: mock or real Savema
                var useMock = context.Configuration.GetValue<bool>("UseMockPrinter")
                    || Environment.GetCommandLineArgs().Contains("--mock");

                if (useMock)
                {
                    services.AddSingleton<MockPrinterAdapterFactory>();
                    services.AddSingleton<IPrinterAdapterFactory>(sp =>
                        sp.GetRequiredService<MockPrinterAdapterFactory>());
                }
                else
                {
                    services.AddSingleton<IPrinterAdapterFactory, SavemaAdapterFactory>();
                }

                // Services
                services.AddSingleton<IDialogService, WpfDialogService>();

                // ViewModels
                services.AddTransient<MainViewModel>();
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ProductsViewModel>();
                services.AddTransient<PrintersViewModel>();
                services.AddTransient<JobsViewModel>();
                services.AddTransient<NewJobViewModel>();
                services.AddTransient<RecoveryViewModel>();
                services.AddTransient<CodesTabViewModel>();

                // Main window
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        // Initialize database (apply migrations + enable WAL mode)
        using (var initScope = _host.Services.CreateScope())
        {
            var db = initScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await DbInitializer.InitializeAsync(db);
        }

        // Initialize localization bridge for WPF bindings
        var locService = _host.Services.GetRequiredService<ILocalizationService>();
        TranslationSource.Instance.Initialize(locService);

        // Auto-connect configured printers (fire-and-forget, non-blocking)
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _host.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var connMgr = scope.ServiceProvider.GetRequiredService<PrinterConnectionManager>();
                var printers = await db.Printers.ToListAsync();

                foreach (var printer in printers)
                {
                    _ = connMgr.ConnectAsync(printer);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error during printer auto-connect on startup");
            }
        });

        // Startup recovery: detect and resolve stale jobs from prior crash
        await RunStartupRecoveryAsync();

        // Create a scope for the main window lifetime
        var windowScope = _host.Services.CreateScope();
        var mainWindow = windowScope.ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.DataContext = windowScope.ServiceProvider.GetRequiredService<MainViewModel>();
        mainWindow.Closed += (s, e) => windowScope.Dispose(); // Dispose scope when window closes

        // Indicate mock mode in the title bar
        if (windowScope.ServiceProvider.GetRequiredService<IPrinterAdapterFactory>() is MockPrinterAdapterFactory)
            mainWindow.Title += " [MOCK PRINTER]";

        mainWindow.Show();
        MainWindow = mainWindow;
    }

    private async Task RunStartupRecoveryAsync()
    {
        try
        {
            using var scope = _host!.Services.CreateScope();
            var jobService = scope.ServiceProvider.GetRequiredService<IPrintJobService>();
            var codePool = scope.ServiceProvider.GetRequiredService<ICodePoolService>();
            var connMgr = scope.ServiceProvider.GetRequiredService<PrinterConnectionManager>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var staleJobs = await jobService.GetStaleJobsAsync();
            if (staleJobs.Count == 0) return;

            var recoveryItems = new List<RecoveryItem>();

            foreach (var job in staleJobs)
            {
                if (job.Status == JobStatus.Preparing)
                {
                    // Only Preparing jobs are auto-cancelled — preparation may be incomplete.
                    await jobService.CancelJobAsync(job.Id);
                    Log.Information("Recovery: auto-cancelled stale Preparing job #{JobId}", job.Id);
                    continue;
                }

                // Ready, Printing, Paused → inspect the printer and present in Recovery Dialog.
                // Ready jobs have TotalBaseline (recorded during Prepare) and a loaded data
                // buffer. Someone may have pressed Print on the touchscreen — we cannot
                // safely auto-cancel without checking SPGGTP first.
                var item = await InspectJobForRecoveryAsync(job, connMgr);
                recoveryItems.Add(item);
            }

            if (recoveryItems.Count > 0)
            {
                var recoveryVm = scope.ServiceProvider.GetRequiredService<RecoveryViewModel>();
                recoveryVm.LoadItems(recoveryItems);

                // Temporarily switch to explicit shutdown so closing the recovery
                // dialog doesn't kill the app before the main window is shown.
                // MainWindow hasn't been assigned yet, so WPF would interpret
                // the dialog close as "main window closed" and shut down.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                try
                {
                    var dialog = new RecoveryDialog { DataContext = recoveryVm };
                    dialog.ShowDialog();
                }
                finally
                {
                    ShutdownMode = ShutdownMode.OnMainWindowClose;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during startup recovery");
        }
    }

    /// <summary>
    /// Inspect a stale job (Ready, Printing, or Paused) against the printer's current state.
    /// Reads SPGGTP, SPGGCP, SPPSTA, SPLGAT, and SPLGSD to build a full RecoveryItem.
    /// </summary>
    private static async Task<RecoveryItem> InspectJobForRecoveryAsync(
        PrintJob job, PrinterConnectionManager connMgr)
    {
        var adapter = connMgr.GetAdapter(job.PrinterId);
        if (adapter == null || !adapter.IsConnected)
        {
            Log.Warning("Recovery: printer {PrinterId} is offline for job #{JobId}", job.PrinterId, job.Id);
            return new RecoveryItem(job, job.CodesConfirmed, -1, 0)
            {
                PrinterOffline = true,
                RecommendedAction = "Connect printer to inspect"
            };
        }

        // Read all inspection values. If any read fails, treat as offline.
        try
        {
            var status = await adapter.GetStatusAsync();
            var currentCounter = await adapter.GetCurrentCounterAsync();
            var lifetimeCounter = await adapter.GetTotalCounterAsync();
            var activeTemplate = await adapter.GetActiveTemplateAsync();
            var csvFiles = await adapter.ListCsvFilesAsync();
            var serialNumber = await adapter.GetSerialNumberAsync();

            // Serial number mismatch detection
            var serialMismatch = !string.IsNullOrEmpty(job.Printer?.SerialNumber)
                && !string.IsNullOrEmpty(serialNumber)
                && !string.Equals(job.Printer.SerialNumber, serialNumber, StringComparison.Ordinal);

            // Compute lifetime delta
            int printerConfirmed;
            if (job.TotalBaseline.HasValue)
                printerConfirmed = lifetimeCounter - job.TotalBaseline.Value;
            else
                printerConfirmed = job.CodesConfirmed; // No baseline → can't compute

            var discrepancy = printerConfirmed - job.CodesConfirmed;

            // Template match check
            var expectedTemplate = job.Product?.TemplateFile;
            var expectedTemplateName = expectedTemplate != null
                ? Path.GetFileName(expectedTemplate)
                : null;
            var templateMatch = expectedTemplateName == null
                || string.Equals(expectedTemplateName, activeTemplate, StringComparison.OrdinalIgnoreCase);

            // CSV presence check
            var expectedCsv = job.Product?.PrinterCsvName;
            var csvPresent = expectedCsv == null || csvFiles.Contains(expectedCsv);

            // Power cycle detection: SPGGCP is cumulative on real hardware (does NOT
            // reset on SPLLTF), so we can't use SPGGCP == 0 as a reliable signal.
            // Instead, detect power cycle via indirect signals:
            //   - CSV file is missing (volatile storage lost on power cycle)
            //   - SPGGTP delta > 0 but CSV is gone (prints happened before power loss)
            // Also keep SPGGCP == 0 as a secondary signal (may reset on some firmware).
            var powerCycled = (currentCounter == 0 || !csvPresent)
                && job.TotalBaseline.HasValue
                && (job.Status == JobStatus.Printing || job.Status == JobStatus.Ready);

            // Build recommendation
            string recommendation;
            if (serialMismatch)
                recommendation = "Hardware swap detected — Abort recommended";
            else if (!templateMatch)
                recommendation = "Template mismatch — Abort recommended";
            else if (printerConfirmed < 0)
                recommendation = "Counter rollback — Abort recommended";
            else if (powerCycled && discrepancy > 0)
                recommendation = "Power cycle + unrecorded prints — Resume with caution";
            else if (powerCycled)
                recommendation = "Power cycle detected — Resume will re-upload CSV";
            else if (discrepancy > 0)
                recommendation = $"{discrepancy} unrecorded print(s) — Resume to continue";
            else if (discrepancy == 0 && job.Status == JobStatus.Ready)
                recommendation = "No printing detected — safe to Resume or Abort";
            else
                recommendation = "Resume to continue";

            Log.Information(
                "Recovery: Job #{JobId} ({Status}): status={PrinterStatus}, SPGGCP={Counter}, " +
                "SPGGTP delta={Delta}, template={Template} (match={Match}), CSV={Csv}, " +
                "powerCycle={PowerCycle}, serialMismatch={SerialMismatch}",
                job.Id, job.Status, status, currentCounter, printerConfirmed,
                activeTemplate, templateMatch, csvPresent, powerCycled, serialMismatch);

            return new RecoveryItem(job, job.CodesConfirmed, printerConfirmed, discrepancy)
            {
                PrinterStatus = status,
                PowerCycleDetected = powerCycled,
                TemplateMatch = templateMatch,
                ActiveTemplate = activeTemplate,
                CsvPresent = csvPresent,
                SerialMismatch = serialMismatch,
                RecommendedAction = recommendation
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Recovery: inspection failed for printer {PrinterId}, job #{JobId}",
                job.PrinterId, job.Id);
            return new RecoveryItem(job, job.CodesConfirmed, -1, 0)
            {
                PrinterOffline = true,
                RecommendedAction = "Inspection failed — retry after reconnect"
            };
        }
    }

    // --- Crash Handlers ---

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "UNHANDLED DISPATCHER EXCEPTION");
        Log.CloseAndFlush();
        // Allow the exception to propagate so the default handler can show the error dialog
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Fatal(ex, "UNHANDLED APPDOMAIN EXCEPTION (IsTerminating={IsTerminating})", e.IsTerminating);
        else
            Log.Fatal("UNHANDLED APPDOMAIN EXCEPTION (non-Exception object): {Object}", e.ExceptionObject);
        Log.CloseAndFlush();
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "UNOBSERVED TASK EXCEPTION (will not crash, but logged for diagnostics)");
        // Don't call e.SetObserved() — let the default behavior apply
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down");

        if (_host != null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
