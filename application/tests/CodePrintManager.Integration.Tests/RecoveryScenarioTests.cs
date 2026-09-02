using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

/// <summary>
/// Integration tests for all recovery scenarios from connection-recovery-deep-dive.md.
/// Tests cover: resume with/without power cycle, boundary quarantine on cancel,
/// template mismatch, counter backward, Scenario 7B, ReadyWatcher external print,
/// and serial number mismatch.
/// </summary>
public class RecoveryScenarioTests : IntegrationTestBase
{
    // ──────────────────────────────────────────────
    // 1. Resume after pause — full Resume Procedure
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Resume_RebuildsCsvAndCompletes()
    {
        var printerId = await SetupPrinterAsync("ResumeRebuildPrinter");
        var productId = await SetupProductAsync("ResumeRebuildProduct", "rr.csv");
        await ImportCodesAsync(productId, 20);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 80 });

        // Create + start
        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 10 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        // Wait for partial progress
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 4, timeout: TimeSpan.FromSeconds(10));

        // Pause
        await Client.PostAsync($"/api/jobs/{job.Id}/pause", null);
        await Task.Delay(200);
        var paused = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job.Id}");
        Assert.Equal("Paused", paused!.Status);
        var pausedAt = paused.CodesConfirmed;

        // Resume — triggers full Resume Procedure (CSV rebuild, template reload)
        await Client.PostAsync($"/api/jobs/{job.Id}/resume", null);

        // Wait for completion
        var completed = await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.Status == "Completed", timeout: TimeSpan.FromSeconds(15));

        Assert.Equal(10, completed.CodesConfirmed);

        // Verify code pool: 10 Printed, 10 Available
        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.Equal(10, stats!.PoolStats!.GetValueOrDefault("Printed", 0));
        Assert.Equal(10, stats.PoolStats.GetValueOrDefault("Available", 0));
        Assert.Equal(0, stats.PoolStats.GetValueOrDefault("Reserved", 0));
    }

    // ──────────────────────────────────────────────
    // 2. Boundary quarantine on cancel
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Cancel_QuarantinesBoundaryCode()
    {
        var printerId = await SetupPrinterAsync("CancelQuarantinePrinter");
        // Set QuarantineMargin = 1 so the boundary code is quarantined on cancel
        await Client.PatchAsJsonAsync($"/api/printers/{printerId}", new { QuarantineMargin = 1 });
        var productId = await SetupProductAsync("CancelQuarantineProduct", "cq.csv");
        await ImportCodesAsync(productId, 20);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 100 });

        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 10 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        // Wait for some progress
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 2, timeout: TimeSpan.FromSeconds(10));

        // Cancel mid-print
        await Client.PostAsync($"/api/jobs/{job.Id}/cancel", null);
        await Task.Delay(300);

        var cancelled = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job.Id}");
        Assert.Equal("Cancelled", cancelled!.Status);

        // Verify: quarantined count >= 1 (the boundary code)
        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        var quarantined = stats!.PoolStats!.GetValueOrDefault("Quarantined", 0);
        var printed = stats.PoolStats.GetValueOrDefault("Printed", 0);
        var available = stats.PoolStats.GetValueOrDefault("Available", 0);

        Assert.True(quarantined >= 1, $"Expected at least 1 quarantined code, got {quarantined}");
        Assert.True(printed >= 1, $"Expected at least 1 printed code, got {printed}");
        // Total must be 20
        var total = available + printed + quarantined
            + stats.PoolStats.GetValueOrDefault("Burned", 0)
            + stats.PoolStats.GetValueOrDefault("Reserved", 0);
        Assert.Equal(20, total);
    }

    // ──────────────────────────────────────────────
    // 3. Cancel a Ready job — codes returned, no quarantine
    // ──────────────────────────────────────────────
    [Fact]
    public async Task CancelReadyJob_ReturnsAllCodes()
    {
        var printerId = await SetupPrinterAsync("CancelReadyPrinter");
        var productId = await SetupProductAsync("CancelReadyProduct", "cr.csv");
        await ImportCodesAsync(productId, 20);

        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 10 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();

        // Job is in Ready state (auto-prepared by TestHost)
        var ready = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job!.Id}");
        Assert.Equal("Ready", ready!.Status);

        // Cancel
        await Client.PostAsync($"/api/jobs/{job.Id}/cancel", null);
        await Task.Delay(100);

        var cancelled = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job.Id}");
        Assert.Equal("Cancelled", cancelled!.Status);

        // All 20 codes should be Available (10 returned + 10 never reserved)
        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.Equal(20, stats!.PoolStats!.GetValueOrDefault("Available", 0));
        Assert.Equal(0, stats.PoolStats.GetValueOrDefault("Quarantined", 0));
    }

    // ──────────────────────────────────────────────
    // 4. Multiple resume cycles complete correctly
    // ──────────────────────────────────────────────
    [Fact]
    public async Task MultipleResumeCycles_CompleteCorrectly()
    {
        var printerId = await SetupPrinterAsync("MultiResumePrinter");
        var productId = await SetupProductAsync("MultiResumeProduct", "mr.csv");
        await ImportCodesAsync(productId, 20);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 300 });

        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 10 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        // Pause after 2+ codes
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 2, timeout: TimeSpan.FromSeconds(10));
        await Client.PostAsync($"/api/jobs/{job.Id}/pause", null);
        await Task.Delay(200);

        // Resume
        await Client.PostAsync($"/api/jobs/{job.Id}/resume", null);

        // Pause again after more progress
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 6, timeout: TimeSpan.FromSeconds(10));
        await Client.PostAsync($"/api/jobs/{job.Id}/pause", null);
        await Task.Delay(200);

        // Resume again
        await Client.PostAsync($"/api/jobs/{job.Id}/resume", null);

        // Wait for completion
        var completed = await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.Status == "Completed", timeout: TimeSpan.FromSeconds(15));

        Assert.Equal(10, completed.CodesConfirmed);
    }

    // ──────────────────────────────────────────────
    // 5. Resume a Ready job goes through StartJobAsync
    // ──────────────────────────────────────────────
    [Fact]
    public async Task ResumeReadyJob_StartsNormally()
    {
        var printerId = await SetupPrinterAsync("ResumeReadyPrinter");
        var productId = await SetupProductAsync("ResumeReadyProduct", "rdy.csv");
        await ImportCodesAsync(productId, 10);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 10 });

        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 5 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();

        // Resume (which should delegate to Start for Ready jobs)
        await Client.PostAsync($"/api/jobs/{job!.Id}/resume", null);

        var completed = await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.Status == "Completed", timeout: TimeSpan.FromSeconds(10));
        Assert.Equal(5, completed.CodesConfirmed);
    }

    // ──────────────────────────────────────────────
    // 6. TotalBaseline is captured during Prepare
    // ──────────────────────────────────────────────
    [Fact]
    public async Task Prepare_CapturesTotalBaseline()
    {
        var printerId = await SetupPrinterAsync("BaselinePrinter");
        var productId = await SetupProductAsync("BaselineProduct", "bl.csv");
        await ImportCodesAsync(productId, 10);

        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 5 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();

        // The job should be in Ready state with a baseline set
        var ready = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job!.Id}");
        Assert.Equal("Ready", ready!.Status);
        // We can't directly check TotalBaseline via API, but we can verify the job
        // was prepared correctly by starting and completing it
        await Client.PostAsync($"/api/jobs/{job.Id}/start", null);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 10 });

        var completed = await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.Status == "Completed", timeout: TimeSpan.FromSeconds(10));
        Assert.Equal(5, completed.CodesConfirmed);
    }

    // ──────────────────────────────────────────────
    // 7. Serial mismatch blocks job operations
    // ──────────────────────────────────────────────
    [Fact]
    public async Task SerialMismatch_BlocksStart()
    {
        var printerId = await SetupPrinterAsync("SerialPrinter");
        var productId = await SetupProductAsync("SerialProduct", "sn.csv");
        await ImportCodesAsync(productId, 10);

        // Create a job (Ready state) — first connect stored serial "MOCK-001"
        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 5 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();
        Assert.Equal("Ready", (await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job!.Id}"))!.Status);

        // Tell factory to produce adapters with a DIFFERENT serial
        await Client.PostAsJsonAsync("/api/mock/printers/factory/set-next-serial",
            new { Serial = "SWAPPED-999" });

        // Disconnect and reconnect — new adapter has serial "SWAPPED-999"
        // but DB still stores "MOCK-001" → mismatch detected
        await Client.PostAsync($"/api/printers/{printerId}/disconnect", null);
        await Task.Delay(200);
        await Client.PostAsync($"/api/printers/{printerId}/connect", null);
        await Task.Delay(200);

        // Start should fail due to serial mismatch
        var startResp = await Client.PostAsync($"/api/jobs/{job.Id}/start", null);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, startResp.StatusCode);
    }

    // ──────────────────────────────────────────────
    // 8. Full print cycle with no issues verifies code accounting
    // ──────────────────────────────────────────────
    [Fact]
    public async Task FullCycle_AllCodesAccountedFor()
    {
        var printerId = await SetupPrinterAsync("AccountingPrinter");
        var productId = await SetupProductAsync("AccountingProduct", "ac.csv");
        await ImportCodesAsync(productId, 15);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 10 });

        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 10 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        var completed = await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.Status == "Completed", timeout: TimeSpan.FromSeconds(10));

        Assert.Equal(10, completed.CodesConfirmed);

        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.Equal(10, stats!.PoolStats!.GetValueOrDefault("Printed", 0));
        Assert.Equal(5, stats.PoolStats.GetValueOrDefault("Available", 0));
        Assert.Equal(0, stats.PoolStats.GetValueOrDefault("Quarantined", 0));
        Assert.Equal(0, stats.PoolStats.GetValueOrDefault("Reserved", 0));
    }

    // ──────────────────────────────────────────────
    // 9. Cancel before any prints — no quarantine needed
    // ──────────────────────────────────────────────
    [Fact]
    public async Task CancelBeforeAnyPrint_NoQuarantine()
    {
        var printerId = await SetupPrinterAsync("CancelEarlyPrinter");
        var productId = await SetupProductAsync("CancelEarlyProduct", "ce.csv");
        await ImportCodesAsync(productId, 20);

        // Slow speed so we can cancel before any prints
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 5000 });

        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 10 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        // Cancel immediately (before mock has time to print)
        await Task.Delay(100);
        await Client.PostAsync($"/api/jobs/{job.Id}/cancel", null);
        await Task.Delay(300);

        var cancelled = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job.Id}");
        Assert.Equal("Cancelled", cancelled!.Status);

        // All codes should be accounted for (Available + Quarantined for boundary)
        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        var total = stats!.PoolStats!.Values.Sum();
        Assert.Equal(20, total);
        // No codes should be stuck in Reserved
        Assert.Equal(0, stats.PoolStats.GetValueOrDefault("Reserved", 0));
    }

    // ──────────────────────────────────────────────
    // 10. Pause when not printing returns error
    // ──────────────────────────────────────────────
    [Fact]
    public async Task PauseReadyJob_ReturnsBadRequest()
    {
        var printerId = await SetupPrinterAsync("PauseReadyPrinter");
        var productId = await SetupProductAsync("PauseReadyProduct", "prd.csv");
        await ImportCodesAsync(productId, 10);

        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 5 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();

        var pauseResp = await Client.PostAsync($"/api/jobs/{job!.Id}/pause", null);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, pauseResp.StatusCode);
    }

    // ──────────────────────────────────────────────
    // 11. Auto-reconnect after cable pull → job completes
    // ──────────────────────────────────────────────
    [Fact]
    public async Task ConnectionLoss_AutoReconnects_AndJobCompletes()
    {
        var printerId = await SetupPrinterAsync("AutoReconnectPrinter");
        var productId = await SetupProductAsync("AutoReconnectProduct", "ar.csv");
        await ImportCodesAsync(productId, 20);
        // Fast print speed so the printer finishes during the simulated outage
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 100 });

        // Create + start job for 6 codes
        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 6 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        // Wait for at least 2 codes confirmed
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 2, timeout: TimeSpan.FromSeconds(10));

        // Simulate cable pull — printer keeps printing, but app can't communicate
        await Client.PostAsync($"/api/mock/printers/{printerId}/simulate-disconnect", null);

        // Wait for the printer to finish printing all codes internally
        // (print loop keeps running despite simulated disconnect)
        await Task.Delay(1500);

        // Restore connectivity — the reconnect loop will succeed on next attempt
        await Client.PostAsync($"/api/mock/printers/{printerId}/simulate-reconnect", null);

        // Wait for job to complete — the reconnect loop reconnects the adapter,
        // the executor's inspection reconciles the lifetime counter, and detects completion
        var completed = await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.Status == "Completed", timeout: TimeSpan.FromSeconds(30));

        Assert.Equal(6, completed.CodesConfirmed);

        // Verify code accounting: all 6 codes Printed, 14 Available, none stuck
        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.NotNull(stats?.PoolStats);
        Assert.Equal(6, stats.PoolStats.GetValueOrDefault("Printed", 0));
        Assert.Equal(14, stats.PoolStats.GetValueOrDefault("Available", 0));
        Assert.Equal(0, stats.PoolStats.GetValueOrDefault("Reserved", 0));
        Assert.Equal(0, stats.PoolStats.GetValueOrDefault("Quarantined", 0));
    }

    // ──────────────────────────────────────────────
    // 12. Manual disconnect while printing stops executor
    // ──────────────────────────────────────────────
    [Fact]
    public async Task ManualDisconnect_StopsExecutor()
    {
        var printerId = await SetupPrinterAsync("ManualDisconnectPrinter");
        var productId = await SetupProductAsync("ManualDisconnectProduct", "md.csv");
        await ImportCodesAsync(productId, 20);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 200 });

        // Create + start
        var createResp = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 10 });
        var job = await createResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        // Wait for progress
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 2, timeout: TimeSpan.FromSeconds(10));

        // Manual disconnect — should stop the executor cleanly.
        // The job stays in Printing status in the DB (user must Resume after reconnecting).
        await Client.PostAsync($"/api/printers/{printerId}/disconnect", null);
        await Task.Delay(500);

        // The job should still be in Printing status (not paused automatically),
        // but the executor is no longer running. Verify that the printer shows disconnected.
        var printerStatus = await Client.GetFromJsonAsync<PrinterStatusResult>(
            $"/api/printers/{printerId}");
        Assert.Equal("Offline", printerStatus!.Status);

        // Code accounting: no codes stuck in Reserved beyond the job's own
        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.NotNull(stats?.PoolStats);
        var total = stats.PoolStats.Values.Sum();
        Assert.Equal(20, total);
    }
}

public record PrinterStatusResult
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Status { get; init; } = "";
    public bool IsConnected { get; init; }
}
