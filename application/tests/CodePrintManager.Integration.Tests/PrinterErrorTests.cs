using System.Net;
using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

/// <summary>
/// Integration tests for printer error scenarios during printing.
/// Validates that injected Error and Blocked states are auto-detected via periodic
/// status polling, and that code conservation invariants hold throughout.
/// </summary>
public class PrinterErrorTests : IntegrationTestBase
{
    // ──────────────────────────────────────────────
    // E1. Printer ERROR mid-print → auto-detected by status polling →
    //     job goes to Error → cancel → conservation holds.
    // The executor checks GetStatusAsync every 10th poll (~5s). Two consecutive
    // Error readings confirm persistence → quarantine + Error.
    // ──────────────────────────────────────────────

    [Fact]
    public async Task E1_PrinterErrorMidPrint_AutoDetected_QuarantineAndError()
    {
        var printerId = await SetupPrinterAsync("E1Printer");
        var productId = await SetupProductAsync("E1Product", "e1.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 200);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for at least 3 codes confirmed
        await WaitForProgressAsync(jobId, 3);

        // Inject printer error — print loop stops, counters stall.
        // The executor's status polling will detect Error on next check.
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/inject-error",
            new { Status = "Error" });

        // Wait for auto-detection: 2 consecutive status checks (~10s apart each at 10th poll)
        // With 500ms poll interval, 10th poll = 5s. Two checks = ~10s.
        var errorJob = await WaitForJobStatusAsync(jobId, "Error", TimeSpan.FromSeconds(30));
        Assert.Equal("Error", errorJob.Status);
        var confirmed = errorJob.CodesConfirmed;
        Assert.True(confirmed >= 3, $"Expected at least 3 confirmed, got {confirmed}");

        // Cancel the Error job
        await CancelJobAsync(jobId);

        // Conservation: all codes accounted for
        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(0, stats.Reserved);
        Assert.True(stats.Quarantined > 0, "Expected quarantined codes from Error auto-detection");
        Assert.True(stats.Printed >= confirmed, $"Expected at least {confirmed} printed");
    }

    // ──────────────────────────────────────────────
    // E2. ERROR during prepare → job fails gracefully → retry succeeds
    // ──────────────────────────────────────────────

    [Fact]
    public async Task E2_ErrorDuringPrepare_RetrySucceeds()
    {
        var printerId = await SetupPrinterAsync("E2Printer");
        var productId = await SetupProductAsync("E2Product", "e2.csv");
        await ImportCodesAsync(productId, 10);

        // Inject error BEFORE creating job — printer is in Error state, not Idle
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/inject-error",
            new { Status = "Error" });

