using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #444 slice gamma: the MCP tools for the FBX-by-level job (host commands from slice beta2, #451).
// Pattern: the save job pair (#138). Start changes the document's visibility for the duration of
// each export and writes files; status is read-only; cancel only changes job state.
public sealed class FbxLevelExportToolsTests
{
    private static MethodInfo Tool(string name)
    {
        var method = typeof(NavisworksFbxLevelExportTools).GetMethod(name);
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<McpServerToolAttribute>());
        return method;
    }

    private static string[] Parameters(MethodInfo method) =>
        method.GetParameters().Select(p => p.Name!).ToArray();

    [Fact]
    public void TheThreeToolsCarryTheJobCapabilities()
    {
        var start = Tool(nameof(NavisworksFbxLevelExportTools.StartFbxLevelExport)).GetCustomAttribute<ToolCapabilitiesAttribute>()!;
        Assert.Equal(ToolEffects.Document | ToolEffects.Files, start.Effects);
        Assert.True(start.RequiresHost);
        Assert.True(start.RequiresDocument);

        var status = Tool(nameof(NavisworksFbxLevelExportTools.FbxLevelExportStatus)).GetCustomAttribute<ToolCapabilitiesAttribute>()!;
        Assert.Equal(ToolEffects.None, status.Effects);
        Assert.True(status.RequiresHost);
        Assert.False(status.RequiresDocument);

        var cancel = Tool(nameof(NavisworksFbxLevelExportTools.CancelFbxLevelExport)).GetCustomAttribute<ToolCapabilitiesAttribute>()!;
        Assert.Equal(ToolEffects.LocalState, cancel.Effects);
        Assert.True(cancel.RequiresHost);
        Assert.False(cancel.RequiresDocument);
    }

    [Fact]
    public void TheParametersMatchTheContracts()
    {
        var start = Tool(nameof(NavisworksFbxLevelExportTools.StartFbxLevelExport));
        Assert.Equal(new[] { "level", "folder", "overwrite", "instanceId", "navisworksVersion", "cancellationToken" }, Parameters(start));
        var parameters = start.GetParameters();
        Assert.Equal(typeof(int), parameters[0].ParameterType);
        Assert.False(parameters[0].HasDefaultValue);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
        Assert.False(parameters[1].HasDefaultValue);
        Assert.Equal(typeof(bool), parameters[2].ParameterType);
        Assert.Equal(false, parameters[2].DefaultValue);

        foreach (var name in new[] { nameof(NavisworksFbxLevelExportTools.FbxLevelExportStatus), nameof(NavisworksFbxLevelExportTools.CancelFbxLevelExport) })
            Assert.Equal(new[] { "operationId", "instanceId", "navisworksVersion", "cancellationToken" }, Parameters(Tool(name)));
    }

    [Fact]
    public void TheDescriptionsStateWhatTheModelNeedsToKnow()
    {
        string Description(string name) => Tool(name).GetCustomAttribute<DescriptionAttribute>()!.Description;

        var start = Description(nameof(NavisworksFbxLevelExportTools.StartFbxLevelExport));
        foreach (var fact in new[] { "Level 1", "root items", "fbx_level_export_status", "cancel_fbx_level_export", "Options Editor", "NNN_", "overwrite" })
            Assert.Contains(fact, start, StringComparison.Ordinal);
        Assert.Contains("host_busy", Description(nameof(NavisworksFbxLevelExportTools.FbxLevelExportStatus)), StringComparison.Ordinal);
        Assert.Contains("next item", Description(nameof(NavisworksFbxLevelExportTools.CancelFbxLevelExport)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheToolsCallTheHostCommandsAndAreRegistered()
    {
        var root = RepositoryPaths.Root;
        var client = File.ReadAllText(Path.Combine(root, "NavisHelper.McpServer", "Services", "HostBridgeClient.FbxLevelExport.cs"));
        foreach (var command in new[] { "HostCommandNames.StartFbxLevelExport", "HostCommandNames.FbxLevelExportStatus", "HostCommandNames.CancelFbxLevelExport" })
            Assert.Contains(command, client, StringComparison.Ordinal);

        var tools = File.ReadAllText(Path.Combine(root, "NavisHelper.McpServer", "Tools", "NavisworksFbxLevelExportTools.cs"));
        Assert.Contains("Level = level", tools, StringComparison.Ordinal);
        Assert.Contains("Folder = folder", tools, StringComparison.Ordinal);
        Assert.Contains("Overwrite = overwrite", tools, StringComparison.Ordinal);

        Assert.Contains("WithTools<NavisworksFbxLevelExportTools>()",
            File.ReadAllText(Path.Combine(root, "NavisHelper.McpServer", "Program.cs")), StringComparison.Ordinal);
        var lifecycle = McpToolProfile.Sets["lifecycle"];
        Assert.Contains("start_fbx_level_export", lifecycle);
        Assert.Contains("fbx_level_export_status", lifecycle);
        Assert.Contains("cancel_fbx_level_export", lifecycle);
    }
}
