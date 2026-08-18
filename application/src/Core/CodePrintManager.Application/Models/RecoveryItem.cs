using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;

namespace CodePrintManager.Application.Models;

public record RecoveryItem(
    PrintJob Job,
    int ConfirmedByApp,
    int ConfirmedByPrinter,
    int Discrepancy)
{
    /// <summary>True if the printer was offline and could not be inspected.</summary>
    public bool PrinterOffline { get; init; }

    /// <summary>Printer status at inspection time (null if offline).</summary>
    public PrinterStatus? PrinterStatus { get; init; }

    /// <summary>True if SPGGCP was 0 and lifetime delta indicates a power cycle.</summary>
    public bool PowerCycleDetected { get; init; }

    /// <summary>True if the active template matches the expected template.</summary>
    public bool TemplateMatch { get; init; } = true;

    /// <summary>Active template name on the printer (null if offline).</summary>
    public string? ActiveTemplate { get; init; }

    /// <summary>True if the expected CSV file is still stored on the printer.</summary>
    public bool CsvPresent { get; init; } = true;

    /// <summary>True if the printer's serial number does not match the stored value.</summary>
    public bool SerialMismatch { get; init; }

    /// <summary>Short human-readable recommendation for the operator.</summary>
    public string? RecommendedAction { get; init; }
}
