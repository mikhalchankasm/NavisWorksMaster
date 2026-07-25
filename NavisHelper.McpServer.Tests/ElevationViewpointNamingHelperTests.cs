using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ElevationViewpointNamingHelperTests
{
    private static readonly DateTime Timestamp = new(2026, 7, 21, 14, 35, 9);

    [Fact]
    public void BuildDayFolderName_UsesDayMonthYear()
    {
        Assert.Equal("Высотный день 21.07.2026", ElevationViewpointNamingHelper.BuildDayFolderName(Timestamp));
    }

    [Fact]
    public void BuildDefaultViewpointName_UsesTwentyFourHourTimeWithSeconds()
    {
        Assert.Equal("Высоты Z 21.07.2026 14:35:09", ElevationViewpointNamingHelper.BuildDefaultViewpointName(Timestamp));
    }
}
