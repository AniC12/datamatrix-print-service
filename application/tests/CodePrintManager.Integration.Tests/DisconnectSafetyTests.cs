using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

/// <summary>
/// Integration tests for CRIT-1 and CRIT-2 safety fixes:
/// - Cancelling a printing job when the printer is disconnected
/// - Crash-recovery cancellation with boundary quarantine
/// - Pausing a printing job when the printer is disconnected
/// All scenarios verify that no codes are lost (stuck in Reserved)
/// and that boundary codes are properly quarantined when uncertain.
/// </summary>
public class DisconnectSafetyTests : IntegrationTestBase
{
    // ──────────────────────────────────────────────
    // CRIT-1: Cancel while printer is disconnected
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CancelPrintingJob_AfterDisconnect_QuarantinesBoundaryAndReturnsCodes()
    {
        var printerId = await SetupPrinterAsync("DisconnectCancelPrinter");
        // Set QuarantineMargin = 2 so boundary codes are quarantined
        await Client.PatchAsJsonAsync($"/api/printers/{printerId}", new { QuarantineMargin = 2 });
        var productId = await SetupProductAsync("DisconnectCancelProduct", "dc.csv");
        await ImportCodesAsync(productId, 20);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 200 });

        // Create + start job for 10 codes
        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 10 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        // Wait for some progress
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 2, timeout: TimeSpan.FromSeconds(10));

        // Disconnect the printer (simulates cable pull / network failure)
        await Client.PostAsync($"/api/printers/{printerId}/disconnect", null);
        await Task.Delay(300);

        // Cancel the job while printer is disconnected
        var cancelResp = await Client.PostAsync($"/api/jobs/{job.Id}/cancel", null);
        cancelResp.EnsureSuccessStatusCode();
        await Task.Delay(500);

        // Verify job is Cancelled
        var finalJob = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job.Id}");
        Assert.Equal("Cancelled", finalJob!.Status);

        // Verify code accounting — no codes stuck in Reserved
        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.NotNull(stats?.PoolStats);

        var available = stats.PoolStats.GetValueOrDefault("Available", 0);
        var printed = stats.PoolStats.GetValueOrDefault("Printed", 0);
        var quarantined = stats.PoolStats.GetValueOrDefault("Quarantined", 0);
        var reserved = stats.PoolStats.GetValueOrDefault("Reserved", 0);
        var burned = stats.PoolStats.GetValueOrDefault("Burned", 0);

        // No codes should be stuck in Reserved
        Assert.Equal(0, reserved);

        // When disconnected during cancel, boundary codes should be quarantined
        // (at least 1 quarantined because QuarantineMargin=2 and we can't read the counter)
        Assert.True(quarantined >= 1,
            $"Expected at least 1 quarantined code when cancelling disconnected, got {quarantined}");

        // Total must equal 20
        var total = available + printed + quarantined + reserved + burned;
        Assert.Equal(20, total);
    }

    // ──────────────────────────────────────────────
    // CRIT-2: Crash-recovery cancel quarantines boundary
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CrashRecoveryCancel_QuarantinesBoundary()
    {
        var printerId = await SetupPrinterAsync("CrashRecoveryPrinter");
        await Client.PatchAsJsonAsync($"/api/printers/{printerId}", new { QuarantineMargin = 2 });
        var productId = await SetupProductAsync("CrashRecoveryProduct", "cr2.csv");
        await ImportCodesAsync(productId, 20);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 200 });

        // Create + start job
        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 10 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        // Wait for some progress
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 3, timeout: TimeSpan.FromSeconds(10));

        // Simulate crash: disconnect printer, then immediately cancel
        // (the executor gets stopped but adapter is gone — no counter read possible)
        await Client.PostAsync($"/api/printers/{printerId}/disconnect", null);
        await Task.Delay(200);

        // Cancel — this should use the CRIT-2 path: quarantine boundary codes
        var cancelResp = await Client.PostAsync($"/api/jobs/{job.Id}/cancel", null);
        cancelResp.EnsureSuccessStatusCode();
        await Task.Delay(500);

        var finalJob = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job.Id}");
        Assert.Equal("Cancelled", finalJob!.Status);

        // Verify quarantine behavior
        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.NotNull(stats?.PoolStats);

        var quarantined = stats.PoolStats.GetValueOrDefault("Quarantined", 0);
        var reserved = stats.PoolStats.GetValueOrDefault("Reserved", 0);

        // Boundary codes must be quarantined (not returned to Available)
        Assert.True(quarantined >= 1,
            $"Expected at least 1 quarantined boundary code in crash-recovery cancel, got {quarantined}");

        // No codes stuck in Reserved
        Assert.Equal(0, reserved);

        // Total accounting
        var total = stats.PoolStats.Values.Sum();
        Assert.Equal(20, total);
    }

    // ──────────────────────────────────────────────
    // CRIT-1: Pause while printer is disconnected
    // ──────────────────────────────────────────────

    [Fact]
    public async Task PausePrintingJob_AfterDisconnect_FallsBackToCodesConfirmed()
    {
        var printerId = await SetupPrinterAsync("DisconnectPausePrinter");
        var productId = await SetupProductAsync("DisconnectPauseProduct", "dp.csv");
        await ImportCodesAsync(productId, 20);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 200 });

        // Create + start job
        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 10 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        // Wait for some progress
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 2, timeout: TimeSpan.FromSeconds(10));

        // Record the confirmed count before disconnect
        var midJob = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job.Id}");
        var confirmedBeforeDisconnect = midJob!.CodesConfirmed;

        // Disconnect printer
        await Client.PostAsync($"/api/printers/{printerId}/disconnect", null);
        await Task.Delay(300);

        // Pause the job while printer is disconnected
        // This should succeed using CodesConfirmed fallback (not throw)
        var pauseResp = await Client.PostAsync($"/api/jobs/{job.Id}/pause", null);
        pauseResp.EnsureSuccessStatusCode();
        await Task.Delay(300);

        // Verify job is Paused (not Error or stuck in Printing)
        var finalJob = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job.Id}");
        Assert.Equal("Paused", finalJob!.Status);

        // CodesConfirmed should be at least what we observed before disconnect
        Assert.True(finalJob.CodesConfirmed >= confirmedBeforeDisconnect,
            $"CodesConfirmed regressed: was {confirmedBeforeDisconnect}, now {finalJob.CodesConfirmed}");

        // No codes stuck in Reserved beyond what's expected for a paused job
        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.NotNull(stats?.PoolStats);
        var total = stats.PoolStats.Values.Sum();
        Assert.Equal(20, total);
    }
}
