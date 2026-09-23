using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksModelTools : NavisworksToolBase
{
    public NavisworksModelTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Finds Navisworks items by property conditions. Supports whole-model or subtree/current-selection scope, shallowest-match pruning, count-only estimates, and a clarification preflight. Old calls remain whole_model + matchDepth=all. Use preflight=true before ambiguous natural-language searches; for repeated inherited names prefer a narrow scope plus matchDepth=first.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<FindItemsResponse> FindItems(
        [Description("Simple scalar query to search for. Prefer this for one lookup. With default settings this mirrors Item/Name/Contains in Navisworks Find Items.")] string query = "",
        [Description("Alias for query, matching the condition terminology used by Navisworks. Pass query or value, not both.")] string value = "",
        [Description("Simple-mode comparison: equals, not_equals, contains, starts_with, ends_with, wildcard, defined, not_defined. Ignored when searches is provided.")] string comparison = FindItemsComparisons.Contains,
        [Description("Simple-mode category display name. Ignored when searches is provided. Default is Item.")] string category = "Item",
        [Description("Simple-mode property display name. Ignored when searches is provided. Default is Name.")] string property = "Name",
        [Description("Optional advanced grouped search. Maximum per call: 1 search. Do not concatenate multiple partNN payloads. The search supports combine_operator all/any and multiple property conditions. Prefer display category/property names with data_type. category_internal/property_internal are fallback-only and can be wrong for custom properties.")] List<FindItemsSearch> searches = null,
        [Description("Maximum preview rows per logical match.")] int previewLimit = 10,
        [Description("Search scope: whole_model (legacy default), current_selection, under_handle, or under_named_node.")] string scope = "",
        [Description("Match handle whose item(s) are subtree roots for scope=under_handle. Runtime-only; never persist it in a scenario.")] string scopeHandle = "",
        [Description("Exact displayed container-node name for scope=under_named_node. Prefer scopeNodePath or scopeHandle on large models.")] string scopeNodeName = "",
        [Description("Exact model-tree path for scope=under_named_node. This is the fast deterministic named-scope option.")] string scopeNodePath = "",
        [Description("Match traversal: all (legacy default) or first. first returns the shallowest match on each branch and prunes its descendants.")] string matchDepth = "",
        [Description("Return counts, depthHistogram, and sample values without creating a match handle or preview array.")] bool countOnly = false,
        [Description("Return a clarification plan without executing the search. Recommended before ambiguous natural-language searches.")] bool preflight = false,
        [Description("Simple-mode string option. Default true.")] bool ignoreCase = true,
        [Description("Simple-mode string option. Default false.")] bool ignoreDiacritics = false,
        [Description("Simple-mode string option. Default false.")] bool ignoreCharWidth = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(query) && !string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(ErrorCodes.SchemaViolation + ": pass query or value, not both.");
        var effectiveQuery = string.IsNullOrWhiteSpace(value) ? query : value;
        var queryCount = 0;
        if (!string.IsNullOrWhiteSpace(effectiveQuery))
            queryCount++;

        var searchCount = searches == null ? 0 : searches.Count;
        var logicalSearchCount = searchCount > 0 ? searchCount : queryCount;
        if (logicalSearchCount > 1)
            throw new InvalidOperationException(ErrorCodes.SchemaViolation + ": find_items accepts exactly one query/search per call. Split part files into separate find_items calls; do not concatenate searches.");

        return _hostBridgeClient.FindItemsAsync(new FindItemsRequest
        {
            Query = effectiveQuery,
            Queries = new List<string>(),
            Comparison = comparison,
            Category = category,
            Property = property,
            Searches = searches ?? new List<FindItemsSearch>(),
            PreviewLimit = previewLimit,
            Scope = scope,
            ScopeHandle = scopeHandle,
            ScopeNodeName = scopeNodeName,
            ScopeNodePath = scopeNodePath,
            MatchDepth = matchDepth,
            CountOnly = countOnly,
            Preflight = preflight,
            IgnoreCase = ignoreCase,
            IgnoreDiacritics = ignoreDiacritics,
            IgnoreCharWidth = ignoreCharWidth,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Finds leaf model items whose axis-aligned bounding boxes intersect a global document-coordinate zone. Coordinates use the active Navisworks document units; this v1 tool does not transform local/grid coordinates. Read-only: it returns a match handle for select_items or visibility tools and never changes the selection.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<FindItemsByBboxResponse> FindItemsByBbox(
        [Description("Minimum global document coordinate of the zone (x, y, z). Required.")] SpatialPoint min,
        [Description("Maximum global document coordinate of the zone (x, y, z). Required.")] SpatialPoint max,
        [Description("Bounding-box match mode: intersects (default), contains (whole item inside zone), or center.")] string matchMode = SpatialSearchOptionsHelper.Intersects,
        [Description("Include hidden items. Default is true.")] bool includeHidden = true,
        [Description("Include container/aggregate nodes as well as leaf model items. Default is false.")] bool includeContainers = false,
        [Description("Optional case-insensitive source-file substring filter.")] string sourceFileContains = "",
        [Description("Maximum model items to scan. Default 100000, maximum 500000; response is marked traversalTruncated when reached.")] int maxScannedItems = SpatialSearchOptionsHelper.DefaultMaxScannedItems,
        [Description("Maximum matching items retained in the result and match handle. Default 5000, maximum 10000.")] int maxResults = SpatialSearchOptionsHelper.DefaultMaxResults,
        [Description("Maximum preview rows. Default 10, maximum 50.")] int previewLimit = SpatialSearchOptionsHelper.DefaultPreviewLimit,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.FindItemsByBboxAsync(new FindItemsByBboxRequest
        {
            Min = min,
            Max = max,
            MatchMode = matchMode,
            IncludeHidden = includeHidden,
            IncludeContainers = includeContainers,
            SourceFileContains = sourceFileContains,
            MaxScannedItems = maxScannedItems,
            MaxResults = maxResults,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Fast path for finding top-level/root Navisworks model items by displayed root name or Source File filename. Use this instead of find_items for long lists of appended .rvm/.dwg model file names. It returns the same match handles as find_items, so select_items, isolate_selected, and zoom_to_selection can be used afterward.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<FindItemsResponse> FindRootItemsByName(
        [Description("Root item or source file names to find. Large lists are allowed; this tool is optimized for top-level .rvm/.dwg model names.")] List<string> names,
        [Description("Name comparison: equals, contains, or wildcard. Use equals for exact filenames.")] string comparison = FindItemsComparisons.Equal,
        [Description("Maximum preview rows per logical match.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.FindRootItemsByNameAsync(new FindRootItemsByNameRequest
        {
            Names = names ?? new List<string>(),
            Comparison = comparison,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Lists top-level/root Navisworks model items and appended model file names visible near the root of the selection tree. Use this before searching when you need the available .rvm/.dwg names.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<ListRootItemsResponse> ListRootItems(
        [Description("Maximum number of root items to return. Default is 1000.")] int limit = 1000,
        [Description("Include alternate aliases used by find_root_items_by_name.")] bool includeAliases = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ListRootItemsAsync(new ListRootItemsRequest
        {
            Limit = limit,
            IncludeAliases = includeAliases,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Lists the immediate children of one Navisworks model item by parentMatchHandle, fast exact parentPath, parentName, or sourceFile. Use this for direct subitems of a level/group such as '/100000-XXX1-YY-01'. It does not full-scan the model tree; for deep unknown nodes first call find_items and pass parentMatchHandle.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<ListItemChildrenResponse> ListItemChildren(
        [Description("Opaque match handle from find_items/find_root_items_by_name/list_item_children resolving to exactly one parent item. Fastest and safest for deep nodes.")] string parentMatchHandle = "",
        [Description("Fast exact model item path. Slash-only one-segment paths like '/100000-XXX1-YY-01' check only model roots and direct root children; multi-segment paths are traversed segment by segment without full-scan. Requires comparison=equals.")] string parentPath = "",
        [Description("Root/top-level parent display name fallback when path is unknown. Does not scan all descendants; use parentMatchHandle for deep nodes.")] string parentName = "",
        [Description("Root/top-level parent source file fallback. Does not scan all descendants; use parentMatchHandle for deep nodes.")] string sourceFile = "",
        [Description("Comparison for parentName/sourceFile: equals, contains, or wildcard. parentPath only supports equals. Default is equals.")] string comparison = FindItemsComparisons.Equal,
        [Description("Include hidden direct children. Default is true.")] bool includeHidden = true,
        [Description("Maximum direct children to return. Default is 200, maximum is 2000.")] int limit = 200,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ListItemChildrenAsync(new ListItemChildrenRequest
        {
            ParentMatchHandle = parentMatchHandle,
            ParentPath = parentPath,
            ParentName = parentName,
            SourceFile = sourceFile,
            Comparison = comparison,
            IncludeHidden = includeHidden,
            Limit = limit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Returns a bounded read-only property preview for items referenced by match handles from find_items or find_root_items_by_name. Use categoryFilters to narrow large property sets. Defaults return up to 5 items per handle and 50 properties per item.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<ItemPropertiesByHandleResponse> ItemPropertiesByHandle(
        [Description("Opaque match handles returned by find_items or find_root_items_by_name.")] List<string> matchHandles,
        [Description("Maximum items to inspect per handle. Default is 5, maximum is 20.")] int itemLimit = 5,
        [Description("Maximum properties to return per item. Default is 50, maximum is 200.")] int propertyLimit = 50,
        [Description("Include Navisworks internal category/property names. Default is false.")] bool includeInternalNames = false,
        [Description("Optional category display or internal names to include, for example Item or Элемент. Empty means all categories until propertyLimit.")] List<string> categoryFilters = null,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ItemPropertiesByHandleAsync(new ItemPropertiesByHandleRequest
        {
            MatchHandles = matchHandles ?? new List<string>(),
            ItemLimit = itemLimit,
            PropertyLimit = propertyLimit,
            IncludeInternalNames = includeInternalNames,
            CategoryFilters = categoryFilters ?? new List<string>(),
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

}
