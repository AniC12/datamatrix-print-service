using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

/// <summary>
/// Integration tests for template mismatch scenarios.
/// D1: External template change during printing → executor detects mismatch → job errors.
/// D2: Template reload (same name) during pause resets SPGGCP → resume catches up via SPGGTP.
/// </summary>
public class TemplateMismatchTests : IntegrationTestBase
{
    // ──────────────────────────────────────────────
    // D1. Template changed while printing → backward counter triggers inspection → mismatch → Error
    // ──────────────────────────────────────────────

    [Fact]
    public async Task D1_TemplateChangedDuringPrint_ExecutorDetectsMismatch_JobErrors()
    {
        var printerId = await SetupPrinterAsync("D1MismatchPrinter");
        var productId = await SetupProductAsync("D1MismatchProduct", "d1.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 300);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for some progress
        await WaitForProgressAsync(jobId, 4);

        // Read the current mock state to preserve the lifetime counter
        var mockState = await GetMockPrinterAsync(printerId);
        Assert.NotNull(mockState);
        var currentLifetime = mockState!.LifetimeCounter;

        // Externally change the active template to a different one (while still connected)
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/external-template-load",
            new { Template = "other.rox" });

        // Reset SPGGCP to 0 to trigger backward-counter detection which leads to inspection
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-counters",
            new { CurrentCounter = 0, LifetimeCounter = currentLifetime });

        // The executor should detect the backward counter, enter inspection,
        // and find a template mismatch (active="other.rox" vs expected="test.rox") → job errors
        var errorJob = await WaitForJobStatusAsync(jobId, "Error", TimeSpan.FromSeconds(30));

        Assert.Equal("Error", errorJob.Status);

        // Remaining unreported codes should be quarantined or printed (not left as Reserved)
        // The executor quarantines remaining codes during Error escalation
        var stats = await AssertCodeConservationAsync(productId, 10);
        // Cancel the Error job to clean up
        await CancelJobAsync(jobId);

        // After cancel, verify all codes accounted for
        stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(0, stats.Reserved);
        Assert.True(stats.Quarantined >= 1,
            $"Expected quarantined codes from template mismatch, got Quarantined={stats.Quarantined}");
    }

    // ──────────────────────────────────────────────
    // D2. Template reloaded (same name) during pause → SPGGCP reset → resume catches up via SPGGTP
    // ──────────────────────────────────────────────

    [Fact]
    public async Task D2_TemplateReloadDuringPause_ResetsSPGGCP_ResumeCatchesUpAndCompletes()
    {
        var printerId = await SetupPrinterAsync("D2ReloadPrinter");
        var productId = await SetupProductAsync("D2ReloadProduct", "d2.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 200);

        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        await StartJobAsync(jobId);

        // Wait for partial progress
        await WaitForProgressAsync(jobId, 4);

        // Pause the job and wait for Paused state deterministically
        await PauseJobAsync(jobId);
        await WaitForJobStatusAsync(jobId, "Paused", TimeSpan.FromSeconds(10));

        // Read the current mock state to preserve the lifetime counter
        var mockState = await GetMockPrinterAsync(printerId);
        Assert.NotNull(mockState);
        var currentLifetime = mockState!.LifetimeCounter;

        // Simulate template reload (same template name) — this resets SPGGCP to 0
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-counters",
            new { CurrentCounter = 0, LifetimeCounter = currentLifetime });

        // Resume — the Resume Procedure rebuilds CSV and continues from SPGGTP position
        await ResumeJobAsync(jobId);

        // Wait for completion — resume should handle the SPGGCP reset gracefully
        await WaitForJobStatusAsync(jobId, "Completed", TimeSpan.FromSeconds(30));

        // All 10 codes should be printed
        var finalJob = await GetJobAsync(jobId);
        Assert.NotNull(finalJob);
        Assert.Equal("Completed", finalJob!.Status);
        Assert.Equal(10, finalJob.CodesConfirmed);

        // Conservation invariant: all 10 codes accounted for
        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(10, stats.Printed);
        Assert.Equal(0, stats.Reserved);

        // Verify conservation holds with no leftover reserved codes
        stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(0, stats.Reserved);
    }
}
