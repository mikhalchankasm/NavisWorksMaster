using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #444 redesign, slice epsilon2 (owner, 2026-11-15): the job exports listed items plus a depth.
// The request names its items like find_duplicate_geometry and item_appearance do: matchHandles,
// or useSelection=true, not both. The root-level Level is gone from the request and the status.
public sealed class FbxExportSourceContractTests
{
    [Fact]
    public void TheRequestNamesItemsAndADepthNotALevel()
    {
        var request = new StartFbxLevelExportRequest();
        Assert.NotNull(request.MatchHandles);
        Assert.Empty(request.MatchHandles);
        Assert.Null(request.UseSelection);
        Assert.Equal(0, request.Depth);

        Assert.Equal(typeof(List<string>), typeof(StartFbxLevelExportRequest).GetProperty("MatchHandles")!.PropertyType);
        Assert.Equal(typeof(bool?), typeof(StartFbxLevelExportRequest).GetProperty("UseSelection")!.PropertyType);
        Assert.Null(typeof(StartFbxLevelExportRequest).GetProperty("Level"));
    }

    [Fact]
    public void TheStatusReportsTheDepth()
    {
        Assert.Equal(typeof(int), typeof(FbxLevelExportStatusResponse).GetProperty("Depth")!.PropertyType);
        Assert.Null(typeof(FbxLevelExportStatusResponse).GetProperty("Level"));
    }

    [Theory]
    [InlineData(new[] { "h1" }, null, null)]
    [InlineData(new string[0], true, null)]
    [InlineData(new[] { "h1" }, true, "Pass matchHandles or useSelection=true, not both.")]
    [InlineData(new string[0], null, "Pass matchHandles or useSelection=true.")]
    public void TheTargetRuleIsTheSharedOne(string[] handles, bool? useSelection, string? expected)
    {
        Assert.Equal(expected, ItemAppearanceRules.DescribeTargetError(handles.ToList(), useSelection));
    }
}
