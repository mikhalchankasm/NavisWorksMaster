using System.Collections.Generic;
using System.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashReportAccumulationHelperTests
{
    [Fact]
    public void BuildFileResponse_ReturnsNullForNullCurrent()
    {
        Assert.Null(ClashReportAccumulationHelper.BuildFileResponse(null, null, new ClashGenerateReportResponse()));
    }

    [Fact]
    public void BuildFileResponse_AccumulatesPreviousAndCurrentRowsByIndex()
    {
        var previous = new ClashGenerateReportResponse
        {
            CreatedViewpointCount = 2,
            ScreenshotCount = 3,
            FullBoxTransparencyItemCount = 4,
            Items = new List<ClashReportItem>
            {
                Item(1, "New", "old-1"),
                Item(3, "Approved", "old-3"),
            },
            Warnings = new List<string> { "previous", "duplicate", " " },
            Clusters = new List<ClashClusterSummary> { new ClashClusterSummary() },
        };
        var current = new ClashGenerateReportResponse
        {
            CreatedViewpointCount = 5,
            ScreenshotCount = 7,
            FullBoxTransparencyItemCount = 11,
            Items = new List<ClashReportItem>
            {
                Item(2, "Active", "new-2"),
                Item(3, "Resolved", "new-3"),
                null,
            },
            Warnings = new List<string> { "current", "duplicate", "" },
        };
        var fileResponse = new ClashGenerateReportResponse
        {
            CreatedViewpointCount = current.CreatedViewpointCount,
            ScreenshotCount = current.ScreenshotCount,
            FullBoxTransparencyItemCount = current.FullBoxTransparencyItemCount,
            Items = current.Items.ToList(),
            Warnings = current.Warnings.ToList(),
        };

        var result = ClashReportAccumulationHelper.BuildFileResponse(fileResponse, current, previous);

        Assert.Same(fileResponse, result);
        Assert.Equal(new[] { 1, 2, 3 }, result.Items.Select(item => item.Index));
        Assert.Equal(new[] { "old-1", "new-2", "new-3" }, result.Items.Select(item => item.ResultName));
        Assert.Equal(3, result.ReturnedResultCount);
        Assert.Equal(3, result.AccumulatedResultCount);
        Assert.Equal(7, result.CreatedViewpointCount);
        Assert.Equal(10, result.ScreenshotCount);
        Assert.Equal(15, result.FullBoxTransparencyItemCount);
        Assert.Equal(new[] { "previous", "duplicate", "current" }, result.Warnings);
        Assert.Same(previous.Clusters, result.Clusters);
        Assert.Equal(1, result.ClusterCount);
        Assert.Equal(1, result.ReturnedClusterCount);
        Assert.Equal(1, result.ReturnedStatusCounts["New"]);
        Assert.Equal(1, result.ReturnedStatusCounts["Active"]);
        Assert.Equal(1, result.ReturnedStatusCounts["Resolved"]);
        Assert.False(result.ReturnedStatusCounts.ContainsKey("Approved"));
    }

    [Fact]
    public void BuildFileResponse_PreservesCurrentClustersWhenPresent()
    {
        var previous = new ClashGenerateReportResponse
        {
            Clusters = new List<ClashClusterSummary> { new ClashClusterSummary(), new ClashClusterSummary() },
        };
        var currentClusters = new List<ClashClusterSummary> { new ClashClusterSummary() };
        var current = new ClashGenerateReportResponse
        {
            Clusters = currentClusters,
            Items = new List<ClashReportItem> { Item(1, "New", "row") },
        };

        var result = ClashReportAccumulationHelper.BuildFileResponse(current, current, previous);

        Assert.Same(currentClusters, result.Clusters);
        Assert.Equal(1, result.ClusterCount);
        Assert.Equal(1, result.ReturnedClusterCount);
    }

    [Fact]
    public void BuildStatusCountsFromReportItems_TrimsStatusAndUsesUnknown()
    {
        var counts = ClashReportAccumulationHelper.BuildStatusCountsFromReportItems(new[]
        {
            Item(1, " New ", "a"),
            Item(2, "new", "b"),
            Item(3, "", "c"),
            Item(4, null, "d"),
            null,
        });

        Assert.Equal(2, counts["New"]);
        Assert.Equal(2, counts["Unknown"]);
    }

    [Fact]
    public void MergeWarnings_FiltersBlankAndKeepsOrdinalDistinctValues()
    {
        var warnings = ClashReportAccumulationHelper.MergeWarnings(
            new[] { "A", "B", " ", null },
            new[] { "B", "b", "C", "" });

        Assert.Equal(new[] { "A", "B", "b", "C" }, warnings);
    }

    private static ClashReportItem Item(int index, string status, string name)
    {
        return new ClashReportItem
        {
            Index = index,
            Status = status,
            ResultName = name,
        };
    }
}
