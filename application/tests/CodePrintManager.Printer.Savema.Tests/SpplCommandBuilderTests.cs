using CodePrintManager.Printer.Savema.Protocol;
using FluentAssertions;

namespace CodePrintManager.Printer.Savema.Tests;

public class SpplCommandBuilderTests
{
    [Fact]
    public void GetSerialNumber_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.GetSerialNumber().Should().Be("~SPGGSN^");
    }

    [Fact]
    public void GetStatus_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.GetStatus().Should().Be("~SPPSTA^");
    }

    [Fact]
    public void GetCurrentCounter_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.GetCurrentCounter().Should().Be("~SPGGCP^");
    }

    [Fact]
    public void GetTotalCounter_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.GetTotalCounter().Should().Be("~SPGGTP^");
    }

    [Fact]
    public void GetRemainingQuantity_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.GetRemainingQuantity().Should().Be("~SPPGLQ^");
    }

    [Fact]
    public void ListTemplates_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.ListTemplates().Should().Be("~SPLGST^");
    }

    [Fact]
    public void GetActiveTemplate_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.GetActiveTemplate().Should().Be("~SPLGAT^");
    }

    [Fact]
    public void ActivateTemplate_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.ActivateTemplate("test.rox").Should().Be("~SPLLTF{test.rox}^");
    }

    [Fact]
    public void DeleteTemplate_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.DeleteTemplate("old.rox").Should().Be("~SPLDTF{old.rox}^");
    }

    [Fact]
    public void UploadTemplate_ReturnsExpectedCommand()
    {
        var data = new byte[] { 1, 2, 3 };
        var expectedBase64 = Convert.ToBase64String(data);

        SpplCommandBuilder.UploadTemplate("tpl.rox", data)
            .Should().Be($"~SPLRTF{{tpl.rox>{expectedBase64}}}^");
    }

    [Fact]
    public void ListCsvFiles_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.ListCsvFiles().Should().Be("~SPLGSD^");
    }

    [Fact]
    public void DeleteCsv_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.DeleteCsv("data.csv").Should().Be("~SPLDDF{data.csv}^");
    }

    [Fact]
    public void UploadCsv_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.UploadCsv("data.csv", new[] { "code1", "code2" })
            .Should().Be("~SPLCDF{data.csv~gt~code1\ncode2}^");
    }

    [Fact]
    public void SetPrintQuantity_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.SetPrintQuantity(100).Should().Be("~SPPSLQ{100}^");
    }

    [Fact]
    public void StartPrint_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.StartPrint().Should().Be("~SPPSAP^");
    }

    [Fact]
    public void StopPrint_ReturnsExpectedCommand()
    {
        SpplCommandBuilder.StopPrint().Should().Be("~SPPSTP^");
    }
}
