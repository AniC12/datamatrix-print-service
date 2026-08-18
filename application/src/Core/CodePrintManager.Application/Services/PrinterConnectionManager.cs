using System.Collections.Concurrent;
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
    }

    /// <summary>Returns true if the printer has a serial number mismatch (hardware swap detected).</summary>
    public bool HasSerialMismatch(int printerId)
        => _serialMismatchFlags.ContainsKey(printerId);

    public IPrinterAdapter? GetAdapter(int printerId)
        => _adapters.TryGetValue(printerId, out var adapter) ? adapter : null;

    public async Task ConnectAsync(Printer printer, CancellationToken ct = default)
    {
        _logger.LogInformation("Connecting printer '{Name}' (Id={PrinterId}) at {Ip}:{Port}, adapter={AdapterType}",
            printer.Name, printer.Id, printer.IpAddress, printer.Port, printer.AdapterType);

        var adapter = CreateAdapter(printer.AdapterType);
        _adapters[printer.Id] = adapter;

        var success = await adapter.ConnectAsync(printer.IpAddress, printer.Port, ct);
        var status = success ? PrinterStatus.Idle : PrinterStatus.Offline;
        PrinterStatusChanged?.Invoke(this, new PrinterStatusChangedEvent(printer.Id, PrinterStatus.Offline, status));

        if (success)
        {
            _logger.LogInformation("Printer '{Name}' (Id={PrinterId}) connected", printer.Name, printer.Id);
            await CheckSerialNumberAsync(printer.Id, adapter);
        }
        else
        {
            _logger.LogWarning("Printer '{Name}' (Id={PrinterId}) connection failed, starting reconnect",
                printer.Name, printer.Id);
            StartReconnectLoop(printer);
        }
    }

    public async Task DisconnectAsync(int printerId)
    {
        _logger.LogInformation("Disconnecting printer Id={PrinterId}", printerId);

        if (_reconnectCts.TryRemove(printerId, out var cts))
            cts.Cancel();

        if (_adapters.TryRemove(printerId, out var adapter))
            await adapter.DisconnectAsync();
    }

    private IPrinterAdapter CreateAdapter(string adapterType)
    {
        _logger.LogDebug("Creating adapter for type '{AdapterType}'", adapterType);
        var factory = _factories.FirstOrDefault(f => f.CanHandle(adapterType))
            ?? throw new InvalidOperationException($"No adapter factory for type '{adapterType}'");
        return factory.Create(adapterType);
    }

    private void StartReconnectLoop(Printer printer)
    {
        var cts = new CancellationTokenSource();
        _reconnectCts[printer.Id] = cts;

        _logger.LogWarning("Reconnect loop started for '{Name}' (Id={PrinterId}), initial delay=1000ms",
            printer.Name, printer.Id);

        _ = Task.Run(async () =>
        {
            var delay = 1000;
            const int maxDelay = 30000;

            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(delay, cts.Token);

                _logger.LogDebug("Reconnect attempt for '{Name}' (Id={PrinterId}), delay={Delay}ms",
                    printer.Name, printer.Id, delay);

                if (_adapters.TryGetValue(printer.Id, out var adapter))
                {
                    var success = await adapter.ConnectAsync(printer.IpAddress, printer.Port, cts.Token);
                    if (success)
                    {
                        _logger.LogInformation("Reconnected to '{Name}' (Id={PrinterId})", printer.Name, printer.Id);
                        await CheckSerialNumberAsync(printer.Id, adapter);
                        PrinterStatusChanged?.Invoke(this,
                            new PrinterStatusChangedEvent(printer.Id, PrinterStatus.Offline, PrinterStatus.Idle));
                        _reconnectCts.TryRemove(printer.Id, out _);
                        return;
                    }
                }

                delay = Math.Min(delay * 2, maxDelay);
            }
        }, cts.Token);
    }

    /// <summary>
    /// Read the printer's serial number (SPGGSN) and compare with the stored value.
    /// On first connect, stores the serial. On subsequent connects, detects hardware swaps.
    /// </summary>
    private async Task CheckSerialNumberAsync(int printerId, IPrinterAdapter adapter)
    {
        try
        {
            var serial = await adapter.GetSerialNumberAsync();
            if (string.IsNullOrEmpty(serial))
            {
                _logger.LogDebug("Printer {PrinterId}: serial number not available", printerId);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var printer = await db.Printers.FindAsync(printerId);
            if (printer == null) return;

            if (string.IsNullOrEmpty(printer.SerialNumber))
            {
                // First time — record the serial number
                printer.SerialNumber = serial;
                await db.SaveChangesAsync();
                _logger.LogInformation(
                    "Printer {PrinterId}: serial number recorded = {Serial}", printerId, serial);
            }
            else if (!string.Equals(printer.SerialNumber, serial, StringComparison.Ordinal))
            {
                // MISMATCH — hardware swap detected!
                _serialMismatchFlags[printerId] = true;
                _logger.LogError(
                    "Printer {PrinterId}: SERIAL NUMBER MISMATCH! Expected={Expected}, Got={Actual}. " +
                    "Hardware swap suspected — blocking job operations.",
                    printerId, printer.SerialNumber, serial);

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
                    "Printer {PrinterId}: serial number verified = {Serial}", printerId, serial);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Printer {PrinterId}: failed to read serial number", printerId);
        }
    }

    public void Dispose()
    {
        _logger.LogDebug("PrinterConnectionManager disposing: {AdapterCount} adapters, {ReconnectCount} reconnect loops",
            _adapters.Count, _reconnectCts.Count);

        foreach (var cts in _reconnectCts.Values)
            cts.Cancel();

        foreach (var adapter in _adapters.Values)
            adapter.Dispose();

        _adapters.Clear();
        _reconnectCts.Clear();
        GC.SuppressFinalize(this);
    }
}
