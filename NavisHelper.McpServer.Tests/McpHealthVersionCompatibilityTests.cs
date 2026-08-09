using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class McpHealthVersionCompatibilityTests
{
    [Theory]
    [InlineData("2.9.0.0", "2.9.0.0", true)]
    [InlineData("2.9.0", "2.9.0.0", true)]
    [InlineData("2.8.9.0", "2.9.0.0", false)]
    [InlineData("2.9.0.1", "2.9.0.0", false)]
    [InlineData("custom", "CUSTOM", true)]
    [InlineData("custom-a", "custom-b", false)]
    public void AreCompatibleVersionStrings_RequiresSameNormalizedPackageVersion(
        string serverVersion,
        string pluginVersion,
        bool expected)
    {
        Assert.Equal(expected, NavisworksTools.AreCompatibleVersionStrings(serverVersion, pluginVersion));
    }
}
