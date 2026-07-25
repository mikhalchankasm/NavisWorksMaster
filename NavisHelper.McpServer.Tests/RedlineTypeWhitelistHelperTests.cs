using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class RedlineTypeWhitelistHelperTests
{
    [Theory]
    [InlineData("RedlineLine")]
    [InlineData("RedlineEllipse")]
    public void SupportedTypes_AreAccepted(string type)
    {
        Assert.True(RedlineTypeWhitelistHelper.IsSupportedForSetRedlines(type));
    }

    [Theory]
    [InlineData("RedlineArrow")]
    [InlineData("RedlineFreehand")]
    [InlineData("")]
    [InlineData(null)]
    public void UnsupportedTypes_AreRejected(string type)
    {
        Assert.False(RedlineTypeWhitelistHelper.IsSupportedForSetRedlines(type));
    }
}
