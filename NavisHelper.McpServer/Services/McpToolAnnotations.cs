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
            annotations.IdempotentHint = annotations.ReadOnlyHint;
            annotations.DestructiveHint = (effects & (ToolEffects.Document | ToolEffects.Files | ToolEffects.Host)) != 0;
            annotations.OpenWorldHint = false;
            var title = tool.ProtocolTool.Title ?? annotations.Title ?? CreateTitle(tool.ProtocolTool.Name);
            tool.ProtocolTool.Title ??= title;
            annotations.Title ??= title;
        }
    }

    private static string CreateTitle(string name)
    {
        var words = name.Split('_');
        for (var index = 0; index < words.Length; index++)
        {
            words[index] = words[index].ToLowerInvariant() switch
            {
                "mcp" => "MCP",
                "mtr" => "MTR",
                "bbox" => "BBox",
                "csv" => "CSV",
                "bcf" => "BCF",
                "xml" => "XML",
                "nwd" => "NWD",
                "id" => "ID",
                var word => index == 0 && word.Length > 0
                    ? char.ToUpperInvariant(word[0]) + word[1..]
                    : word
            };
        }

        return string.Join(" ", words);
    }
}
