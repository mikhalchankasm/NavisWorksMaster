using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class HostTargetResolverTests
{
    [Fact]
    public void Resolve_UsesExplicitInstanceBeforeVersion()
    {
        var selected = HostTargetResolver.Resolve(
            new[]
            {
                Record("nw-2026", "2026"),
                Record("nw-2027", "2027"),
            },
            new HostTargetOptions { InstanceId = "NW-2026", NavisworksVersion = "2027" },
            string.Empty,
            JsonOptions);

        Assert.Equal("nw-2026", selected.InstanceId);
    }

    [Fact]
    public void Resolve_RejectsAmbiguousVersionWithExistingErrorCode()
    {
        var error = Assert.Throws<HostCallException>(() => HostTargetResolver.Resolve(
            new[] { Record("nw-a", "2027"), Record("nw-b", "2027") },
            new HostTargetOptions { NavisworksVersion = "2027" },
            string.Empty,
            JsonOptions));

        Assert.Equal(ErrorCodes.MultipleHostsDetected, error.ErrorCode);
    }

    [Fact]
    public void Resolve_UsesConfiguredInstanceBeforeSingleHostFallback()
    {
        var selected = HostTargetResolver.Resolve(
            new[] { Record("nw-a", "2026"), Record("nw-b", "2027") },
            null,
            "nw-b",
            JsonOptions);

        Assert.Equal("nw-b", selected.InstanceId);
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };

    private static InstanceDiscoveryRecord Record(string instanceId, string version)
    {
        return new InstanceDiscoveryRecord { InstanceId = instanceId, NavisworksVersion = version };
    }
}
