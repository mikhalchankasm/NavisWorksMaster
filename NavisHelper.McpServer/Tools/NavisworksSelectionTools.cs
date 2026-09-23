using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksSelectionTools : NavisworksToolBase
{
    public NavisworksSelectionTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Returns read-only status of the current Navisworks selection: selected item count and optional combined bounding box. Use before visibility or view operations to verify what is selected.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<SelectionStatusResponse> SelectionStatus(
        [Description("Include the combined selection bounding box. Default is true.")] bool includeBoundingBox = true,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectionStatusAsync(new SelectionStatusRequest
        {
            IncludeBoundingBox = includeBoundingBox,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Returns display names for the current Navisworks selection in copy-ready order. Use this when the user asks to copy, list, export, or summarize selected object names. Optional path/source fields help disambiguate repeated names.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<SelectionCopyNamesResponse> SelectionCopyNames(
        [Description("Maximum selected items to return. Default is 10000, maximum is 100000.")] int limit = 10000,
        [Description("Include full item paths. Default is false.")] bool includePaths = false,
        [Description("Include source file names when available. Default is false.")] bool includeSourceFiles = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectionCopyNamesAsync(new SelectionCopyNamesRequest
        {
            Limit = limit,
            IncludePaths = includePaths,
            IncludeSourceFiles = includeSourceFiles,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Returns a read-only preview of currently selected top-level Navisworks items: display name, class, path, source file, hidden state, child count, and optional per-item bounding boxes. It does not traverse descendants.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<SelectedItemsPreviewResponse> SelectedItemsPreview(
        [Description("Maximum selected items to return. Default is 20, maximum is 100.")] int limit = 20,
        [Description("Include per-item bounding boxes. Default is false because bounding boxes can be expensive on large selections.")] bool includeBoundingBoxes = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectedItemsPreviewAsync(new SelectedItemsPreviewRequest
        {
            Limit = limit,
            IncludeBoundingBoxes = includeBoundingBoxes,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Returns currently selected Navisworks items with their structured parent chain from model root to each selected item. Use when the user asks for selected objects, their owners, parents, hierarchy, or structure up to the top; the response is suitable for exporting to text or JSON.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<SelectedItemsAncestryResponse> SelectedItemsAncestry(
        [Description("Maximum selected items to return. Default is 20, maximum is 100.")] int limit = 20,
        [Description("Include bounding boxes for each chain node. Default is false because bounding boxes can be expensive on large selections.")] bool includeBoundingBoxes = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectedItemsAncestryAsync(new SelectedItemsAncestryRequest
        {
            Limit = limit,
            IncludeBoundingBoxes = includeBoundingBoxes,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Returns the full current Navisworks selection as either a merged parent tree or a flat list. It reads Application.ActiveDocument.CurrentSelection without changing selection, supports more than 100 selected items, and includes selected counts/truncation flags.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<SelectedItemsTreeResponse> SelectedItemsTree(
        [Description("Maximum selected items to return. Default is 10000, maximum is 100000. If selection is larger, response.truncated is true.")] int maxItems = 10000,
        [Description("Optional maximum path depth to return from model root. Omit for full chains.")] int? maxDepth = null,
        [Description("Response format: tree or flat. Default is tree.")] string format = "tree",
        [Description("Include bounding boxes for returned nodes/items. Default is false because bounding boxes can be expensive on large selections.")] bool includeBoundingBoxes = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectedItemsTreeAsync(new SelectedItemsTreeRequest
        {
            MaxItems = maxItems,
            MaxDepth = maxDepth,
            Format = format,
            IncludeBoundingBoxes = includeBoundingBoxes,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Selects previously matched Navisworks items by opaque match handles.")]
    [ToolCapabilities(ToolEffects.View, RequiresHost = true, RequiresDocument = true)]
    public Task<SelectItemsResponse> SelectItems(
        [Description("Opaque match handles returned by find_items.")] List<string> matchHandles,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectItemsAsync(new SelectItemsRequest
        {
            MatchHandles = matchHandles ?? new List<string>(),
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

}
