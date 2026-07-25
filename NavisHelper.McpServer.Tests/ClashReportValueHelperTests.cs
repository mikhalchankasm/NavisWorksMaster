using System.Collections.Generic;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashReportValueHelperTests
{
    [Fact]
    public void NormalizeTextFilters_ReturnsEmptyListForNullOrBlankValues()
    {
        Assert.Empty(ClashReportValueHelper.NormalizeTextFilters(null));
        Assert.Empty(ClashReportValueHelper.NormalizeTextFilters(new[] { null, "", "   " }));
    }

    [Fact]
    public void NormalizeTextFilters_TrimsAndDeduplicatesCaseInsensitively()
    {
        var result = ClashReportValueHelper.NormalizeTextFilters(new[] { " Pump ", "pump", "Valve", " valve " });

        Assert.Equal(new[] { "Pump", "Valve" }, result);
    }

    [Fact]
    public void IncrementStatusCount_IgnoresNullDictionary()
    {
        ClashReportValueHelper.IncrementStatusCount(null, "New");
    }

    [Fact]
    public void IncrementStatusCount_TrimsStatusAndUsesUnknownForBlank()
    {
        var counts = new Dictionary<string, int>();

        ClashReportValueHelper.IncrementStatusCount(counts, " New ");
        ClashReportValueHelper.IncrementStatusCount(counts, "New");
        ClashReportValueHelper.IncrementStatusCount(counts, " ");
        ClashReportValueHelper.IncrementStatusCount(counts, null);

        Assert.Equal(2, counts["New"]);
        Assert.Equal(2, counts["Unknown"]);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(new string[0], "")]
    [InlineData(new[] { "A", "B" }, "A")]
    [InlineData(new string[] { null, "B" }, "")]
    public void FirstOrEmpty_ReturnsFirstNonThrowingValue(IList<string> values, string expected)
    {
        Assert.Equal(expected, ClashReportValueHelper.FirstOrEmpty(values));
    }
}
