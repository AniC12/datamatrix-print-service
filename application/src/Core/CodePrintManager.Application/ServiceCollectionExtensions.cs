using CodePrintManager.Application.Services;
using CodePrintManager.Data;
using CodePrintManager.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CodePrintManager.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCodePrintManager(this IServiceCollection services, string dbPath)
    {
        // Database
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Services
        services.AddSingleton<PrinterConnectionManager>();
        services.AddSingleton<IAlertService, AlertService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICodePoolService, CodePoolService>();
        services.AddScoped<IPrintJobService, PrintJobService>();

        return services;
    }
}
