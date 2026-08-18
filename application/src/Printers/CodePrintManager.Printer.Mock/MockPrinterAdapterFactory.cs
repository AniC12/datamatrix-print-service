using System.Collections.Concurrent;
using CodePrintManager.Domain.Interfaces;

namespace CodePrintManager.Printer.Mock;

public class MockPrinterAdapterFactory : IPrinterAdapterFactory
{
    private readonly ConcurrentDictionary<int, MockPrinterAdapter> _registry = new();
    private int _nextId;

    /// <summary>
    /// When set, newly created adapters inherit this serial number instead of the default.
    /// </summary>
    public string? NextSerialNumber { get; set; }

    public bool CanHandle(string adapterType)
        => adapterType.StartsWith("mock", StringComparison.OrdinalIgnoreCase);

    public IPrinterAdapter Create(string adapterType)
    {
        var adapter = new MockPrinterAdapter();
        if (NextSerialNumber != null)
            adapter.SerialNumber = NextSerialNumber;
        var id = Interlocked.Increment(ref _nextId);
        _registry[id] = adapter;
        return adapter;
    }

    /// <summary>
    /// Register a mock adapter with a specific printer ID (for test inspection).
    /// Called by PrinterConnectionManager when it creates the adapter.
    /// </summary>
    public void RegisterForPrinter(int printerId, MockPrinterAdapter adapter)
    {
        _registry[printerId] = adapter;
    }

    /// <summary>
    /// Get the mock adapter for a specific printer ID (for test assertions / control).
    /// </summary>
    public MockPrinterAdapter? GetMock(int printerId)
        => _registry.TryGetValue(printerId, out var adapter) ? adapter : null;

    /// <summary>
    /// Get all registered mock adapters.
    /// </summary>
    public IReadOnlyDictionary<int, MockPrinterAdapter> GetAll() => _registry;
}
