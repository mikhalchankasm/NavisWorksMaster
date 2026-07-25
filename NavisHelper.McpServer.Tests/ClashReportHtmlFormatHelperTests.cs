using System.Collections.Generic;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashReportHtmlFormatHelperTests
{
    [Fact]
    public void FormatStatusCounts_ReturnsEmptyForNullOrEmptyCounts()
    {
        Assert.Equal(string.Empty, ClashReportHtmlFormatHelper.FormatStatusCounts(null));
        Assert.Equal(string.Empty, ClashReportHtmlFormatHelper.FormatStatusCounts(new Dictionary<string, int>()));
    }

    [Fact]
    public void FormatStatusCounts_UsesClashStatusOrderThenOrdinalKeyOrder()
    {
        var counts = new Dictionary<string, int>
        {
            ["Resolved"] = 5,
            ["New"] = 1,
            ["CustomB"] = 7,
            ["Approved"] = 4,
            ["Active"] = 2,
            ["Reviewed"] = 3,
            ["CustomA"] = 6,
        };

        var result = ClashReportHtmlFormatHelper.FormatStatusCounts(counts);

        Assert.Equal("New: 1, Active: 2, Reviewed: 3, Approved: 4, Resolved: 5, CustomA: 6, CustomB: 7", result);
    }

    [Theory]
    [InlineData("New", 0)]
    [InlineData("active", 1)]
    [InlineData("Reviewed", 2)]
    [InlineData("APPROVED", 3)]
    [InlineData("Resolved", 4)]
    [InlineData("Other", 100)]
    [InlineData(null, 100)]
    public void GetStatusSortOrder_ReturnsExpectedOrder(string status, int expected)
    {
        Assert.Equal(expected, ClashReportHtmlFormatHelper.GetStatusSortOrder(status));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("A&B<C>\"D'E", "A&amp;B&lt;C&gt;&quot;D&#39;E")]
    public void HtmlEncode_EscapesReportText(string value, string expected)
    {
        Assert.Equal(expected, ClashReportHtmlFormatHelper.HtmlEncode(value));
    }

    [Fact]
    public void HtmlAttributeEncode_NormalizesBackslashesBeforeEncoding()
    {
        var result = ClashReportHtmlFormatHelper.HtmlAttributeEncode("images\\a&b.jpg");

        Assert.Equal("images/a&amp;b.jpg", result);
    }
}
