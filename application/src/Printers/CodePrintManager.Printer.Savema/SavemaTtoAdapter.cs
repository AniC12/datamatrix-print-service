using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using CodePrintManager.Printer.Savema.Protocol;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Printer.Savema;

public class SavemaTtoAdapter : IPrinterAdapter
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<SavemaTtoAdapter> _logger;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private string? _lastConnectedHost;
    private int _lastConnectedPort;

    /// <summary>
    /// Persistent receive buffer. Preserves remainder data when multiple SPPL
    /// frames arrive in a single TCP read (e.g., unsolicited SPPSTP:OK followed
    /// by the actual SPGGCP:3 response). Without this buffer, the remainder would
    /// be discarded and ReadResponseAsync would block indefinitely on the next call.
    /// Cleared in DisposeSocket() to prevent stale data after reconnection.
    /// </summary>
    private readonly StringBuilder _receiveBuffer = new();

    public bool IsConnected => _client?.Connected == true;

    public SavemaTtoAdapter(ILogger<SavemaTtoAdapter> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        _logger.LogTrace("-> ConnectAsync(host={Host}, port={Port})", host, port);
        var sw = Stopwatch.StartNew();
        await _lock.WaitAsync(ct);
        try
        {
            DisposeSocket();
            _client = new TcpClient
            {
                ReceiveTimeout = SpplConstants.DefaultReceiveTimeoutMs,
                SendTimeout = SpplConstants.DefaultSendTimeoutMs
            };
            // Enable TCP keepalive to detect half-open connections faster
            _client.Client.SetSocketOption(
                SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            _logger.LogDebug("TCP socket created (keepalive=on), connecting to {Host}:{Port}...", host, port);
            await _client.ConnectAsync(host, port, ct);
            _stream = _client.GetStream();
            _lastConnectedHost = host;
            _lastConnectedPort = port;
            _logger.LogInformation("Connected to {Host}:{Port} in {ElapsedMs}ms", host, port, sw.ElapsedMilliseconds);
            _logger.LogTrace("<- ConnectAsync = true ({ElapsedMs}ms)", sw.ElapsedMilliseconds);
            return true;
        }
        catch (OperationCanceledException)
        {
            DisposeSocket();
            throw; // Let caller distinguish cancellation from connection failure
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to {Host}:{Port} after {ElapsedMs}ms", host, port, sw.ElapsedMilliseconds);
            DisposeSocket();
            _logger.LogTrace("<- ConnectAsync = false ({ElapsedMs}ms)", sw.ElapsedMilliseconds);
            return false;
        }
        finally { _lock.Release(); }
    }

    public async Task DisconnectAsync()
    {
        _logger.LogTrace("-> DisconnectAsync()");
        _logger.LogInformation("Disconnecting from {Host}:{Port}", _lastConnectedHost, _lastConnectedPort);
        await _lock.WaitAsync();
        try { DisposeSocket(); }
        finally { _lock.Release(); }
        _logger.LogTrace("<- DisconnectAsync completed");
    }

    public async Task<string?> GetSerialNumberAsync(CancellationToken ct = default)
    {
        _logger.LogTrace("-> GetSerialNumberAsync()");
        var response = await SendCommandAsync(SpplCommandBuilder.GetSerialNumber(), ct);
        var result = response.IsFail ? null : response.Payload;
        _logger.LogTrace("<- GetSerialNumberAsync = {Result}", result ?? "(null)");
        return result;
    }

    public async Task<PrinterStatus> GetStatusAsync(CancellationToken ct = default)
    {
        _logger.LogTrace("-> GetStatusAsync()");
        var response = await SendCommandAsync(SpplCommandBuilder.GetStatus(), ct);
        var (state, info) = SpplResponseParser.ParseStatus(response.Payload);
        var result = state switch
        {
            "WAITING" => PrinterStatus.Idle,
            "RUNNING" => PrinterStatus.Printing,
            "ERROR" => PrinterStatus.Error,
            "INIT" => PrinterStatus.Init,
            "BLOCKED" => PrinterStatus.Blocked,
            _ => PrinterStatus.Offline
        };
        _logger.LogTrace("<- GetStatusAsync = {Status} (raw={RawState}, info={Info})", result, state, info ?? "(none)");
        return result;
    }

    public async Task<int> GetCurrentCounterAsync(CancellationToken ct = default)
    {
        _logger.LogTrace("-> GetCurrentCounterAsync()");
        var response = await SendCommandAsync(SpplCommandBuilder.GetCurrentCounter(), ct);
        var result = response.AsInt();
        _logger.LogTrace("<- GetCurrentCounterAsync = {Counter}", result);
        return result;
    }

    public async Task<int> GetTotalCounterAsync(CancellationToken ct = default)
    {
        _logger.LogTrace("-> GetTotalCounterAsync()");
        var response = await SendCommandAsync(SpplCommandBuilder.GetTotalCounter(), ct);
        var result = response.AsInt();
        _logger.LogTrace("<- GetTotalCounterAsync = {Counter}", result);
        return result;
    }

    public async Task<int?> GetRemainingQuantityAsync(CancellationToken ct = default)
    {
        _logger.LogTrace("-> GetRemainingQuantityAsync()");
        try
        {
            var response = await SendCommandAsync(SpplCommandBuilder.GetRemainingQuantity(), ct);
            var result = response.AsInt();
            _logger.LogTrace("<- GetRemainingQuantityAsync = {Remaining}", result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogTrace("<- GetRemainingQuantityAsync = null (error: {Error})", ex.Message);
            return null;
        }
    }

    public async Task<List<string>> ListTemplatesAsync(CancellationToken ct = default)
    {
        _logger.LogTrace("-> ListTemplatesAsync()");
        var response = await SendCommandAsync(SpplCommandBuilder.ListTemplates(), ct);
        var result = response.AsList();
        _logger.LogTrace("<- ListTemplatesAsync = [{Count} templates: {Templates}]", result.Count, string.Join(", ", result));
        return result;
    }

    public async Task<bool> UploadTemplateAsync(string name, byte[] rox, CancellationToken ct = default)
    {
        _logger.LogTrace("-> UploadTemplateAsync(name={Name}, size={Size} bytes)", name, rox.Length);
        var sw = Stopwatch.StartNew();
        var response = await SendCommandAsync(SpplCommandBuilder.UploadTemplate(name, rox), ct,
            readTimeout: TimeSpan.FromSeconds(60));
        var result = response.IsOk;
        _logger.LogInformation("UploadTemplate '{Name}' ({Size} bytes): {Result} in {ElapsedMs}ms",
            name, rox.Length, result ? "OK" : "FAIL", sw.ElapsedMilliseconds);
        _logger.LogTrace("<- UploadTemplateAsync = {Result} ({ElapsedMs}ms)", result, sw.ElapsedMilliseconds);
        return result;
    }

    public async Task<bool> ActivateTemplateAsync(string name, CancellationToken ct = default)
    {
        _logger.LogTrace("-> ActivateTemplateAsync(name={Name})", name);
        var sw = Stopwatch.StartNew();
        var response = await SendCommandAsync(SpplCommandBuilder.ActivateTemplate(name), ct);
        var result = response.IsOk;
        _logger.LogInformation("ActivateTemplate '{Name}': {Result} in {ElapsedMs}ms",
            name, result ? "OK" : "FAIL", sw.ElapsedMilliseconds);
        _logger.LogTrace("<- ActivateTemplateAsync = {Result} ({ElapsedMs}ms)", result, sw.ElapsedMilliseconds);
        return result;
    }

    public async Task<string?> GetActiveTemplateAsync(CancellationToken ct = default)
    {
        _logger.LogTrace("-> GetActiveTemplateAsync()");
        var response = await SendCommandAsync(SpplCommandBuilder.GetActiveTemplate(), ct);
        var result = response.IsFail ? null : response.Payload;
        _logger.LogTrace("<- GetActiveTemplateAsync = {Template}", result ?? "(null)");
        return result;
    }

    public async Task<bool> DeleteTemplateAsync(string name, CancellationToken ct = default)
    {
        _logger.LogTrace("-> DeleteTemplateAsync(name={Name})", name);
        var response = await SendCommandAsync(SpplCommandBuilder.DeleteTemplate(name), ct);
        var result = response.IsOk;
        _logger.LogTrace("<- DeleteTemplateAsync = {Result}", result);
        return result;
    }

    public async Task<List<string>> ListCsvFilesAsync(CancellationToken ct = default)
    {
        _logger.LogTrace("-> ListCsvFilesAsync()");
        var response = await SendCommandAsync(SpplCommandBuilder.ListCsvFiles(), ct);
        var result = response.AsList();
        _logger.LogTrace("<- ListCsvFilesAsync = [{Count} files: {Files}]", result.Count, string.Join(", ", result));
        return result;
    }

    public async Task<bool> UploadCsvAsync(string filename, IReadOnlyList<string> codes, CancellationToken ct = default)
    {
        _logger.LogTrace("-> UploadCsvAsync(filename={Filename}, codeCount={Count})", filename, codes.Count);
        var sw = Stopwatch.StartNew();
        var response = await SendCommandAsync(SpplCommandBuilder.UploadCsv(filename, codes), ct,
            readTimeout: TimeSpan.FromSeconds(60));
        var result = response.IsOk;
        _logger.LogInformation("UploadCsv '{Filename}' ({Count} codes): {Result} in {ElapsedMs}ms",
            filename, codes.Count, result ? "OK" : "FAIL", sw.ElapsedMilliseconds);
        _logger.LogTrace("<- UploadCsvAsync = {Result} ({ElapsedMs}ms)", result, sw.ElapsedMilliseconds);
        return result;
    }

    public async Task<bool> VerifyCsvExistsAsync(string filename, CancellationToken ct = default)
    {
        _logger.LogTrace("-> VerifyCsvExistsAsync(filename={Filename})", filename);
        var files = await ListCsvFilesAsync(ct);
        var result = files.Contains(filename);
        _logger.LogTrace("<- VerifyCsvExistsAsync = {Result} (files on printer: {Files})", result, string.Join(", ", files));
        return result;
    }

    public async Task<bool> DeleteCsvAsync(string filename, CancellationToken ct = default)
    {
        _logger.LogTrace("-> DeleteCsvAsync(filename={Filename})", filename);
        var response = await SendCommandAsync(SpplCommandBuilder.DeleteCsv(filename), ct);
        var result = response.IsOk;
        _logger.LogTrace("<- DeleteCsvAsync = {Result}", result);
        return result;
    }

    public async Task<bool> SetPrintQuantityAsync(int quantity, CancellationToken ct = default)
    {
        _logger.LogTrace("-> SetPrintQuantityAsync(quantity={Quantity})", quantity);
        var response = await SendCommandAsync(SpplCommandBuilder.SetPrintQuantity(quantity), ct);
        var result = response.IsOk;
        _logger.LogInformation("SetPrintQuantity({Quantity}): {Result}", quantity, result ? "OK" : "FAIL");
        _logger.LogTrace("<- SetPrintQuantityAsync = {Result}", result);
        return result;
    }

    public async Task<bool> StartPrintAsync(CancellationToken ct = default)
    {
        _logger.LogTrace("-> StartPrintAsync()");
        var response = await SendCommandAsync(SpplCommandBuilder.StartPrint(), ct);
        var result = response.IsOk;
        _logger.LogInformation("StartPrint: {Result}", result ? "OK" : "FAIL");
        _logger.LogTrace("<- StartPrintAsync = {Result}", result);
        return result;
    }

    public async Task<bool> StopPrintAsync(CancellationToken ct = default)
    {
        _logger.LogTrace("-> StopPrintAsync()");
        var response = await SendCommandAsync(SpplCommandBuilder.StopPrint(), ct);
        var result = response.IsOk;
        _logger.LogInformation("StopPrint: {Result}", result ? "OK" : "FAIL");
        _logger.LogTrace("<- StopPrintAsync = {Result}", result);
        return result;
    }

    private async Task<SpplResponse> SendCommandAsync(string cmd, CancellationToken ct,
        TimeSpan? readTimeout = null)
    {
        var sw = Stopwatch.StartNew();
        await _lock.WaitAsync(ct);
        try
        {
            if (_stream == null)
                throw new InvalidOperationException("Not connected to printer");

            // Extract expected command from sent string (e.g., "~SPGGCP^" → "SPGGCP")
            var expectedCommand = ExtractCommandName(cmd);
            var txLog = FormatCommandForLog(cmd);

            // Best-effort flush of unsolicited data before sending (supplementary to validation).
            // DataAvailable checks if data is buffered RIGHT NOW. An unsolicited frame can
            // arrive after this check but before ReadResponseAsync — this just reduces the window.
            if (_stream.DataAvailable)
            {
                var staleBuffer = new byte[4096];
                var flushed = new StringBuilder();
                while (_stream.DataAvailable)
                {
                    var n = await _stream.ReadAsync(staleBuffer, ct);
                    if (n > 0) flushed.Append(Encoding.ASCII.GetString(staleBuffer, 0, n));
                }
                if (flushed.Length > 0)
                    _logger.LogWarning("SPPL: flushed {Len} bytes of stale data before {Cmd}: {Data}",
                        flushed.Length, expectedCommand,
                        flushed.Length > 200 ? flushed.ToString()[..200] : flushed.ToString());
            }

            _logger.LogDebug("SPPL TX -> {Command} ({ByteCount} bytes)", txLog, Encoding.ASCII.GetByteCount(cmd));
            var bytes = Encoding.ASCII.GetBytes(cmd);
            await _stream.WriteAsync(bytes, ct);

            // Read with response command validation: retry if response doesn't match.
            // This is the PRIMARY defense against unsolicited SPPL frames (e.g., SPPSTP:OK
            // arriving when we sent SPGGCP). Single-frame unsolicited messages are the
            // dangerous case — they shift all subsequent reads by one frame.
            const int maxRetries = 3;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                var response = await ReadResponseAsync(ct, readTimeout);

                if (response.Command == expectedCommand)
                {
                    sw.Stop();
                    var responseStr = response.Payload ?? "";
                    _logger.LogDebug("SPPL RX <- {Command}:{Response} ({ElapsedMs}ms)",
                        response.Command, responseStr.Length > 500 ? responseStr[..500] + "..." : responseStr,
                        sw.ElapsedMilliseconds);

                    if (response.IsFail)
                        _logger.LogWarning("SPPL FAIL: {Command} -> FAIL ({ElapsedMs}ms)", txLog, sw.ElapsedMilliseconds);

                    return response;
                }

                // Mismatched response — log and discard
                _logger.LogWarning(
                    "SPPL: expected response for {Expected}, got {Actual}:{Payload} (attempt {Attempt}/{Max}). " +
                    "Discarding unsolicited frame.",
                    expectedCommand, response.Command, response.Payload, attempt + 1, maxRetries);
            }

            // All retries exhausted — stream is permanently misaligned.
            // Force disconnect+reconnect to get a fresh TCP stream.
            // Without this, the executor would re-read from the same corrupted stream
            // on every subsequent poll (there is no auto-reconnect for mid-session disconnects).
            _logger.LogError(
                "SPPL stream desynchronized after {Max} retries for {Cmd}. Forcing reconnect.",
                maxRetries, expectedCommand);
            DisposeSocket();
            try
            {
                _client = new TcpClient
                {
                    ReceiveTimeout = SpplConstants.DefaultReceiveTimeoutMs,
                    SendTimeout = SpplConstants.DefaultSendTimeoutMs
                };
                await _client.ConnectAsync(_lastConnectedHost!, _lastConnectedPort, ct);
                _stream = _client.GetStream();
                _logger.LogInformation("SPPL: reconnected to {Host}:{Port} after stream corruption",
                    _lastConnectedHost, _lastConnectedPort);
            }
            catch (Exception reconnectEx)
            {
                _logger.LogError(reconnectEx,
                    "SPPL: reconnect failed after stream corruption. Adapter is now disconnected.");
                DisposeSocket();
            }
            throw new IOException(
                $"SPPL stream desynchronized: sent {expectedCommand}, " +
                $"received {maxRetries} consecutive mismatched responses. " +
                "Adapter reconnected — retry needed.");
        }
        catch (IOException ex)
        {
            sw.Stop();
            _logger.LogError(ex, "SPPL IO ERROR during command (after {ElapsedMs}ms, connected={IsConnected})",
                sw.ElapsedMilliseconds, _client?.Connected);
            throw;
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Extracts the SPPL command name from a raw command string.
    /// E.g., "~SPGGCP^" → "SPGGCP", "~SPLCDF{data}^" → "SPLCDF"
    /// </summary>
    private static string ExtractCommandName(string cmd)
    {
        var start = cmd.IndexOf(SpplConstants.CommandStart) + 1;
        var end = cmd.IndexOfAny(['{', SpplConstants.CommandEnd], start);
        return end > start ? cmd[start..end] : cmd[start..];
    }

    /// <summary>
    /// Formats a command for logging with smart truncation:
    /// - CSV uploads: show filename + first 5 codes + total count
    /// - Template uploads: show name + size
    /// - Everything else: full command
    /// </summary>
    private static string FormatCommandForLog(string cmd)
    {
        // CSV upload: ~SPLCDF{filename~gt~code1\ncode2\n...}^
        if (cmd.Contains("SPLCDF{"))
        {
            var start = cmd.IndexOf("SPLCDF{") + 7;
            var end = cmd.LastIndexOf('}');
            if (start > 7 && end > start)
            {
                var content = cmd[start..end];
                var sepIdx = content.IndexOf(SpplConstants.ParameterSeparator);
                if (sepIdx > 0)
                {
                    var filename = content[..sepIdx];
                    var data = content[(sepIdx + SpplConstants.ParameterSeparator.Length)..];
                    var codes = data.Split('\n');
                    var preview = string.Join(", ", codes.Take(5));
                    return $"SPLCDF{{{filename}}} [{codes.Length} codes, first 5: {preview}]";
                }
            }
        }

        // Template upload: ~SPLTDS{<Template>...</Template>}^
        if (cmd.Contains("SPLTDS{"))
        {
            // Extract template name from the XML <Name> element
            var nameMatch = System.Text.RegularExpressions.Regex.Match(cmd, @"<Name>([^<]+)</Name>");
            var tplName = nameMatch.Success ? nameMatch.Groups[1].Value : "?";
            var xmlLen = cmd.Length - "~SPLTDS{}^".Length;
            return $"SPLTDS{{{tplName}}} [{xmlLen} xml chars]";
        }

        // All other commands: show in full (they're short)
        return cmd.Length > 120 ? cmd[..120] + "..." : cmd;
    }

    private async Task<SpplResponse> ReadResponseAsync(CancellationToken ct,
        TimeSpan? readTimeout = null)
    {
        _logger.LogTrace("-> ReadResponseAsync() waiting for data...");
        var buffer = new byte[65536];
        var totalBytesRead = 0;

        // Check if persistent buffer already has a complete frame from a previous read
        // (e.g., two frames arrived together last time — we returned the first,
        // and the second is still in _receiveBuffer)
        var buffered = _receiveBuffer.ToString();
        if (buffered.Contains(SpplConstants.CommandEnd))
        {
            var frameEnd = buffered.IndexOf(SpplConstants.CommandEnd) + 1;
            var frame = buffered[..frameEnd];
            _receiveBuffer.Remove(0, frameEnd);
            if (_receiveBuffer.Length > 0)
                _logger.LogDebug("SPPL: returning buffered frame, {RemLen} bytes remain in buffer",
                    _receiveBuffer.Length);
            return SpplResponseParser.Parse(frame);
        }

        // Read from network until we have at least one complete frame
        while (true)
        {
            // Timeout per individual read to prevent indefinite blocking.
            // TcpClient.ReceiveTimeout does NOT apply to async ReadAsync.
            var timeout = readTimeout ?? TimeSpan.FromSeconds(10);
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(timeout);

            int bytesRead;
            try
            {
                bytesRead = await _stream!.ReadAsync(buffer, readCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Our read timeout fired, not the caller's token
                throw new IOException($"Read timeout: no data received from printer within {timeout.TotalSeconds:F0} seconds");
            }

            if (bytesRead == 0)
            {
                _logger.LogError("Connection closed by printer during read (totalBytesRead={TotalBytes}, partial={Partial})",
                    totalBytesRead, _receiveBuffer.ToString().Length > 200
                        ? _receiveBuffer.ToString()[..200] : _receiveBuffer.ToString());
                throw new IOException("Connection closed by printer");
            }

            totalBytesRead += bytesRead;
            _receiveBuffer.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
            var raw = _receiveBuffer.ToString();

            // Check if we have a complete frame (contains ^)
            if (raw.Contains(SpplConstants.CommandEnd))
            {
                // Extract the first complete frame
                var frameEnd = raw.IndexOf(SpplConstants.CommandEnd) + 1;
                var frame = raw[..frameEnd];
                _receiveBuffer.Remove(0, frameEnd); // preserve remainder for next call

                if (_receiveBuffer.Length > 0)
                    _logger.LogDebug("SPPL: extracted frame ({FrameLen} bytes), {RemLen} bytes remain in buffer",
                        frame.Length, _receiveBuffer.Length);

                _logger.LogTrace("<- ReadResponseAsync: {TotalBytes} bytes from network", totalBytesRead);
                return SpplResponseParser.Parse(frame);
            }

            _logger.LogTrace("   ReadResponseAsync: partial read {BytesRead} bytes (total={TotalBytes}), waiting for more...",
                bytesRead, totalBytesRead);
        }
    }

    private void DisposeSocket()
    {
        _logger.LogTrace("-> DisposeSocket()");
        _stream?.Dispose(); _stream = null;
        _client?.Dispose(); _client = null;
        _receiveBuffer.Clear(); // discard stale buffered data from previous connection
        _logger.LogTrace("<- DisposeSocket completed");
    }

    public void Dispose()
    {
        _logger.LogTrace("-> Dispose()");
        DisposeSocket();
        _lock.Dispose();
        GC.SuppressFinalize(this);
        _logger.LogTrace("<- Dispose completed");
    }
}
