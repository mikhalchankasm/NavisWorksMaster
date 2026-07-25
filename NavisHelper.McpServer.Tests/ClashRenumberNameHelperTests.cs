using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashRenumberNameHelperTests
{
    [Theory]
    [InlineData(1, 4, "0001")]
    [InlineData(42, 2, "42")]
    [InlineData(42, 4, "0042")]
    [InlineData(12345, 4, "12345")]
    [InlineData(7, 0, "7")]
    public void FormatNumber_UsesMinimumWidth(int number, int width, string expected)
    {
        Assert.Equal(expected, ClashRenumberNameHelper.FormatNumber(number, width));
    }

    [Fact]
    public void BuildName_PreservesCleanNameAndRemovesExistingNumberByDefault()
    {
        var result = ClashRenumberNameHelper.BuildName(
            "0007 - Pump Station [NH:B]",
            "0001",
            "",
            "",
            " - ",
            preserveExistingName: true,
            stripExistingNumber: true);

        Assert.Equal("0001 - Pump Station [NH:B]", result);
    }

    [Fact]
    public void BuildName_CanKeepExistingNumberInsidePreservedName()
    {
        var result = ClashRenumberNameHelper.BuildName(
            "0007 - Pump Station [NH:A]",
            "0001",
            "",
            "",
            " - ",
            preserveExistingName: true,
            stripExistingNumber: false);

        Assert.Equal("0001 - 0007 - Pump Station [NH:A]", result);
    }

    [Fact]
    public void BuildName_CanGenerateNumberOnlyName()
    {
        var result = ClashRenumberNameHelper.BuildName(
            "Pump Station [NH:B]",
            "0001",
            "C-",
            "A",
            " - ",
            preserveExistingName: false,
            stripExistingNumber: true);

        Assert.Equal("C-0001A [NH:B]", result);
    }

    [Fact]
    public void BuildName_UsesDefaultSeparatorWhenSeparatorIsNull()
    {
        var result = ClashRenumberNameHelper.BuildName(
            "Pump Station",
            "0001",
            "",
            "",
            null,
            preserveExistingName: true,
            stripExistingNumber: true);

        Assert.Equal("0001 - Pump Station", result);
    }

    [Fact]
    public void BuildName_KeepsSideTagAfterTruncation()
    {
        var longName = new string('A', 260) + " [NH:A]";

        var result = ClashRenumberNameHelper.BuildName(
            longName,
            "0001",
            "",
            "",
            " - ",
            preserveExistingName: true,
            stripExistingNumber: true);

        Assert.EndsWith(" [NH:A]", result);
        Assert.True(result.Length <= 220);
    }

    [Fact]
    public void SanitizeNamePart_ReplacesInvalidCharactersAndCollapsesWhitespace()
    {
        var result = ClashRenumberNameHelper.SanitizeNamePart(" Pump\r\n  A:B*C? ", 100);

        Assert.Equal("Pump__ A_B_C_", result);
    }

    [Fact]
    public void SanitizeNamePart_FallsBackToItemForEmptyValue()
    {
        Assert.Equal("item", ClashRenumberNameHelper.SanitizeNamePart("   ", 100));
    }
}
