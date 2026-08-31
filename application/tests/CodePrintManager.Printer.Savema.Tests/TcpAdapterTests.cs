using CodePrintManager.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodePrintManager.Printer.Savema.Tests;

/// <summary>
/// TCP-level integration tests for SavemaTtoAdapter against MockSpplTcpServer.
/// These test the real adapter's socket behavior, command framing, response parsing,
/// timeout handling, and fault tolerance — not through mocks.
/// </summary>
public class TcpAdapterTests : IDisposable
{
    private readonly MockSpplTcpServer _server;
    private readonly SavemaTtoAdapter _adapter;

    public TcpAdapterTests()
    {
        _server = new MockSpplTcpServer();
        _adapter = new SavemaTtoAdapter(NullLogger<SavemaTtoAdapter>.Instance);
    }

    public void Dispose()
    {
        _adapter.Dispose();
        _server.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ConnectAsync()
    {
        var connected = await _adapter.ConnectAsync("127.0.0.1", _server.Port);
        Assert.True(connected, "Failed to connect to mock SPPL server");
    }

    /// <summary>
    /// K1. Normal command/response cycle — verifies basic SPPL round-trip through real TCP.
    /// </summary>
    [Fact]
    public async Task K1_NormalCommandResponse_ReturnsCorrectValues()
    {
        _server.SetStatus("WAITING");
        _server.SetCounters(42, 1000);

        await ConnectAsync();

        var status = await _adapter.GetStatusAsync();
        Assert.Equal(PrinterStatus.Idle, status);

        var current = await _adapter.GetCurrentCounterAsync();
        Assert.Equal(42, current);

        var total = await _adapter.GetTotalCounterAsync();
        Assert.Equal(1000, total);

        var serial = await _adapter.GetSerialNumberAsync();
        Assert.Equal("MOCK-TCP-001", serial);
    }

    /// <summary>
    /// K2. Connection drop after sending command → IOException.
    /// Server accepts the command but drops the connection before responding.
    /// </summary>
    [Fact]
    public async Task K2_ConnectionDropAfterCommand_ThrowsIOException()
    {
        await ConnectAsync();

        // First call works
        var status = await _adapter.GetStatusAsync();
        Assert.Equal(PrinterStatus.Idle, status);

        // Next call: server will drop after receiving command
        _server.EnqueueDropAfterReceive();

        await Assert.ThrowsAsync<IOException>(async () =>
            await _adapter.GetCurrentCounterAsync());
    }

    /// <summary>
    /// K3. Partial/malformed response → IOException or FormatException.
    /// Server sends truncated SPPL frame then closes connection.
    /// </summary>
    [Fact]
    public async Task K3_PartialResponse_ThrowsException()
    {
        await ConnectAsync();

        // Server will send partial response then close
        _server.EnqueuePartialResponse("SPPSTA");

        // The adapter should throw — either IOException (connection closed mid-read)
        // or FormatException (malformed frame)
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _adapter.GetStatusAsync());
    }

    /// <summary>
    /// K4. Response timeout (printer hangs) — adapter should throw within ~11s.
    /// The ReadResponseAsync uses a 10s timeout by default.
    /// </summary>
    [Fact]
    public async Task K4_ResponseTimeout_ThrowsIOException()
    {
        await ConnectAsync();

        // Server will accept command but never respond
        _server.EnqueueHang();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<IOException>(async () =>
            await _adapter.GetStatusAsync());
        sw.Stop();

        // Should timeout within ~11s (10s read timeout + overhead)
        Assert.True(sw.Elapsed.TotalSeconds < 15,
            $"Expected timeout within ~11s, took {sw.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>
    /// K5. Unsolicited frame before response → correct response extracted.
    /// Server sends an unexpected SPPSTP:OK frame before the actual SPPSTA response.
    /// The adapter's receive buffer and command validation should handle this.
    /// </summary>
    [Fact]
    public async Task K5_UnsolicitedFrameBeforeResponse_CorrectResponseExtracted()
    {
        await ConnectAsync();

        // Server will send unsolicited SPPSTP:OK before the real SPPSTA response
        _server.EnqueueUnsolicitedFrame("SPPSTP", "OK");

        var status = await _adapter.GetStatusAsync();
        // The adapter should discard the unsolicited SPPSTP frame and return
        // the correct SPPSTA response
        Assert.Equal(PrinterStatus.Idle, status);
    }

    /// <summary>
    /// K6. Reconnect after disconnect works.
    /// </summary>
    [Fact]
    public async Task K6_ReconnectAfterDisconnect_Works()
    {
        await ConnectAsync();

        // Verify connection works
        var status1 = await _adapter.GetStatusAsync();
        Assert.Equal(PrinterStatus.Idle, status1);
        Assert.True(_adapter.IsConnected);

        // Disconnect
        await _adapter.DisconnectAsync();
        Assert.False(_adapter.IsConnected);

        // Reconnect
        await ConnectAsync();
        Assert.True(_adapter.IsConnected);

        // Verify it works again
        var status2 = await _adapter.GetStatusAsync();
        Assert.Equal(PrinterStatus.Idle, status2);
    }

    /// <summary>
    /// K7. Multiple rapid commands — adapter serializes via SemaphoreSlim.
    /// Fire multiple concurrent calls; all should return correct results.
    /// </summary>
    [Fact]
    public async Task K7_MultipleRapidCommands_AllReturnCorrectResults()
    {
        _server.SetCounters(10, 100);
        await ConnectAsync();

        // Fire 5 concurrent GetStatusAsync calls
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => _adapter.GetStatusAsync())
            .ToList();

        var results = await Task.WhenAll(tasks);

        // All should return Idle (WAITING)
        Assert.All(results, r => Assert.Equal(PrinterStatus.Idle, r));
    }

    /// <summary>
    /// K8. TCP RST during command → IOException.
    /// </summary>
    [Fact]
    public async Task K8_TcpReset_ThrowsIOException()
    {
        await ConnectAsync();

        // Verify connection works
        var status = await _adapter.GetStatusAsync();
        Assert.Equal(PrinterStatus.Idle, status);

        // Next call: server will RST the connection
        _server.EnqueueReset();

        // The adapter should throw an IOException or SocketException
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _adapter.GetCurrentCounterAsync());
    }
}
