using CodePrintManager.Application.Services;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Printer.Mock;

namespace CodePrintManager.TestHost.Endpoints;

public static class MockControlEndpoints
{
    public static void MapMockControlEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/mock/printers");

        group.MapGet("/{id:int}", (int id, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null)
                return Results.NotFound(new { Error = "Mock printer not found or not connected" });

            return Results.Ok(new
            {
                IsConnected = adapter.IsConnected,
                Status = adapter.CurrentState.ToString(),
                CurrentCounter = adapter.InspectCurrentCounter,
                LifetimeCounter = adapter.InspectLifetimeCounter,
                ActiveTemplate = adapter.InspectActiveTemplate,
                StoredTemplates = adapter.StoredTemplates,
                StoredCsvFiles = adapter.StoredCsvFiles,
                PrintSpeedMs = adapter.PrintSpeedMs
            });
        });

        group.MapPost("/{id:int}/inject-error", (int id, InjectErrorRequest req, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            var status = Enum.Parse<PrinterStatus>(req.Status, ignoreCase: true);
            adapter.InjectError(status);
            return Results.Ok(new { InjectedError = status.ToString() });
        });

        group.MapPost("/{id:int}/clear-error", (int id, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            adapter.ClearError();
            return Results.Ok(new { Status = "Error cleared" });
        });

        group.MapPost("/{id:int}/set-speed", (int id, SetSpeedRequest req, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            adapter.PrintSpeedMs = req.Ms;
            return Results.Ok(new { PrintSpeedMs = adapter.PrintSpeedMs });
        });

        group.MapPost("/{id:int}/reset-counter", (int id, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            adapter.SimulatePowerCycle();
            return Results.Ok(new { Status = "Power cycle simulated", CurrentCounter = 0 });
        });

        group.MapPost("/{id:int}/set-counters", (int id, SetCountersRequest req, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            adapter.SetCounters(req.CurrentCounter, req.LifetimeCounter);
            return Results.Ok(new { CurrentCounter = req.CurrentCounter, LifetimeCounter = req.LifetimeCounter });
        });

        group.MapPost("/{id:int}/set-serial", (int id, SetSerialRequest req, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            adapter.SerialNumber = req.Serial;
            return Results.Ok(new { SerialNumber = adapter.SerialNumber });
        });

        // Set the serial number that the NEXT created adapter will use.
        // Useful for simulating hardware swaps across disconnect/reconnect cycles.
        group.MapPost("/factory/set-next-serial", (SetSerialRequest req, MockPrinterAdapterFactory factory) =>
        {
            factory.NextSerialNumber = req.Serial;
            return Results.Ok(new { NextSerialNumber = factory.NextSerialNumber });
        });
    }
}

public record InjectErrorRequest(string Status);
public record SetSpeedRequest(int Ms);
public record SetSerialRequest(string Serial);
public record SetCountersRequest(int CurrentCounter, int LifetimeCounter);
