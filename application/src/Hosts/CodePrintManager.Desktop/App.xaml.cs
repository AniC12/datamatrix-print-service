using System.IO;
using System.Windows;
using CodePrintManager.Application;
using CodePrintManager.Application.Models;
using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
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

        var appDir = AppContext.BaseDirectory;
        var dbPath = Path.Combine(appDir, "codeprintmanager.db");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(appDir, "logs", "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Application starting. AppDir={AppDir}, DbPath={DbPath}", appDir, dbPath);

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureAppConfiguration(config =>
            {
                config.SetBasePath(appDir);
                config.AddJsonFile("appsettings.json", optional: true);
            })
            .ConfigureServices((context, services) =>
            {
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
                if (job.Status is JobStatus.Preparing or JobStatus.Ready)
                {
                    // Auto-cancel preparing/ready jobs — return reserved codes
                    await jobService.CancelJobAsync(job.Id);
                    Log.Information("Recovery: auto-cancelled stale {Status} job #{JobId}", job.Status, job.Id);
                }
                else if (job.Status == JobStatus.Printing)
                {
                    // Try to read SPGGTP from printer to detect discrepancy
                    int printerConfirmed = -1; // -1 = offline
                    try
                    {
                        var adapter = connMgr.GetAdapter(job.PrinterId);
                        if (adapter != null)
                        {
                            var lifetime = await adapter.GetTotalCounterAsync();
                            printerConfirmed = job.TotalBaseline.HasValue
                                ? lifetime - job.TotalBaseline.Value
                                : job.CodesConfirmed;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Recovery: could not read counter for printer {PrinterId}", job.PrinterId);
                    }

                    var discrepancy = printerConfirmed >= 0
                        ? printerConfirmed - job.CodesConfirmed
                        : 0;

                    recoveryItems.Add(new RecoveryItem(job, job.CodesConfirmed, printerConfirmed, discrepancy));
                }
            }

            // Show recovery dialog if there are printing jobs to resolve
            if (recoveryItems.Count > 0)
            {
                var recoveryVm = scope.ServiceProvider.GetRequiredService<RecoveryViewModel>();
                recoveryVm.LoadItems(recoveryItems);

                var dialog = new RecoveryDialog { DataContext = recoveryVm };
                dialog.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during startup recovery");
        }
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
