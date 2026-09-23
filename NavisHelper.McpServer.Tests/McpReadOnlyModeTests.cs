using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class McpReadOnlyModeTests
{
    [Theory]
    [InlineData(false, null, false)]
    [InlineData(false, "0", false)]
    [InlineData(false, "false", false)]
    [InlineData(false, "garbage", false)]
    [InlineData(false, "1", true)]
    [InlineData(false, "TRUE", true)]
    [InlineData(true, null, true)]
    [InlineData(true, "false", true)]
    public void ParseCombinesSwitchAndEnvironment(bool hasSwitch, string value, bool expected)
    {
        Assert.Equal(expected, McpReadOnlyMode.Parse(hasSwitch ? new[] { "--read-only" } : Array.Empty<string>(), value));
    }

    [Fact]
    public void ReadOnlyKeepsExactlyEffectFreeRegisteredTools()
    {
        var methods = McpReadOnlyMode.RegisteredToolMethods().ToList();
        var mode = new McpReadOnlyMode(true, methods);
        Assert.Equal(104, methods.Count);
        Assert.Equal(36, methods.Count(method => method.GetCustomAttribute<ToolCapabilitiesAttribute>().Effects == ToolEffects.None));
        foreach (var method in methods)
        {
            var name = System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(method.Name, "(.)([A-Z][a-z]+)", "$1_$2"),
                "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
            var expected = method.GetCustomAttribute<ToolCapabilitiesAttribute>().Effects == ToolEffects.None;
            Assert.Equal(expected, mode.IsAllowed(name));
        }
        Assert.False(mode.IsAllowed("unknown_tool"));
        var normal = new McpReadOnlyMode(false, methods);
        Assert.All(methods, method => Assert.True(normal.IsAllowed(method.Name)));
    }

    [Fact]
    public void RefusalCarriesStableCodeAndNamesTool()
    {
        var mode = new McpReadOnlyMode(true, McpReadOnlyMode.RegisteredToolMethods());
        Assert.False(mode.IsAllowed("create_viewpoint"));
        var refusal = McpReadOnlyMode.Refuse("create_viewpoint");
        Assert.True(refusal.IsError);
        var message = Assert.Single(refusal.Content.OfType<TextContentBlock>()).Text;
        Assert.Contains(ErrorCodes.ReadOnlyMode, message);
        Assert.Contains("create_viewpoint", message);
        Assert.Contains("started read-only", message);
    }

    [Fact]
    public void ErrorContractIncludesNonRetryableRefusal()
    {
        var item = Assert.Single(HostBridgeClient.GetErrorContract().Errors, item => item.ErrorCode == ErrorCodes.ReadOnlyMode);
        Assert.False(item.Retryable);
    }
}