        // Attempt to create job — should fail (printer not Idle)
        var failedResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 5
        });
        Assert.Equal(HttpStatusCode.BadRequest, failedResp.StatusCode);

        // Verify no codes stuck in Reserved — all should still be Available
        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(10, stats.Available);
        Assert.Equal(0, stats.Reserved);

        // Clear the error
        await Client.PostAsync($"/api/mock/printers/{printerId}/clear-error", null);

        // Retry — create job, start, and complete successfully
        await SetPrintSpeedAsync(printerId, 50);
        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 5);
        await StartJobAsync(jobId);

        await WaitForJobStatusAsync(jobId, "Completed", TimeSpan.FromSeconds(15));

        var finalJob = await GetJobAsync(jobId);
        Assert.Equal("Completed", finalJob!.Status);
        Assert.Equal(5, finalJob.CodesConfirmed);

        // Conservation: 5 printed + 5 available = 10
        var finalStats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(5, finalStats.Printed);
        Assert.Equal(5, finalStats.Available);
        Assert.Equal(0, finalStats.Reserved);
        Assert.Equal(0, finalStats.Quarantined);
    }

    // ──────────────────────────────────────────────
    // E3. BLOCKED state during print → commands fail → clear → job recovers
    // ──────────────────────────────────────────────

    [Fact]
    public async Task E3_BlockedStateDuringPrint_ClearAndRecover_Completes()
    {
        var printerId = await SetupPrinterAsync("E3Printer");
        var productId = await SetupProductAsync("E3Product", "e3.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 300);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for at least 3 codes confirmed
        await WaitForProgressAsync(jobId, 3);

        // Inject BLOCKED state — executor's polls fail with InvalidOperationException
        await Client.PostAsync($"/api/mock/printers/{printerId}/inject-blocked", null);

        // Wait 2s — executor accumulates failures (below MaxConsecutiveFailures=30)
        await Task.Delay(2000);

        // Clear the blocked state — executor resumes polling successfully
        await Client.PostAsync($"/api/mock/printers/{printerId}/clear-blocked", null);

        // Wait for job to complete
        await WaitForJobStatusAsync(jobId, "Completed", TimeSpan.FromSeconds(30));

        // Assert final state
        var finalJob = await GetJobAsync(jobId);
        Assert.Equal("Completed", finalJob!.Status);
        Assert.Equal(10, finalJob.CodesConfirmed);

        // Conservation holds
        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(10, stats.Printed);
        Assert.Equal(0, stats.Quarantined);
        Assert.Equal(0, stats.Reserved);
    }

    // ──────────────────────────────────────────────
    // E4. Printer error → auto-detected → Error → cancel → new job → error again →
    //     auto-detected → Error → cancel → final job completes.
    //     Multi-cycle error/recovery test across cancel-and-recreate.
    // ──────────────────────────────────────────────

    [Fact]
    public async Task E4_MultiCycleErrors_CancelAndRestart_ConservationHolds()
    {
        var printerId = await SetupPrinterAsync("E4Printer");
        var productId = await SetupProductAsync("E4Product", "e4.csv");
        await ImportCodesAsync(productId, 20);
        await SetPrintSpeedAsync(printerId, 200);

        // --- Cycle 1: start → error → auto-detected → cancel ---
        var jobId1 = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId1);

        await WaitForProgressAsync(jobId1, 3, TimeSpan.FromSeconds(30));

        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/inject-error",
            new { Status = "Error" });

        await WaitForJobStatusAsync(jobId1, "Error", TimeSpan.FromSeconds(30));
        var job1 = await GetJobAsync(jobId1);
        var confirmed1 = job1!.CodesConfirmed;

        await CancelJobAsync(jobId1);

        // Mid-cycle conservation
        var stats1 = await AssertCodeConservationAsync(productId, 20);
        Assert.Equal(0, stats1.Reserved);
        Assert.True(stats1.Quarantined > 0, "Cycle 1: Expected quarantined codes");

        // Clear error for next cycle
        await Client.PostAsync($"/api/mock/printers/{printerId}/clear-error", null);

        // --- Cycle 2: start new job → error → auto-detected → cancel ---
        var available2 = stats1.Available;
        Assert.True(available2 > 0, "Should have available codes for cycle 2");

        var qty2 = Math.Min(available2, 5);
        var jobId2 = await CreateAndPrepareJobAsync(productId, printerId, qty2);
        await StartJobAsync(jobId2);

        await WaitForProgressAsync(jobId2, 2, TimeSpan.FromSeconds(30));

        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/inject-error",
            new { Status = "Error" });

        await WaitForJobStatusAsync(jobId2, "Error", TimeSpan.FromSeconds(30));

        await CancelJobAsync(jobId2);

        var stats2 = await AssertCodeConservationAsync(productId, 20);
        Assert.Equal(0, stats2.Reserved);

        // Clear error for final cycle
        await Client.PostAsync($"/api/mock/printers/{printerId}/clear-error", null);

        // --- Cycle 3: final job → completes successfully ---
        var available3 = stats2.Available;
        if (available3 > 0)
        {
            var qty3 = Math.Min(available3, 3);
            var jobId3 = await CreateAndPrepareJobAsync(productId, printerId, qty3);
            await SetPrintSpeedAsync(printerId, 50);
            await StartJobAsync(jobId3);
            await WaitForJobStatusAsync(jobId3, "Completed", TimeSpan.FromSeconds(30));

            var job3 = await GetJobAsync(jobId3);
            Assert.Equal("Completed", job3!.Status);
            Assert.Equal(qty3, job3.CodesConfirmed);
        }

        // Final conservation check across all cycles
        var finalStats = await AssertCodeConservationAsync(productId, 20);
        Assert.Equal(0, finalStats.Reserved);
    }

    // ──────────────────────────────────────────────
    // E5. Transient error — inject Error, clear within 5s → job continues.
    // Tests that the two-consecutive-polls confirmation mechanism ignores
    // transient errors that clear before the second check.
    // ──────────────────────────────────────────────

    [Fact]
    public async Task E5_TransientError_ClearsBeforeConfirmation_JobContinues()
    {
        var printerId = await SetupPrinterAsync("E5Printer");
        var productId = await SetupProductAsync("E5Product", "e5.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 200);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for some progress
        await WaitForProgressAsync(jobId, 3);

        // Inject error — the first status check sees Error
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/inject-error",
            new { Status = "Error" });

        // Wait 2 seconds then clear — this is within the ~5s between status checks,
        // so the second confirmation check should see the error cleared.
        await Task.Delay(2000);
        await Client.PostAsync($"/api/mock/printers/{printerId}/clear-error", null);

        // Job should continue and complete (transient error ignored)
        await WaitForJobStatusAsync(jobId, "Completed", TimeSpan.FromSeconds(30));

        var finalJob = await GetJobAsync(jobId);
        Assert.Equal("Completed", finalJob!.Status);
        Assert.Equal(10, finalJob.CodesConfirmed);

        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(10, stats.Printed);
        Assert.Equal(0, stats.Quarantined);
        Assert.Equal(0, stats.Reserved);
    }
}
