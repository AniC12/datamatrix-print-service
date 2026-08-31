using System.Net;
using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

/// <summary>
/// Integration tests for Ready-state behaviour and the ReadyWatcher background service.
/// H1: External print detected by ReadyWatcher → transitions to Printing.
/// H2: Ready job survives printer disconnect/reconnect → Start still works.
/// H3: Ready job → serial number changes → Start blocked.
/// </summary>
public class ReadyStateTests : IntegrationTestBase
{
    // ──────────────────────────────────────────────
    // H1. External print detected by ReadyWatcher → auto-transitions to Printing → completes.
    // The ReadyWatcher polls SPGGCP every 3s (after 2s initial delay). When
    // SPGGCP > baseline, it fires ExternalPrintDetected → HandleExternalPrintDetectedAsync
    // → StartJobAsync → Printing. Verifies the entire automatic detection chain.
    // ──────────────────────────────────────────────

    [Fact]
    public async Task H1_ExternalPrintDetected_WatcherTransitionsToPointing_Completes()
    {
        var printerId = await SetupPrinterAsync("H1ReadyWatcherPrinter");
        var productId = await SetupProductAsync("H1ReadyWatcherProduct", "h1.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 50);

        // Create job — auto-prepared → Ready (ReadyWatcher spawns automatically)
        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        var readyJob = await GetJobAsync(jobId);
        Assert.Equal("Ready", readyJob!.Status);

        // Verify TotalBaseline was recorded during Prepare
        Assert.True(readyJob.TotalBaseline.HasValue, "TotalBaseline should be set for Ready jobs");

        // Wait for the ReadyWatcher to start (2s initial delay + buffer)
        await Task.Delay(3000);

        // Simulate external printing: advance SPGGCP by 1 (someone pressed Print on the touchscreen)
        // The ReadyWatcher will detect SPGGCP > baseline on its next poll.
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/external-prints",
            new { Count = 1 });

        // Wait for the watcher to detect and auto-transition to Printing, then complete.
        // Watcher polls every 3s, so detection takes up to 3s. Then StartJobAsync starts
        // the executor, which runs the print job to completion.
        await WaitForJobStatusAsync(jobId, "Completed", TimeSpan.FromSeconds(30));

        // Assert: correct accounting
        var finalJob = await GetJobAsync(jobId);
        Assert.Equal("Completed", finalJob!.Status);
        Assert.Equal(10, finalJob.CodesConfirmed);

        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(10, stats.Printed);
        Assert.Equal(0, stats.Reserved);
        Assert.Equal(0, stats.Quarantined);
    }

    // ──────────────────────────────────────────────
    // H2. Ready job → printer disconnects → reconnects → Start still works
    // ──────────────────────────────────────────────

    [Fact]
    public async Task H2_ReadyJob_DisconnectReconnect_StartStillWorks()
    {
        var printerId = await SetupPrinterAsync("H2ReconnectPrinter");
        var productId = await SetupProductAsync("H2ReconnectProduct", "h2.csv");
        await ImportCodesAsync(productId, 10);
        await SetPrintSpeedAsync(printerId, 50);

        // Create job → Ready
        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        var readyJob = await GetJobAsync(jobId);
        Assert.Equal("Ready", readyJob!.Status);

        // Disconnect printer, wait 1s, reconnect
        await Client.PostAsync($"/api/printers/{printerId}/disconnect", null);
        await Task.Delay(1000);
        await Client.PostAsync($"/api/printers/{printerId}/connect", null);
        await SetPrintSpeedAsync(printerId, 50);

        // Start job — should succeed after reconnect
        await StartJobAsync(jobId);

        // Wait for Completed
        await WaitForJobStatusAsync(jobId, "Completed", TimeSpan.FromSeconds(30));

        // Assert: correct accounting
        var finalJob = await GetJobAsync(jobId);
        Assert.Equal("Completed", finalJob!.Status);
        Assert.Equal(10, finalJob.CodesConfirmed);

        var stats = await AssertCodeConservationAsync(productId, 10);
        Assert.Equal(10, stats.Printed);
        Assert.Equal(0, stats.Reserved);
        Assert.Equal(0, stats.Quarantined);
    }

    // ──────────────────────────────────────────────
    // H3. Ready job → serial number changes → Start blocked
    // ──────────────────────────────────────────────

    [Fact]
    public async Task H3_ReadyJob_SerialChanges_StartBlocked()
    {
        var printerId = await SetupPrinterAsync("H3SerialPrinter");
        var productId = await SetupProductAsync("H3SerialProduct", "h3.csv");
        await ImportCodesAsync(productId, 10);

        // Create job → Ready
        var jobId = await CreateAndPrepareJobAsync(productId, printerId, 10);
        var readyJob = await GetJobAsync(jobId);
        Assert.Equal("Ready", readyJob!.Status);

        // Disconnect printer
        await Client.PostAsync($"/api/printers/{printerId}/disconnect", null);
        await Task.Delay(200);

        // Set next serial for factory — new adapter will have a different serial
        await Client.PostAsJsonAsync("/api/mock/printers/factory/set-next-serial",
            new { Serial = "DIFFERENT-001" });

        // Reconnect — creates new adapter with different serial
        await Client.PostAsync($"/api/printers/{printerId}/connect", null);
        await Task.Delay(200);

        // Try to start → expect BadRequest (serial mismatch)
        var startResp = await Client.PostAsync($"/api/jobs/{jobId}/start", null);
        Assert.Equal(HttpStatusCode.BadRequest, startResp.StatusCode);

        // Assert: job remains Ready
        var finalJob = await GetJobAsync(jobId);
        Assert.Equal("Ready", finalJob!.Status);
    }
}
