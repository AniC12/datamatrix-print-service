namespace CodePrintManager.Printer.Savema.Protocol;

public static class SpplConstants
{
    public const char CommandStart = '~';
    public const char CommandEnd = '^';
    public const char CommandSeparator = '|';
    public const string ParameterSeparator = "~gt~";
    public const string ColumnSeparator = "~sc~";
    public const string ResponseListSeparator = "<";

    public const string ResponsePrefix = "SPGRES";
    public const string ResponseOk = "OK";
    public const string ResponseFail = "FAIL";

    // Forbidden sequences in code values (no escape mechanism in SPPL)
    public static readonly string[] ForbiddenSequences = { "^", "~gt~", "~sc~", "~" };

    public const int DefaultPort = 9100;
    public const int DefaultReceiveTimeoutMs = 5000;
    public const int DefaultSendTimeoutMs = 5000;
}
