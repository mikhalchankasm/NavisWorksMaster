using NavisHelper.Agent.Contracts;
using System.Collections.Generic;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class VisibilityRootSummaryHelperTests
{
    [Fact]
    public void Summarize_GroupsItemsByRootPathAndReturnsLargestGroupsFirst()
    {
        var result = VisibilityRootSummaryHelper.Summarize(new[]
        {
            new VisibilityRootSummaryInput { RootDisplayName = "A", RootPath = "A", SourceFile = "C:\\A.rvm" },
            new VisibilityRootSummaryInput { RootDisplayName = "B", RootPath = "B", SourceFile = "C:\\B.rvm" },
            new VisibilityRootSummaryInput { RootDisplayName = "A", RootPath = "A", SourceFile = "C:\\A.rvm" },
        }, 10);

        Assert.False(result.Truncated);
        Assert.Equal(2, result.TotalRootCount);
        Assert.Collection(result.Summaries,
            summary =>
            {
                Assert.Equal("A", summary.RootPath);
                Assert.Equal("C:\\A.rvm", summary.SourceFile);
                Assert.Equal(2, summary.AffectedItemCount);
            },
            summary => Assert.Equal("B", summary.RootPath));
    }

    [Fact]
    public void Summarize_UsesSourceFileWhenRootPathIsUnavailable()
    {
        var result = VisibilityRootSummaryHelper.Summarize(new[]
        {
            new VisibilityRootSummaryInput { RootDisplayName = "", RootPath = "", SourceFile = "C:\\Shared.rvm" },
            new VisibilityRootSummaryInput { RootDisplayName = "", RootPath = "", SourceFile = "c:\\shared.rvm" },
        }, 10);

        var summary = Assert.Single(result.Summaries);
        Assert.Equal(1, result.TotalRootCount);
        Assert.Equal(2, summary.AffectedItemCount);
    }

    [Fact]
    public void Summarize_ReportsTruncationWithoutChangingTotalCount()
    {
        var result = VisibilityRootSummaryHelper.Summarize(new[]
        {
            new VisibilityRootSummaryInput { RootDisplayName = "C", RootPath = "C" },
            new VisibilityRootSummaryInput { RootDisplayName = "B", RootPath = "B" },
            new VisibilityRootSummaryInput { RootDisplayName = "A", RootPath = "A" },
        }, 2);

        Assert.True(result.Truncated);
        Assert.Equal(3, result.TotalRootCount);
        Assert.Equal(new[] { "A", "B" }, result.Summaries.ConvertAll(summary => summary.RootPath));
    }

    [Fact]
    public void Summarize_EnumeratesInputsOnce()
    {
        var enumerationCount = 0;

        IEnumerable<VisibilityRootSummaryInput> Inputs()
        {
            enumerationCount++;
            yield return new VisibilityRootSummaryInput { RootDisplayName = "A", RootPath = "A" };
        }

        var result = VisibilityRootSummaryHelper.Summarize(Inputs(), 10);

        Assert.Equal(1, enumerationCount);
        Assert.Single(result.Summaries);
    }

    [Fact]
    public void Summarize_AccumulatesPreGroupedInputCounts()
    {
        var result = VisibilityRootSummaryHelper.Summarize(new[]
        {
            new VisibilityRootSummaryInput { RootDisplayName = "A", RootPath = "A", AffectedItemCount = 4 },
            new VisibilityRootSummaryInput { RootDisplayName = "B", RootPath = "B", AffectedItemCount = 2 },
        }, 10);

        Assert.Equal(4, result.Summaries[0].AffectedItemCount);
        Assert.Equal(2, result.Summaries[1].AffectedItemCount);
    }
}
