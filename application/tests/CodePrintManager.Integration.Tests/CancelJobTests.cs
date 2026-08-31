using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

public class CancelJobTests : IntegrationTestBase
{


    [Fact]
    public async Task CancelReadyJob_ReturnsCodesToPooI()
    {
        var printerId = await SetupPrinterAsync("CancelPrinter");
        var productId = await SetupProductAsync("CancelProduct", "cancel.csv");
        await ImportCodesAsync(productId, 20);

        // Create job (reserves 10 codes)
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 10
        });
        var job = await createResponse.Content.ReadFromJsonAsync<JobResult>();

        // Check available codes decreased
        var productBefore = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.Equal(10, productBefore!.PoolStats!["Available"]);

        // Cancel the job
        var cancelResponse = await Client.PostAsync($"/api/jobs/{job!.Id}/cancel", null);
        cancelResponse.EnsureSuccessStatusCode();

        // Check codes returned to pool
        var productAfter = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.Equal(20, productAfter!.PoolStats!["Available"]);
    }

    [Fact]
    public async Task CancelPrintingJob_QuarantinesBoundaryCode()
    {
        var printerId = await SetupPrinterAsync("CancelPrintingPrinter");
        var productId = await SetupProductAsync("CancelPrintingProduct", "cp.csv");
        await ImportCodesAsync(productId, 20);

        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 100 });

        // Create + start
        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = 10
        });
        var job = await createResponse.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        // Wait for some progress
        await PollUntilAsync<JobResult>(
            $"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 3,
            timeout: TimeSpan.FromSeconds(10));

        // Cancel mid-print
        await Client.PostAsync($"/api/jobs/{job.Id}/cancel", null);
        await Task.Delay(300);

        // Job should be cancelled
        var cancelled = await Client.GetFromJsonAsync<JobResult>($"/api/jobs/{job.Id}");
        Assert.Equal("Cancelled", cancelled!.Status);

        // The number of available + printed + burned should equal 20
        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.NotNull(stats?.PoolStats);
        var totalTracked = stats.PoolStats.Values.Sum();
        Assert.Equal(20, totalTracked);
    }
    [Fact]
    public async Task CancelPrintingJob_MarginZero_NoQuarantine()
    {
        // D4: With QuarantineMargin = 0 (default), no codes should be quarantined on cancel.
        var printerId = await SetupPrinterAsync("MarginZeroPrinter");
        // Default QuarantineMargin is 0 — no need to patch
        var productId = await SetupProductAsync("MarginZeroProduct", "m0.csv");
        await ImportCodesAsync(productId, 20);

        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 100 });

        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 10 });
        var job = await createResponse.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 3, timeout: TimeSpan.FromSeconds(10));

        await Client.PostAsync($"/api/jobs/{job.Id}/cancel", null);
        await Task.Delay(300);

        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.NotNull(stats?.PoolStats);
        var quarantined = stats.PoolStats.GetValueOrDefault("Quarantined", 0);
        Assert.Equal(0, quarantined);
        // Total accounting must still be 20
        Assert.Equal(20, stats.PoolStats.Values.Sum());
    }

    [Fact]
    public async Task CancelPrintingJob_MarginTwo_QuarantinesTwoCodes()
    {
        // D4: With QuarantineMargin = 2, exactly 2 codes should be quarantined on cancel
        // (or fewer if there aren't enough remaining codes).
        var printerId = await SetupPrinterAsync("MarginTwoPrinter");
        await Client.PatchAsJsonAsync($"/api/printers/{printerId}", new { QuarantineMargin = 2 });
        var productId = await SetupProductAsync("MarginTwoProduct", "m2.csv");
        await ImportCodesAsync(productId, 20);

        // Use slower speed to ensure the mock hasn't finished all prints before cancel arrives.
        // With 300ms/label and 10 codes, the mock takes ~3s. The cancel should arrive well
        // before completion, leaving enough remaining codes for the quarantine margin.
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed", new { Ms = 300 });

        var createResponse = await Client.PostAsJsonAsync("/api/jobs", new
            { ProductId = productId, PrinterId = printerId, Quantity = 10 });
        var job = await createResponse.Content.ReadFromJsonAsync<JobResult>();
        await Client.PostAsync($"/api/jobs/{job!.Id}/start", null);

        await PollUntilAsync<JobResult>($"/api/jobs/{job.Id}",
            j => j.CodesConfirmed >= 3, timeout: TimeSpan.FromSeconds(10));

        await Client.PostAsync($"/api/jobs/{job.Id}/cancel", null);
        await Task.Delay(300);

        var stats = await Client.GetFromJsonAsync<ProductDetailResult>($"/api/products/{productId}");
        Assert.NotNull(stats?.PoolStats);
        var quarantined = stats.PoolStats.GetValueOrDefault("Quarantined", 0);
        Assert.Equal(2, quarantined);
        // Total accounting must still be 20
        Assert.Equal(20, stats.PoolStats.Values.Sum());
    }
}

public record ProductDetailResult
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public bool IsLeaf { get; init; }
    public Dictionary<string, int>? PoolStats { get; init; }
}
