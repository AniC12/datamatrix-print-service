using CodePrintManager.Domain.Enums;

namespace CodePrintManager.Domain.Interfaces;

public interface IPrinterAdapter : IDisposable
{
    // Connection
    Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default);
    Task DisconnectAsync();
    bool IsConnected { get; }

    // Identity
    Task<string?> GetSerialNumberAsync(CancellationToken ct = default);

    // Status
    Task<PrinterStatus> GetStatusAsync(CancellationToken ct = default);
    Task<int> GetCurrentCounterAsync(CancellationToken ct = default);
    Task<int> GetTotalCounterAsync(CancellationToken ct = default);
    Task<int?> GetRemainingQuantityAsync(CancellationToken ct = default);

    // Template management
    Task<List<string>> ListTemplatesAsync(CancellationToken ct = default);
    Task<bool> UploadTemplateAsync(string name, byte[] rox, CancellationToken ct = default);
    Task<bool> ActivateTemplateAsync(string name, CancellationToken ct = default);
    Task<string?> GetActiveTemplateAsync(CancellationToken ct = default);
    Task<bool> DeleteTemplateAsync(string name, CancellationToken ct = default);

    // CSV / Data management
    Task<List<string>> ListCsvFilesAsync(CancellationToken ct = default);
    Task<bool> UploadCsvAsync(string filename, IReadOnlyList<string> codes, CancellationToken ct = default);
    Task<bool> VerifyCsvExistsAsync(string filename, CancellationToken ct = default);
    Task<bool> DeleteCsvAsync(string filename, CancellationToken ct = default);

    // Print control
    Task<bool> SetPrintQuantityAsync(int quantity, CancellationToken ct = default);
    Task<bool> StartPrintAsync(CancellationToken ct = default);
    Task<bool> StopPrintAsync(CancellationToken ct = default);
}
