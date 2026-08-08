namespace CodePrintManager.Printer.Savema.Protocol;

public static class SpplResponseParser
{
    /// <summary>
    /// Parses a raw SPPL response string into a structured SpplResponse.
    /// Format: ~SPGRES{COMMAND:PAYLOAD}^ (with possible whitespace after ~)
    /// </summary>
    public static SpplResponse Parse(string raw)
    {
        // Strip leading ~ (with possible whitespace) and trailing ^
        var trimmed = raw.Trim();

        if (!trimmed.StartsWith(SpplConstants.CommandStart))
            throw new FormatException($"Response does not start with '{SpplConstants.CommandStart}': {raw}");

        if (!trimmed.EndsWith(SpplConstants.CommandEnd))
            throw new FormatException($"Response does not end with '{SpplConstants.CommandEnd}': {raw}");

        // Remove ~ and ^, trim whitespace between ~ and SPGRES
        var inner = trimmed[1..^1].Trim();

        // Verify SPGRES{ wrapper
        if (!inner.StartsWith($"{SpplConstants.ResponsePrefix}{{"))
            throw new FormatException($"Response missing '{SpplConstants.ResponsePrefix}{{' wrapper: {raw}");

        if (!inner.EndsWith("}"))
            throw new FormatException($"Response missing closing '}}': {raw}");

        // Extract content between SPGRES{ and }
        var content = inner[(SpplConstants.ResponsePrefix.Length + 1)..^1];

        // Split on first ':' → command name + payload
        var colonIndex = content.IndexOf(':');
        if (colonIndex < 0)
            throw new FormatException($"Response missing ':' separator: {raw}");

        var command = content[..colonIndex];
        var payload = content[(colonIndex + 1)..];

        return new SpplResponse(command, payload);
    }

    /// <summary>
    /// Parses SPPSTA response payload into status and optional info.
    /// Format: "WAITING&lt;" or "ERROR&lt;Ribbon not found" or "RUNNING&lt;BLOCKED"
    /// </summary>
    public static (string State, string? Info) ParseStatus(string payload)
    {
        var parts = payload.Split('<', 2);
        var state = parts[0];
        var info = parts.Length > 1 && !string.IsNullOrEmpty(parts[1]) ? parts[1] : null;
        return (state, info);
    }

    /// <summary>
    /// Validates that a code value does not contain forbidden SPPL sequences.
    /// </summary>
    public static bool IsValidCodeValue(string code)
    {
        foreach (var forbidden in SpplConstants.ForbiddenSequences)
        {
            if (code.Contains(forbidden))
                return false;
        }
        return true;
    }
}
