using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ArrowLineGeometryHelperTests
{
    [Fact]
    public void Build_ReturnsShaftAndTwoHeadLinesWithTipAtTheirStart()
    {
        var lines = ArrowLineGeometryHelper.Build(-8, 0, 0, 0);

        Assert.Equal(3, lines.Count);
        Assert.Equal(-8, lines[0].StartX, 8);
        Assert.Equal(0, lines[0].EndX, 8);
        Assert.Equal(0, lines[1].StartX, 8);
        Assert.Equal(0, lines[1].StartY, 8);
        Assert.Equal(0, lines[2].StartX, 8);
        Assert.Equal(0, lines[2].StartY, 8);
        Assert.NotEqual(lines[1].EndY, lines[2].EndY);
    }
}
