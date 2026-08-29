namespace CodePrintManager.Domain.Entities;

public class Printer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 9100;
    public string? Model { get; set; }
    public string AdapterType { get; set; } = "savema_tto";
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// Last known serial number read from the printer (SPGGSN).
    /// Used to detect hardware swaps at the same IP/port.
    /// </summary>
    public string? SerialNumber { get; set; }

    /// <summary>
    /// Number of boundary codes to quarantine when cancelling/aborting a job mid-print.
    /// 0 = no boundary quarantine (operator accepts the risk of the boundary code).
    /// 1 = quarantine the single code at the print boundary (safest default for production).
    /// Higher values provide extra margin for printers with known buffering behavior.
    /// </summary>
    public int QuarantineMargin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<PrintJob> PrintJobs { get; set; } = new List<PrintJob>();
}
