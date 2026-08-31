using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

/// <summary>
/// Tests for the most common real-world operator workflow:
/// "start printing → printer goes unreachable → what do we do?"
/// </summary>
public class ManualInterventionTests : IntegrationTestBase
{
    /// <summary>
    /// I1. Disconnect → operator pauses (while disconnected) → reconnect → resume → complete.
    /// This is the primary manual intervention path that operators will use.
    /// </summary>
    [Fact]
    public async Task I1_Disconnect_PauseWhileDisconnected_Reconnect_Resume_Complete()
    {
        var printerId = await SetupPrinterAsync("I1Printer");
        var productId = await SetupProductAsync("I1Product", "i1.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 200);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for progress
        await WaitForProgressAsync(jobId, 4);

        // Disconnect printer
        await Client.PostAsync($"/api/printers/{printerId}/disconnect", null);

        // Operator pauses the job while disconnected — should succeed using CodesConfirmed fallback
        await PauseJobAsync(jobId);

        // Verify job is Paused
        var pausedJob = await GetJobAsync(jobId);
        Assert.Equal("Paused", pausedJob!.Status);
        Assert.True(pausedJob.CodesConfirmed >= 4,
            $"Expected at least 4 confirmed, got {pausedJob.CodesConfirmed}");

        // Reconnect printer
        await Client.PostAsync($"/api/printers/{printerId}/connect", null);
        await SetPrintSpeedAsync(printerId, 50);

        // Resume — should rebuild CSV with remaining codes and continue
        await ResumeJobAsync(jobId);

        // Wait for completion
        await WaitForJobStatusAsync(jobId, "Completed", TimeSpan.FromSeconds(30));

        var finalJob = await GetJobAsync(jobId);
        Assert.Equal("Completed", finalJob!.Status);
        Assert.Equal(10, finalJob.CodesConfirmed);

        // Verify code accounting
        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(10, stats.Printed);
        Assert.Equal(0, stats.Reserved);
        Assert.Equal(0, stats.Quarantined);
    }

    /// <summary>
    /// I2. Disconnect → operator cancels → verify quarantine → start new job with remaining codes.
    /// Tests the clean restart path after an abort.
    /// </summary>
    [Fact]
    public async Task I2_Disconnect_Cancel_Quarantine_StartNewJob()
    {
        var printerId = await SetupPrinterAsync("I2Printer");
        await Client.PatchAsJsonAsync($"/api/printers/{printerId}", new { QuarantineMargin = 2 });
        var productId = await SetupProductAsync("I2Product", "i2.csv");
        await ImportCodesAsync(productId, 20);
        await SetPrintSpeedAsync(printerId, 200);

        // Start first job for 10 codes
        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for progress
        await WaitForProgressAsync(jobId, 4);

        // Disconnect
        await Client.PostAsync($"/api/printers/{printerId}/disconnect", null);

        // Cancel the job
        await CancelJobAsync(jobId);

        // Verify quarantine
        var stats1 = await AssertCodeConservationAsync(productId, 20);
        Assert.True(stats1.Quarantined >= 2,
            $"Expected quarantine >= 2 (margin=2), got {stats1.Quarantined}");
        Assert.Equal(0, stats1.Reserved);

        // Assert first job is Cancelled
        var cancelledJob = await GetJobAsync(jobId);
        Assert.Equal("Cancelled", cancelledJob!.Status);

        // Reconnect and start a new job using the remaining available codes
        await Client.PostAsync($"/api/printers/{printerId}/connect", null);
        await SetPrintSpeedAsync(printerId, 50);

        var availableCodes = stats1.Available;
        Assert.True(availableCodes > 0, "Should have available codes for a new job");

        // Create a new job for the available codes (use smaller quantity to avoid "not enough codes")
        var newQty = Math.Min(availableCodes, 5);
        var newJobId = await CreateAndPrepareJobAsync(productId, printerId, newQty);
        await StartJobAsync(newJobId);
        await WaitForJobStatusAsync(newJobId, "Completed", TimeSpan.FromSeconds(30));

        // Verify new job completed
        var newJob = await GetJobAsync(newJobId);
        Assert.Equal("Completed", newJob!.Status);
        Assert.Equal(newQty, newJob.CodesConfirmed);

        // Final conservation check
        var stats2 = await AssertCodeConservationAsync(productId, 20);
        Assert.Equal(0, stats2.Reserved);
        // printed should include codes from both jobs
        Assert.True(stats2.Printed == stats1.Printed + newQty,
            $"Expected exactly {stats1.Printed + newQty} printed, got {stats2.Printed}");
    }
}
