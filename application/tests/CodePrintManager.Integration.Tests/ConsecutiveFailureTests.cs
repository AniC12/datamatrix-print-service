using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

/// <summary>
/// Integration tests for consecutive failure escalation in the JobExecutor.
/// J1: Prolonged IO failures exceed MaxConsecutiveFailures → job goes to Error.
/// J2: Intermittent failures below threshold → executor recovers → job completes.
/// </summary>
public class ConsecutiveFailureTests : IntegrationTestBase
{
    // ──────────────────────────────────────────────
    // J1. Prolonged IO failures → MaxConsecutiveFailures → Error
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Slow")]
    public async Task J1_ProlongedIOFailures_ExceedsMaxConsecutiveFailures_JobErrors()
    {
        var printerId = await SetupPrinterAsync("J1FailurePrinter");
        var productId = await SetupProductAsync("J1FailureProduct", "j1.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 200);

        // Start printing 10 codes
        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for 2+ confirmed
        await WaitForProgressAsync(jobId, 2);

        // Inject 100 IO failures (well above MaxConsecutiveFailures=30)
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/inject-io-failure",
            new { Count = 100 });

        // Wait for job to go to Error (may take ~60s because of 2s retry delay)
        var errorJob = await WaitForJobStatusAsync(jobId, "Error",
            TimeSpan.FromSeconds(120));

        // Assert: job status = Error
        Assert.Equal("Error", errorJob.Status);

        // Assert: remaining codes quarantined, conservation holds
        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(0, stats.Reserved);
        Assert.True(stats.Quarantined >= 1,
            $"Expected at least 1 quarantined code after max consecutive failures, got {stats.Quarantined}");
        Assert.True(stats.Printed >= 1,
            "Expected at least some codes printed before failure");
    }

    // ──────────────────────────────────────────────
    // J2. Intermittent failures (below threshold) → recovery → job continues
    // ──────────────────────────────────────────────

    [Fact]
    public async Task J2_IntermittentFailures_BelowThreshold_JobCompletes()
    {
        var printerId = await SetupPrinterAsync("J2IntermittentPrinter");
        var productId = await SetupProductAsync("J2IntermittentProduct", "j2.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 200);

        // Start printing 10 codes
        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for 2+ confirmed
        await WaitForProgressAsync(jobId, 2);

        // Inject 5 IO failures (well below threshold of 30)
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/inject-io-failure",
            new { Count = 5 });

        // Wait for executor to retry through them and complete
        await WaitForJobStatusAsync(jobId, "Completed", TimeSpan.FromSeconds(30));

        // Assert: 10 printed, 0 quarantined, conservation holds
        var finalJob = await GetJobAsync(jobId);
        Assert.Equal("Completed", finalJob!.Status);
        Assert.Equal(10, finalJob.CodesConfirmed);

        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(10, stats.Printed);
        Assert.Equal(0, stats.Quarantined);
        Assert.Equal(0, stats.Reserved);
    }
}
