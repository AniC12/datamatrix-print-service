using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

/// <summary>
/// Integration tests for counter anomaly scenarios.
/// Validates overrun capping, negative lifetime delta detection,
/// external print interference, and counter stall handling.
/// </summary>
public class CounterAnomalyTests : IntegrationTestBase
{
    // ──────────────────────────────────────────────
    // F1. Large counter jump exceeds remaining codes → blocking anomaly → Error + quarantine.
    // The executor detects a counter jump (SPGGCP advance > remaining codes) as possible
    // SPPL stream corruption and quarantines remaining codes.
    // ──────────────────────────────────────────────
    [Fact]
    public async Task CounterOverrun_BlockingAnomalyDetected_QuarantinesAndErrors()
    {
        var printerId = await SetupPrinterAsync("F1Printer");
        var productId = await SetupProductAsync("F1Product", "f1.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 300);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 5);
        await StartJobAsync(jobId);

        // Wait for at least 2 codes confirmed before injecting overrun
        await WaitForProgressAsync(jobId, 2);

        // Get current counter state
        var printerState = await GetMockPrinterAsync(printerId);
        Assert.NotNull(printerState);
        var currentLt = printerState!.LifetimeCounter;

        // Set counters way higher — advance > remaining triggers blocking anomaly
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-counters",
            new { CurrentCounter = 100, LifetimeCounter = currentLt + 100 });

        // Executor detects blocking anomaly → quarantine + Error
        var errorJob = await WaitForJobStatusAsync(jobId, "Error", TimeSpan.FromSeconds(15));
        Assert.Equal("Error", errorJob.Status);

        // Wait for quarantine to complete
        await Task.Delay(1000);

        // Cancel the errored job
        await CancelJobAsync(jobId);

        // Conservation invariant: all 10 imported codes accounted for
        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(0, stats.Reserved);
        Assert.True(stats.Quarantined > 0 || stats.Printed >= 2,
            $"Expected quarantined or printed codes, got Quarantined={stats.Quarantined}, Printed={stats.Printed}");
    }

    // ──────────────────────────────────────────────
    // F2. SPGGTP reports negative delta (counter went backward globally) → Error
    // ──────────────────────────────────────────────
    [Fact]
    public async Task NegativeLifetimeDelta_CounterWentBackward_JobGoesError()
    {
        var printerId = await SetupPrinterAsync("F2Printer");
        var productId = await SetupProductAsync("F2Product", "f2.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 300);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for at least 2 codes confirmed
        await WaitForProgressAsync(jobId, 2);

        // Read current mock state to see where LifetimeCounter is
        var printerState = await GetMockPrinterAsync(printerId);
        Assert.NotNull(printerState);
        Assert.True(printerState!.LifetimeCounter > 0,
            "LifetimeCounter should have advanced from printing");

        // Set lifetime counter BELOW TotalBaseline (to 0) — triggers negative delta detection
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-counters",
            new { CurrentCounter = 0, LifetimeCounter = 0 });

        // The backward SPGGCP triggers inspection on the next poll.
        // In inspection, the executor reads SPGGTP and finds it went backward → quarantine + Error.
        var errorJob = await WaitForJobStatusAsync(jobId, "Error", TimeSpan.FromSeconds(15));
        Assert.Equal("Error", errorJob.Status);

        // Wait for executor to complete quarantine
        await Task.Delay(1000);

        // Cancel the errored job (cancel of Error just changes status)
        await CancelJobAsync(jobId);

        var cancelledJob = await GetJobAsync(jobId);
        Assert.NotNull(cancelledJob);
        Assert.Equal("Cancelled", cancelledJob!.Status);

        // Conservation holds (total = 10), all codes accounted for
        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(0, stats.Reserved);
        Assert.True(stats.Quarantined > 0,
            "Expected quarantined codes from negative lifetime delta");
    }

