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

    /// <summary>
    /// Creates a mock printer and connects it. Returns the printer ID.
    /// </summary>
    protected async Task<int> SetupPrinterAsync(string name = "TestPrinter")
    {
        var response = await Client.PostAsJsonAsync("/api/printers", new
        {
            Name = name,
            Ip = "mock",
            Port = 9100,
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

    public void Dispose()
    {
        // Handled by DisposeAsync
        GC.SuppressFinalize(this);
    }

    private record IdResult(int Id, string? Name);
}
