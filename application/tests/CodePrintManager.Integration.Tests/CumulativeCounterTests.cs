using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

/// <summary>
/// Tests that the application correctly handles cumulative SPGGCP counters.
/// Real Savema printers do NOT reset SPGGCP on template load (SPLLTF).
/// The app uses baseline-delta tracking to handle this.
/// </summary>
public class CumulativeCounterTests : IntegrationTestBase
{
    [Fact]
    public async Task PrintJob_WithHighCumulativeCounter_CompletesCorrectly()
    {
        // Simulate a printer that has been used extensively — counters start at 5000
        var printerId = await SetupPrinterAsync("CumulativePrinter1");
        var productId = await SetupProductAsync("CumulativeProduct1", "cumul1.csv");
        await ImportCodesAsync(productId, 10);

        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 10 });
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-counters",
            new { CurrentCounter = 5000, LifetimeCounter = 5000 });

        // Create and prepare the job
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 3
        });
        createResponse.EnsureSuccessStatusCode();
        var job = await createResponse.Content.ReadFromJsonAsync<JobResult>();
        Assert.NotNull(job);

        // Start printing
        var startResponse = await Client.PostAsync($"/api/jobs/{job.Id}/start", null);
        startResponse.EnsureSuccessStatusCode();

        // Poll until completed — should show 3/3, not jump to 5003
        var completed = await PollUntilAsync<JobResult>(
            $"/api/jobs/{job.Id}",
            j => j.Status == "Completed",
            timeout: TimeSpan.FromSeconds(10));

        Assert.Equal("Completed", completed.Status);
        Assert.Equal(3, completed.CodesConfirmed);
    }

    [Fact]
    public async Task PrintJob_WithCumulativeCounter_NoFalseCounterMismatch()
    {
        // With cumulative SPGGCP, the cross-check (SPGGTP delta vs effective counter)
        // should NOT produce false "counter mismatch" anomalies
        var printerId = await SetupPrinterAsync("CumulativePrinter2");
        var productId = await SetupProductAsync("CumulativeProduct2", "cumul2.csv");
        await ImportCodesAsync(productId, 10);

        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 50 });
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-counters",
            new { CurrentCounter = 2392, LifetimeCounter = 2392 });

        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 5
        });
        createResponse.EnsureSuccessStatusCode();
        var job = await createResponse.Content.ReadFromJsonAsync<JobResult>();

        var startResponse = await Client.PostAsync($"/api/jobs/{job.Id}/start", null);
        startResponse.EnsureSuccessStatusCode();

        var completed = await PollUntilAsync<JobResult>(
            $"/api/jobs/{job.Id}",
            j => j.Status == "Completed",
            timeout: TimeSpan.FromSeconds(15));

        Assert.Equal("Completed", completed.Status);
        Assert.Equal(5, completed.CodesConfirmed);
    }

    [Fact]
    public async Task ReadyWatcher_WithCumulativeCounter_NoFalseExternalPrintDetection()
    {
        // After Prepare, the ReadyWatcher should NOT fire a false "external print"
        // alert just because SPGGCP > 0 (it's cumulative)
        var printerId = await SetupPrinterAsync("CumulativePrinter3");
        var productId = await SetupProductAsync("CumulativeProduct3", "cumul3.csv");
        await ImportCodesAsync(productId, 10);

        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-counters",
            new { CurrentCounter = 3000, LifetimeCounter = 3000 });

        // Create and prepare the job (this spawns a ReadyWatcher)
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 3
        });
        createResponse.EnsureSuccessStatusCode();
        var job = await createResponse.Content.ReadFromJsonAsync<JobResult>();

        // Wait long enough for the ReadyWatcher to poll (initial 2s delay + 3s interval)
        await Task.Delay(6000);

        // Job should still be in Ready state — NOT auto-transitioned to Printing
        var jobStatus = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job.Id}");
        Assert.NotNull(jobStatus);
        Assert.Equal("Ready", jobStatus.Status);
    }

    [Fact]
    public async Task PowerCycleDetection_CounterGoesBackward_DetectedByExecutor()
    {
        // When SPGGCP goes backward (power cycle), the executor should detect it
        var printerId = await SetupPrinterAsync("CumulativePrinter4");
        var productId = await SetupProductAsync("CumulativeProduct4", "cumul4.csv");
        await ImportCodesAsync(productId, 20);

        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 200 });
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-counters",
            new { CurrentCounter = 1000, LifetimeCounter = 1000 });

        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 10
        });
        createResponse.EnsureSuccessStatusCode();
        var job = await createResponse.Content.ReadFromJsonAsync<JobResult>();

        // Start printing and let a few codes print
        await Client.PostAsync($"/api/jobs/{job.Id}/start", null);
        await Task.Delay(1500); // ~7 codes at 200ms each

        // Simulate power cycle (counter goes to 0)
        await Client.PostAsync($"/api/mock/printers/{printerId}/reset-counter", null);

        // The executor should detect the backward movement and set job to Error
        var errorJob = await PollUntilAsync<JobResult>(
            $"/api/jobs/{job.Id}",
            j => j.Status == "Error" || j.Status == "Completed",
            timeout: TimeSpan.FromSeconds(15));

        // Power cycle should be detected (counter went from ~1007 to 0)
        Assert.Equal("Error", errorJob.Status);
    }
}
