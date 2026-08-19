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
            _logger.LogDebug("TCP socket created, connecting to {Host}:{Port}...", host, port);
            await _client.ConnectAsync(host, port, ct);
            _stream = _client.GetStream();
            _lastConnectedHost = host;
            _lastConnectedPort = port;
            _logger.LogInformation("Connected to {Host}:{Port} in {ElapsedMs}ms", host, port, sw.ElapsedMilliseconds);
            _logger.LogTrace("<- ConnectAsync = true ({ElapsedMs}ms)", sw.ElapsedMilliseconds);
            return true;
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
        var response = await SendCommandAsync(SpplCommandBuilder.UploadTemplate(name, rox), ct);
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
        var response = await SendCommandAsync(
            $"{SpplConstants.CommandStart}SPLGAT{SpplConstants.CommandEnd}", ct);
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
        var response = await SendCommandAsync(SpplCommandBuilder.UploadCsv(filename, codes), ct);
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

    private async Task<SpplResponse> SendCommandAsync(string cmd, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await _lock.WaitAsync(ct);
        try
        {
            if (_stream == null)
                throw new InvalidOperationException("Not connected to printer");

            var txLog = FormatCommandForLog(cmd);
            _logger.LogDebug("SPPL TX -> {Command} ({ByteCount} bytes)", txLog, Encoding.ASCII.GetByteCount(cmd));
            var bytes = Encoding.ASCII.GetBytes(cmd);
            await _stream.WriteAsync(bytes, ct);
            var response = await ReadResponseAsync(ct);
            sw.Stop();

            var responseStr = response.Payload ?? "";
            _logger.LogDebug("SPPL RX <- {Command}:{Response} ({ElapsedMs}ms)",
                response.Command, responseStr.Length > 500 ? responseStr[..500] + "..." : responseStr, sw.ElapsedMilliseconds);

            if (response.IsFail)
            {
                _logger.LogWarning("SPPL FAIL: {Command} -> FAIL ({ElapsedMs}ms)", txLog, sw.ElapsedMilliseconds);
            }

            return response;
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

        // Template upload: ~SPLRTF{name>base64data}^
        if (cmd.Contains("SPLRTF{"))
        {
            var start = cmd.IndexOf("SPLRTF{") + 7;
            var end = cmd.LastIndexOf('}');
            if (start > 7 && end > start)
            {
                var content = cmd[start..end];
                var sepIdx = content.IndexOf('>');
                if (sepIdx > 0)
                {
                    var name = content[..sepIdx];
                    var dataLen = content.Length - sepIdx - 1;
                    return $"SPLRTF{{{name}}} [{dataLen} base64 chars]";
                }
            }
        }

        // All other commands: show in full (they're short)
        return cmd.Length > 120 ? cmd[..120] + "..." : cmd;
    }

    private async Task<SpplResponse> ReadResponseAsync(CancellationToken ct)
    {
        _logger.LogTrace("-> ReadResponseAsync() waiting for data...");
        var buffer = new byte[65536];
        var sb = new StringBuilder();
        var totalBytesRead = 0;

        while (true)
        {
            var bytesRead = await _stream!.ReadAsync(buffer, ct);
            if (bytesRead == 0)
            {
                _logger.LogError("Connection closed by printer during read (totalBytesRead={TotalBytes}, partial={Partial})",
                    totalBytesRead, sb.ToString().Length > 200 ? sb.ToString()[..200] : sb.ToString());
                throw new IOException("Connection closed by printer");
            }

            totalBytesRead += bytesRead;
            sb.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
            var raw = sb.ToString();

            // Check if we have a complete response (ends with ^)
            if (raw.Contains(SpplConstants.CommandEnd))
            {
                _logger.LogTrace("<- ReadResponseAsync: {TotalBytes} bytes total", totalBytesRead);
                return SpplResponseParser.Parse(raw);
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
