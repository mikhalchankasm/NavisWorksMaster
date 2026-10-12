using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NavisHelper.McpServer.Tools;

namespace NavisHelper.McpServer.Services;

internal static class McpToolAnnotations
{
    internal static void Apply(IEnumerable<McpServerTool> tools)
    {
        var effectsByName = new Dictionary<string, ToolEffects>(StringComparer.Ordinal);
        foreach (var method in McpReadOnlyMode.RegisteredToolMethods())
        {
            var capabilities = method.GetCustomAttribute<ToolCapabilitiesAttribute>(inherit: false)
                ?? throw new InvalidOperationException("Missing tool capabilities: " + method.Name);
            effectsByName.Add(McpReadOnlyMode.ToolName(method), capabilities.Effects);
        }

        foreach (var tool in tools)
        {
            var effects = effectsByName[tool.ProtocolTool.Name];
            var annotations = tool.ProtocolTool.Annotations ??= new ToolAnnotations();
            annotations.ReadOnlyHint = effects == ToolEffects.None;
            annotations.DestructiveHint = (effects & (ToolEffects.Document | ToolEffects.Files | ToolEffects.Host)) != 0;
            annotations.OpenWorldHint = false;
        }
    }
}