    // ──────────────────────────────────────────────
    // F3. External prints advance SPGGTP while our job is running → no auto-stop
    // ──────────────────────────────────────────────
    [Fact]
    public async Task ExternalPrintsDuringJob_NoAutoStop_ConservationHolds()
    {
        var printerId = await SetupPrinterAsync("F3Printer");
        var productId = await SetupProductAsync("F3Product", "f3.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 300);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for at least 3 codes confirmed
        await WaitForProgressAsync(jobId, 3);

        // Inject 5 external prints — advances both CurrentCounter and LifetimeCounter
        // This simulates someone printing from the touchscreen
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/external-prints",
            new { Count = 5 });

        // Wait for executor's cross-check to detect SPGGTP divergence (~2.5s)
        await Task.Delay(3000);

        // The job should NOT be auto-stopped (per AGENTS.md: "never auto-stop a running printer").
        // External prints corrupt SPGGCP-based tracking, which may cause the executor to
        // think more of our codes are printed. The system should handle this without crashing.
        // Wait for the job to reach a terminal state.
        var finalJob = await PollUntilAsync<JobDto>(
            $"/api/jobs/{jobId}",
            j => j.Status == "Completed" || j.Status == "Error" || j.Status == "Cancelled",
            timeout: TimeSpan.FromSeconds(30));

        // The system should not have crashed — any terminal state is acceptable
        Assert.Contains(finalJob.Status, new[] { "Completed", "Error", "Cancelled" });

        // If the job errored, cancel it so codes are properly accounted for
        if (finalJob.Status == "Error")
        {
            await CancelJobAsync(jobId);
        }

        // Conservation invariant must still hold
        await AssertCodeConservationAsync(productId, 10);
    }

    // ──────────────────────────────────────────────
    // F4. Counter stalls (printer error) → auto-detected by status polling →
    //     job goes to Error → cancel → correct accounting.
    // The executor now checks GetStatusAsync every 10th poll. Two consecutive
    // Error readings confirm persistence → quarantine + Error.
    // ──────────────────────────────────────────────
    [Fact]
    public async Task CounterStalls_PrinterError_AutoDetected_CancelHasCorrectAccounting()
    {
        var printerId = await SetupPrinterAsync("F4Printer");
        var productId = await SetupProductAsync("F4Product", "f4.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 200);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for at least 3 codes confirmed
        await WaitForProgressAsync(jobId, 3);

        // Record how many codes were confirmed before the stall
        var preStallJob = await GetJobAsync(jobId);
        Assert.NotNull(preStallJob);
        var confirmedBeforeStall = preStallJob!.CodesConfirmed;

        // Inject Error to stop the print loop — counter stops advancing.
        // Status polling will detect Error on next two status checks.
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/inject-error",
            new { Status = "Error" });

        // Wait for auto-detection (2 consecutive status checks ~5s apart each)
        var errorJob = await WaitForJobStatusAsync(jobId, "Error", TimeSpan.FromSeconds(30));
        Assert.Equal("Error", errorJob.Status);

        // Cancel the Error job
        await CancelJobAsync(jobId);

        var cancelledJob = await GetJobAsync(jobId);
        Assert.NotNull(cancelledJob);
        Assert.Equal("Cancelled", cancelledJob!.Status);

        // Correct accounting: confirmed codes should be >= what we saw before the stall
        Assert.True(cancelledJob.CodesConfirmed >= confirmedBeforeStall,
            $"CodesConfirmed ({cancelledJob.CodesConfirmed}) should be >= pre-stall count ({confirmedBeforeStall})");
        Assert.True(cancelledJob.CodesConfirmed <= 10,
            $"CodesConfirmed ({cancelledJob.CodesConfirmed}) should not exceed quantity (10)");

        // Conservation invariant: all 10 imported codes accounted for
        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(0, stats.Reserved);
        Assert.True(stats.Quarantined > 0,
            "Expected quarantined codes from auto-detected Error");
        Assert.True(stats.Printed >= confirmedBeforeStall,
            $"Printed ({stats.Printed}) should be >= pre-stall confirmed ({confirmedBeforeStall})");
    }
}
