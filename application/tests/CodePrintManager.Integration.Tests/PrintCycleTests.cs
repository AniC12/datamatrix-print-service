using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

public class PrintCycleTests : IntegrationTestBase
{


    [Fact]
    public async Task FullPrintCycle_CreatesPreparesStartsAndCompletes()
    {
        // Setup
        var printerId = await SetupPrinterAsync("Printer1");
        var productId = await SetupProductAsync("Product1", "batch1.csv");
        await ImportCodesAsync(productId, 10);

        // Speed up mock printer for testing (10ms per code instead of 500ms)
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 10 });

        // Create job (also prepares it)
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 5
        });
        createResponse.EnsureSuccessStatusCode();
        var job = await createResponse.Content.ReadFromJsonAsync<JobResult>();
        Assert.NotNull(job);
        Assert.True(job.Id > 0);

        // Start printing
        var startResponse = await Client.PostAsync($"/api/jobs/{job.Id}/start", null);
        startResponse.EnsureSuccessStatusCode();

        // Poll until completed
        var completed = await PollUntilAsync<JobResult>(
            $"/api/jobs/{job.Id}",
            j => j.Status == "Completed",
            timeout: TimeSpan.FromSeconds(10));

        Assert.Equal("Completed", completed.Status);
        Assert.Equal(5, completed.CodesConfirmed);
    }

    [Fact]
    public async Task CreateJob_InsufficientCodes_ReturnsBadRequest()
    {
        var printerId = await SetupPrinterAsync("Printer2");
        var productId = await SetupProductAsync("Product2", "batch2.csv");
        await ImportCodesAsync(productId, 3);

        // Try to create a job needing more codes than available
        var response = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 10
        });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetActiveJobs_ReturnsOnlyRunningJobs()
    {
        var printerId = await SetupPrinterAsync("Printer3");
        var productId = await SetupProductAsync("Product3", "batch3.csv");
        await ImportCodesAsync(productId, 20);

        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 200 });

        // Create and start a job
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 10
        });
        var job = await createResponse.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        // Check active jobs
        var activeJobs = await Client.GetFromJsonAsync<List<JobResult>>("/api/jobs/active");
        Assert.NotNull(activeJobs);
        Assert.Contains(activeJobs, j => j.Id == job.Id && j.Status == "Printing");

        // Cancel it and check again
        await Client.PostAsync($"/api/jobs/{job.Id}/cancel", null);
        await Task.Delay(200);
        var afterCancel = await Client.GetFromJsonAsync<List<JobResult>>("/api/jobs/active");
        Assert.DoesNotContain(afterCancel!, j => j.Id == job.Id);
    }
}

public record JobResult
{
    public int Id { get; init; }
    public string Status { get; init; } = "";
    public int Quantity { get; init; }
    public int CodesConfirmed { get; init; }
    public int? ProductId { get; init; }
    public int? PrinterId { get; init; }
    public string? ProductName { get; init; }
    public string? PrinterName { get; init; }
}
