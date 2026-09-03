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

    // Fault injection
    private int _ioFailuresRemaining;
    private bool _isBlocked;

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

    /// <summary>
    /// Inspects the CSV contents stored on the mock printer.
    /// Returns null if the file doesn't exist.
    /// </summary>
    public IReadOnlyList<string>? InspectCsvContents(string filename)
        => _csvFiles.TryGetValue(filename, out var codes) ? codes.AsReadOnly() : null;

    // ───────────────────────────────────────────────────────
    // Connection guard — simulates IOException on disconnected adapter.
    // The real SavemaTtoAdapter throws IOException when the TCP socket
    // is dead. Without this, the JobExecutor never enters the
    // error/inspection path during integration tests.
    // ───────────────────────────────────────────────────────
    private void ThrowIfDisconnected()
    {
        if (!IsConnected)
            throw new IOException("Mock: connection lost — adapter is disconnected");
    }

    /// <summary>
    /// Throws IOException if IO failures have been injected, and decrements
    /// the remaining count. Thread-safe via Interlocked.
    /// </summary>
    private void ThrowIfIOFailureInjected()
    {
        if (_ioFailuresRemaining > 0)
        {
            var remaining = Interlocked.Decrement(ref _ioFailuresRemaining);
            _logger.LogInformation("Mock: IO failure injected (remaining={Remaining})", remaining);
            throw new IOException($"Mock: injected IO failure ({remaining} remaining)");
        }
    }

    /// <summary>
    /// Throws InvalidOperationException if BLOCKED state is injected.
    /// In real SPPL, BLOCKED means all commands except SPPSTA return FAIL.
    /// </summary>
    private void ThrowIfBlocked()
    {
        if (_isBlocked)
            throw new InvalidOperationException("Mock: SPPL FAIL — printer is BLOCKED (not on main screen)");
    }

    /// <summary>
    /// Combined guard for all command methods (except GetStatusAsync which
    /// must work even when BLOCKED, per real SPPL behavior).
    /// </summary>
    private void GuardCommand()
    {
        ThrowIfDisconnected();
        ThrowIfIOFailureInjected();
        ThrowIfBlocked();
    }

    /// <summary>
    /// Guard for read-only status command — works when BLOCKED (per SPPL spec)
    /// but still fails when disconnected or IO failures are injected.
    /// </summary>
    private void GuardStatusCommand()
    {
        ThrowIfDisconnected();
        ThrowIfIOFailureInjected();
    }

    public Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        if (_simulateDisconnect)
        {
            _logger.LogDebug("Mock: ConnectAsync rejected (simulated disconnect active)");
            return Task.FromResult(false);
        }

        _logger.LogDebug("Mock: Connected to {Host}:{Port}", host, port);
        IsConnected = true;
        // If the print loop is still running (e.g., after a network drop while printing),
        // preserve the Printing status. Otherwise set to Idle.
        if (_printTask != null && !_printTask.IsCompleted)
            _status = PrinterStatus.Printing;
        else
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
        GuardCommand();
        return Task.FromResult<string?>(SerialNumber);
    }

    public Task<PrinterStatus> GetStatusAsync(CancellationToken ct = default)
    {
        GuardStatusCommand();
        if (_isBlocked)
            return Task.FromResult(PrinterStatus.Blocked);
        return Task.FromResult(_injectedError ?? _status);
    }

    public Task<int> GetCurrentCounterAsync(CancellationToken ct = default)
    {
        GuardCommand();
        return Task.FromResult(_currentCounter);
    }

    public Task<int> GetTotalCounterAsync(CancellationToken ct = default)
    {
        GuardCommand();
        return Task.FromResult(_lifetimeCounter);
    }

    public Task<int?> GetRemainingQuantityAsync(CancellationToken ct = default)
    {
        GuardCommand();
        var remaining = _printQuantity - _currentCounter;
        return Task.FromResult<int?>(remaining > 0 ? remaining : 0);
    }

    // Template management
    public Task<List<string>> ListTemplatesAsync(CancellationToken ct = default)
    {
        GuardCommand();
        return Task.FromResult(_templates.Keys.ToList());
    }

    public Task<bool> UploadTemplateAsync(string name, byte[] rox, CancellationToken ct = default)
    {
        GuardCommand();
        _templates[name] = rox;
        return Task.FromResult(true);
    }

    public Task<bool> ActivateTemplateAsync(string name, CancellationToken ct = default)
    {
        GuardCommand();
        _activeTemplate = name;
        // Real hardware: SPGGCP is cumulative and does NOT reset on SPLLTF.
        // The application uses baseline-delta tracking to handle this.
        return Task.FromResult(true);
    }

    public Task<string?> GetActiveTemplateAsync(CancellationToken ct = default)
    {
        GuardCommand();
        return Task.FromResult(_activeTemplate);
    }

    public Task<bool> DeleteTemplateAsync(string name, CancellationToken ct = default)
    {
        GuardCommand();
        return Task.FromResult(_templates.TryRemove(name, out _));
    }

    // CSV management
    public Task<List<string>> ListCsvFilesAsync(CancellationToken ct = default)
    {
        GuardCommand();
        return Task.FromResult(_csvFiles.Keys.ToList());
    }

    public Task<bool> UploadCsvAsync(string filename, IReadOnlyList<string> codes, CancellationToken ct = default)
    {
        GuardCommand();
        _csvFiles[filename] = codes.ToList();
        return Task.FromResult(true);
    }

    public Task<bool> VerifyCsvExistsAsync(string filename, CancellationToken ct = default)
    {
        GuardCommand();
        return Task.FromResult(_csvFiles.ContainsKey(filename));
    }

    public Task<bool> DeleteCsvAsync(string filename, CancellationToken ct = default)
    {
        GuardCommand();
        return Task.FromResult(_csvFiles.TryRemove(filename, out _));
    }

    // Print control
    public Task<bool> SetPrintQuantityAsync(int quantity, CancellationToken ct = default)
    {
        GuardCommand();
        // "Print quantity more from current position" — matches SPPL SPPSLQ semantics
        _printQuantity = _currentCounter + quantity;
        return Task.FromResult(true);
    }

    public Task<bool> StartPrintAsync(CancellationToken ct = default)
    {
        GuardCommand();
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
        GuardCommand();
        _logger.LogDebug("Mock: Print stopped at counter={Counter}", _currentCounter);
        StopPrintInternal();
        _printQuantity = _currentCounter; // Mark: no more prints expected (prevents ClearError from restarting)
        _status = PrinterStatus.Idle;
        return Task.FromResult(true);
    }

    // ───────────────────────────────────────────────────────
    // Fault injection for testing
    // ───────────────────────────────────────────────────────

    // Error injection (status override + stops print loop like a real printer would)
    public void InjectError(PrinterStatus errorState)
    {
        _logger.LogInformation("Mock: Error injected → {Status}", errorState);
        _injectedError = errorState;
        StopPrintInternal(); // Real printer stops printing on error
    }

    public void ClearError()
    {
        _logger.LogInformation("Mock: Error cleared → checking for remaining prints");
        _injectedError = null;
        // Restart print loop if there are remaining prints.
        // Models a real printer resuming after error resolution (e.g., ribbon replaced).
        lock (_lock)
        {
            if (_printTask == null && _currentCounter < _printQuantity)
            {
                _logger.LogInformation("Mock: Restarting print loop ({Remaining} remaining)",
                    _printQuantity - _currentCounter);
                _printCts = new CancellationTokenSource();
                _status = PrinterStatus.Printing;
                _printTask = RunPrintLoopAsync(_printCts.Token);
                return;
            }
        }
        _status = PrinterStatus.Idle;
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

    // Simulate power cycle: reset current counter but keep lifetime.
    // Power-cycle reset behavior is plausible (unconfirmed on real hardware)
    // and useful for testing the backward-movement detection path.
    public void SimulatePowerCycle()
    {
        StopPrintInternal();
        _currentCounter = 0;
        _printQuantity = 0; // Data buffer lost on power cycle
        _status = PrinterStatus.Idle;
    }

    /// <summary>
    /// Queue N IOException throws on the next N adapter command calls.
    /// Thread-safe. The counter auto-decrements on each throw.
    /// </summary>
    public void InjectIOFailure(int callCount = 1)
    {
        Interlocked.Exchange(ref _ioFailuresRemaining, callCount);
        _logger.LogInformation("Mock: IO failure injection queued (count={Count})", callCount);
    }

    /// <summary>
    /// Clear any remaining injected IO failures.
    /// </summary>
    public void ClearIOFailure()
    {
        Interlocked.Exchange(ref _ioFailuresRemaining, 0);
        _logger.LogInformation("Mock: IO failure injection cleared");
    }

    /// <summary>
    /// Simulate BLOCKED state — GetStatusAsync returns Blocked,
    /// all other commands throw InvalidOperationException (per SPPL spec:
    /// all commands except SPPSTA return FAIL when BLOCKED).
    /// </summary>
    public void InjectBlockedState()
    {
        _isBlocked = true;
        _logger.LogInformation("Mock: BLOCKED state injected");
    }

    /// <summary>
    /// Clear the BLOCKED state.
    /// </summary>
    public void ClearBlockedState()
    {
        _isBlocked = false;
        _logger.LogInformation("Mock: BLOCKED state cleared");
    }

    /// <summary>
    /// Simulate TCP connection drop without removing from PrinterConnectionManager.
    /// The adapter stays in _adapters dict, but all commands throw IOException until
    /// ConnectAsync is called again (simulating network restoration).
    /// This is how real network drops work: the adapter object stays registered but
    /// the TCP stream is dead. TryReconnectAsync can restore it in-place.
    /// </summary>
    public void SimulateNetworkDrop()
    {
        _logger.LogInformation("Mock: Network drop simulated (was connected={Connected})", IsConnected);
        IsConnected = false;
        // Do NOT stop the print loop — on a real printer, physical printing
        // continues regardless of network connectivity. The counters keep
        // advancing; we just can't read them until we reconnect.
    }

    /// <summary>
    /// Simulate power cycle while printing: stop the print loop, reset SPGGCP to 0,
    /// keep SPGGTP (persists across power cycles), clear active template and data buffer.
    /// Does NOT disconnect the adapter — real power cycle causes TCP drop, but we test
    /// that separately via disconnect.
    /// </summary>
    public void SimulatePowerCycleWhilePrinting()
    {
        _logger.LogInformation("Mock: Power cycle while printing (counter={Current}, lifetime={Lifetime})",
            _currentCounter, _lifetimeCounter);
        StopPrintInternal();
        _currentCounter = 0;
        _printQuantity = 0; // Data buffer lost on power cycle
        _activeTemplate = null;
        _status = PrinterStatus.Idle;
        _logger.LogInformation("Mock: Power cycle complete — SPGGCP=0, template cleared, status=Idle, SPGGTP={Lifetime}",
            _lifetimeCounter);
    }

    /// <summary>
    /// Simulate external printing: advance both counters by N without
    /// the job's knowledge (simulates someone printing from the touchscreen).
    /// </summary>
    public void SimulateExternalPrints(int count)
    {
        _currentCounter += count;
        _lifetimeCounter += count;
        _logger.LogInformation("Mock: External prints simulated (count={Count}, current={Current}, lifetime={Lifetime})",
            count, _currentCounter, _lifetimeCounter);
    }

    /// <summary>
    /// Simulate someone loading a different template on the printer's touchscreen
    /// or via another SPPL client. Changes the active template name.
    /// </summary>
    public void SimulateExternalTemplateLoad(string templateName)
    {
        _activeTemplate = templateName;
        _logger.LogInformation("Mock: External template load → '{Template}'", templateName);
    }

    /// <summary>
    /// Delete a CSV file from mock storage (simulates storage corruption
    /// or manual cleanup).
    /// </summary>
    public void DeleteCsvFromStorage(string filename)
    {
        _csvFiles.TryRemove(filename, out _);
        _logger.LogInformation("Mock: CSV '{Filename}' deleted from storage", filename);
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
