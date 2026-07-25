using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashGroupingPathPolicyTests
{
    [Fact]
    public void BuildSegments_CreatesRootToLeafPathsAndDepths()
    {
        var result = ClashGroupingPathPolicy.BuildSegments(new[] { "Model", "Level 01", "Pump" });

        Assert.Collection(
            result,
            item => AssertSegment(item, "Model", "Model", 0),
            item => AssertSegment(item, "Level 01", "Model/Level 01", 1),
            item => AssertSegment(item, "Pump", "Model/Level 01/Pump", 2));
    }

    [Fact]
    public void BuildSegments_PreservesLegacySegmentTextWithoutAdditionalNormalization()
    {
        var result = ClashGroupingPathPolicy.BuildSegments(new[] { " A ", "B/C", string.Empty });

        Assert.Equal(" A /B/C/", result[2].Path);
        Assert.Equal(string.Empty, result[2].Name);
    }

    [Fact]
    public void BuildSegments_NullInputReturnsEmptyList()
    {
        Assert.Empty(ClashGroupingPathPolicy.BuildSegments(null));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(new string[0], "")]
    public void BuildPath_EmptyInputsReturnEmptyString(string[] names, string expected)
    {
        Assert.Equal(expected, ClashGroupingPathPolicy.BuildPath(names));
    }

    [Fact]
    public void BuildPath_ReturnsLastCumulativePath()
    {
        Assert.Equal("Model/Level/Pump", ClashGroupingPathPolicy.BuildPath(new[] { "Model", "Level", "Pump" }));
    }

    [Theory]
    [InlineData("model/level", 1)]
    [InlineData("MODEL", 0)]
    [InlineData("Missing", -1)]
    [InlineData("", -1)]
    [InlineData("   ", -1)]
    [InlineData(null, -1)]
    public void FindPathIndex_UsesFirstOrdinalIgnoreCaseMatch(string selectedPath, int expected)
    {
        var paths = new[] { "Model", "Model/Level", "Model/Level", "Model/Level/Pump" };

        Assert.Equal(expected, ClashGroupingPathPolicy.FindPathIndex(paths, selectedPath));
    }

    [Fact]
    public void FindPathIndex_NullPathsReturnMinusOne()
    {
        Assert.Equal(-1, ClashGroupingPathPolicy.FindPathIndex(null, "Model"));
    }

    private static void AssertSegment(ClashGroupingPathSegment segment, string name, string path, int depth)
    {
        Assert.Equal(name, segment.Name);
        Assert.Equal(path, segment.Path);
        Assert.Equal(depth, segment.Depth);
    }
}
