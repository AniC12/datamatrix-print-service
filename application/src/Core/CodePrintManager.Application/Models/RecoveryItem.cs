using CodePrintManager.Domain.Entities;

namespace CodePrintManager.Application.Models;

public record RecoveryItem(
    PrintJob Job,
    int ConfirmedByApp,
    int ConfirmedByPrinter,
    int Discrepancy);
