namespace CodePrintManager.Printer.Savema.Protocol;

public static class SpplCommandBuilder
{
    public static string GetStatus() => Wrap("SPPSTA");
    public static string GetCurrentCounter() => Wrap("SPGGCP");
    public static string GetTotalCounter() => Wrap("SPGGTP");
    public static string GetRemainingQuantity() => Wrap("SPPGLQ");

    public static string ListTemplates() => Wrap("SPLGST");
    public static string GetActiveTemplate() => Wrap("SPLGAT");
    public static string ActivateTemplate(string name) => Wrap($"SPLLTF{{{name}}}");
    public static string DeleteTemplate(string name) => Wrap($"SPLDTF{{{name}}}");

    public static string UploadTemplate(string name, byte[] data)
    {
        var base64 = Convert.ToBase64String(data);
        return Wrap($"SPLRTF{{{name}>{base64}}}");
    }

    public static string ListCsvFiles() => Wrap("SPLGSD");
    public static string DeleteCsv(string filename) => Wrap($"SPLDDF{{{filename}}}");

    public static string UploadCsv(string filename, IReadOnlyList<string> codes)
    {
        var data = string.Join("\n", codes);
        return Wrap($"SPLCDF{{{filename}{SpplConstants.ParameterSeparator}{data}}}");
    }

    public static string SetPrintQuantity(int quantity) => Wrap($"SPPSLQ{{{quantity}}}");
    public static string StartPrint() => Wrap("SPPSAP");
    public static string StopPrint() => Wrap("SPPSTP");

    private static string Wrap(string command)
        => $"{SpplConstants.CommandStart}{command}{SpplConstants.CommandEnd}";
}
