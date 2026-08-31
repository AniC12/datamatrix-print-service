using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

/// <summary>
/// Tests for connection drop scenarios during printing.
/// Validates recovery-deep-dive Scenarios 1-3 and failure escalation.
/// </summary>
public class ConnectionDropTests : IntegrationTestBase
{
    /// <summary>
    /// A1. Network blip → executor auto-reconnects via TryReconnectAsync → inspection runs → job completes.
    /// Tests recovery-deep-dive Scenario 1: "Network Blip — Printer Still Printing Our Job."
    /// The adapter stays in PrinterConnectionManager; only the TCP connection is dropped.
    /// TryReconnectAsync reconnects the same adapter in-place; the executor's reference stays valid.
    /// </summary>
    [Fact]
    public async Task A1_NetworkBlip_AutoReconnect_InspectionRuns_JobCompletes()
    {
        var printerId = await SetupPrinterAsync("A1Printer");
        var productId = await SetupProductAsync("A1Product", "a1.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 100);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for some progress
        await WaitForProgressAsync(jobId, 3);

        // Simulate network drop — adapter stays registered, just throws IOException.
        // The executor will catch IOException → call TryReconnectAsync → reconnect in-place.
        await SimulateNetworkDropAsync(printerId);

        // The executor should auto-reconnect and continue.
        // Wait for completion — the reconnection callback restores the adapter,
        // then RunPostReconnectInspectionAsync catches up any missed progress.
        await WaitForJobStatusAsync(jobId, "Completed", TimeSpan.FromSeconds(30));

        var job = await GetJobAsync(jobId);
        Assert.Equal("Completed", job!.Status);
        Assert.Equal(10, job.CodesConfirmed);

        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(10, stats.Printed);
        Assert.Equal(0, stats.Reserved);
        Assert.Equal(0, stats.Quarantined);
    }

    /// <summary>
    /// A1b. Prolonged disconnect (adapter removed) → executor cannot reconnect → escalates to Error.
    /// This tests the degraded path: user-initiated disconnect removes the adapter entirely,
    /// so TryReconnectAsync returns false (adapter not in dictionary). After 30 consecutive
    /// failures, the executor quarantines and sets Error.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")]
    public async Task A1b_ProlongedDisconnect_EscalatesToError_CancelAndRestartNewJob()
    {
        var printerId = await SetupPrinterAsync("A1bPrinter");
        var productId = await SetupProductAsync("A1bProduct", "a1b.csv");
        await ImportCodesAsync(productId, 20);
        await SetPrintSpeedAsync(printerId, 100);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for some progress
        await WaitForProgressAsync(jobId, 3);

        // Full disconnect — removes adapter from PrinterConnectionManager.
        // TryReconnectAsync will return false because the adapter is gone.
        await Client.PostAsync($"/api/printers/{printerId}/disconnect", null);

        // Wait for job to escalate to Error (30 failures × ~2s delay = ~60s)
        var job = await WaitForJobStatusAsync(jobId, "Error", TimeSpan.FromSeconds(120));
        Assert.Equal("Error", job.Status);
        var confirmed = job.CodesConfirmed;
        Assert.True(confirmed >= 3, $"Expected at least 3 confirmed, got {confirmed}");

        // Cancel the Error job
        await CancelJobAsync(jobId);

        // Conservation invariant — all codes accounted for
        var stats = await AssertCodeConservationAsync(productId, 20);
        Assert.Equal(0, stats.Reserved);
        Assert.True(stats.Quarantined > 0, "Expected quarantined codes from Error escalation");
        Assert.True(stats.Printed >= confirmed, $"Expected at least {confirmed} printed");

        // Reconnect and start a new job with remaining available codes
        await Client.PostAsync($"/api/printers/{printerId}/connect", null);
        await SetPrintSpeedAsync(printerId, 50);

        var availableCodes = stats.Available;
        Assert.True(availableCodes > 0, "Should have available codes for a new job");

        var newQty = Math.Min(availableCodes, 5);
        var newJobId = await CreateAndPrepareJobAsync(productId, printerId, newQty);
        await StartJobAsync(newJobId);
        await WaitForJobStatusAsync(newJobId, "Completed", TimeSpan.FromSeconds(30));

        var newJob = await GetJobAsync(newJobId);
        Assert.Equal("Completed", newJob!.Status);
        Assert.Equal(newQty, newJob.CodesConfirmed);

        // Final conservation check
        var finalStats = await AssertCodeConservationAsync(productId, 20);
        Assert.Equal(0, finalStats.Reserved);
    }

    /// <summary>
    /// A2. Network drop → printer advances counters while disconnected → auto-reconnect →
    /// inspection detects progress → job catches up and completes.
    /// Tests recovery-deep-dive Scenario 2: "Printer Finished Our Job While Disconnected."
    /// </summary>
    [Fact]
    public async Task A2_NetworkDrop_PrinterFinishedWhileDisconnected_AutoReconnect_Completes()
    {
        var printerId = await SetupPrinterAsync("A2Printer");
        var productId = await SetupProductAsync("A2Product", "a2.csv");
        await ImportCodesAsync(productId, 5);
        await SetPrintSpeedAsync(printerId, 100);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 5);
        await StartJobAsync(jobId);

        // Wait for some progress
        await WaitForProgressAsync(jobId, 2);

        // Simulate network drop — adapter stays in connection manager
        await SimulateNetworkDropAsync(printerId);

        // Simulate that the printer finished all 5 prints while we were disconnected
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-counters",
            new { CurrentCounter = 5, LifetimeCounter = 5 });

