using System.Collections.Generic;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashScopeLabelHelperTests
{
    [Fact]
    public void FormatRequestedTestNames_ReturnsEmptyForEmptyScope()
    {
        Assert.Equal(string.Empty, ClashScopeLabelHelper.FormatRequestedTestNames(null, null, null));
    }

    [Fact]
    public void FormatRequestedTestNames_PreservesScopeOrder()
    {
        var label = ClashScopeLabelHelper.FormatRequestedTestNames(
            " Test A ",
            new[] { " Test B ", "Test C" },
            new[] { " clash-test:1 " },
            " NH- ",
            5);

        Assert.Equal("Test A, Test B, Test C, clash-test:1, prefix:NH-, firstN:5", label);
    }

    [Fact]
    public void FormatRequestedTestNames_FiltersBlankValues()
    {
        var label = ClashScopeLabelHelper.FormatRequestedTestNames(
            " ",
            new[] { "", " Test B ", null },
            new[] { " ", "clash-test:2" },
            "",
            null);

        Assert.Equal("Test B, clash-test:2", label);
    }

    [Fact]
    public void FormatRequestedTestNames_DeduplicatesOrdinalIgnoreCaseAndKeepsFirstCasing()
    {
        var label = ClashScopeLabelHelper.FormatRequestedTestNames(
            "Test A",
            new[] { "test a", "TEST B", "test b" },
            new[] { "CLASH-TEST:1", "clash-test:1" },
            "test a",
            null);

        Assert.Equal("Test A, TEST B, CLASH-TEST:1, prefix:test a", label);
    }

    [Fact]
    public void FormatRequestedTestNames_FormatsNegativeFirstNLikeExistingService()
    {
        var label = ClashScopeLabelHelper.FormatRequestedTestNames(
            null,
            null,
            null,
            null,
            -3);

        Assert.Equal("firstN:-3", label);
    }

    [Fact]
    public void FormatRequestedTestNames_AcceptsListInstances()
    {
        var names = new List<string> { "A", "B" };
        var handles = new List<string> { "clash-test:1" };

        var label = ClashScopeLabelHelper.FormatRequestedTestNames(null, names, handles);

        Assert.Equal("A, B, clash-test:1", label);
    }
}
