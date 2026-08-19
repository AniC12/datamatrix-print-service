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
    private readonly ILogger<PrinterConnectionManager> _logger;

    public event EventHandler<PrinterStatusChangedEvent>? PrinterStatusChanged;

    public PrinterConnectionManager(
        IEnumerable<IPrinterAdapterFactory> factories,
        IServiceScopeFactory scopeFactory,
        IAlertService alerts,
        ILogger<PrinterConnectionManager> logger)
    {
        _factories = factories;
        _scopeFactory = scopeFactory;
        _alerts = alerts;
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

        var adapter = CreateAdapter(printer.AdapterType);
        _adapters[printer.Id] = adapter;

        var success = await adapter.ConnectAsync(printer.IpAddress, printer.Port, ct);
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

    public async Task DisconnectAsync(int printerId)
    {
        _logger.LogTrace("-> DisconnectAsync(printerId={PrinterId})", printerId);
        _logger.LogInformation("Disconnecting printer Id={PrinterId}", printerId);

        if (_reconnectCts.TryRemove(printerId, out var cts))
        {
            _logger.LogDebug("Cancelling reconnect loop for printer {PrinterId}", printerId);
            cts.Cancel();
        }

        if (_adapters.TryRemove(printerId, out var adapter))
        {
            await adapter.DisconnectAsync();
            _logger.LogDebug("Adapter removed for printer {PrinterId}", printerId);
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
