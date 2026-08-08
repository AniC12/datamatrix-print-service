namespace CodePrintManager.Domain.Interfaces;

public interface IPrinterAdapterFactory
{
    bool CanHandle(string adapterType);
    IPrinterAdapter Create(string adapterType);
}
