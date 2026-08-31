using System.Net.Http.Json;
using System.Text.Json;

namespace CodePrintManager.Integration.Tests;

public abstract class IntegrationTestBase : IAsyncLifetime, IDisposable
{
    protected HttpClient Client = null!;
    protected TestHostFactory Factory = null!;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Task InitializeAsync()
    {
        Factory = new TestHostFactory();
        Client = Factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
    }

    private static int _printerPortCounter;

    /// <summary>
    /// Creates a mock printer and connects it. Returns the printer ID.
    /// Each call uses a unique port to avoid the UNIQUE(IpAddress, Port) constraint.
    /// </summary>
    protected async Task<int> SetupPrinterAsync(string name = "TestPrinter")
    {
        var port = 9100 + Interlocked.Increment(ref _printerPortCounter);
        var response = await Client.PostAsJsonAsync("/api/printers", new
        {
            Name = name,
            Ip = "mock",
            Port = port,
            AdapterType = "mock"
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResult>(JsonOptions);

        await Client.PostAsync($"/api/printers/{result!.Id}/connect", null);
        return result.Id;
    }

    /// <summary>
    /// Creates a leaf product with template and csv name. Returns the product ID.
    /// </summary>
    protected async Task<int> SetupProductAsync(string name = "TestProduct", string csvName = "data.csv")
    {
        var response = await Client.PostAsJsonAsync("/api/products", new
        {
            Name = name,
            IsLeaf = true,
            TemplateFile = "test.rox",
            CsvName = csvName
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResult>(JsonOptions);
        return result!.Id;
    }

    /// <summary>
    /// Imports codes for a product. Returns import stats.
    /// </summary>
    protected async Task ImportCodesAsync(int productId, int count, string prefix = "CODE")
    {
        var codes = Enumerable.Range(1, count).Select(i => $"{prefix}{i:D8}").ToList();
        var response = await Client.PostAsJsonAsync($"/api/products/{productId}/import-csv", new
        {
            Codes = codes,
            BatchName = "test-batch"
        });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Polls a GET endpoint until the condition is met or timeout.
    /// </summary>
    protected async Task<T> PollUntilAsync<T>(string url, Func<T, bool> condition,
        TimeSpan? timeout = null, int pollIntervalMs = 200) where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            var result = await Client.GetFromJsonAsync<T>(url, JsonOptions);
            if (result != null && condition(result))
                return result;
            await Task.Delay(pollIntervalMs);
        }
        throw new TimeoutException($"Polling {url} timed out after {timeout?.TotalSeconds ?? 30}s");
    }

    // ─── Code accounting assertion helpers ───

    /// <summary>
    /// Asserts the conservation invariant: Available + Reserved + Printed + Quarantined + Burned = total.
    /// Returns the code stats for further assertions.
    /// </summary>
    protected async Task<CodeStats> AssertCodeConservationAsync(int productId, int expectedTotal)
    {
        var stats = await Client.GetFromJsonAsync<CodeStats>(
            $"/api/products/{productId}/code-stats", JsonOptions);
        Assert.NotNull(stats);
        var actualTotal = stats!.Available + stats.Reserved + stats.Printed
                        + stats.Quarantined + stats.Burned;
        Assert.Equal(expectedTotal, actualTotal);
        return stats;
    }

    /// <summary>
    /// Gets the code stats for a product. Returns null if the endpoint fails.
    /// </summary>
    protected async Task<CodeStats?> GetCodeStatsAsync(int productId)
    {
        return await Client.GetFromJsonAsync<CodeStats>(
            $"/api/products/{productId}/code-stats", JsonOptions);
    }

    /// <summary>
    /// Gets a job by ID.
    /// </summary>
    protected async Task<JobDto?> GetJobAsync(int jobId)
    {
        return await Client.GetFromJsonAsync<JobDto>(
            $"/api/jobs/{jobId}", JsonOptions);
    }

    /// <summary>
    /// Gets the mock printer state for inspection.
    /// </summary>
    protected async Task<MockPrinterState?> GetMockPrinterAsync(int printerId)
    {
        return await Client.GetFromJsonAsync<MockPrinterState>(
            $"/api/mock/printers/{printerId}", JsonOptions);
    }

    /// <summary>
    /// Sets mock print speed. Lower = faster tests.
    /// </summary>
    protected async Task SetPrintSpeedAsync(int printerId, int speedMs)
    {
        await Client.PostAsJsonAsync($"/api/mock/printers/{printerId}/set-speed",
            new { Ms = speedMs });
    }

    /// <summary>
    /// Creates a job, auto-prepares it, and returns the job ID.
    /// </summary>
    protected async Task<int> CreateAndPrepareJobAsync(int productId, int printerId, int quantity)
    {
        var response = await Client.PostAsJsonAsync("/api/jobs", new
        {
            ProductId = productId,
            PrinterId = printerId,
            Quantity = quantity
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JobCreatedResult>(JsonOptions);
        return result!.Id;
    }

    /// <summary>
    /// Starts a Ready job.
    /// </summary>
    protected async Task StartJobAsync(int jobId)
    {
        var response = await Client.PostAsync($"/api/jobs/{jobId}/start", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Pauses a Printing job.
    /// </summary>
    protected async Task PauseJobAsync(int jobId)
    {
        var response = await Client.PostAsync($"/api/jobs/{jobId}/pause", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Resumes a Paused job.
    /// </summary>
    protected async Task ResumeJobAsync(int jobId)
    {
        var response = await Client.PostAsync($"/api/jobs/{jobId}/resume", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Cancels a job.
    /// </summary>
    protected async Task CancelJobAsync(int jobId)
    {
        var response = await Client.PostAsync($"/api/jobs/{jobId}/cancel", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Waits until the job reaches a specific status or times out.
    /// </summary>
    protected async Task<JobDto> WaitForJobStatusAsync(int jobId, string expectedStatus,
        TimeSpan? timeout = null)
    {
        return await PollUntilAsync<JobDto>(
            $"/api/jobs/{jobId}",
            j => j.Status == expectedStatus,
            timeout ?? TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Waits until CodesConfirmed reaches at least the target count.
    /// </summary>
    protected async Task<JobDto> WaitForProgressAsync(int jobId, int minConfirmed,
        TimeSpan? timeout = null)
    {
        return await PollUntilAsync<JobDto>(
            $"/api/jobs/{jobId}",
            j => j.CodesConfirmed >= minConfirmed,
            timeout ?? TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Simulates a TCP connection drop without removing the adapter from
    /// PrinterConnectionManager. The adapter stays registered so TryReconnectAsync
    /// can reconnect it in-place. Use this for network-drop tests (not user disconnect).
    /// </summary>
    protected async Task SimulateNetworkDropAsync(int printerId)
    {
        await Client.PostAsync($"/api/mock/printers/{printerId}/network-drop", null);
    }

    public void Dispose()
    {
        // Handled by DisposeAsync
        GC.SuppressFinalize(this);
    }

    // ─── DTO records for JSON deserialization ───

    private record IdResult(int Id, string? Name);
    private record JobCreatedResult(int Id, string Status, int Quantity);

    protected record JobDto(
        int Id, int ProductId, int PrinterId, int Quantity,
        string Status, int CodesConfirmed, int? TotalBaseline,
        DateTime CreatedAt, DateTime? StartedAt, DateTime? CompletedAt,
        string? ProductName, string? PrinterName);

    protected record CodeStats(
        int Available, int Reserved, int Printed, int Quarantined, int Burned);

    protected record MockPrinterState(
        bool IsConnected, string Status, int CurrentCounter, int LifetimeCounter,
        string? ActiveTemplate, List<string> StoredTemplates, List<string> StoredCsvFiles,
        int PrintSpeedMs);
}
