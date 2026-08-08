namespace CodePrintManager.Domain.Interfaces;

public interface ICurrentUser
{
    string Username { get; }
    bool HasPermission(string permission);
}
