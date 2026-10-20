using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// The clash_* family is the largest part of tools/list (31 tools, about 38 % of the
// catalogue). Its descriptions were rewritten for brevity; this budget keeps them
// from growing back. Only descriptions may change: names, types, required lists,
// defaults and enums are pinned by the catalogue and schema tests.
[Collection("MCP stdio")]
public sealed class ClashToolDescriptionBudgetTests
{
    // Served (compacted) bytes of every clash_* tool before the rewrite: 74979.
    private const int BudgetBytes = 63700;

    [Fact]
    public async Task ClashFamilyFitsItsDescriptionBudget()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpCatalogResourceTests.ConnectAsync("all", false, timeout.Token);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token))
            .Where(tool => tool.Name.StartsWith("clash_", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(31, tools.Count);
        var bytes = tools.Sum(tool => Encoding.UTF8.GetByteCount(
            JsonSerializer.Serialize(tool.ProtocolTool, McpJsonUtilities.DefaultOptions)));
        Assert.True(bytes <= BudgetBytes, $"clash_* tools take {bytes} bytes; the budget is {BudgetBytes}.");
    }

    // SHA-256 of every clash_* input schema with all "description" members removed,
    // tools ordered by name. A rewrite of descriptions must leave it unchanged.
    private const string SchemaShapeSha256 = "828D486927DCAA25B6954629FD81DCA7D381EF91B59539F2FB6521AA6A4ED1F1";

    [Fact]
    public async Task RewriteChangesDescriptionsOnly()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpCatalogResourceTests.ConnectAsync("all", false, timeout.Token);
        var shapes = new JsonObject();
        foreach (var tool in (await client.ListToolsAsync(cancellationToken: timeout.Token))
                     .Where(tool => tool.Name.StartsWith("clash_", StringComparison.Ordinal))
                     .OrderBy(tool => tool.Name, StringComparer.Ordinal))
        {
            var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!;
            StripDescriptions(schema);
            shapes[tool.Name] = schema;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shapes.ToJsonString())));
        Assert.True(SchemaShapeSha256 == hash, "shape hash " + hash);
    }

    private static void StripDescriptions(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("description");
            foreach (var (_, child) in obj.ToList())
                StripDescriptions(child);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                StripDescriptions(child);
        }
    }

    [Fact]
    public async Task EveryClashToolAndParameterKeepsADescription()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpCatalogResourceTests.ConnectAsync("all", false, timeout.Token);
        var missing = new List<string>();
        foreach (var tool in (await client.ListToolsAsync(cancellationToken: timeout.Token))
                     .Where(tool => tool.Name.StartsWith("clash_", StringComparison.Ordinal)))
        {
            if (string.IsNullOrWhiteSpace(tool.Description))
                missing.Add(tool.Name);
            var properties = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!["properties"]?.AsObject();
            foreach (var (name, node) in properties ?? new JsonObject())
            {
                // instanceId and navisworksVersion are explained once in the server instructions (#336).
                if (name is "instanceId" or "navisworksVersion")
                    continue;
                if (string.IsNullOrWhiteSpace(node?["description"]?.GetValue<string>()))
                    missing.Add(tool.Name + "." + name);
            }
        }

        Assert.True(missing.Count == 0, "no description: " + string.Join(", ", missing));
    }
}
