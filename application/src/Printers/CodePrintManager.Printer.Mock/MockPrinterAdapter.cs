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
    private bool _simulateDisconnect;

    // Storage
    private readonly ConcurrentDictionary<string, byte[]> _templates = new();
    private readonly ConcurrentDictionary<string, List<string>> _csvFiles = new();

    // Configuration
    public int PrintSpeedMs { get; set; } = 500;
    public string SerialNumber { get; set; } = "MOCK-001";

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
        if (_simulateDisconnect)
        {
            _logger.LogDebug("Mock: ConnectAsync rejected (simulated disconnect active)");
            return Task.FromResult(false);
        }

        _logger.LogDebug("Mock: Connected to {Host}:{Port}", host, port);
        IsConnected = true;
        // Only set Idle on initial connect (from Offline).
        // On reconnect after cable pull, preserve the actual printer state
        // (may have finished printing → Idle, or still printing → Printing).
        if (_status == PrinterStatus.Offline)
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

    public Task<string?> GetSerialNumberAsync(CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        return Task.FromResult<string?>(SerialNumber);
    }

    public Task<PrinterStatus> GetStatusAsync(CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        return Task.FromResult(_injectedError ?? _status);
    }

    public Task<int> GetCurrentCounterAsync(CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        return Task.FromResult(_currentCounter);
    }

    public Task<int> GetTotalCounterAsync(CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        return Task.FromResult(_lifetimeCounter);
    }

    public Task<int?> GetRemainingQuantityAsync(CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        var remaining = _printQuantity - _currentCounter;
        return Task.FromResult<int?>(remaining > 0 ? remaining : 0);
    }

    // Template management
    public Task<List<string>> ListTemplatesAsync(CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        return Task.FromResult(_templates.Keys.ToList());
    }

    public Task<bool> UploadTemplateAsync(string name, byte[] rox, CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        _templates[name] = rox;
        return Task.FromResult(true);
    }

    public Task<bool> ActivateTemplateAsync(string name, CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        _activeTemplate = name;
        // Real hardware: SPGGCP is cumulative and does NOT reset on SPLLTF.
        // The application uses baseline-delta tracking to handle this.
        return Task.FromResult(true);
    }

    public Task<string?> GetActiveTemplateAsync(CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        return Task.FromResult(_activeTemplate);
    }

    public Task<bool> DeleteTemplateAsync(string name, CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        return Task.FromResult(_templates.TryRemove(name, out _));
    }

    // CSV management
    public Task<List<string>> ListCsvFilesAsync(CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        return Task.FromResult(_csvFiles.Keys.ToList());
    }

    public Task<bool> UploadCsvAsync(string filename, IReadOnlyList<string> codes, CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        _csvFiles[filename] = codes.ToList();
        return Task.FromResult(true);
    }

    public Task<bool> VerifyCsvExistsAsync(string filename, CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        return Task.FromResult(_csvFiles.ContainsKey(filename));
    }

    public Task<bool> DeleteCsvAsync(string filename, CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        return Task.FromResult(_csvFiles.TryRemove(filename, out _));
    }

    // Print control
    public Task<bool> SetPrintQuantityAsync(int quantity, CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        // "Print quantity more from current position" — matches SPPL SPPSLQ semantics
        _printQuantity = _currentCounter + quantity;
        return Task.FromResult(true);
    }

    public Task<bool> StartPrintAsync(CancellationToken ct = default)
    {
        ThrowIfDisconnected();
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
        ThrowIfDisconnected();
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

    // Set counters to specific values (for testing cumulative SPGGCP scenarios).
    // Simulates a printer that has been used before — both counters start high.
    public void SetCounters(int currentCounter, int lifetimeCounter)
    {
        _currentCounter = currentCounter;
        _lifetimeCounter = lifetimeCounter;
        _logger.LogInformation("Mock: Counters set to current={Current}, lifetime={Lifetime}",
            currentCounter, lifetimeCounter);
    }

    /// <summary>
    /// Simulates a mid-session connection loss (e.g., network cable pull).
    /// All adapter commands will throw <see cref="IOException"/>.
    /// <see cref="ConnectAsync"/> will return false until <see cref="SimulateConnectionRestore"/> is called.
    /// The internal print loop is NOT stopped — the printer continues printing physically,
    /// incrementing counters, just like real hardware would when the cable is pulled.
    /// </summary>
    public void SimulateConnectionLoss()
    {
        _logger.LogInformation("Mock: Simulating connection loss (IsConnected → false, print loop continues)");
        _simulateDisconnect = true;
        IsConnected = false;
        // Do NOT call StopPrintInternal() — printer keeps printing physically
        // Do NOT change _status — printer state is unchanged by cable pull
    }

    /// <summary>
    /// Restores the ability to connect after a simulated connection loss.
    /// The next <see cref="ConnectAsync"/> call will succeed.
    /// </summary>
    public void SimulateConnectionRestore()
    {
        _logger.LogInformation("Mock: Connection restore allowed (next ConnectAsync will succeed)");
        _simulateDisconnect = false;
    }

    /// <summary>
    /// Throws <see cref="IOException"/> if the adapter is not connected.
    /// Used to simulate connection failures on all command methods during a simulated disconnect.
    /// </summary>
    private void ThrowIfDisconnected()
    {
        if (!IsConnected)
            throw new IOException("Mock: not connected to printer");
    }

    // Simulate power cycle: reset current counter but keep lifetime.
    // Power-cycle reset behavior is plausible (unconfirmed on real hardware)
    // and useful for testing the backward-movement detection path.
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
