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

        Assert.Equal(113, tools.Count);
        var mismatches = new List<string>();
        for (var index = 0; index < tools.Count; index++)
        {
            var effects = methods[index].GetCustomAttribute<ToolCapabilitiesAttribute>()!.Effects;
            var annotations = tools[index].ProtocolTool.Annotations;
            var expected = (ReadOnly: effects == ToolEffects.None,
                Destructive: (effects & DestructiveEffects) != 0,
                OpenWorld: false);
            var actual = (annotations?.ReadOnlyHint, annotations?.DestructiveHint, annotations?.OpenWorldHint);
            if (actual != (expected.ReadOnly, expected.Destructive, expected.OpenWorld))
                mismatches.Add(tools[index].ProtocolTool.Name + " " + effects + " -> " + actual);
        }

        Assert.True(mismatches.Count == 0, mismatches.Count + " tools mismatch:\n" + string.Join("\n", mismatches));
    }
}
