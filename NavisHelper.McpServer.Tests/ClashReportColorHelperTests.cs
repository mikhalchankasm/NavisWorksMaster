using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashReportColorHelperTests
{
    [Theory]
    [InlineData("#FF2626", 255, 38, 38)]
    [InlineData("2666FF", 38, 102, 255)]
    [InlineData("  #00aaCC  ", 0, 170, 204)]
    [InlineData("12 345", 18, 3, 69)]
    public void TryParseHexRgb_ParsesSixDigitRgbWithOptionalHash(string value, int r, int g, int b)
    {
        var ok = ClashReportColorHelper.TryParseHexRgb(value, out var color);

        Assert.True(ok);
        Assert.NotNull(color);
        Assert.Equal((byte)r, color.R);
        Assert.Equal((byte)g, color.G);
        Assert.Equal((byte)b, color.B);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void TryParseHexRgb_TreatsBlankAsNoOverride(string value)
    {
        var ok = ClashReportColorHelper.TryParseHexRgb(value, out var color);

        Assert.True(ok);
        Assert.Null(color);
    }

    [Theory]
    [InlineData("#FFF")]
    [InlineData("#FFFFFFFF")]
    [InlineData("#GG0000")]
    [InlineData("red")]
    [InlineData("12x345")]
    public void TryParseHexRgb_RejectsInvalidValues(string value)
    {
        var ok = ClashReportColorHelper.TryParseHexRgb(value, out var color);

        Assert.False(ok);
        Assert.Null(color);
    }
}
