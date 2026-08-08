using CodePrintManager.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Printer.Savema;

public class SavemaAdapterFactory : IPrinterAdapterFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public SavemaAdapterFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public bool CanHandle(string adapterType)
        => adapterType.StartsWith("savema", StringComparison.OrdinalIgnoreCase);

    public IPrinterAdapter Create(string adapterType)
    {
        var logger = _loggerFactory.CreateLogger<SavemaTtoAdapter>();
        return new SavemaTtoAdapter(logger);
    }
}
