using System.Net;
using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

/// <summary>
/// Full end-to-end tests covering the happy path and all corner cases
/// for a single-product, single-printer workflow.
/// </summary>
public class FullE2ETests : IntegrationTestBase
{
    // ──────────────────────────────────────────────
    // HAPPY PATH: Full cycle from scratch
    // ──────────────────────────────────────────────

    [Fact]
    public async Task HappyPath_FullCycleFromCleanDatabase()
    {
        // 1. Verify clean DB — no printers, no products, no jobs
        var printers = await Client.GetFromJsonAsync<List<PrinterResult>>("/api/printers");
        Assert.NotNull(printers);
        Assert.Empty(printers);

        var products = await Client.GetFromJsonAsync<List<ProductResult>>("/api/products");
        Assert.NotNull(products);
        Assert.Empty(products);

        var activeJobs = await Client.GetFromJsonAsync<List<JobResult>>("/api/jobs/active");
        Assert.NotNull(activeJobs);
        Assert.Empty(activeJobs);

        // 2. Add a product
        var productResp = await Client.PostAsJsonAsync("/api/products", new
        {
            Name = "Aspirin 500mg",
            IsLeaf = true,
            TemplateFile = "aspirin.rox",
            CsvName = "aspirin_codes.csv"
        });
        productResp.EnsureSuccessStatusCode();
        var product = await productResp.Content.ReadFromJsonAsync<IdResult>();
        Assert.NotNull(product);
        Assert.True(product.Id > 0);

        // 3. Verify product exists with zero codes
        var productDetail = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{product.Id}");
        Assert.NotNull(productDetail);
        Assert.Equal("Aspirin 500mg", productDetail.Name);
        Assert.True(productDetail.IsLeaf);

        // 4. Add a mock printer
        var printerResp = await Client.PostAsJsonAsync("/api/printers", new
        {
            Name = "Line-1 Savema",
            Ip = "mock",
            Port = 9100,
            AdapterType = "mock"
        });
        printerResp.EnsureSuccessStatusCode();
        var printer = await printerResp.Content.ReadFromJsonAsync<IdResult>();
        Assert.NotNull(printer);
        Assert.True(printer.Id > 0);

        // 5. Connect printer
        var connectResp = await Client.PostAsync($"/api/printers/{printer.Id}/connect", null);
        connectResp.EnsureSuccessStatusCode();

        // Verify printer is connected
        var printerDetail = await Client.GetFromJsonAsync<PrinterDetailResult>($"/api/printers/{printer.Id}");
        Assert.NotNull(printerDetail);
        Assert.True(printerDetail.IsConnected);
        Assert.Equal("Idle", printerDetail.Status);

        // 6. Import codes
        var codes = Enumerable.Range(1, 50).Select(i => $"ASP{i:D10}").ToList();
        var importResp = await Client.PostAsJsonAsync($"/api/products/{product.Id}/import-csv", new
        {
            Codes = codes,
            BatchName = "batch-001"
        });
        importResp.EnsureSuccessStatusCode();
        var importResult = await importResp.Content.ReadFromJsonAsync<ImportResult>();
        Assert.NotNull(importResult);
        Assert.Equal(50, importResult.Imported);
        Assert.Equal(0, importResult.Duplicates);

        // Verify pool stats
        var statsResp = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{product.Id}");
        Assert.NotNull(statsResp?.PoolStats);
        Assert.Equal(50, statsResp.PoolStats.GetValueOrDefault("Available", 0));

        // 7. Speed up mock printer (10ms per code)
        await Client.PostAsJsonAsync($"/api/mock/printers/{printer.Id}/set-speed", new { Ms = 10 });

        // 8. Create + prepare a print job (10 codes)
        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = product.Id,
            PrinterId = printer.Id,
            Quantity = 10
        });
        jobResp.EnsureSuccessStatusCode();
        var job = await jobResp.Content.ReadFromJsonAsync<JobResult>();
        Assert.NotNull(job);
        Assert.Equal("Ready", job.Status);
        Assert.Equal(10, job.Quantity);

        // 9. Start printing
        var startResp = await Client.PostAsync($"/api/jobs/{job.Id}/start", null);
        startResp.EnsureSuccessStatusCode();

        // 10. Poll until completed
        var completed = await PollUntilAsync<JobResult>(
            $"/api/jobs/{job.Id}",
            j => j.Status == "Completed",
            timeout: TimeSpan.FromSeconds(15));

        Assert.Equal("Completed", completed.Status);
        Assert.Equal(10, completed.CodesConfirmed);

        // 11. Verify code pool shows 10 printed, 40 available
        var finalStats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{product.Id}");
        Assert.NotNull(finalStats?.PoolStats);
        Assert.Equal(40, finalStats.PoolStats.GetValueOrDefault("Available", 0));
        Assert.Equal(10, finalStats.PoolStats.GetValueOrDefault("Printed", 0));

        // 12. Verify job appears in history (not active)
        var historyJobs = await Client.GetFromJsonAsync<List<JobResult>>("/api/jobs/history");
        Assert.NotNull(historyJobs);
        Assert.Contains(historyJobs, j => j.Id == job.Id && j.Status == "Completed");

        var active = await Client.GetFromJsonAsync<List<JobResult>>("/api/jobs/active");
        Assert.DoesNotContain(active!, j => j.Id == job.Id);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Print without a connected printer
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_PrintWithoutPrinterConnected_Fails()
    {
        var productId = await SetupProductAsync("NoPrinterProduct", "noprinter.csv");
        await ImportCodesAsync(productId, 20);

        // Create printer but DON'T connect it
        var printerResp = await Client.PostAsJsonAsync("/api/printers", new
        {
            Name = "Disconnected Printer",
            Ip = "mock",
            Port = 9100,
            AdapterType = "mock"
        });
        var printer = await printerResp.Content.ReadFromJsonAsync<IdResult>();

        // Attempt to create job — should fail because printer is not connected
        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printer!.Id,
            Quantity = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, jobResp.StatusCode);
        var error = await jobResp.Content.ReadFromJsonAsync<ErrorResult>();
        Assert.Contains("not connected", error!.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Print with no codes imported (zero available)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_PrintWithNoCodes_Fails()
    {
        var printerId = await SetupPrinterAsync("EmptyCodesPrinter");
        var productId = await SetupProductAsync("EmptyProduct", "empty.csv");
        // Do NOT import any codes

        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, jobResp.StatusCode);
        var error = await jobResp.Content.ReadFromJsonAsync<ErrorResult>();
        Assert.NotNull(error);
        // Should fail because not enough codes are available
        Assert.NotEmpty(error.Error);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Request more codes than available
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_RequestMoreCodesThanAvailable_Fails()
    {
        var printerId = await SetupPrinterAsync("OverflowPrinter");
        var productId = await SetupProductAsync("SmallPool", "small.csv");
        await ImportCodesAsync(productId, 5); // only 5 codes

        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 50 // requesting 50, only 5 available
        });

        Assert.Equal(HttpStatusCode.BadRequest, jobResp.StatusCode);
        var error = await jobResp.Content.ReadFromJsonAsync<ErrorResult>();
        Assert.NotNull(error);
        Assert.NotEmpty(error.Error);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Import duplicate codes
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_ImportDuplicateCodes_SkipsDuplicates()
    {
        var productId = await SetupProductAsync("DupeProduct", "dupe.csv");

        // Import first batch
        var codes1 = new List<string> { "DUP001", "DUP002", "DUP003" };
        var resp1 = await Client.PostAsJsonAsync($"/api/products/{productId}/import-csv", new
        {
            Codes = codes1,
            BatchName = "batch1"
        });
        var result1 = await resp1.Content.ReadFromJsonAsync<ImportResult>();
        Assert.Equal(3, result1!.Imported);
        Assert.Equal(0, result1.Duplicates);

        // Import overlapping batch (2 duplicates + 2 new)
        var codes2 = new List<string> { "DUP002", "DUP003", "DUP004", "DUP005" };
        var resp2 = await Client.PostAsJsonAsync($"/api/products/{productId}/import-csv", new
        {
            Codes = codes2,
            BatchName = "batch2"
        });
        var result2 = await resp2.Content.ReadFromJsonAsync<ImportResult>();
        Assert.Equal(2, result2!.Imported);
        Assert.Equal(2, result2.Duplicates);

        // Total available should be 5 (not 7)
        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.Equal(5, stats!.PoolStats!.GetValueOrDefault("Available", 0));
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Start a job that's not ready
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_StartJobNotReady_Fails()
    {
        // A completed job cannot be started again
        var printerId = await SetupPrinterAsync("StartNotReadyPrinter");
        var productId = await SetupProductAsync("StartNotReadyProduct", "snr.csv");
        await ImportCodesAsync(productId, 10);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 10 });

        // Create, prepare, start, wait for completion
        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 3
        });
        var job = await jobResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}", j => j.Status == "Completed",
            timeout: TimeSpan.FromSeconds(10));

        // Try to start the completed job again
        var startResp = await Client.PostAsync($"/api/jobs/{job.Id}/start", null);
        Assert.Equal(HttpStatusCode.BadRequest, startResp.StatusCode);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Cancel a job that's already completed
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_CancelCompletedJob_IsRejected()
    {
        var printerId = await SetupPrinterAsync("CancelCompletedPrinter");
        var productId = await SetupProductAsync("CancelCompletedProduct", "cc.csv");
        await ImportCodesAsync(productId, 10);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 10 });

        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 3
        });
        var job = await jobResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}", j => j.Status == "Completed",
            timeout: TimeSpan.FromSeconds(10));

        // Cancel a completed job — should be rejected
        var cancelResp = await Client.PostAsync($"/api/jobs/{job.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.BadRequest, cancelResp.StatusCode);

        // Verify job remains Completed
        var final = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job.Id}");
        Assert.Equal("Completed", final!.Status);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Pause a non-printing job
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_PauseReadyJob_Fails()
    {
        var printerId = await SetupPrinterAsync("PauseReadyPrinter");
        var productId = await SetupProductAsync("PauseReadyProduct", "pr.csv");
        await ImportCodesAsync(productId, 10);

        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 5
        });
        var job = await jobResp.Content.ReadFromJsonAsync<JobResult>();
        Assert.Equal("Ready", job!.Status);

        // Try to pause a job that's Ready (not Printing)
        var pauseResp = await Client.PostAsync($"/api/jobs/{job.Id}/pause", null);
        Assert.Equal(HttpStatusCode.BadRequest, pauseResp.StatusCode);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Resume a non-paused job
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_ResumeCompletedJob_Fails()
    {
        var printerId = await SetupPrinterAsync("ResumeCompletedPrinter");
        var productId = await SetupProductAsync("ResumeCompletedProduct", "rc.csv");
        await ImportCodesAsync(productId, 10);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 10 });

        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 3
        });
        var job = await jobResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}", j => j.Status == "Completed",
            timeout: TimeSpan.FromSeconds(10));

        // Try to resume a completed job
        var resumeResp = await Client.PostAsync($"/api/jobs/{job.Id}/resume", null);
        Assert.Equal(HttpStatusCode.BadRequest, resumeResp.StatusCode);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Cancel a paused job returns codes without burning
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_CancelPausedJob_NoBurn()
    {
        var printerId = await SetupPrinterAsync("PauseCancelPrinter");
        var productId = await SetupProductAsync("PauseCancelProduct", "pc.csv");
        await ImportCodesAsync(productId, 20);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 200 });

        // Create, start, wait for some progress
        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 10
        });
        var job = await jobResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 2,
            timeout: TimeSpan.FromSeconds(15));

        // Pause
        var pauseResp = await Client.PostAsync($"/api/jobs/{job.Id}/pause", null);
        pauseResp.EnsureSuccessStatusCode();
        await Task.Delay(200);

        var pausedJob = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job.Id}");
        Assert.Equal("Paused", pausedJob!.Status);
        var confirmedAtPause = pausedJob.CodesConfirmed;

        // Cancel the paused job
        var cancelResp = await Client.PostAsync($"/api/jobs/{job.Id}/cancel", null);
        cancelResp.EnsureSuccessStatusCode();
        await Task.Delay(200);

        // Verify: no codes burned — all unprinted codes returned to pool
        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.NotNull(stats?.PoolStats);
        var available = stats.PoolStats.GetValueOrDefault("Available", 0);
        var printed = stats.PoolStats.GetValueOrDefault("Printed", 0);
        var burned = stats.PoolStats.GetValueOrDefault("Burned", 0);

        Assert.Equal(0, burned); // No uncertainty from paused state
        Assert.Equal(confirmedAtPause, printed);
        Assert.Equal(20 - confirmedAtPause, available); // All unprinted codes returned
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Two jobs on the same printer simultaneously
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_TwoJobsSamePrinter_SecondFails()
    {
        var printerId = await SetupPrinterAsync("BusyPrinter");
        var productId = await SetupProductAsync("BusyProduct", "busy.csv");
        await ImportCodesAsync(productId, 100);

        // Slow printer so the first job is still running
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 500 });

        // Start first job
        var job1Resp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 20
        });
        job1Resp.EnsureSuccessStatusCode();
        var job1 = await job1Resp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job1!.Id}/start", null);

        // Wait a bit for it to be in Printing state
        await Task.Delay(200);

        // Try to start second job on same printer — should fail
        // (either "not idle" status check or DB constraint violation)
        var job2Resp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, job2Resp.StatusCode);

        // Clean up: cancel the first job
        await Client.PostAsync($"/api/jobs/{job1.Id}/cancel", null);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Print with injected printer error
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_PrinterErrorDuringPrep_FailsGracefully()
    {
        var printerId = await SetupPrinterAsync("ErrorPrinter");
        var productId = await SetupProductAsync("ErrorProduct", "err.csv");
        await ImportCodesAsync(productId, 10);

        // Inject error BEFORE creating the job — printer will report Error status
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/inject-error",
            new { Status = "Error" });

        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 5
        });

        // Should fail because printer status is Error (not Idle)
        Assert.Equal(HttpStatusCode.BadRequest, jobResp.StatusCode);
        var error = await jobResp.Content.ReadFromJsonAsync<ErrorResult>();
        Assert.Contains("not idle", error!.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Verify printer storage shows uploaded files
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_VerifyPrinterStorageAfterJob()
    {
        var printerId = await SetupPrinterAsync("StoragePrinter");
        var productId = await SetupProductAsync("StorageProduct", "storage_data.csv");
        await ImportCodesAsync(productId, 10);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 10 });

        // Create and complete a job
        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 5
        });
        var job = await jobResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}", j => j.Status == "Completed",
            timeout: TimeSpan.FromSeconds(10));

        // Check printer storage has the CSV and template from the job
        // SetupProductAsync uses "test.rox" as the template file
        var storage = await Client.GetFromJsonAsync<StorageResult>($"/api/printers/{printerId}/storage");
        Assert.NotNull(storage);
        Assert.Contains("storage_data.csv", storage.CsvFiles);
        Assert.Contains("test.rox", storage.Templates);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Delete product with active codes
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_DeleteProductWithActiveJob_Fails()
    {
        var printerId = await SetupPrinterAsync("DeleteProdPrinter");
        var productId = await SetupProductAsync("DeleteProdProduct", "del.csv");
        await ImportCodesAsync(productId, 20);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 500 });

        // Create job (puts codes in Reserved state)
        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 5
        });
        jobResp.EnsureSuccessStatusCode();

        // Try to delete the product — should fail
        var deleteResp = await Client.DeleteAsync($"/api/products/{productId}");
        Assert.Equal(HttpStatusCode.Conflict, deleteResp.StatusCode);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Create job with quantity = 0
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_ZeroQuantityJob_Fails()
    {
        var printerId = await SetupPrinterAsync("ZeroQtyPrinter");
        var productId = await SetupProductAsync("ZeroQtyProduct", "zero.csv");
        await ImportCodesAsync(productId, 10);

        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 0
        });

        Assert.Equal(HttpStatusCode.BadRequest, jobResp.StatusCode);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Create job with non-existent product
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_NonExistentProduct_Fails()
    {
        var printerId = await SetupPrinterAsync("NoProductPrinter");

        // Attempt to create a job for a non-existent product
        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = 9999,
            PrinterId = printerId,
            Quantity = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, jobResp.StatusCode);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Create job with non-existent printer
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_NonExistentPrinter_Fails()
    {
        var productId = await SetupProductAsync("GhostPrinterProduct", "ghost.csv");
        await ImportCodesAsync(productId, 10);

        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = 9999, // doesn't exist
            Quantity = 5
        });

        // Should fail — no adapter found
        Assert.Equal(HttpStatusCode.BadRequest, jobResp.StatusCode);
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Codes are properly returned on cancel
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_CancelReturnsUnprintedCodes()
    {
        var printerId = await SetupPrinterAsync("ReturnCodesPrinter");
        var productId = await SetupProductAsync("ReturnCodesProduct", "return.csv");
        await ImportCodesAsync(productId, 20);

        // Slow printer so we can cancel mid-print
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 500 });

        // Create and start job for 10 codes
        var jobResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 10
        });
        var job = await jobResp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        // Wait for some progress (at least 1 code printed)
        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 1,
            timeout: TimeSpan.FromSeconds(10));

        // Cancel
        await Client.PostAsync($"/api/jobs/{job.Id}/cancel", null);
        await Task.Delay(500); // let cancellation settle

        // Check final state
        var finalJob = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job.Id}");
        Assert.Equal("Cancelled", finalJob!.Status);

        // Check code pool — codes should be returned to pool (not all stuck in Reserved)
        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.NotNull(stats?.PoolStats);
        var available = stats.PoolStats.GetValueOrDefault("Available", 0);
        var printed = stats.PoolStats.GetValueOrDefault("Printed", 0);
        var burned = stats.PoolStats.GetValueOrDefault("Burned", 0);
        var reserved = stats.PoolStats.GetValueOrDefault("Reserved", 0);

        // After cancel: some codes were printed, some burned, rest returned to available
        Assert.True(printed >= 1, $"Expected at least 1 printed, got {printed}");
        // Total accounted for must equal 20
        var total = available + printed + burned + reserved;
        Assert.Equal(20, total);
        // Available should be > 10 (10 from the untouched pool + some returned)
        Assert.True(available >= 10, $"Expected available >= 10, got {available}");
    }

    // ──────────────────────────────────────────────
    // CORNER CASE: Multiple print runs deplete the pool correctly
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CornerCase_MultipleJobsDepleteCodes()
    {
        var printerId = await SetupPrinterAsync("DepletePrinter");
        var productId = await SetupProductAsync("DepleteProduct", "deplete.csv");
        await ImportCodesAsync(productId, 15);
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 10 });

        // Job 1: print 5
        var job1Resp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 5
        });
        var job1 = await job1Resp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job1!.Id}/start", null);
        await PollUntilAsync<JobResult>($"/api/jobs/{job1.Id}", j => j.Status == "Completed",
            timeout: TimeSpan.FromSeconds(10));

        // Job 2: print 5 more
        var job2Resp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 5
        });
        var job2 = await job2Resp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job2!.Id}/start", null);
        await PollUntilAsync<JobResult>($"/api/jobs/{job2.Id}", j => j.Status == "Completed",
            timeout: TimeSpan.FromSeconds(10));

        // Job 3: print 5 more (uses last 5)
        var job3Resp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 5
        });
        var job3 = await job3Resp.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job3!.Id}/start", null);
        await PollUntilAsync<JobResult>($"/api/jobs/{job3.Id}", j => j.Status == "Completed",
            timeout: TimeSpan.FromSeconds(10));

        // Verify: all codes consumed (printed + burned), none available
        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        var available = stats!.PoolStats!.GetValueOrDefault("Available", 0);
        var printed = stats.PoolStats.GetValueOrDefault("Printed", 0);
        var burned = stats.PoolStats.GetValueOrDefault("Burned", 0);

        Assert.Equal(0, available);
        // All 15 codes consumed: printed + burned = 15
        Assert.Equal(15, printed + burned);
        // Most should be printed (some may be burned at counter boundaries)
        Assert.True(printed >= 13, $"Expected at least 13 printed, got {printed}");

        // Job 4: should fail — no codes left
        var job4Resp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId, PrinterId = printerId, Quantity = 1
        });
        Assert.Equal(HttpStatusCode.BadRequest, job4Resp.StatusCode);
    }

    // ──────────────────────────────────────────────
    // DTOs for deserialization
    // ──────────────────────────────────────────────

    private record IdResult(int Id, string? Name);
    private record ErrorResult(string Error);
    private record ImportResult(int Imported, int Duplicates, List<string>? Errors);
    private record PrinterResult(int Id, string Name, string IpAddress, int Port, bool IsConnected);
    private record PrinterDetailResult(int Id, string Name, bool IsConnected, string Status);
    private record ProductResult(int Id, string Name, bool IsLeaf);
    private record ProductDetailResult(int Id, string Name, bool IsLeaf,
        Dictionary<string, int>? PoolStats);
    private record StorageResult(List<string> Templates, List<string> CsvFiles);
}
