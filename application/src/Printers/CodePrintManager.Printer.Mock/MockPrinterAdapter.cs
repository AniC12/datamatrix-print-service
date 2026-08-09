using System.Collections.Concurrent;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodePrintManager.Printer.Mock;

public class MockPrinterAdapter : IPrinterAdapter
{
    private readonly object _lock = new();
    private readonly ILogger _logger;
    private CancellationTokenSource? _printCts;
    private Task? _printTask;

    // State
    private PrinterStatus _status = PrinterStatus.Offline;
    private int _currentCounter;
    private int _lifetimeCounter;
    private int _printQuantity;
    private string? _activeTemplate;
    private PrinterStatus? _injectedError;

    // Storage
    private readonly ConcurrentDictionary<string, byte[]> _templates = new();
    private readonly ConcurrentDictionary<string, List<string>> _csvFiles = new();

    // Configuration
    public int PrintSpeedMs { get; set; } = 500;

    public MockPrinterAdapter(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    // Test inspection
    public PrinterStatus CurrentState => _injectedError ?? _status;
    public int InspectCurrentCounter => _currentCounter;
    public int InspectLifetimeCounter => _lifetimeCounter;
    public IReadOnlyList<string> StoredTemplates => _templates.Keys.ToList();
    public IReadOnlyList<string> StoredCsvFiles => _csvFiles.Keys.ToList();
    public string? InspectActiveTemplate => _activeTemplate;
    public bool IsConnected { get; private set; }

    public Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        _logger.LogDebug("Mock: Connected to {Host}:{Port}", host, port);
        IsConnected = true;
        _status = PrinterStatus.Idle;
        return Task.FromResult(true);
    }

    public Task DisconnectAsync()
    {
        _logger.LogDebug("Mock: Disconnected");
        IsConnected = false;
        _status = PrinterStatus.Offline;
        StopPrintInternal();
        return Task.CompletedTask;
    }

    public Task<PrinterStatus> GetStatusAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_injectedError ?? _status);
    }

    public Task<int> GetCurrentCounterAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_currentCounter);
    }

    public Task<int> GetTotalCounterAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_lifetimeCounter);
    }

    public Task<int?> GetRemainingQuantityAsync(CancellationToken ct = default)
    {
        var remaining = _printQuantity - _currentCounter;
        return Task.FromResult<int?>(remaining > 0 ? remaining : 0);
    }

    // Template management
    public Task<List<string>> ListTemplatesAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_templates.Keys.ToList());
    }

    public Task<bool> UploadTemplateAsync(string name, byte[] rox, CancellationToken ct = default)
    {
        _templates[name] = rox;
        return Task.FromResult(true);
    }

    public Task<bool> ActivateTemplateAsync(string name, CancellationToken ct = default)
    {
        _activeTemplate = name;
        // Activating a template resets the current counter (per SPPL spec)
        _currentCounter = 0;
        return Task.FromResult(true);
    }

    public Task<string?> GetActiveTemplateAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_activeTemplate);
    }

    public Task<bool> DeleteTemplateAsync(string name, CancellationToken ct = default)
    {
        return Task.FromResult(_templates.TryRemove(name, out _));
    }

    // CSV management
    public Task<List<string>> ListCsvFilesAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_csvFiles.Keys.ToList());
    }

    public Task<bool> UploadCsvAsync(string filename, IReadOnlyList<string> codes, CancellationToken ct = default)
    {
        _csvFiles[filename] = codes.ToList();
        return Task.FromResult(true);
    }

    public Task<bool> VerifyCsvExistsAsync(string filename, CancellationToken ct = default)
    {
        return Task.FromResult(_csvFiles.ContainsKey(filename));
    }

    public Task<bool> DeleteCsvAsync(string filename, CancellationToken ct = default)
    {
        return Task.FromResult(_csvFiles.TryRemove(filename, out _));
    }

    // Print control
    public Task<bool> SetPrintQuantityAsync(int quantity, CancellationToken ct = default)
    {
        // "Print quantity more from current position" — matches SPPL SPPSLQ semantics
        _printQuantity = _currentCounter + quantity;
        return Task.FromResult(true);
    }

    public Task<bool> StartPrintAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_status == PrinterStatus.Printing)
                return Task.FromResult(true);

            _logger.LogDebug("Mock: Print started (qty={Qty}, counter={Counter})", _printQuantity, _currentCounter);
            _status = PrinterStatus.Printing;
            _printCts = new CancellationTokenSource();
            _printTask = RunPrintLoopAsync(_printCts.Token);
        }
        return Task.FromResult(true);
    }

    public Task<bool> StopPrintAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Mock: Print stopped at counter={Counter}", _currentCounter);
        StopPrintInternal();
        _status = PrinterStatus.Idle;
        return Task.FromResult(true);
    }

    // Error injection for testing
    public void InjectError(PrinterStatus errorState)
    {
        _logger.LogInformation("Mock: Error injected → {Status}", errorState);
        _injectedError = errorState;
    }

    public void ClearError()
    {
        _logger.LogInformation("Mock: Error cleared");
        _injectedError = null;
    }

    // Simulate power cycle: reset current counter but keep lifetime
    public void SimulatePowerCycle()
    {
        StopPrintInternal();
        _currentCounter = 0;
        _status = PrinterStatus.Idle;
    }

    private async Task RunPrintLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_currentCounter >= _printQuantity)
            {
                _status = PrinterStatus.Idle;
                return;
            }

            await Task.Delay(PrintSpeedMs, ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested) return;

            _currentCounter++;
            _lifetimeCounter++;

            if (_currentCounter >= _printQuantity)
            {
                _status = PrinterStatus.Idle;
                return;
            }
        }
    }

    private void StopPrintInternal()
    {
        lock (_lock)
        {
            _printCts?.Cancel();
            try { _printTask?.Wait(500); } catch { /* ignore */ }
            _printCts?.Dispose();
            _printCts = null;
            _printTask = null;
        }
    }

    public void Dispose()
    {
        StopPrintInternal();
        GC.SuppressFinalize(this);
    }
}
