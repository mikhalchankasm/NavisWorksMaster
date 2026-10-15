using System.Reflection;
using ModelContextProtocol.Server;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// The MCP annotations every client sees must agree with the effects each tool
// declares in [ToolCapabilities]: read-only exactly when the tool has no effect,
// destructive exactly when it changes the document, files or the host process,
// and never open-world, because every tool talks only to the local Navisworks.
// Read-only tools are also idempotent. Every tool has a human-readable title,
// advertised both as Tool.title and as annotations.title.
public sealed class McpToolAnnotationsTests
{
    private const ToolEffects DestructiveEffects = ToolEffects.Document | ToolEffects.Files | ToolEffects.Host;

    [Fact]
    public void AnnotationsFollowDeclaredEffects()
    {
        var methods = McpReadOnlyMode.RegisteredToolMethods().ToList();
        var tools = methods
            .Select(method => method.IsStatic
                ? McpServerTool.Create(method)
                : McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked")))
            .ToList();

        McpToolAnnotations.Apply(tools);

        Assert.Equal(117, tools.Count);
        var mismatches = new List<string>();
        for (var index = 0; index < tools.Count; index++)
        {
            var effects = methods[index].GetCustomAttribute<ToolCapabilitiesAttribute>()!.Effects;
            var annotations = tools[index].ProtocolTool.Annotations;
            var expected = (ReadOnly: effects == ToolEffects.None,
                Destructive: (effects & DestructiveEffects) != 0,
                OpenWorld: false);
            var actual = (annotations?.ReadOnlyHint, annotations?.DestructiveHint, annotations?.OpenWorldHint, annotations?.IdempotentHint);
            if (actual != (expected.ReadOnly, expected.Destructive, expected.OpenWorld, expected.ReadOnly))
                mismatches.Add(tools[index].ProtocolTool.Name + " " + effects + " -> " + actual);
        }

        Assert.True(mismatches.Count == 0, mismatches.Count + " tools mismatch:\n" + string.Join("\n", mismatches));
    }

    [Fact]
    public void EveryToolHasADistinctReadableTitle()
    {
        var tools = McpReadOnlyMode.RegisteredToolMethods()
            .Select(method => method.IsStatic
                ? McpServerTool.Create(method)
                : McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked")))
            .ToList();

        McpToolAnnotations.Apply(tools);

        var titles = new List<string>();
        foreach (var tool in tools)
        {
            var name = tool.ProtocolTool.Name;
            var title = tool.ProtocolTool.Title;
            Assert.False(string.IsNullOrWhiteSpace(title), name + " has no title");
            Assert.Equal(title, tool.ProtocolTool.Annotations?.Title);
            Assert.DoesNotContain("_", title);
            Assert.NotEqual(name, title);
            Assert.True(char.IsUpper(title[0]), name + " title must start with a capital: " + title);
            titles.Add(title);
        }

        Assert.Equal(titles.Count, titles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("MCP health check", Title(tools, "mcp_health_check"));
        Assert.Equal("Build MTR viewpoints", Title(tools, "build_mtr_viewpoints"));
        Assert.Equal("Clash BBox pair plan", Title(tools, "clash_bbox_pair_plan"));
        Assert.Equal("List root items", Title(tools, "list_root_items"));
    }

    private static string Title(IEnumerable<McpServerTool> tools, string name) =>
        tools.Single(tool => tool.ProtocolTool.Name == name).ProtocolTool.Title;
}
