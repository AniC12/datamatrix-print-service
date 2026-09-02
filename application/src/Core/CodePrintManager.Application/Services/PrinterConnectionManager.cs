using System.Collections.Concurrent;
using System.Diagnostics;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Events;
using CodePrintManager.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Application.Services;

public class PrinterConnectionManager : IDisposable
{
    private readonly ConcurrentDictionary<int, IPrinterAdapter> _adapters = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _reconnectCts = new();
    private readonly ConcurrentDictionary<int, int> _reconnectAttemptCounts = new();
    private readonly ConcurrentDictionary<int, DateTime> _disconnectedSince = new();
    /// <summary>Printers flagged as having a serial number mismatch. Job operations should be blocked.</summary>
    private readonly ConcurrentDictionary<int, bool> _serialMismatchFlags = new();
    private readonly IEnumerable<IPrinterAdapterFactory> _factories;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAlertService _alerts;
    private readonly ActiveJobRegistry _jobRegistry;
    private readonly ILogger<PrinterConnectionManager> _logger;

    public event EventHandler<PrinterStatusChangedEvent>? PrinterStatusChanged;

    public PrinterConnectionManager(
        IEnumerable<IPrinterAdapterFactory> factories,
        IServiceScopeFactory scopeFactory,
        IAlertService alerts,
        ActiveJobRegistry jobRegistry,
        ILogger<PrinterConnectionManager> logger)
    {
        _factories = factories;
        _scopeFactory = scopeFactory;
        _alerts = alerts;
        _jobRegistry = jobRegistry;
        _logger = logger;
        _logger.LogTrace("-> PrinterConnectionManager constructed");
    }

    /// <summary>Returns true if the printer has a serial number mismatch (hardware swap detected).</summary>
    public bool HasSerialMismatch(int printerId)
    {
        var result = _serialMismatchFlags.ContainsKey(printerId);
        _logger.LogTrace("-> HasSerialMismatch(printerId={PrinterId}) = {Result}", printerId, result);
        return result;
    }

    public IPrinterAdapter? GetAdapter(int printerId)
    {
        var found = _adapters.TryGetValue(printerId, out var adapter);
        _logger.LogTrace("-> GetAdapter(printerId={PrinterId}) = {Found} (connected={Connected})",
            printerId, found, found ? adapter!.IsConnected : false);
        return found ? adapter : null;
    }

