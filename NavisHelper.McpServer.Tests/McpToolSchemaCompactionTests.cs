using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// tools/list cost about 50k tokens per request. McpToolSchemaCompaction trims it at
// startup without changing what a tool accepts: the boilerplate host-selection
// descriptions go (the server instructions explain instanceId and
// navisworksVersion once), null/empty defaults disappear, and a trailing
// "Default is X." sentence goes when the schema already says default X.
// Everything else - names, types, required, other defaults, the rest of
// every description - must survive byte for byte.
public sealed class McpToolSchemaCompactionTests
{
    // Host-selection boilerplate, repeated on about a hundred tools. These exact
    // texts lose their description; any other text on these two parameters (for
    // example start_navisworks's version list) is tool-specific and stays.
    internal static readonly IReadOnlyDictionary<string, string[]> RoutingDescriptions = new Dictionary<string, string[]>
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

    private static bool IsRoutingDescription(string key, string text) =>
        RoutingDescriptions.TryGetValue(key, out var texts) && texts.Contains(text, StringComparer.Ordinal);

    private static List<McpServerTool> CreateTools() => McpReadOnlyMode.RegisteredToolMethods()
        .Select(method => method.IsStatic
            ? McpServerTool.Create(method)
            : McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked")))
        .ToList();

    private static JsonObject Schema(McpServerTool tool) =>
        JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!.AsObject();

    private static int Size(IEnumerable<McpServerTool> tools) =>
        tools.Sum(tool => JsonSerializer.Serialize(tool.ProtocolTool, McpJsonUtilities.DefaultOptions).Length);

    [Fact]
    public void CompactionShrinksTheSurfaceWithoutChangingWhatToolsAccept()
    {
        var original = CreateTools();
        var compacted = CreateTools();
        var before = Size(original);
        McpToolSchemaCompaction.Apply(compacted);
        var after = Size(compacted);

        Assert.True(after <= before * 0.93, $"tools/list shrank only from {before} to {after} characters");

        var problems = new List<string>();
        for (var index = 0; index < original.Count; index++)
        {
            var name = original[index].ProtocolTool.Name;
            Assert.Equal(name, compacted[index].ProtocolTool.Name);
            Assert.Equal(original[index].ProtocolTool.Description, compacted[index].ProtocolTool.Description);

            var oldSchema = Schema(original[index]);
            var newSchema = Schema(compacted[index]);
            Assert.True(JsonNode.DeepEquals(oldSchema["required"], newSchema["required"]), name + " required changed");
            var oldProperties = oldSchema["properties"]?.AsObject() ?? new JsonObject();
            var newProperties = newSchema["properties"]?.AsObject() ?? new JsonObject();
            Assert.Equal(oldProperties.Select(p => p.Key), newProperties.Select(p => p.Key));

            foreach (var (key, oldNode) in oldProperties)
            {
                var oldProperty = oldNode!.AsObject();
                var newProperty = newProperties[key]!.AsObject();
                var where = name + "." + key;
                foreach (var field in oldProperty.Select(p => p.Key).Where(f => f != "description" && f != "default"))
                    if (!JsonNode.DeepEquals(oldProperty[field], newProperty[field])) problems.Add(where + " changed " + field);

                var hadDefault = oldProperty.TryGetPropertyValue("default", out var oldDefault);
                var emptyDefault = hadDefault && (oldDefault == null || (oldDefault.GetValueKind() == JsonValueKind.String && oldDefault.GetValue<string>() == ""));
                if (emptyDefault && newProperty.ContainsKey("default")) problems.Add(where + " kept a null/empty default");
                if (hadDefault && !emptyDefault && !JsonNode.DeepEquals(oldDefault, newProperty["default"])) problems.Add(where + " changed its default");
                if (!hadDefault && newProperty.ContainsKey("default")) problems.Add(where + " gained a default");

                var oldText = oldProperty["description"]?.GetValue<string>() ?? "";
                var newText = newProperty["description"]?.GetValue<string>() ?? "";
                if (IsRoutingDescription(key, oldText))
                {
                    if (newProperty.ContainsKey("description")) problems.Add(where + " kept its host-selection description");
                    continue;
                }
                if (newText.Length == 0) { problems.Add(where + " lost its description"); continue; }
                if (!AllowedDescription(oldText, newText, emptyDefault ? null : oldDefault))
                    problems.Add(where + ": '" + oldText + "' -> '" + newText + "'");
            }
        }

        Assert.True(problems.Count == 0, problems.Count + " problems:\n" + string.Join("\n", problems.Take(40)));
    }

    // The new text equals the old one, or the old one with its final "Default is X." / "Default is X/dry-run." sentence removed where
    // X is exactly the schema default.
    private static bool AllowedDescription(string oldText, string newText, JsonNode schemaDefault)
    {
        if (newText == oldText) return true;
        var match = Regex.Match(oldText, @"^(?<rest>.*\S)\s+Default is (?<value>[^.\s/]+)(?:/dry-run)?\.$", RegexOptions.Singleline);
        if (!match.Success || schemaDefault == null) return false;
        var literal = schemaDefault.GetValueKind() == JsonValueKind.String ? schemaDefault.GetValue<string>() : schemaDefault.ToJsonString();
        return string.Equals(match.Groups["value"].Value, literal, StringComparison.OrdinalIgnoreCase)
            && newText == match.Groups["rest"].Value;
    }

    [Fact]
    public async Task ServedToolsAreCompacted()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpCatalogResourceTests.ConnectAsync("all", false, timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var host = JsonNode.Parse(tools.Single(tool => tool.Name == "host_status").ProtocolTool.InputSchema.GetRawText())!;
        var instanceId = host["properties"]!["instanceId"]!.AsObject();
        Assert.Equal("string", instanceId["type"]!.GetValue<string>());
        Assert.False(instanceId.ContainsKey("description"), "host_status.instanceId kept its description");

        // The rule fires across the surface, and the tool-specific version texts survive.
        var dropped = 0;
        foreach (var tool in tools)
        {
            var properties = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!["properties"]?.AsObject();
            foreach (var key in RoutingDescriptions.Keys)
                if (properties?[key] is JsonObject property && !property.ContainsKey("description"))
                    dropped++;
        }
        Assert.True(dropped >= 180, $"only {dropped} host-selection parameters lost their description");
        var start = JsonNode.Parse(tools.Single(tool => tool.Name == "start_navisworks").ProtocolTool.InputSchema.GetRawText())!;
        Assert.StartsWith("Optional Navisworks version: 2024, 2025, 2026, or 2027.",
            start["properties"]!["navisworksVersion"]!["description"]!.GetValue<string>(), StringComparison.Ordinal);
    }
}
