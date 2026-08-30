using CodePrintManager.Printer.Savema.Protocol;
using FluentAssertions;

namespace CodePrintManager.Printer.Savema.Tests;

public class SpplResponseParserTests
{
    // ── Parse: happy paths ──────────────────────────────────────────

    [Fact]
    public void Parse_ExtractsCommandAndPayload()
    {
        var result = SpplResponseParser.Parse("~SPGRES{SPGGCP:42}^");

        result.Command.Should().Be("SPGGCP");
        result.Payload.Should().Be("42");
    }

    [Fact]
    public void Parse_OkPayload_SetsIsOk()
    {
        var result = SpplResponseParser.Parse("~SPGRES{SPLLTF:OK}^");

        result.IsOk.Should().BeTrue();
    }

    [Fact]
    public void Parse_FailPayload_SetsIsFail()
    {
        var result = SpplResponseParser.Parse("~SPGRES{SPLLTF:FAIL}^");

        result.IsFail.Should().BeTrue();
    }

    [Fact]
    public void Parse_NumericPayload_AsIntReturnsValue()
    {
        var result = SpplResponseParser.Parse("~SPGRES{SPGGTP:1234}^");

        result.AsInt().Should().Be(1234);
    }

    [Fact]
    public void Parse_ListPayload_AsListReturnsSplitValues()
    {
        var result = SpplResponseParser.Parse("~SPGRES{SPLGST:template1.rox<template2.rox}^");

        result.AsList().Should().Equal("template1.rox", "template2.rox");
    }

    // ── Parse: whitespace tolerance ─────────────────────────────────

    [Fact]
    public void Parse_SpaceAfterTilde_StillParses()
    {
        var result = SpplResponseParser.Parse("~ SPGRES{SPGGCP:42}^");

        result.Command.Should().Be("SPGGCP");
        result.Payload.Should().Be("42");
    }

    // ── Parse: malformed inputs ─────────────────────────────────────

    [Fact]
    public void Parse_MissingTilde_Throws()
    {
        var act = () => SpplResponseParser.Parse("SPGRES{SPGGCP:42}^");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_MissingCaret_Throws()
    {
        var act = () => SpplResponseParser.Parse("~SPGRES{SPGGCP:42}");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_MissingResponsePrefix_Throws()
    {
        var act = () => SpplResponseParser.Parse("~CUSTOM{SPGGCP:42}^");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_MissingColon_Throws()
    {
        var act = () => SpplResponseParser.Parse("~SPGRES{SPGGCP42}^");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_MissingClosingBrace_Throws()
    {
        var act = () => SpplResponseParser.Parse("~SPGRES{SPGGCP:42^");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_EmptyString_Throws()
    {
        var act = () => SpplResponseParser.Parse("");

        act.Should().Throw<FormatException>();
    }

    // ── ParseStatus ─────────────────────────────────────────────────

    [Fact]
    public void ParseStatus_WaitingWithTrailingSeparator_ReturnsNullInfo()
    {
        var (state, info) = SpplResponseParser.ParseStatus("WAITING<");

        state.Should().Be("WAITING");
        info.Should().BeNull();
    }

    [Fact]
    public void ParseStatus_ErrorWithDetail_ReturnsInfo()
    {
        var (state, info) = SpplResponseParser.ParseStatus("ERROR<Ribbon not found");

        state.Should().Be("ERROR");
        info.Should().Be("Ribbon not found");
    }

    [Fact]
    public void ParseStatus_RunningBlocked_ReturnsInfo()
    {
        var (state, info) = SpplResponseParser.ParseStatus("RUNNING<BLOCKED");

        state.Should().Be("RUNNING");
        info.Should().Be("BLOCKED");
    }

    // ── IsValidCodeValue ────────────────────────────────────────────

    [Theory]
    [InlineData("ABC123", true)]
    [InlineData("01234567890128", true)]
    [InlineData("has^caret", false)]
    [InlineData("has~gt~seq", false)]
    [InlineData("has~sc~seq", false)]
    [InlineData("has~tilde", false)]
    [InlineData("has|pipe", false)]
    [InlineData("has\nnewline", false)]
    [InlineData("has\rreturn", false)]
    public void IsValidCodeValue_ReturnsExpected(string code, bool expected)
    {
        SpplResponseParser.IsValidCodeValue(code).Should().Be(expected);
    }
}

public class SpplResponseTests
{
    // ── AsInt ────────────────────────────────────────────────────────

    [Fact]
    public void AsInt_NumericPayload_ReturnsParsedInt()
    {
        new SpplResponse("CMD", "42").AsInt().Should().Be(42);
    }

    [Fact]
    public void AsInt_NonNumericPayload_Throws()
    {
        var act = () => new SpplResponse("CMD", "abc").AsInt();

        act.Should().Throw<FormatException>();
    }

    // ── AsList ──────────────────────────────────────────────────────

    [Fact]
    public void AsList_MultipleItems_ReturnsSplitList()
    {
        new SpplResponse("CMD", "a<b<c").AsList().Should().Equal("a", "b", "c");
    }

    [Fact]
    public void AsList_EmptyPayload_ReturnsSingleEmptyElement()
    {
        new SpplResponse("CMD", "").AsList().Should().Equal("");
    }

    [Fact]
    public void AsList_SingleValue_ReturnsSingleElement()
    {
        new SpplResponse("CMD", "single").AsList().Should().Equal("single");
    }

    // ── IsOk / IsFail ───────────────────────────────────────────────

    [Fact]
    public void IsOk_OkPayload_TrueAndIsFailFalse()
    {
        var response = new SpplResponse("CMD", "OK");

        response.IsOk.Should().BeTrue();
        response.IsFail.Should().BeFalse();
    }

    [Fact]
    public void IsFail_FailPayload_TrueAndIsOkFalse()
    {
        var response = new SpplResponse("CMD", "FAIL");

        response.IsFail.Should().BeTrue();
        response.IsOk.Should().BeFalse();
    }

    [Fact]
    public void IsOkAndIsFail_OtherPayload_BothFalse()
    {
        var response = new SpplResponse("CMD", "other");

        response.IsOk.Should().BeFalse();
        response.IsFail.Should().BeFalse();
    }
}
