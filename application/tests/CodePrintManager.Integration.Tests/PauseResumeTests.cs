using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

public class PauseResumeTests : IntegrationTestBase
{


    [Fact]
    public async Task PauseAndResume_ContinuesFromCorrectPosition()
    {
        var printerId = await SetupPrinterAsync("PauseResumePrinter");
        var productId = await SetupProductAsync("PauseResumeProduct", "pr.csv");
        await ImportCodesAsync(productId, 20);

        // Set moderate speed so we can pause mid-print
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 100 });

        // Create + start
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 10
        });
        var job = await createResponse.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        // Wait for some progress
        var midway = await PollUntilAsync<JobResult>(
            $"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 3,
            timeout: TimeSpan.FromSeconds(10));

        // Pause
        var pauseResponse = await Client.PostAsync($"/api/jobs/{job.Id}/pause", null);
        pauseResponse.EnsureSuccessStatusCode();

        // Verify paused state
        await Task.Delay(200);
        var pausedJob = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job.Id}");
        Assert.Equal("Paused", pausedJob!.Status);
        var pausedAt = pausedJob.CodesConfirmed;
        Assert.True(pausedAt >= 3);

        // Resume
        var resumeResponse = await Client.PostAsync($"/api/jobs/{job.Id}/resume", null);
        resumeResponse.EnsureSuccessStatusCode();

        // Wait for completion
        var completed = await PollUntilAsync<JobResult>(
            $"/api/jobs/{job.Id}",
            j => j.Status == "Completed",
            timeout: TimeSpan.FromSeconds(15));

        Assert.Equal(10, completed.CodesConfirmed);
    }

    [Fact]
    public async Task Pause_WhenNotPrinting_ReturnsBadRequest()
    {
        var printerId = await SetupPrinterAsync("PauseFailPrinter");
        var productId = await SetupProductAsync("PauseFailProduct", "pf.csv");
        await ImportCodesAsync(productId, 10);

        // Create job but don't start
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 5
        });
        var job = await createResponse.Content.ReadFromJsonAsync<JobResult>();

        // Try to pause a Ready (not Printing) job
        var pauseResponse = await Client.PostAsync($"/api/jobs/{job!.Id}/pause", null);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, pauseResponse.StatusCode);
    }
}
