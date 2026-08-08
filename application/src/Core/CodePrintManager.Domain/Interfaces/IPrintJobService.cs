using CodePrintManager.Domain.Entities;

namespace CodePrintManager.Domain.Interfaces;

public interface IPrintJobService
{
    Task<PrintJob> CreateJobAsync(int productId, int printerId, int quantity);
    Task PrepareJobAsync(int jobId, CancellationToken ct = default);
    Task StartJobAsync(int jobId, CancellationToken ct = default);
    Task CancelJobAsync(int jobId);
    Task<List<PrintJob>> GetActiveJobsAsync();
    Task<List<PrintJob>> GetJobHistoryAsync(int? printerId = null, int? productId = null);
    Task<List<PrintJob>> GetStaleJobsAsync();
    Task ResumeJobAsync(int jobId, CancellationToken ct = default);
}