        // The executor will IOException → TryReconnectAsync → reconnect → inspection →
        // detect that SPGGTP delta == quantity → complete.
        await WaitForJobStatusAsync(jobId, "Completed", TimeSpan.FromSeconds(30));

        var finalJob = await GetJobAsync(jobId);
        Assert.Equal("Completed", finalJob!.Status);
        Assert.Equal(5, finalJob.CodesConfirmed);

        var stats = await AssertCodeConservationAsync(productId, 5);
        Assert.Equal(5, stats.Printed);
        Assert.Equal(0, stats.Reserved);
    }

    /// <summary>
    /// A3. Operator pauses quickly after disconnect (before 30 failures), then reconnects and resumes.
    /// Uses full disconnect (adapter removed) — operator intervention path.
    /// </summary>
    [Fact]
    public async Task A3_Disconnect_PauseQuickly_Reconnect_Resume_Complete()
    {
        var printerId = await SetupPrinterAsync("A3Printer");
        var productId = await SetupProductAsync("A3Product", "a3.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 150);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for some progress
        await WaitForProgressAsync(jobId, 4);

        // Full disconnect (operator action)
        await Client.PostAsync($"/api/printers/{printerId}/disconnect", null);
        await Task.Delay(500);

        // Quickly pause before the executor accumulates 30 failures
        await PauseJobAsync(jobId);

        var pausedJob = await GetJobAsync(jobId);
        Assert.Equal("Paused", pausedJob!.Status);

        // Reconnect — creates a new adapter
        await Client.PostAsync($"/api/printers/{printerId}/connect", null);
        await SetPrintSpeedAsync(printerId, 50);

        // Resume — should rebuild CSV and continue with the new adapter
        await ResumeJobAsync(jobId);

        await WaitForJobStatusAsync(jobId, "Completed", TimeSpan.FromSeconds(30));

        var finalJob = await GetJobAsync(jobId);
        Assert.Equal("Completed", finalJob!.Status);
        Assert.Equal(10, finalJob.CodesConfirmed);

        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(10, stats.Printed);
        Assert.Equal(0, stats.Reserved);
    }

    /// <summary>
    /// A4. Intermittent IO failures (inject N failures, auto-clear) → job continues.
    /// Validates that transient failures don't kill the job; the executor retries and recovers.
    /// </summary>
    [Fact]
    public async Task A4_IntermittentIOFailures_JobContinues()
    {
        var printerId = await SetupPrinterAsync("A4Printer");
        var productId = await SetupProductAsync("A4Product", "a4.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 100);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for some progress
        await WaitForProgressAsync(jobId, 2);

        // Inject 3 IO failures — next 3 adapter calls throw IOException
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/inject-io-failure",
            new { Count = 3 });

        // Wait — the executor should retry through the 3 failures and continue
        // (3 failures is well below MaxConsecutiveFailures=30)
        await Task.Delay(3000);

        // Job should still be running or close to completion
        var job = await GetJobAsync(jobId);
        Assert.NotNull(job);
        Assert.True(job!.Status == "Printing" || job.Status == "Completed",
            $"Expected Printing or Completed, got {job.Status}");

        // Wait for final completion
        await WaitForJobStatusAsync(jobId, "Completed", TimeSpan.FromSeconds(30));

        var finalJob = await GetJobAsync(jobId);
        Assert.Equal("Completed", finalJob!.Status);
        Assert.Equal(10, finalJob.CodesConfirmed);

        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(10, stats.Printed);
        Assert.Equal(0, stats.Reserved);
        Assert.Equal(0, stats.Quarantined);
    }
}
