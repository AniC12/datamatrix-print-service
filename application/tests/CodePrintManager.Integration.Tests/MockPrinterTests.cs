using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

public class MockPrinterTests : IntegrationTestBase
{


    [Fact]
    public async Task MockPrinterState_InspectableViaApi()
    {
        var printerId = await SetupPrinterAsync("InspectPrinter");

        var state = await Client.GetFromJsonAsync<MockPrinterState>($"/api/mock/printers/{printerId}");
        Assert.NotNull(state);
        Assert.True(state.IsConnected);
        Assert.Equal("Idle", state.Status);
        Assert.Equal(0, state.CurrentCounter);
        Assert.Equal(0, state.LifetimeCounter);
    }

    [Fact]
    public async Task InjectError_MockReportsErrorStatus()
    {
        var printerId = await SetupPrinterAsync("ErrorPrinter");

        // Inject error
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/inject-error", new { Status = "Error" });

        // Check via normal printer endpoint
        var printer = await Client.GetFromJsonAsync<PrinterDetailResult>($"/api/printers/{printerId}");
        Assert.Equal("Error", printer!.Status);

        // Clear error
        await Client.PostAsync($"/api/mock/printers/{printerId}/clear-error", null);

        var printerAfter = await Client.GetFromJsonAsync<PrinterDetailResult>($"/api/printers/{printerId}");
        Assert.Equal("Idle", printerAfter!.Status);
    }

    [Fact]
    public async Task SetSpeed_AffectsPrintDuration()
    {
        var printerId = await SetupPrinterAsync("SpeedPrinter");
        var productId = await SetupProductAsync("SpeedProduct", "speed.csv");
        await ImportCodesAsync(productId, 10);

        // Set very fast speed
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 5 });

        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 5
        });
        var job = await createResponse.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        // Should complete very quickly (5 codes * 5ms = 25ms + overhead)
        var completed = await PollUntilAsync<JobResult>(
            $"/api/jobs/{job.Id}",
            j => j.Status == "Completed",
            timeout: TimeSpan.FromSeconds(5));

        Assert.Equal("Completed", completed.Status);
    }

    [Fact]
    public async Task PrinterStorage_ShowsUploadedFiles()
    {
        var printerId = await SetupPrinterAsync("StoragePrinter");
        var productId = await SetupProductAsync("StorageProduct", "storage.csv");
        await ImportCodesAsync(productId, 10);

        // Create a job (which triggers CSV upload to mock printer)
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 5 });
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 5
        });
        createResponse.EnsureSuccessStatusCode();

        // Check storage
        var storage = await Client.GetFromJsonAsync<StorageResult>($"/api/printers/{printerId}/storage");
        Assert.NotNull(storage);
        Assert.NotEmpty(storage.CsvFiles);
    }
}

public record MockPrinterState
{
    public bool IsConnected { get; init; }
    public string Status { get; init; } = "";
    public int CurrentCounter { get; init; }
    public int LifetimeCounter { get; init; }
    public string? ActiveTemplate { get; init; }
    public List<string>? StoredTemplates { get; init; }
    public List<string>? StoredCsvFiles { get; init; }
    public int PrintSpeedMs { get; init; }
}

public record PrinterDetailResult
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Status { get; init; } = "";
    public bool IsConnected { get; init; }
}

public record StorageResult
{
    public List<string> Templates { get; init; } = new();
    public List<string> CsvFiles { get; init; } = new();
}
