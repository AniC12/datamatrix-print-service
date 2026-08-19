using CodePrintManager.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Printer.Savema;

public class SavemaAdapterFactory : IPrinterAdapterFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SavemaAdapterFactory> _logger;

    public SavemaAdapterFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SavemaAdapterFactory>();
    }

    public bool CanHandle(string adapterType)
    {
        var result = adapterType.StartsWith("savema", StringComparison.OrdinalIgnoreCase);
        _logger.LogTrace("-> CanHandle(adapterType={AdapterType}) = {Result}", adapterType, result);
        return result;
    }

    public IPrinterAdapter Create(string adapterType)
    {
        _logger.LogTrace("-> Create(adapterType={AdapterType})", adapterType);
        var logger = _loggerFactory.CreateLogger<SavemaTtoAdapter>();
        var adapter = new SavemaTtoAdapter(logger);
        _logger.LogInformation("Created SavemaTtoAdapter for type '{AdapterType}'", adapterType);
        _logger.LogTrace("<- Create completed");
        return adapter;
    }
}
