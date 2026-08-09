namespace CodePrintManager.Domain.Validation;

/// <summary>
/// Domain-level validation for code values.
/// Ensures codes do not contain sequences that are illegal in the SPPL printer protocol.
/// </summary>
public static class CodeValidator
{
    /// <summary>
    /// Sequences that cannot appear in code values because they have special meaning
    /// in the SPPL protocol and would corrupt printer communication.
    /// </summary>
    private static readonly string[] ForbiddenSequences = { "^", "~gt~", "~sc~", "~" };

    /// <summary>
    /// Returns true if the code value is safe to use with SPPL-based printers.
    /// </summary>
    public static bool IsValid(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        foreach (var forbidden in ForbiddenSequences)
        {
            if (code.Contains(forbidden))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns an error message describing why the code is invalid, or null if valid.
    /// </summary>
    public static string? GetValidationError(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "Code is empty or whitespace";

        foreach (var forbidden in ForbiddenSequences)
        {
            if (code.Contains(forbidden))
                return $"Code contains forbidden sequence '{forbidden}'";
        }
        return null;
    }
}
