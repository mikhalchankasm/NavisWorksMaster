using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace NavisHelper.McpServer.Services;

internal static class McpToolSchemaCompaction
{
    private static readonly IReadOnlyDictionary<string, string[]> RoutingDescriptions = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["instanceId"] = new[]
        {
            "Optional explicit Navisworks host instance_id from list_navisworks_hosts.",
            "Optional explicit Navisworks host instance_id.",
            "Optional explicit Navisworks host instance_id from list_navisworks_hosts. Recommended when several versions/instances are running.",
        },
        ["navisworksVersion"] = new[]
        {
            "Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.",
            "Optional Navisworks version.",
            "Optional Navisworks version, when exactly one host of that version is running.",
            "Optional Navisworks version, for example 2027.",
        },
    };

    private static readonly Regex DefaultSentence = new(
        @"^(?<rest>.*\S)\s+Default is (?<value>[^.\s/]+)(?:/dry-run)?\.$",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    internal static void Apply(IEnumerable<McpServerTool> tools)
    {
        foreach (var tool in tools)
        {
            var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText());
            if (schema is not JsonObject root || root["properties"] is not JsonObject properties)
                continue;

            // Only parameter annotations change; nested schemas and validation keywords stay intact.
            foreach (var (key, node) in properties)
            {
                if (node is not JsonObject property)
                    continue;

                var hasDefault = property.TryGetPropertyValue("default", out var schemaDefault);
                if (hasDefault && (schemaDefault is null ||
                    (schemaDefault.GetValueKind() == JsonValueKind.String && schemaDefault.GetValue<string>() == "")))
                {
                    property.Remove("default");
                    schemaDefault = null;
                }

                if (property["description"] is not JsonValue description ||
                    !description.TryGetValue<string>(out var text))
                    continue;

                if (RoutingDescriptions.TryGetValue(key, out var texts) && texts.Contains(text, StringComparer.Ordinal))
                {
                    property.Remove("description");
                    continue;
                }

                if (schemaDefault is null)
                    continue;

                var match = DefaultSentence.Match(text);
                if (!match.Success)
                    continue;

                var literal = schemaDefault.GetValueKind() == JsonValueKind.String
                    ? schemaDefault.GetValue<string>()
                    : schemaDefault.ToJsonString();
                if (string.Equals(match.Groups["value"].Value, literal, StringComparison.OrdinalIgnoreCase))
                    property["description"] = match.Groups["rest"].Value;
            }

            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(schema);
        }
    }
}
