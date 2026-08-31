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

        // ─── New fault injection endpoints ───

        group.MapPost("/{id:int}/inject-io-failure", (int id, InjectIOFailureRequest req, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            adapter.InjectIOFailure(req.Count);
            return Results.Ok(new { IOFailuresQueued = req.Count });
        });

        group.MapPost("/{id:int}/clear-io-failure", (int id, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            adapter.ClearIOFailure();
            return Results.Ok(new { Status = "IO failures cleared" });
        });

        group.MapPost("/{id:int}/power-cycle-while-printing", (int id, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            adapter.SimulatePowerCycleWhilePrinting();
            return Results.Ok(new
            {
                Status = "Power cycle simulated while printing",
                CurrentCounter = adapter.InspectCurrentCounter,
                LifetimeCounter = adapter.InspectLifetimeCounter,
                ActiveTemplate = adapter.InspectActiveTemplate
            });
        });

        group.MapPost("/{id:int}/inject-blocked", (int id, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            adapter.InjectBlockedState();
            return Results.Ok(new { Status = "BLOCKED state injected" });
        });

        group.MapPost("/{id:int}/clear-blocked", (int id, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            adapter.ClearBlockedState();
            return Results.Ok(new { Status = "BLOCKED state cleared" });
        });

        group.MapPost("/{id:int}/external-template-load", (int id, ExternalTemplateRequest req, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            adapter.SimulateExternalTemplateLoad(req.Template);
            return Results.Ok(new { ActiveTemplate = req.Template });
        });

        group.MapPost("/{id:int}/external-prints", (int id, ExternalPrintsRequest req, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            adapter.SimulateExternalPrints(req.Count);
            return Results.Ok(new
            {
                ExternalPrints = req.Count,
                CurrentCounter = adapter.InspectCurrentCounter,
                LifetimeCounter = adapter.InspectLifetimeCounter
            });
        });

        // Simulate TCP connection drop without removing the adapter from
        // PrinterConnectionManager. The adapter stays registered but throws
        // IOException on all commands until TryReconnectAsync reconnects it in-place.
        // Use this instead of /api/printers/{id}/disconnect for network-drop tests.
        group.MapPost("/{id:int}/network-drop", (int id, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            adapter.SimulateNetworkDrop();
            return Results.Ok(new { Status = "Network drop simulated", IsConnected = adapter.IsConnected });
        });

        group.MapPost("/{id:int}/delete-csv", (int id, DeleteCsvRequest req, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            adapter.DeleteCsvFromStorage(req.Filename);
            return Results.Ok(new { Deleted = req.Filename });
        });

        group.MapGet("/{id:int}/csv-contents/{filename}", (int id, string filename, PrinterConnectionManager connMgr) =>
        {
            var adapter = connMgr.GetAdapter(id) as MockPrinterAdapter;
            if (adapter == null) return Results.NotFound();

            var contents = adapter.InspectCsvContents(filename);
            if (contents == null) return Results.NotFound(new { Error = $"CSV '{filename}' not found" });

            return Results.Ok(new { Filename = filename, Codes = contents, Count = contents.Count });
        });
    }
}

public record InjectErrorRequest(string Status);
public record SetSpeedRequest(int Ms);
public record SetSerialRequest(string Serial);
public record SetCountersRequest(int CurrentCounter, int LifetimeCounter);
public record InjectIOFailureRequest(int Count);
public record ExternalTemplateRequest(string Template);
public record ExternalPrintsRequest(int Count);
public record DeleteCsvRequest(string Filename);
