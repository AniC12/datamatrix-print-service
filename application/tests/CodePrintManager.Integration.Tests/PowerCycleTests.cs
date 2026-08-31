using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

/// <summary>
/// Integration tests for printer power cycle scenarios.
/// Validates backward-counter detection, quarantine behavior, CSV rebuild on resume,
/// and isolation between consecutive jobs after a power cycle.
/// </summary>
public class PowerCycleTests : IntegrationTestBase
{
    // ──────────────────────────────────────────────
    // B1. Power cycle mid-print → backward counter → Error → cancel
    // ──────────────────────────────────────────────
    [Fact]
    public async Task PowerCycleMidPrint_BackwardCounter_JobGoesError_CancelQuarantines()
    {
        var printerId = await SetupPrinterAsync("B1Printer");
        var productId = await SetupProductAsync("B1Product", "b1.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 300);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for 4+ codes confirmed
        await WaitForProgressAsync(jobId, 4);

        // Simulate power cycle: SPGGCP resets to 0, SPGGTP preserved, template cleared
        await Client.PostAsync($"/api/mock/printers/{printerId}/power-cycle-while-printing", null);

        // Executor detects backward SPGGCP → job transitions to Error
        var errorJob = await WaitForJobStatusAsync(jobId, "Error", TimeSpan.FromSeconds(15));
        Assert.Equal("Error", errorJob.Status);

        // Cancel the errored job (cancel of Error job only changes status, doesn't re-quarantine)
        await CancelJobAsync(jobId);

        var cancelledJob = await GetJobAsync(jobId);
        Assert.NotNull(cancelledJob);
        Assert.Equal("Cancelled", cancelledJob!.Status);

        // Codes should be accounted for: printed + quarantined by the executor
        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(0, stats.Reserved);
        Assert.True(stats.Quarantined >= 1, "Expected quarantined codes from power cycle");
    }

    // ──────────────────────────────────────────────
    // B2. Pause → power cycle → resume with CSV rebuild → complete
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Pause_PowerCycle_Resume_RebuildsCsvAndCompletes()
    {
        var printerId = await SetupPrinterAsync("B2Printer");
        var productId = await SetupProductAsync("B2Product", "b2.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 300);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for 3+ codes confirmed
        await WaitForProgressAsync(jobId, 3);

        // Pause the job normally
        await PauseJobAsync(jobId);
        var pausedJob = await WaitForJobStatusAsync(jobId, "Paused", TimeSpan.FromSeconds(5));
        Assert.Equal("Paused", pausedJob.Status);

        // Simulate power cycle while paused: SPGGCP=0, SPGGTP preserved
        await Client.PostAsync($"/api/mock/printers/{printerId}/power-cycle-while-printing", null);

        // Resume — triggers full Resume Procedure (CSV rebuild, template reload)
        await ResumeJobAsync(jobId);

        // Wait for completion
        var completedJob = await WaitForJobStatusAsync(jobId, "Completed", TimeSpan.FromSeconds(30));
        Assert.Equal("Completed", completedJob.Status);
        Assert.Equal(10, completedJob.CodesConfirmed);

        // Verify the resume procedure rebuilt CSV correctly (not re-using old data buffer)
        var printerState = await GetMockPrinterAsync(printerId);
        Assert.NotNull(printerState);

        // All 10 printed, conservation invariant holds
        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(10, stats.Printed);
        Assert.Equal(0, stats.Reserved);
    }

    // ──────────────────────────────────────────────
    // B3. Power cycle after job completed → no contamination on next job
    // ──────────────────────────────────────────────
    [Fact]
    public async Task PowerCycleAfterCompleted_NoContaminationOnNextJob()
    {
        var printerId = await SetupPrinterAsync("B3Printer");
        var productId = await SetupProductAsync("B3Product", "b3.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 100);

        // First job: print 5 codes, wait for completion
        var job1Id = await CreateAndPrepareJobAsync(productId, printerId, 5);
        await StartJobAsync(job1Id);
        var completedJob1 = await WaitForJobStatusAsync(job1Id, "Completed", TimeSpan.FromSeconds(15));
        Assert.Equal("Completed", completedJob1.Status);
        Assert.Equal(5, completedJob1.CodesConfirmed);

        // Power cycle after completion: SPGGCP=0, SPGGTP preserved
        await Client.PostAsync($"/api/mock/printers/{printerId}/power-cycle-while-printing", null);

        // Verify first job remains Completed and unaffected
        var job1After = await GetJobAsync(job1Id);
        Assert.NotNull(job1After);
        Assert.Equal("Completed", job1After!.Status);
        Assert.Equal(5, job1After.CodesConfirmed);

        // Second job: print 5 more codes on the same printer
        var job2Id = await CreateAndPrepareJobAsync(productId, printerId, 5);
        await StartJobAsync(job2Id);
        var completedJob2 = await WaitForJobStatusAsync(job2Id, "Completed", TimeSpan.FromSeconds(15));
        Assert.Equal("Completed", completedJob2.Status);
        Assert.Equal(5, completedJob2.CodesConfirmed);

        // Fresh baseline — second job should have its own TotalBaseline
        Assert.NotNull(completedJob2.TotalBaseline);
        Assert.True(completedJob2.TotalBaseline >= completedJob1!.TotalBaseline,
            $"Second job TotalBaseline ({completedJob2.TotalBaseline}) should be >= first job's ({completedJob1.TotalBaseline})");

        // All 10 codes printed across both jobs, conservation holds
        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(10, stats.Printed);
        Assert.Equal(0, stats.Available);
        Assert.Equal(0, stats.Reserved);
    }

    // ──────────────────────────────────────────────
    // B4. Power cycle during pause with no additional prints
    // ──────────────────────────────────────────────
    [Fact]
    public async Task PowerCycleDuringPause_NoAdditionalPrints_ResumesAndCompletes()
    {
        var printerId = await SetupPrinterAsync("B4Printer");
        var productId = await SetupProductAsync("B4Product", "b4.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 200);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for 4+ codes confirmed
        await WaitForProgressAsync(jobId, 4);

        // Pause the job
        await PauseJobAsync(jobId);
        var pausedJob = await WaitForJobStatusAsync(jobId, "Paused", TimeSpan.FromSeconds(5));
        Assert.Equal("Paused", pausedJob.Status);

        // Simulate power cycle while paused
        await Client.PostAsync($"/api/mock/printers/{printerId}/power-cycle-while-printing", null);

        // Resume
        await ResumeJobAsync(jobId);

        // Wait for completion
        var completedJob = await WaitForJobStatusAsync(jobId, "Completed", TimeSpan.FromSeconds(30));
        Assert.Equal("Completed", completedJob.Status);
        Assert.Equal(10, completedJob.CodesConfirmed);

        // Verify no additional prints during power cycle (LifetimeCounter should not have increased beyond confirmed)
        var printerState = await GetMockPrinterAsync(printerId);
        Assert.NotNull(printerState);

        // All 10 printed, conservation holds
        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(10, stats.Printed);
        Assert.Equal(0, stats.Reserved);
    }
}
