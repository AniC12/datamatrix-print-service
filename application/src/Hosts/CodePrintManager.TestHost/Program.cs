using CodePrintManager.Application;
using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Interfaces;
using CodePrintManager.Printer.Mock;
using CodePrintManager.TestHost.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Database: use temp file by default, or configurable
var dbPath = builder.Configuration["DbPath"]
    ?? Path.Combine(Path.GetTempPath(), $"cpm_test_{Guid.NewGuid():N}.db");

// Register core services (same as WPF host)
builder.Services.AddCodePrintManager(dbPath);

// Register mock printer adapter factory instead of Savema
builder.Services.AddSingleton<MockPrinterAdapterFactory>();
builder.Services.AddSingleton<IPrinterAdapterFactory>(sp => sp.GetRequiredService<MockPrinterAdapterFactory>());

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.InitializeAsync(db);
}

// Map all endpoint groups
app.MapPrinterEndpoints();
app.MapProductEndpoints();
app.MapJobEndpoints();
app.MapDashboardEndpoints();
app.MapMockControlEndpoints();
app.MapEventEndpoints();

app.Run();

// Make Program class accessible for WebApplicationFactory
public partial class Program { }