    public async Task ConnectAsync(Printer printer, CancellationToken ct = default)
    {
        _logger.LogTrace("-> ConnectAsync(printer='{Name}', id={PrinterId}, ip={Ip}, port={Port}, adapterType={AdapterType})",
            printer.Name, printer.Id, printer.IpAddress, printer.Port, printer.AdapterType);
        var sw = Stopwatch.StartNew();

        _logger.LogInformation("Connecting printer '{Name}' (Id={PrinterId}) at {Ip}:{Port}, adapter={AdapterType}",
            printer.Name, printer.Id, printer.IpAddress, printer.Port, printer.AdapterType);

        // Guard: check that a factory exists for this adapter type before creating.
        // Without this, CreateAdapter throws InvalidOperationException, which can
        // go unobserved when ConnectAsync is called fire-and-forget (e.g., during
        // startup for mock printers with no MockAdapterFactory registered).
        if (!_factories.Any(f => f.CanHandle(printer.AdapterType)))
        {
            _logger.LogInformation(
                "Skipping printer '{Name}' (Id={Id}): no adapter factory for type '{Type}'",
                printer.Name, printer.Id, printer.AdapterType);
            return;
        }

        // Cancel any existing reconnect loop to prevent it from using
        // the old adapter after we dispose it (race condition prevention)
        if (_reconnectCts.TryRemove(printer.Id, out var existingCts))
        {
            _logger.LogDebug("Cancelling existing reconnect loop for printer {PrinterId}", printer.Id);
            existingCts.Cancel();
        }

        // Dispose the old adapter to avoid TcpClient leaks (e.g., rapid reconnect clicks)
        if (_adapters.TryRemove(printer.Id, out var existing))
        {
            _logger.LogDebug("Disposing existing adapter for printer {PrinterId} before creating new one", printer.Id);
            try { existing.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error disposing old adapter for printer {PrinterId}", printer.Id); }
        }

        var adapter = CreateAdapter(printer.AdapterType);
        _adapters[printer.Id] = adapter;

        bool success;
        try
        {
            success = await adapter.ConnectAsync(printer.IpAddress, printer.Port, ct);
        }
        catch (OperationCanceledException)
        {
            // Cancelled — clean up the adapter we just stored
            _adapters.TryRemove(printer.Id, out _);
            adapter.Dispose();
            throw;
        }
        var status = success ? PrinterStatus.Idle : PrinterStatus.Offline;
        PrinterStatusChanged?.Invoke(this, new PrinterStatusChangedEvent(printer.Id, PrinterStatus.Offline, status));

        if (success)
        {
            _logger.LogInformation("Printer '{Name}' (Id={PrinterId}) connected successfully in {ElapsedMs}ms",
                printer.Name, printer.Id, sw.ElapsedMilliseconds);
            _disconnectedSince.TryRemove(printer.Id, out _);
            _reconnectAttemptCounts.TryRemove(printer.Id, out _);
            await CheckSerialNumberAsync(printer.Id, adapter);
        }
        else
        {
            _disconnectedSince[printer.Id] = DateTime.Now;
            _logger.LogWarning("Printer '{Name}' (Id={PrinterId}) connection failed after {ElapsedMs}ms, starting reconnect loop",
                printer.Name, printer.Id, sw.ElapsedMilliseconds);
            StartReconnectLoop(printer);
        }
        _logger.LogTrace("<- ConnectAsync completed ({ElapsedMs}ms)", sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Called by the job executor or watcher when a mid-session connection loss is detected.
    /// Starts a reconnect loop if one isn't already running for this printer.
    /// The reconnect loop reuses the same adapter instance from <see cref="_adapters"/>,
    /// so any <see cref="JobExecutor"/> holding a direct reference to the adapter will
    /// automatically benefit from reconnection (the adapter's internal TCP socket is
    /// replaced, but the object identity stays the same).
    /// </summary>
    public void NotifyConnectionLost(int printerId)
    {
        _logger.LogTrace("-> NotifyConnectionLost(printerId={PrinterId})", printerId);

        // Guard: reconnect loop already active for this printer
        if (_reconnectCts.TryGetValue(printerId, out var existingCts)
            && !existingCts.IsCancellationRequested)
        {
            _logger.LogDebug("NotifyConnectionLost: reconnect already active for printer {PrinterId}", printerId);
            return;
        }

        // Guard: adapter must exist (if removed via manual disconnect, skip)
        if (!_adapters.ContainsKey(printerId))
        {
            _logger.LogDebug("NotifyConnectionLost: no adapter for printer {PrinterId} (manually disconnected?)", printerId);
            return;
        }

        _disconnectedSince.TryAdd(printerId, DateTime.Now);

        // Notify UI that printer is offline
        PrinterStatusChanged?.Invoke(this,
            new PrinterStatusChangedEvent(printerId, PrinterStatus.Idle, PrinterStatus.Offline));

        _logger.LogWarning("Connection loss detected for printer {PrinterId}. Starting reconnect loop.", printerId);

        // Look up printer from DB and start reconnect loop (fire-and-forget)
        _ = StartReconnectFromNotificationAsync(printerId);
    }

    private async Task StartReconnectFromNotificationAsync(int printerId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var printer = await db.Printers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == printerId);
            if (printer == null)
            {
                _logger.LogWarning("NotifyConnectionLost: printer {PrinterId} not found in DB", printerId);
                return;
            }
            StartReconnectLoop(printer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start reconnect loop for printer {PrinterId}", printerId);
        }
    }

    public async Task DisconnectAsync(int printerId)
    {
        _logger.LogTrace("-> DisconnectAsync(printerId={PrinterId})", printerId);
        _logger.LogInformation("Disconnecting printer Id={PrinterId}", printerId);

        // Stop any ReadyWatchers for this printer before disposing the adapter.
        // They hold a direct reference to the adapter and would poll a disposed object.
        var stoppedJobIds = await _jobRegistry.StopWatchersForPrinterAsync(printerId);
        if (stoppedJobIds.Count > 0)
            _logger.LogInformation("Stopped {Count} ReadyWatcher(s) for printer {PrinterId} on disconnect: jobs [{JobIds}]",
                stoppedJobIds.Count, printerId, string.Join(", ", stoppedJobIds));

        // Stop any running executors for this printer before disposing the adapter.
        // Without this, executors keep polling a disposed adapter and loop forever.
        var stoppedExecutorIds = await _jobRegistry.StopExecutorsForPrinterAsync(printerId);
        if (stoppedExecutorIds.Count > 0)
            _logger.LogWarning("Stopped {Count} executor(s) for printer {PrinterId} on disconnect: jobs [{JobIds}]",
                stoppedExecutorIds.Count, printerId, string.Join(", ", stoppedExecutorIds));

        if (_reconnectCts.TryRemove(printerId, out var cts))
        {
            _logger.LogDebug("Cancelling reconnect loop for printer {PrinterId}", printerId);
            cts.Cancel();
        }

        if (_adapters.TryRemove(printerId, out var adapter))
        {
            await adapter.DisconnectAsync();
            _logger.LogDebug("Adapter removed for printer {PrinterId}", printerId);
            PrinterStatusChanged?.Invoke(this,
                new PrinterStatusChangedEvent(printerId, PrinterStatus.Idle, PrinterStatus.Offline));
        }

        _disconnectedSince.TryRemove(printerId, out _);
        _reconnectAttemptCounts.TryRemove(printerId, out _);
        _logger.LogTrace("<- DisconnectAsync completed");
    }

    private IPrinterAdapter CreateAdapter(string adapterType)
    {
        _logger.LogTrace("-> CreateAdapter(adapterType={AdapterType})", adapterType);
        var factory = _factories.FirstOrDefault(f => f.CanHandle(adapterType))
            ?? throw new InvalidOperationException($"No adapter factory for type '{adapterType}'");
        var adapter = factory.Create(adapterType);
        _logger.LogTrace("<- CreateAdapter completed (factory={FactoryType})", factory.GetType().Name);
        return adapter;
    }

    private void StartReconnectLoop(Printer printer)
    {
        _logger.LogTrace("-> StartReconnectLoop(printer='{Name}', id={PrinterId})", printer.Name, printer.Id);
        var cts = new CancellationTokenSource();
        _reconnectCts[printer.Id] = cts;
        _reconnectAttemptCounts[printer.Id] = 0;

        _logger.LogWarning("Reconnect loop STARTED for '{Name}' (Id={PrinterId}), initial delay=1000ms",
            printer.Name, printer.Id);

        _ = Task.Run(async () =>
        {
            try
            {
                var delay = 1000;
                const int maxDelay = 30000;

                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(delay, cts.Token);

                    var attemptNum = _reconnectAttemptCounts.AddOrUpdate(printer.Id, 1, (_, c) => c + 1);
                    var disconnectedFor = _disconnectedSince.TryGetValue(printer.Id, out var since)
                        ? (DateTime.Now - since).TotalSeconds : 0;

                    _logger.LogDebug("Reconnect attempt #{Attempt} for '{Name}' (Id={PrinterId}), delay={Delay}ms, disconnected for {DisconnectedSec:F1}s",
                        attemptNum, printer.Name, printer.Id, delay, disconnectedFor);

                    if (_adapters.TryGetValue(printer.Id, out var adapter))
                    {
                        var success = await adapter.ConnectAsync(printer.IpAddress, printer.Port, cts.Token);
                        if (success)
                        {
                            _logger.LogInformation(
                                "RECONNECTED to '{Name}' (Id={PrinterId}) after {Attempts} attempts ({DisconnectedSec:F1}s offline)",
                                printer.Name, printer.Id, attemptNum, disconnectedFor);
                            _disconnectedSince.TryRemove(printer.Id, out _);
                            _reconnectAttemptCounts.TryRemove(printer.Id, out _);
                            await CheckSerialNumberAsync(printer.Id, adapter);
                            PrinterStatusChanged?.Invoke(this,
                                new PrinterStatusChangedEvent(printer.Id, PrinterStatus.Offline, PrinterStatus.Idle));
                            _reconnectCts.TryRemove(printer.Id, out _);
                            return;
                        }
                        else
                        {
                            _logger.LogDebug("Reconnect attempt #{Attempt} FAILED for '{Name}' (Id={PrinterId}), next delay={NextDelay}ms",
                                attemptNum, printer.Name, printer.Id, Math.Min(delay * 2, maxDelay));
                        }
                    }

                    delay = Math.Min(delay * 2, maxDelay);
                }
            }
            catch (OperationCanceledException) { /* expected on shutdown / manual disconnect */ }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Reconnect loop for printer '{Name}' (Id={PrinterId}) crashed unexpectedly. " +
                    "Reconnection will not be retried automatically.",
                    printer.Name, printer.Id);
            }
        }, cts.Token);

