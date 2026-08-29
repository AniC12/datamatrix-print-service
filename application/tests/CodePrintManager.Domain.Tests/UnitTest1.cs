using CodePrintManager.Domain.Validation;

namespace CodePrintManager.Domain.Tests;

public class CodeValidatorTests
{
    [Theory]
    [InlineData("ABC123")]
    [InlineData("010460043993125621ABCDEF")]
    [InlineData("simple-code-value")]
    public void IsValid_ValidCode_ReturnsTrue(string code)
    {
        Assert.True(CodeValidator.IsValid(code));
        Assert.Null(CodeValidator.GetValidationError(code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValid_EmptyOrWhitespace_ReturnsFalse(string? code)
    {
        Assert.False(CodeValidator.IsValid(code!));
        Assert.NotNull(CodeValidator.GetValidationError(code!));
    }

    [Theory]
    [InlineData("code^end", "^")]
    [InlineData("code~gt~end", "~gt~")]
    [InlineData("code~sc~end", "~sc~")]
    [InlineData("code~end", "~")]
    public void IsValid_OriginalForbiddenSequence_ReturnsFalse(string code, string expectedForbidden)
    {
        Assert.False(CodeValidator.IsValid(code));
        var error = CodeValidator.GetValidationError(code);
        Assert.Contains(expectedForbidden, error);
    }

    [Fact]
    public void IsValid_Pipe_ReturnsFalse()
    {
        // "|" is the SPPL command separator — would corrupt command framing
        Assert.False(CodeValidator.IsValid("code|value"));
        Assert.Contains("|", CodeValidator.GetValidationError("code|value"));
    }

    [Fact]
    public void IsValid_Newline_ReturnsFalse()
    {
        // "\n" is the CSV row separator in SPLCDF — would split into two rows
        Assert.False(CodeValidator.IsValid("code\nvalue"));
        Assert.Contains("\n", CodeValidator.GetValidationError("code\nvalue"));
    }

    [Fact]
    public void IsValid_CarriageReturn_ReturnsFalse()
    {
        // "\r" could confuse the printer's CSV parser
        Assert.False(CodeValidator.IsValid("code\rvalue"));
        Assert.Contains("\r", CodeValidator.GetValidationError("code\rvalue"));
    }
}
