using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CodePrintManager.Application;
using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Interfaces;
using CodePrintManager.Desktop.Localization;
using CodePrintManager.Desktop.ViewModels;
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

        // Auto-connect configured printers.
        // ConnectAsync registers the adapter object synchronously before the TCP
        // handshake awaits, so GetAdapter returns non-null by the time recovery runs.
        // Individual TCP connections complete asynchronously (fire-and-forget).
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

        // Startup recovery: automatically restore stale jobs from prior crash.
        // Runs non-blocking — no dialog, no operator interaction required.
        // Must run AFTER auto-connect loop so adapters are registered.
        await RestoreStaleJobsOnStartupAsync();

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

    private async Task RestoreStaleJobsOnStartupAsync()
    {
        try
        {
            using var scope = _host!.Services.CreateScope();
            var jobService = scope.ServiceProvider.GetRequiredService<IPrintJobService>();
            await jobService.RestoreStaleJobsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during startup recovery");
        }
    }

    // --- Crash Handlers ---

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "UNHANDLED DISPATCHER EXCEPTION");
        // Do NOT call Log.CloseAndFlush() here — it causes a blind logging period
        // where subsequent log writes are silently dropped. The file sink writes
        // FATAL synchronously, and OnExit handles the final flush.
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Fatal(ex, "UNHANDLED APPDOMAIN EXCEPTION (IsTerminating={IsTerminating})", e.IsTerminating);
        else
            Log.Fatal("UNHANDLED APPDOMAIN EXCEPTION (non-Exception object): {Object}", e.ExceptionObject);
        // Do NOT call Log.CloseAndFlush() here — same reason as above.
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
            // Stop all running executors/watchers before disposing the host
            // (which disposes PrinterConnectionManager and its adapters)
            try
            {
                var registry = _host.Services.GetRequiredService<ActiveJobRegistry>();
                await registry.StopAllAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error stopping executors during shutdown");
            }

            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
