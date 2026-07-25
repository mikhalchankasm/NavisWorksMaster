using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashClusterKeyHelperTests
{
    [Theory]
    [InlineData(null, 100)]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(42, 42)]
    [InlineData(6000, 5000)]
    public void ClampClusterLimit_AppliesDefaultMinimumAndMaximum(int? value, int expected)
    {
        Assert.Equal(expected, ClashClusterKeyHelper.ClampClusterLimit(value));
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(12, 12)]
    [InlineData(99, 50)]
    public void ClampPreviewRowsPerCluster_AppliesDefaultMinimumAndMaximum(int? value, int expected)
    {
        Assert.Equal(expected, ClashClusterKeyHelper.ClampPreviewRowsPerCluster(value));
    }

    [Theory]
    [InlineData(null, "hybrid")]
    [InlineData("", "hybrid")]
    [InlineData("point-cluster", "spatial")]
    [InlineData("proximity", "spatial")]
    [InlineData("pair", "object_pair")]
    [InlineData("association pair", "object_pair")]
    [InlineData("mixed", "hybrid")]
    [InlineData("association-spatial", "hybrid")]
    [InlineData("bad", null)]
    public void NormalizeMode_RecognizesListClusterAliases(string value, string expected)
    {
        Assert.Equal(expected, ClashClusterKeyHelper.NormalizeMode(value));
    }

    [Theory]
    [InlineData(null, "none")]
    [InlineData("", "none")]
    [InlineData("none", "none")]
    [InlineData("off", "none")]
    [InlineData("false", "none")]
    [InlineData("pair", "object_pair")]
    [InlineData("bad", null)]
    public void NormalizeReportMode_AllowsNoneAndClusterAliases(string value, string expected)
    {
        Assert.Equal(expected, ClashClusterKeyHelper.NormalizeReportMode(value));
    }

    [Theory]
    [InlineData(" Pump   Station ", "pump station")]
    [InlineData("Root\r\n\tPump", "root pump")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeKey_CollapsesWhitespaceAndLowercases(string value, string expected)
    {
        Assert.Equal(expected, ClashClusterKeyHelper.NormalizeKey(value));
    }

    [Theory]
    [InlineData("ID", " A  B ", "id:a b")]
    [InlineData(" path ", "Root / Pump", "path:root / pump")]
    [InlineData("source", "", "source:")]
    public void BuildTypedKey_NormalizesPrefixAndValue(string prefix, string value, string expected)
    {
        Assert.Equal(expected, ClashClusterKeyHelper.BuildTypedKey(prefix, value));
    }

    [Theory]
    [InlineData(null, "cbf29ce484222325")]
    [InlineData("", "cbf29ce484222325")]
    [InlineData("abc", "e71fa2190541574b")]
    [InlineData("clash", "d1166e952fcbd61a")]
    public void StableHexHash_ReturnsStableFnv1aHash(string value, string expected)
    {
        Assert.Equal(expected, ClashClusterKeyHelper.StableHexHash(value));
    }

    [Theory]
    [InlineData(0, 0, 0, "0:0:0")]
    [InlineData(12, -3, 45, "12:-3:45")]
    public void BuildSpatialCellKey_UsesInvariantColonSeparatedCoordinates(int x, int y, int z, string expected)
    {
        Assert.Equal(expected, ClashClusterKeyHelper.BuildSpatialCellKey(x, y, z));
    }
}
