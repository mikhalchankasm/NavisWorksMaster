using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ElapsedTimeFormatterTests
{
    [Theory]
    [InlineData(-1, "0 seconds")]
    [InlineData(0, "0 seconds")]
    [InlineData(1000, "1 second")]
    [InlineData(2000, "2 seconds")]
    [InlineData(5000, "5 seconds")]
    [InlineData(11000, "11 seconds")]
    [InlineData(21000, "21 seconds")]
    [InlineData(60000, "1 minute")]
    [InlineData(61000, "1 minute 1 second")]
    [InlineData(3_784_000, "1 hour 3 minutes 4 seconds")]
    [InlineData(90_000_000, "1 day 1 hour")]
    public void Format_UsesBoundedEnglishDuration(long elapsedMs, string expected)
    {
        Assert.Equal(expected, ElapsedTimeFormatter.Format(elapsedMs));
    }

    [Fact]
    public void BuildUserMessage_UsesCustomPrefixAndFormattedElapsedTime()
    {
        Assert.Equal("Command completed: 1 minute.", ElapsedTimeFormatter.BuildUserMessage(60_000, "Command completed"));
    }

    [Fact]
    public void ReportThreshold_IsOneMinute()
    {
        Assert.Equal(60_000, ElapsedTimeFormatter.ReportThresholdMs);
    }
}