        _logger.LogTrace("<- StartReconnectLoop (background task launched)");
    }

    /// <summary>
    /// Read the printer's serial number (SPGGSN) and compare with the stored value.
    /// On first connect, stores the serial. On subsequent connects, detects hardware swaps.
    /// </summary>
    private async Task CheckSerialNumberAsync(int printerId, IPrinterAdapter adapter)
    {
        _logger.LogTrace("-> CheckSerialNumberAsync(printerId={PrinterId})", printerId);
        try
        {
            var serial = await adapter.GetSerialNumberAsync();
            if (string.IsNullOrEmpty(serial))
            {
                _logger.LogDebug("Printer {PrinterId}: serial number not available (returned null/empty)", printerId);
                _logger.LogTrace("<- CheckSerialNumberAsync (no serial)");
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var printer = await db.Printers.FindAsync(printerId);
            if (printer == null)
            {
                _logger.LogWarning("Printer {PrinterId}: not found in DB during serial check", printerId);
                _logger.LogTrace("<- CheckSerialNumberAsync (printer not in DB)");
                return;
            }

            if (string.IsNullOrEmpty(printer.SerialNumber))
            {
                // First time — record the serial number
                printer.SerialNumber = serial;
                await db.SaveChangesAsync();
                _logger.LogInformation(
                    "Printer {PrinterId} '{Name}': serial number RECORDED = {Serial}", printerId, printer.Name, serial);
            }
            else if (!string.Equals(printer.SerialNumber, serial, StringComparison.Ordinal))
            {
                // MISMATCH — hardware swap detected!
                _serialMismatchFlags[printerId] = true;
                _logger.LogError(
                    "Printer {PrinterId} '{Name}': SERIAL NUMBER MISMATCH! Expected={Expected}, Got={Actual}. " +
                    "Hardware swap suspected - blocking job operations.",
                    printerId, printer.Name, printer.SerialNumber, serial);

                _alerts.Raise(AlertSeverity.Error, printer.Name,
                    $"Hardware swap detected! Serial changed from {printer.SerialNumber} to {serial}. " +
                    $"Job operations blocked until confirmed.",
                    printerId: printerId);
            }
            else
            {
                // Serial matches — clear any stale mismatch flag
                _serialMismatchFlags.TryRemove(printerId, out _);
                _logger.LogDebug(
                    "Printer {PrinterId} '{Name}': serial number VERIFIED = {Serial}", printerId, printer.Name, serial);
            }
            _logger.LogTrace("<- CheckSerialNumberAsync completed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Printer {PrinterId}: failed to read/verify serial number", printerId);
            _logger.LogTrace("<- CheckSerialNumberAsync FAILED");
        }
    }

    /// <summary>
    /// Attempts a single reconnect to the printer. Returns true if connected successfully.
    /// Used by job preparation to recover from connection drops after template activation.
    /// </summary>
    public async Task<bool> TryReconnectAsync(int printerId, CancellationToken ct = default)
    {
        _logger.LogTrace("-> TryReconnectAsync(printerId={PrinterId})", printerId);
        if (!_adapters.TryGetValue(printerId, out var adapter))
            return false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var printer = await db.Printers.FindAsync(new object[] { printerId }, ct);
        if (printer == null)
            return false;

        var success = await adapter.ConnectAsync(printer.IpAddress, printer.Port, ct);
        if (success)
        {
            _logger.LogInformation("TryReconnect: printer '{Name}' (Id={PrinterId}) reconnected", printer.Name, printerId);
            await CheckSerialNumberAsync(printerId, adapter);
            PrinterStatusChanged?.Invoke(this,
                new PrinterStatusChangedEvent(printerId, PrinterStatus.Offline, PrinterStatus.Idle));
        }
        _logger.LogTrace("<- TryReconnectAsync = {Success}", success);
        return success;
    }

    public void Dispose()
    {
        _logger.LogTrace("-> Dispose()");
        _logger.LogInformation("PrinterConnectionManager disposing: {AdapterCount} adapters, {ReconnectCount} reconnect loops",
            _adapters.Count, _reconnectCts.Count);

        foreach (var cts in _reconnectCts.Values)
            cts.Cancel();

        foreach (var adapter in _adapters.Values)
            adapter.Dispose();

        _adapters.Clear();
        _reconnectCts.Clear();
        _reconnectAttemptCounts.Clear();
        _disconnectedSince.Clear();
        GC.SuppressFinalize(this);
        _logger.LogTrace("<- Dispose completed");
    }
}
