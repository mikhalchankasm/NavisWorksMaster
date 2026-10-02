using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NavisHelper.McpServer.Resources;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Real server processes must not compete with unit tests that exercise short deadlines.
[CollectionDefinition("MCP stdio", DisableParallelization = true)]
public sealed class McpStdioCollection { }

[Collection("MCP stdio")]
public sealed class McpCatalogResourceTests
{
    [Theory]
    [InlineData("all", false, 111)]
    [InlineData("core", false, 46)]
    [InlineData("all", true, 39)]
    [InlineData("core", true, -1)]
    public async Task CatalogMatchesAdvertisedToolsAndReturnsExactSchema(string profile, bool readOnly, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await ConnectAsync(profile, readOnly, timeout.Token);
        var resources = await client.ListResourcesAsync(cancellationToken: timeout.Token);
        Assert.Contains(resources, resource => resource.Uri == McpToolCatalogResources.CatalogUri);
        var templates = await client.ListResourceTemplatesAsync(cancellationToken: timeout.Token);
        Assert.Contains(templates, resource => resource.UriTemplate == McpToolCatalogResources.CatalogUri + "/{name}");

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var catalogResult = await client.ReadResourceAsync(McpToolCatalogResources.CatalogUri, cancellationToken: timeout.Token);
        var content = Assert.Single(catalogResult.Contents.OfType<TextResourceContents>());
        Assert.Equal("application/json", content.MimeType);
        using var catalog = JsonDocument.Parse(content.Text);
        Assert.Equal(readOnly, catalog.RootElement.GetProperty("readOnly").GetBoolean());
        Assert.Equal(tools.Count, catalog.RootElement.GetProperty("toolCount").GetInt32());
        if (count >= 0)
            Assert.Equal(count, tools.Count);
        var entries = catalog.RootElement.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal),
            entries.Select(entry => entry.GetProperty("name").GetString()));
        Assert.All(entries, entry =>
        {
            Assert.False(entry.TryGetProperty("inputSchema", out _));
            Assert.NotEmpty(entry.GetProperty("groups").EnumerateArray());
        });

        var host = tools.Single(tool => tool.Name == "host_status");
        var schemaResult = await client.ReadResourceAsync(McpToolCatalogResources.CatalogUri + "/host_status",
            cancellationToken: timeout.Token);
        var schema = JsonNode.Parse(Assert.Single(schemaResult.Contents.OfType<TextResourceContents>()).Text);
        Assert.Equal("host_status", schema["name"].GetValue<string>());
        Assert.True(JsonNode.DeepEquals(
            JsonSerializer.SerializeToNode(host.ProtocolTool, McpJsonUtilities.DefaultOptions), schema));
    }

    [Theory]
    [InlineData("core", false, "clash_list_tests")]
    [InlineData("all", true, "create_viewpoint")]
    [InlineData("all", false, "unknown_tool")]
    public async Task HiddenOrUnknownToolSchemasAreRefused(string profile, bool readOnly, string name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await ConnectAsync(profile, readOnly, timeout.Token);
        var error = await Assert.ThrowsAsync<McpProtocolException>(() => client.ReadResourceAsync(
            McpToolCatalogResources.CatalogUri + "/" + name, cancellationToken: timeout.Token).AsTask());
        Assert.Equal(McpErrorCode.ResourceNotFound, error.ErrorCode);
    }

    internal static Task<McpClient> ConnectAsync(string profile, bool readOnly, CancellationToken cancellationToken) =>
        McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "NavisHelper metadata test",
            Command = "dotnet",
            Arguments = new[] { typeof(McpToolCatalogResources).Assembly.Location, "--tools=" + profile },
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["NAVISHELPER_MCP_READ_ONLY"] = readOnly ? "1" : "0",
                ["NAVISHELPER_MCP_TOOLS"] = profile,
            },
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        }), cancellationToken: cancellationToken);
}
