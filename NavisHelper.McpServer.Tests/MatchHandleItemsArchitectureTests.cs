using System.Reflection;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class MatchHandleItemsArchitectureTests
{
    private static MethodInfo Tool =>
        typeof(NavisworksMatchHandleTools).GetMethod(nameof(NavisworksMatchHandleTools.MatchHandleItems))!;

    [Fact]
    public void McpTool_IsSeparateTypedContainerAndReadsOnly()
    {
        var capabilities = Tool.GetCustomAttribute<ToolCapabilitiesAttribute>();

        Assert.NotNull(Tool.GetCustomAttribute<McpServerToolAttribute>());
        Assert.NotNull(capabilities);
        Assert.Equal(ToolEffects.None, capabilities!.Effects);
        Assert.True(capabilities.RequiresHost);
        Assert.True(capabilities.RequiresDocument);
    }

    [Fact]
    public void McpTool_PagesWithTheDocumentedDefaultsAndHasNoApply()
    {
        var parameters = Tool.GetParameters();

        Assert.Equal(typeof(string), parameters.Single(parameter => parameter.Name == "matchHandle").ParameterType);
        Assert.False(parameters.Single(parameter => parameter.Name == "matchHandle").IsOptional);
        Assert.Equal(0, parameters.Single(parameter => parameter.Name == "offset").DefaultValue);
        Assert.Equal(MatchHandleItemsPaging.DefaultLimit, parameters.Single(parameter => parameter.Name == "limit").DefaultValue);
        Assert.Equal(true, parameters.Single(parameter => parameter.Name == "includePaths").DefaultValue);
        Assert.Equal(false, parameters.Single(parameter => parameter.Name == "includeSourceFiles").DefaultValue);
        Assert.DoesNotContain(parameters, parameter => parameter.Name == "apply");
    }

    [Fact]
    public void Registrations_ExposeOneNewCommandThroughDedicatedBoundaries()
    {
        var root = RepositoryPaths.Root;
        var program = Read(root, "NavisHelper.McpServer", "Program.cs");
        var router = Read(root, "NavisHelper", "Agent", "Host", "AgentHostService.CommandRouter.cs");
        var project = Read(root, "NavisHelper", "NavisHelper.csproj");

        Assert.Contains("WithTools<NavisworksMatchHandleTools>()", program, StringComparison.Ordinal);
        Assert.Contains("HostCommandNames.MatchHandleItems", router, StringComparison.Ordinal);
        Assert.Contains("_matchHandleItemsService.GetItems", router, StringComparison.Ordinal);
        Assert.Contains("Agent\\Services\\MatchHandleItemsService.cs", project, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tool exists so a caller can see a match it already has without changing
    /// anything, so the host side must resolve the stored handle and read it: no
    /// second search, no new handle, and none of the state the selection, visibility
    /// and camera tools own.
    /// </summary>
    [Fact]
    public void HostService_ReadsTheStoredHandleAndChangesNoDocumentState()
    {
        var service = Read(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "MatchHandleItemsService.cs");

        Assert.Contains("internal sealed class MatchHandleItemsService", service, StringComparison.Ordinal);
        Assert.Contains("_sessionStore.TryGet(handle, out items, out reason)", service, StringComparison.Ordinal);
        Assert.Contains("ErrorCodes.StaleMatchReference", service, StringComparison.Ordinal);
        Assert.Contains("ItemChainPaths.Build", service, StringComparison.Ordinal);
        Assert.Contains("MatchHandleItemsPaging.Plan", service, StringComparison.Ordinal);
        Assert.DoesNotContain("_sessionStore.Add", service, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentSelection", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SetHidden", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Descendants", service, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentViewpoint", service, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class DocumentCommandService", service, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchHandleFamily_GetsItsOwnTypesOnBothSides()
    {
        var root = RepositoryPaths.Root;
        var tool = Read(root, "NavisHelper.McpServer", "Tools", "NavisworksMatchHandleTools.cs");
        var service = Read(root, "NavisHelper", "Agent", "Services", "MatchHandleItemsService.cs");

        Assert.Contains("internal sealed class NavisworksMatchHandleTools", tool, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class NavisworksTools", tool, StringComparison.Ordinal);
        Assert.Contains("internal sealed class MatchHandleItemsService", service, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class DocumentCommandService", service, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class SearchService", service, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWireNameIsTheToolName()
    {
        Assert.Equal("match_handle_items", HostCommandNames.MatchHandleItems);
    }

    private static string Read(string root, params string[] parts)
    {
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}  
