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

    public bool IsConnected => _client?.Connected == true;

    public SavemaTtoAdapter(ILogger<SavemaTtoAdapter> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            DisposeSocket();
            _client = new TcpClient
            {
                ReceiveTimeout = SpplConstants.DefaultReceiveTimeoutMs,
                SendTimeout = SpplConstants.DefaultSendTimeoutMs
            };
            await _client.ConnectAsync(host, port, ct);
            _stream = _client.GetStream();
            _logger.LogInformation("Connected to {Host}:{Port}", host, port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to {Host}:{Port}", host, port);
            DisposeSocket();
            return false;
        }
        finally { _lock.Release(); }
    }

    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync();
        try { DisposeSocket(); }
        finally { _lock.Release(); }
    }

    public async Task<PrinterStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync(SpplCommandBuilder.GetStatus(), ct);
        var (state, _) = SpplResponseParser.ParseStatus(response.Payload);
        return state switch
        {
            "WAITING" => PrinterStatus.Idle,
            "RUNNING" => PrinterStatus.Printing,
            "ERROR" => PrinterStatus.Error,
            "INIT" => PrinterStatus.Init,
            "BLOCKED" => PrinterStatus.Blocked,
            _ => PrinterStatus.Offline
        };
    }

    public async Task<int> GetCurrentCounterAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync(SpplCommandBuilder.GetCurrentCounter(), ct);
        return response.AsInt();
    }

    public async Task<int> GetTotalCounterAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync(SpplCommandBuilder.GetTotalCounter(), ct);
        return response.AsInt();
    }

    public async Task<int?> GetRemainingQuantityAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await SendCommandAsync(SpplCommandBuilder.GetRemainingQuantity(), ct);
            return response.AsInt();
        }
        catch { return null; }
    }

    public async Task<List<string>> ListTemplatesAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync(SpplCommandBuilder.ListTemplates(), ct);
        return response.AsList();
    }

    public async Task<bool> UploadTemplateAsync(string name, byte[] rox, CancellationToken ct = default)
    {
        var response = await SendCommandAsync(SpplCommandBuilder.UploadTemplate(name, rox), ct);
        return response.IsOk;
    }

    public async Task<bool> ActivateTemplateAsync(string name, CancellationToken ct = default)
    {
        var response = await SendCommandAsync(SpplCommandBuilder.ActivateTemplate(name), ct);
        return response.IsOk;
    }

    public async Task<string?> GetActiveTemplateAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync(
            $"{SpplConstants.CommandStart}SPLGAT{SpplConstants.CommandEnd}", ct);
        return response.IsFail ? null : response.Payload;
    }

    public async Task<bool> DeleteTemplateAsync(string name, CancellationToken ct = default)
    {
        var response = await SendCommandAsync(SpplCommandBuilder.DeleteTemplate(name), ct);
        return response.IsOk;
    }

    public async Task<List<string>> ListCsvFilesAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync(SpplCommandBuilder.ListCsvFiles(), ct);
        return response.AsList();
    }

    public async Task<bool> UploadCsvAsync(string filename, IReadOnlyList<string> codes, CancellationToken ct = default)
    {
        var response = await SendCommandAsync(SpplCommandBuilder.UploadCsv(filename, codes), ct);
        return response.IsOk;
    }

    public async Task<bool> VerifyCsvExistsAsync(string filename, CancellationToken ct = default)
    {
        var files = await ListCsvFilesAsync(ct);
        return files.Contains(filename);
    }

    public async Task<bool> DeleteCsvAsync(string filename, CancellationToken ct = default)
    {
        var response = await SendCommandAsync(SpplCommandBuilder.DeleteCsv(filename), ct);
        return response.IsOk;
    }

    public async Task<bool> SetPrintQuantityAsync(int quantity, CancellationToken ct = default)
    {
        var response = await SendCommandAsync(SpplCommandBuilder.SetPrintQuantity(quantity), ct);
        return response.IsOk;
    }

    public async Task<bool> StartPrintAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync(SpplCommandBuilder.StartPrint(), ct);
        return response.IsOk;
    }

    public async Task<bool> StopPrintAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync(SpplCommandBuilder.StopPrint(), ct);
        return response.IsOk;
    }

    private async Task<SpplResponse> SendCommandAsync(string cmd, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_stream == null)
                throw new InvalidOperationException("Not connected to printer");

            var bytes = Encoding.ASCII.GetBytes(cmd);
            await _stream.WriteAsync(bytes, ct);
            return await ReadResponseAsync(ct);
        }
        finally { _lock.Release(); }
    }

    private async Task<SpplResponse> ReadResponseAsync(CancellationToken ct)
    {
        var buffer = new byte[65536];
        var sb = new StringBuilder();

        while (true)
        {
            var bytesRead = await _stream!.ReadAsync(buffer, ct);
            if (bytesRead == 0)
                throw new IOException("Connection closed by printer");

            sb.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
            var raw = sb.ToString();

            // Check if we have a complete response (ends with ^)
            if (raw.Contains(SpplConstants.CommandEnd))
                return SpplResponseParser.Parse(raw);
        }
    }

    private void DisposeSocket()
    {
        _stream?.Dispose(); _stream = null;
        _client?.Dispose(); _client = null;
    }

    public void Dispose()
    {
        DisposeSocket();
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
