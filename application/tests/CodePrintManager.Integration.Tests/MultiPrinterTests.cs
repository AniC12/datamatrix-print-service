using System.Net;
using System.Net.Http.Json;

namespace CodePrintManager.Integration.Tests;

/// <summary>
/// Integration tests for multi-printer concurrency scenarios.
/// Validates independent parallel jobs, fault isolation, and uniqueness constraints.
/// </summary>
public class MultiPrinterTests : IntegrationTestBase
{
    // ──────────────────────────────────────────────
    // G1. Two printers, two products, independent jobs complete simultaneously
    // ──────────────────────────────────────────────

    [Fact]
    public async Task G1_TwoPrinters_TwoProducts_IndependentJobsComplete()
    {
        // Setup printer A + product A
        var printerA = await SetupPrinterAsync("G1PrinterA");
        var productA = await SetupProductAsync("G1ProductA", "g1a.csv");
        await ImportCodesAsync(productA, 10, "PA");

        // Setup printer B + product B
        var printerB = await SetupPrinterAsync("G1PrinterB");
        var productB = await SetupProductAsync("G1ProductB", "g1b.csv");
        await ImportCodesAsync(productB, 10, "PB");

        // Set speed on both printers
        await SetPrintSpeedAsync(printerA, 100);
        await SetPrintSpeedAsync(printerB, 100);

        // Create and start both jobs
        var jobA = await CreateAndPrepareJobAsync(productA, printerA, 10);
        var jobB = await CreateAndPrepareJobAsync(productB, printerB, 10);
        await StartJobAsync(jobA);
        await StartJobAsync(jobB);

        // Wait for both to complete
        await Task.WhenAll(
            WaitForJobStatusAsync(jobA, "Completed", TimeSpan.FromSeconds(30)),
            WaitForJobStatusAsync(jobB, "Completed", TimeSpan.FromSeconds(30)));

        // Assert both jobs completed with correct counts
        var finalA = await GetJobAsync(jobA);
        Assert.Equal("Completed", finalA!.Status);
        Assert.Equal(10, finalA.CodesConfirmed);

        var finalB = await GetJobAsync(jobB);
        Assert.Equal("Completed", finalB!.Status);
        Assert.Equal(10, finalB.CodesConfirmed);

        // No cross-contamination: each product's codes are independent
        var statsA = await AssertCodeConservationAsync(productA, 10);
        Assert.Equal(10, statsA.Printed);
        Assert.Equal(0, statsA.Reserved);

        var statsB = await AssertCodeConservationAsync(productB, 10);
        Assert.Equal(10, statsB.Printed);
        Assert.Equal(0, statsB.Reserved);
    }

    // ──────────────────────────────────────────────
    // G2. One printer fails, other continues unaffected
    // ──────────────────────────────────────────────

