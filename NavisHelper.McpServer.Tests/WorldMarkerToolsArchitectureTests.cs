using System.Reflection;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class WorldMarkerToolsArchitectureTests
{
    [Fact]
    public void McpTools_AreSeparateTypedContainerWithDryRunAndReadOnlyList()
    {
        var set = Method(nameof(NavisworksWorldMarkerTools.WorldMarkersSet));
        var manage = Method(nameof(NavisworksWorldMarkerTools.WorldMarkersManage));
        var list = Method(nameof(NavisworksWorldMarkerTools.WorldMarkersList));

        Assert.NotNull(set?.GetCustomAttribute<McpServerToolAttribute>());
        Assert.NotNull(manage?.GetCustomAttribute<McpServerToolAttribute>());
        Assert.NotNull(list?.GetCustomAttribute<McpServerToolAttribute>());

        Assert.Equal(
            typeof(List<WorldMarkerOverlaySpec>),
            set!.GetParameters().Single(parameter => parameter.Name == "markers").ParameterType);
        Assert.Equal(false, set.GetParameters().Single(parameter => parameter.Name == "apply").DefaultValue);
        Assert.Equal(false, manage!.GetParameters().Single(parameter => parameter.Name == "apply").DefaultValue);

        var setCapabilities = set.GetCustomAttribute<ToolCapabilitiesAttribute>();
        var manageCapabilities = manage.GetCustomAttribute<ToolCapabilitiesAttribute>();
        var listCapabilities = list!.GetCustomAttribute<ToolCapabilitiesAttribute>();
        Assert.Equal(ToolEffects.View | ToolEffects.LocalState, setCapabilities.Effects);
        Assert.Equal(ToolEffects.View | ToolEffects.LocalState, manageCapabilities.Effects);
        Assert.Equal(ToolEffects.None, listCapabilities.Effects);
        foreach (var capabilities in new[] { setCapabilities, manageCapabilities, listCapabilities })
        {
            Assert.True(capabilities.RequiresHost);
            Assert.True(capabilities.RequiresDocument);
        }
    }

    [Fact]
    public void Registrations_ExposeTheFamilyThroughDedicatedBoundaries()
    {
        var root = FindRepositoryRoot();
        var program = Read(root, "NavisHelper.McpServer", "Program.cs");
        var router = Read(root, "NavisHelper", "Agent", "Host", "AgentHostService.CommandRouter.cs");
        var bridge = Read(root, "NavisHelper.McpServer", "Services", "HostBridgeClient.WorldMarkers.cs");
        var profile = Read(root, "NavisHelper.McpServer", "Services", "McpToolProfile.cs");

        Assert.Contains("WithTools<NavisworksWorldMarkerTools>()", program, StringComparison.Ordinal);
        Assert.Contains("HostCommandNames.WorldMarkersSet", router, StringComparison.Ordinal);
        Assert.Contains("HostCommandNames.WorldMarkersManage", router, StringComparison.Ordinal);
        Assert.Contains("HostCommandNames.WorldMarkersList", router, StringComparison.Ordinal);
        Assert.Contains("_worldMarkerOverlayService.Set", router, StringComparison.Ordinal);
        Assert.Contains("_worldMarkerOverlayService.Manage", router, StringComparison.Ordinal);
        Assert.Contains("_worldMarkerOverlayService.List", router, StringComparison.Ordinal);
        Assert.DoesNotContain("WorldMarkers", Read(root, "NavisHelper.McpServer", "Services", "HostBridgeClient.Commands.cs"), StringComparison.Ordinal);
        Assert.Contains("HostCommandNames.WorldMarkersSet", bridge, StringComparison.Ordinal);
        Assert.Contains("HostCommandNames.WorldMarkersManage", bridge, StringComparison.Ordinal);
        Assert.Contains("HostCommandNames.WorldMarkersList", bridge, StringComparison.Ordinal);
        foreach (var name in new[] { "world_markers_set", "world_markers_manage", "world_markers_list" })
            Assert.Contains("\"" + name + "\"", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkerFamily_DoesNotGrowLegacyGodPartials()
    {
        var root = FindRepositoryRoot();
        var tool = Read(root, "NavisHelper.McpServer", "Tools", "NavisworksWorldMarkerTools.cs");
        var service = Read(root, "NavisHelper", "Agent", "Services", "WorldMarkerOverlayService.cs");

        Assert.Contains("internal sealed class NavisworksWorldMarkerTools", tool, StringComparison.Ordinal);
        Assert.Contains("internal sealed class WorldMarkerOverlayService", service, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class NavisworksTools", tool, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class DocumentCommandService", service, StringComparison.Ordinal);
    }

    private static MethodInfo Method(string name)
    {
        return typeof(NavisworksWorldMarkerTools).GetMethod(name);
    }

    private static string Read(string root, params string[] parts)
    {
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
