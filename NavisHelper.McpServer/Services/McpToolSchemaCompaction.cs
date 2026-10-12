using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace NavisHelper.McpServer.Services;

internal static class McpToolSchemaCompaction
{
    private static readonly IReadOnlyDictionary<string, string> Rewrites = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Optional explicit Navisworks host instance_id from list_navisworks_hosts."] = "Host instance_id from list_navisworks_hosts.",
        ["Optional explicit Navisworks host instance_id."] = "Host instance_id from list_navisworks_hosts.",
        ["Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running."] =
            "Navisworks version, e.g. 2027, when one host of it runs.",
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
            foreach (var property in properties.Select(entry => entry.Value).OfType<JsonObject>())
            {
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

                if (Rewrites.TryGetValue(text, out var rewrite))
                {
                    property["description"] = rewrite;
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