    [Fact]
    public async Task G2_OnePrinterFails_OtherContinuesUnaffected()
    {
        // Setup printer A + product A
        var printerA = await SetupPrinterAsync("G2PrinterA");
        var productA = await SetupProductAsync("G2ProductA", "g2a.csv");
        await ImportCodesAsync(productA, 10, "PA");
        await SetPrintSpeedAsync(printerA, 200);

        // Setup printer B + product B
        var printerB = await SetupPrinterAsync("G2PrinterB");
        var productB = await SetupProductAsync("G2ProductB", "g2b.csv");
        await ImportCodesAsync(productB, 10, "PB");
        await SetPrintSpeedAsync(printerB, 100);

        // Start both jobs
        var jobA = await CreateAndPrepareJobAsync(productA, printerA, 10);
        var jobB = await CreateAndPrepareJobAsync(productB, printerB, 10);
        await StartJobAsync(jobA);
        await StartJobAsync(jobB);

        // Wait for some progress on A before injecting error
        await WaitForProgressAsync(jobA, 3);

        // Inject error on printer A mid-print — stops the print loop
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerA}/inject-error",
            new { Status = "Error" });

        // Status polling auto-detects Error on printer A (2 consecutive checks).
        // Printer B should continue unaffected.
        var errorA = await WaitForJobStatusAsync(jobA, "Error", TimeSpan.FromSeconds(30));
        Assert.Equal("Error", errorA.Status);

        // Wait for B to complete — should be unaffected by A's failure
        await WaitForJobStatusAsync(jobB, "Completed", TimeSpan.FromSeconds(30));

        var finalB = await GetJobAsync(jobB);
        Assert.Equal("Completed", finalB!.Status);
        Assert.Equal(10, finalB.CodesConfirmed);

        // Assert B's code accounting is correct — fault isolation
        var statsB = await AssertCodeConservationAsync(productB, 10);
        Assert.Equal(10, statsB.Printed);
        Assert.Equal(0, statsB.Reserved);
        Assert.Equal(0, statsB.Quarantined);

        // Cancel Error job A
        await CancelJobAsync(jobA);

        // Clear error on A, start a new job with remaining codes
        await Client.PostAsync($"/api/mock/printers/{printerA}/clear-error", null);
        await SetPrintSpeedAsync(printerA, 50);

        var statsA = await AssertCodeConservationAsync(productA, 10);
        Assert.Equal(0, statsA.Reserved);
        Assert.True(statsA.Quarantined > 0, "Expected quarantined codes from Error on printer A");

        if (statsA.Available > 0)
        {
            var newJobA = await CreateAndPrepareJobAsync(productA, printerA, statsA.Available);
            await StartJobAsync(newJobA);
            await WaitForJobStatusAsync(newJobA, "Completed", TimeSpan.FromSeconds(30));
        }

        // Final conservation holds for both products
        var finalStatsA = await AssertCodeConservationAsync(productA, 10);
        Assert.Equal(0, finalStatsA.Reserved);
    }

    // ──────────────────────────────────────────────
    // G3. Concurrent create on same printer -> second fails
    // ──────────────────────────────────────────────

    [Fact]
    public async Task G3_ConcurrentCreateOnSamePrinter_SecondFails()
    {
        var printerA = await SetupPrinterAsync("G3Printer");
        var productA = await SetupProductAsync("G3ProductA", "g3a.csv");
        var productB = await SetupProductAsync("G3ProductB", "g3b.csv");
        await ImportCodesAsync(productA, 10, "PA");
        await ImportCodesAsync(productB, 10, "PB");
        await SetPrintSpeedAsync(printerA, 200);

        // Start first job on printer A
        var jobA = await CreateAndPrepareJobAsync(productA, printerA, 10);
        await StartJobAsync(jobA);

        // Wait for printing to be in progress
        await WaitForProgressAsync(jobA, 2);

        // Try to create another job on the same printer (different product)
        var secondResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productB,
            PrinterId = printerA,
            Quantity = 5
        });

        // Second create should fail — partial unique index blocks it
        Assert.Equal(HttpStatusCode.BadRequest, secondResp.StatusCode);

        // Clean up: let the first job finish
        await WaitForJobStatusAsync(jobA, "Completed", TimeSpan.FromSeconds(30));
    }

    // ──────────────────────────────────────────────
    // G4. Concurrent create on same product -> second fails
    // ──────────────────────────────────────────────

    [Fact]
    public async Task G4_ConcurrentCreateOnSameProduct_SecondFails()
    {
        var printerA = await SetupPrinterAsync("G4PrinterA");
        var printerB = await SetupPrinterAsync("G4PrinterB");
        var product = await SetupProductAsync("G4Product", "g4.csv");
        await ImportCodesAsync(product, 20, "PX");
        await SetPrintSpeedAsync(printerA, 200);
        await SetPrintSpeedAsync(printerB, 200);

        // Start job for the product on printer A
        var jobA = await CreateAndPrepareJobAsync(product, printerA, 10);
        await StartJobAsync(jobA);

        // Wait for printing to be in progress
        await WaitForProgressAsync(jobA, 2);

        // Try to create job for same product on printer B
        var secondResp = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = product,
            PrinterId = printerB,
            Quantity = 10
        });

        // Second create should fail — one active job per product
        Assert.Equal(HttpStatusCode.BadRequest, secondResp.StatusCode);

        // Clean up: let the first job finish
        await WaitForJobStatusAsync(jobA, "Completed", TimeSpan.FromSeconds(30));
    }
}
