using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Resources;

[McpServerResourceType]
internal sealed class McpToolCatalogResources
{
    internal const string CatalogUri = "navishelper://catalog/tools";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IOptions<McpServerOptions> _options;
    private readonly McpReadOnlyMode _readOnly;

    public McpToolCatalogResources(IOptions<McpServerOptions> options, McpReadOnlyMode readOnly)
    {
        _options = options;
        _readOnly = readOnly;
    }

    [McpServerResource(Name = "navishelper_tool_catalog", UriTemplate = CatalogUri,
        MimeType = "application/json")]
    [Description("Active NavisHelper tools and profile groups, without input schemas. Honors tool profile and read-only mode. No host connection required.")]
    public string Catalog()
    {
        var tools = AvailableTools().Select(tool => new
        {
            name = tool.ProtocolTool.Name,
            description = tool.ProtocolTool.Description,
            groups = McpToolProfile.Sets.Where(group => group.Value.Contains(tool.ProtocolTool.Name))
                .Select(group => group.Key).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            schemaUri = CatalogUri + "/" + Uri.EscapeDataString(tool.ProtocolTool.Name)
        }).ToArray();
        return JsonSerializer.Serialize(new { schemaVersion = 1, readOnly = _readOnly.Enabled,
            toolCount = tools.Length, tools }, JsonOptions);
    }

    [McpServerResource(Name = "navishelper_tool_schema", UriTemplate = CatalogUri + "/{name}",
        MimeType = "application/json")]
    [Description("Exact protocol definition and input schema for one currently available NavisHelper tool. Hidden and unknown tools are refused.")]
    public string ToolSchema(string name)
    {
        var tool = AvailableTools().FirstOrDefault(candidate => candidate.ProtocolTool.Name == name);
        if (tool == null)
            throw new McpProtocolException("The tool is not available in this server profile.", McpErrorCode.ResourceNotFound);
        return JsonSerializer.Serialize(tool.ProtocolTool, JsonOptions);
    }

    private IEnumerable<McpServerTool> AvailableTools() =>
        (_options.Value.ToolCollection ?? throw new InvalidOperationException("MCP tools are not initialized."))
            .Where(tool => _readOnly.IsAllowed(tool.ProtocolTool.Name))
            .OrderBy(tool => tool.ProtocolTool.Name, StringComparer.Ordinal);
}
