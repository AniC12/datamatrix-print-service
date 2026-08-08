namespace CodePrintManager.Printer.Savema.Protocol;

public record SpplResponse(string Command, string Payload)
{
    public bool IsOk => Payload == SpplConstants.ResponseOk;
    public bool IsFail => Payload == SpplConstants.ResponseFail;

    public int AsInt() => int.Parse(Payload);
    public List<string> AsList() => Payload.Split(SpplConstants.ResponseListSeparator).ToList();
}
