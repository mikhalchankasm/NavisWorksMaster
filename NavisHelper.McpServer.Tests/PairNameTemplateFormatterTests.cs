using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class PairNameTemplateFormatterTests
{
    [Theory]
    [InlineData("/100000-XXX1-YY-01-АТХ", "/100000-XXX1-YY-01-АОВ", 1, "001 100000-XXX1-YY АТХ-АОВ")]
    [InlineData("/100000-XXX1-YY-01_ПС", "/100000-XXX1-YY-01-ВК1", 19, "019 100000-XXX1-YY ПС-ВК1")]
    [InlineData("/100000-XXX1-YY-01-ЭМ2", "/100000-XXX1-YY-01-ОВ2", 171, "171 100000-XXX1-YY ЭМ2-ОВ2")]
    public void Format_StripAndZeroPadProduceCleanDisciplineNames(
        string aName,
        string bName,
        int index,
        string expected)
    {
        const string template =
            "{index|zeroPad:3} 100000-XXX1-YY " +
            "{aName|strip:#^/100000-XXX1-YY-01[-_]#}-" +
            "{bName|strip:#^/100000-XXX1-YY-01[-_]#}";

        Assert.Equal(expected, PairNameTemplateFormatter.Format(template, index, aName, bName));
    }

    [Fact]
    public void Format_CodeTokensHandleDashAndUnderscore()
    {
        var result = PairNameTemplateFormatter.Format(
            "{index|zeroPad:3} {aCode|upper}-{bCode|lower}",
            7,
            "/100000-XXX1-YY-01_ПС",
            "/100000-XXX1-YY-01-ЭМ2");

        Assert.Equal("007 ПС-эм2", result);
    }

    [Fact]
    public void Format_RejectsUnknownTokenAndUnsafeWidth()
    {
        Assert.Throws<ArgumentException>(() => PairNameTemplateFormatter.Format("{unknown}", 1, "A", "B"));
        Assert.Throws<ArgumentException>(() => PairNameTemplateFormatter.Format("{index|zeroPad:99}", 1, "A", "B"));
    }
}
