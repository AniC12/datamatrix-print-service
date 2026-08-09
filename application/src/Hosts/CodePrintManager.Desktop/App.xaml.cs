using System.IO;
using System.Windows;
using CodePrintManager.Application;
using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Interfaces;
using CodePrintManager.Desktop.ViewModels;
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
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(appDir, "logs", "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .CreateLogger();

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

                // Register printer adapter factories
                services.AddSingleton<IPrinterAdapterFactory, SavemaAdapterFactory>();

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

        // Create a scope for the main window lifetime
        var windowScope = _host.Services.CreateScope();
        var mainWindow = windowScope.ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.DataContext = windowScope.ServiceProvider.GetRequiredService<MainViewModel>();
        mainWindow.Closed += (s, e) => windowScope.Dispose(); // Dispose scope when window closes
        mainWindow.Show();
        MainWindow = mainWindow;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
