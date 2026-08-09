using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksTools : NavisworksToolBase
{
    public NavisworksTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Lists running Navisworks MCP host instances. Use instance_id from this tool when multiple Navisworks windows are open, or navisworks_version when exactly one host of that version is running.")]
    public ListNavisworksHostsResponse ListNavisworksHosts()
    {
        return _hostBridgeClient.ListNavisworksHosts();
    }

    [McpServerTool]
    [Description("Returns MCP diagnostics: JSONL log file path, discovery instances directory, and currently running Navisworks host records.")]
    public McpDiagnosticsResponse McpDiagnostics()
    {
        return _hostBridgeClient.GetDiagnostics();
    }

    [McpServerTool]
    [Description("Returns the last MCP JSONL call log lines. Use after failures or long runs to confirm which tools were invoked, their target Navisworks instance, elapsed time, status, and error code.")]
    public McpRecentCallsResponse McpRecentCalls(
        [Description("Number of recent JSONL log lines to return. Default is 50, maximum is 200.")] int lineCount = 50)
    {
        return _hostBridgeClient.GetRecentCalls(lineCount);
    }

    [McpServerTool]
    [Description("Returns the NavisHelper MCP error contract: stable error codes, meanings, retryability, and recommended client actions.")]
    public McpErrorContractResponse McpErrorContract()
    {
        return HostBridgeClient.GetErrorContract();
    }

    [McpServerTool]
    [Description("Runs a read-only MCP/Navisworks health check and returns a verdict instead of throwing on partial failures. Use after long runs, timeouts, or suspected host hangs.")]
    public async Task<McpHealthCheckResponse> McpHealthCheck(
        [Description("Maximum root model items to touch during the context check. Default is 10.")] int rootItemLimit = 10,
        [Description("Also include the current viewpoint check. Default is true.")] bool includeViewpointCheck = true,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        var target = CreateTarget(instanceId, navisworksVersion);
        var response = new McpHealthCheckResponse
        {
            McpServerVersion = HostBridgeClient.McpServerVersion,
            ProtocolVersion = ProtocolConstants.CurrentProtocolVersion,
            LogFilePath = _hostBridgeClient.GetDiagnostics().LogFilePath,
        };

        HostStatusResponse status = null;
        await RunHealthStep(response, "host_status", async () =>
        {
            status = await _hostBridgeClient.HostStatusAsync(new HostStatusRequest(), cancellationToken, target).ConfigureAwait(false);
            response.InstanceId = status.InstanceId;
            response.Pid = status.Pid;
            response.NavisworksVersion = status.NavisworksVersion;
            response.DocumentTitle = status.DocumentTitle;
            response.PluginVersion = status.PluginVersion;
            response.PluginAssemblyPath = status.PluginAssemblyPath;
            response.PluginAssemblyLastWriteUtc = status.PluginAssemblyLastWriteUtc;
            response.PluginAssemblyLength = status.PluginAssemblyLength;
            response.HostLogFilePath = status.HostLogFilePath;
            response.WorkingSetMb = status.WorkingSetMb;
            response.RootItemCount = status.RootItemCount;
            if (!status.HasActiveDocument)
                throw new InvalidOperationException(ErrorCodes.NoActiveDocument + ": No active document.");
        }).ConfigureAwait(false);

        await RunHealthStep(response, "active_model_context", async () =>
        {
            var context = await ActiveModelContext(
                rootItemLimit: rootItemLimit,
                includeRootAliases: false,
                includeSavedItemsSummary: true,
                instanceId: instanceId,
                navisworksVersion: navisworksVersion,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (context.HostStatus == null || !context.HostStatus.HasActiveDocument)
                throw new InvalidOperationException(ErrorCodes.NoActiveDocument + ": Active model context has no active document.");
            if (context.RootItems == null || context.RootItems.RootItemCount < 1)
                throw new InvalidOperationException(ErrorCodes.CommandFailed + ": Active model context returned no root items.");
        }).ConfigureAwait(false);

        if (includeViewpointCheck)
        {
            await RunHealthStep(response, "current_viewpoint_info", async () =>
            {
                var viewpoint = await _hostBridgeClient.CurrentViewpointInfoAsync(new CurrentViewpointInfoRequest(), cancellationToken, target).ConfigureAwait(false);
                if (!viewpoint.HasActiveView || !viewpoint.HasCurrentViewpoint)
                    throw new InvalidOperationException(ErrorCodes.NoActiveView + ": No active view/current viewpoint.");
            }).ConfigureAwait(false);
        }

        response.Ok = response.Checks.All(check => check.Ok);
        response.Verdict = response.Ok ? "healthy" : "degraded";
        if (!string.IsNullOrWhiteSpace(response.McpServerVersion) &&
            !string.IsNullOrWhiteSpace(response.PluginVersion) &&
            !AreCompatibleVersionStrings(response.McpServerVersion, response.PluginVersion))
        {
            response.Ok = false;
            response.Verdict = "degraded";
            response.RecommendedActions.Add("MCP server version (" + response.McpServerVersion + ") differs from NavisHelper plugin version (" + response.PluginVersion + "). Reinstall/update the bundle and MCP server from the same package.");
        }

        if (!response.Ok)
        {
            response.RecommendedActions.Add("Call mcp_recent_calls to inspect recent JSONL records and elapsed times.");
            response.RecommendedActions.Add("If host_status failed, call list_navisworks_hosts and retarget by instanceId or restart Navisworks.");
            response.RecommendedActions.Add("If only a model/view check failed, confirm that a model is open and Navisworks has an active view.");
        }

        return response;
    }

    internal static bool AreCompatibleVersionStrings(string left, string right)
    {
        if (Version.TryParse(left, out var leftVersion) && Version.TryParse(right, out var rightVersion))
        {
            return leftVersion.Major == rightVersion.Major &&
                   leftVersion.Minor == rightVersion.Minor &&
                   NormalizeVersionPart(leftVersion.Build) == NormalizeVersionPart(rightVersion.Build) &&
                   NormalizeVersionPart(leftVersion.Revision) == NormalizeVersionPart(rightVersion.Revision);
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static int NormalizeVersionPart(int value)
    {
        return value < 0 ? 0 : value;
    }

    [McpServerTool]
    [Description("Returns a compact read-only context package for the active Navisworks model: host status, root model filenames, saved viewpoint/selection set counts, and recommended MCP workflow. Call this before searching a large model or when the user gives .rvm/.dwg names.")]
    public async Task<ActiveModelContextResponse> ActiveModelContext(
        [Description("Maximum root model items to include. Default is 100; use 1000 when you need the full root filename list.")] int rootItemLimit = 100,
        [Description("Include alternate root item aliases used by find_root_items_by_name.")] bool includeRootAliases = false,
        [Description("Include saved viewpoint and selection set summary counts.")] bool includeSavedItemsSummary = true,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        var target = CreateTarget(instanceId, navisworksVersion);
        var status = await _hostBridgeClient.HostStatusAsync(new HostStatusRequest(), cancellationToken, target).ConfigureAwait(false);
        var rootItems = await _hostBridgeClient.ListRootItemsAsync(new ListRootItemsRequest
        {
            Limit = rootItemLimit,
            IncludeAliases = includeRootAliases,
        }, cancellationToken, target).ConfigureAwait(false);

        ListSavedViewpointsResponse savedViewpoints = null;
        ListSelectionSetsResponse selectionSets = null;
        if (includeSavedItemsSummary)
        {
            savedViewpoints = await _hostBridgeClient.ListSavedViewpointsAsync(new ListSavedViewpointsRequest
            {
                Limit = 20,
            }, cancellationToken, target).ConfigureAwait(false);
            selectionSets = await _hostBridgeClient.ListSelectionSetsAsync(new ListSelectionSetsRequest
            {
                Limit = 20,
            }, cancellationToken, target).ConfigureAwait(false);
        }

        return new ActiveModelContextResponse
        {
            HostStatus = status,
            RootItems = rootItems,
            SavedViewpointTotalItemCount = savedViewpoints?.TotalItemCount,
            SavedViewpointReturnedItemCount = savedViewpoints?.ReturnedItemCount,
            SelectionSetTotalItemCount = selectionSets?.TotalItemCount,
            SelectionSetReturnedItemCount = selectionSets?.ReturnedItemCount,
            SearchGuidance = new List<string>
            {
                "For top-level .rvm/.dwg model filenames, use find_root_items_by_name with comparison=equals instead of generic find_items.",
                "If the requested filename is not exact, call list_root_items with a larger limit and then retry find_root_items_by_name with exact names or comparison=contains.",
                "When the user asks for immediate children/direct subitems of a known model tree node, use list_item_children with parentPath instead of dumping the whole subtree.",
                "Use generic find_items only for property/category searches; use display category/property names plus data_type, exactly one query/search per call.",
            },
            RecommendedWorkflow = new List<string>
            {
                "Confirm target host with host_status or list_navisworks_hosts.",
                "Use active_model_context or list_root_items to inspect available root filenames.",
                "Use find_root_items_by_name to get match handles, then select_items, selected_items_preview, zoom_to_selection, or dry-run visibility tools.",
                "Use list_item_children when the next step needs direct children of a parent node; pass returned child paths to matrix tools or childrenMatchHandle to selection tools.",
                "Use selected_items_ancestry when the user asks for manually selected objects and their owners/parents up to the model root.",
                "Use selected_items_tree for large current selections when the user needs the full parent tree or a flat export beyond 100 selected items.",
                "For existing saved selections/views, call list_selection_sets or list_saved_viewpoints and pass exact paths into select_selection_set or activate_saved_viewpoint.",
                "For write-oriented tools, first call without apply; send apply=true only after checking preview counts and names.",
            },
        };
    }

    [McpServerTool]
    [Description("Finds Navisworks items by property conditions. Supports whole-model or subtree/current-selection scope, shallowest-match pruning, count-only estimates, and a clarification preflight. Old calls remain whole_model + matchDepth=all. Use preflight=true before ambiguous natural-language searches; for repeated inherited names prefer a narrow scope plus matchDepth=first.")]
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
    [Description("Returns current Navisworks MCP host status: active document, process id, memory use, model count, and indexed root item count.")]
    public Task<HostStatusResponse> HostStatus(
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.HostStatusAsync(new HostStatusRequest(), cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Returns host-side status for a recent request_id. Use after request_timeout, transport disconnect, or oversized-response suspicion to determine whether the Navisworks-side command eventually completed, failed, or is still running.")]
    public Task<LastOperationStatusResponse> LastOperationStatus(
        [Description("request_id from mcp_recent_calls or the MCP server error log.")] string requestId,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.LastOperationStatusAsync(new LastOperationStatusRequest
        {
            RequestId = requestId,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Returns read-only status of the current Navisworks selection: selected item count and optional combined bounding box. Use before visibility or view operations to verify what is selected.")]
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
    [Description("Synchronously streams item names from one small root .rvm/.dwg subtree to a CSV or JSONL file. Hard-limited to avoid long Navisworks UI hangs; for large roots use start_subtree_names_dump plus dump_subtree_names_status.")]
    public Task<DumpSubtreeNamesResponse> DumpSubtreeNames(
        [Description("Displayed root item name or root source filename, for example example-model.rvm. Exact match only.")] string rootName,
        [Description("Output CSV/JSONL file path on this machine.")] string outputPath,
        [Description("Output format: csv or jsonl. Default is csv.")] string format = "csv",
        [Description("Optional exact Source File value or filename. Use when rootName is empty or ambiguous.")] string sourceFile = "",
        [Description("Include full Navisworks item path in each row. Default is true. For large name/position lookup dumps, pass false for much better throughput.")] bool includePath = true,
        [Description("Include inherited Source File in each row. Default is false for speed.")] bool includeSourceFile = false,
        [Description("Include hidden items. Default is true. When false, hidden item rows are skipped but descendants are still traversed.")] bool includeHidden = true,
        [Description("Overwrite outputPath if it already exists. Default is false.")] bool overwrite = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.DumpSubtreeNamesAsync(new DumpSubtreeNamesRequest
        {
            RootName = rootName,
            SourceFile = sourceFile,
            OutputPath = outputPath,
            Format = format,
            IncludePath = includePath,
            IncludeSourceFile = includeSourceFile,
            IncludeHidden = includeHidden,
            Overwrite = overwrite,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Starts a chunked CSV/JSONL dump job for all item names under one root .rvm/.dwg subtree. This returns quickly with jobId and instanceId and writes to outputPath.partial while running. Poll/cancel using the same instanceId. On success it atomically replaces outputPath; on failed/cancelled it removes the partial file.")]
    public Task<DumpSubtreeNamesJobStatusResponse> StartSubtreeNamesDump(
        [Description("Displayed root item name or root source filename, for example example-model.rvm. Exact match only.")] string rootName,
        [Description("Final output CSV/JSONL file path on this machine. The running job writes to this path plus .partial first.")] string outputPath,
        [Description("Output format: csv or jsonl. Default is csv.")] string format = "csv",
        [Description("Optional exact Source File value or filename. Use when rootName is empty or ambiguous.")] string sourceFile = "",
        [Description("Include full Navisworks item path in each row. Default is true. For large name/position lookup dumps, pass false for much better throughput.")] bool includePath = true,
        [Description("Include inherited Source File in each row. Default is false for speed.")] bool includeSourceFile = false,
        [Description("Include hidden items. Default is true. When false, hidden items are skipped but descendants are still traversed.")] bool includeHidden = true,
        [Description("Overwrite outputPath if it already exists and overwrite any stale .partial file. Default is false.")] bool overwrite = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.StartSubtreeNamesDumpAsync(new DumpSubtreeNamesRequest
        {
            RootName = rootName,
            SourceFile = sourceFile,
            OutputPath = outputPath,
            Format = format,
            IncludePath = includePath,
            IncludeSourceFile = includeSourceFile,
            IncludeHidden = includeHidden,
            Overwrite = overwrite,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Advances and returns status for a subtree name dump job. Poll this until state is done/failed/cancelled using the same instanceId returned by start_subtree_names_dump. Each poll processes a bounded chunk on the Navisworks UI thread; keep maxElapsedMs near the 500 ms default when a user is actively working.")]
    public Task<DumpSubtreeNamesJobStatusResponse> DumpSubtreeNamesStatus(
        [Description("Job id returned by start_subtree_names_dump.")] string jobId,
        [Description("Maximum items to process in this poll. Default 1000, maximum 10000.")] int maxItemsPerPoll = 1000,
        [Description("Maximum host-side processing time for this poll in milliseconds. Default 500, maximum 3000.")] int maxElapsedMs = 500,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.DumpSubtreeNamesStatusAsync(new DumpSubtreeNamesStatusRequest
        {
            JobId = jobId,
            MaxItemsPerPoll = maxItemsPerPoll,
            MaxElapsedMs = maxElapsedMs,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Cancels a running subtree name dump job on the same instanceId returned by start_subtree_names_dump, closes its writer, clears queued ModelItem references, and removes its .partial file.")]
    public Task<DumpSubtreeNamesJobStatusResponse> CancelSubtreeNamesDump(
        [Description("Job id returned by start_subtree_names_dump.")] string jobId,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.CancelSubtreeNamesDumpAsync(new CancelSubtreeNamesDumpRequest
        {
            JobId = jobId,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Returns a read-only preview of currently selected top-level Navisworks items: display name, class, path, source file, hidden state, child count, and optional per-item bounding boxes. It does not traverse descendants.")]
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
    [Description("Returns a bounded read-only property preview for items referenced by match handles from find_items or find_root_items_by_name. Use categoryFilters to narrow large property sets. Defaults return up to 5 items per handle and 50 properties per item.")]
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

    [McpServerTool]
    [Description("Returns read-only information about the current Navisworks viewpoint, including position, rotation, and common viewpoint properties when available.")]
    public Task<CurrentViewpointInfoResponse> CurrentViewpointInfo(
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.CurrentViewpointInfoAsync(new CurrentViewpointInfoRequest(), cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Lists saved viewpoints and folders in the active Navisworks document without changing the current view. Returns name, path, type, depth, and child count.")]
    public Task<ListSavedViewpointsResponse> ListSavedViewpoints(
        [Description("Maximum saved viewpoint/folder records to return. Default is 200, maximum is 1000.")] int limit = 200,
        [Description("Include duplicate-safe itemId values for later saved_viewpoints_manage/reorder calls. Default is true.")] bool includeItemIds = true,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ListSavedViewpointsAsync(new ListSavedViewpointsRequest
        {
            Limit = limit,
            IncludeItemIds = includeItemIds,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Exports the full Saved Viewpoints tree to CSV, JSON, or Markdown on the Navisworks host machine. Use before bulk rename/reorder work so duplicate names can be reviewed with current-tree itemId, path, parentPath, and index.")]
    public Task<SavedViewpointsExportResponse> SavedViewpointsExport(
        [Description("Output file path on the Navisworks host machine. Extensions .csv, .json, and .md are understood when format is empty.")] string outputPath,
        [Description("Export format: csv, json, or md. Default is inferred from outputPath extension, otherwise csv.")] string format = "csv",
        [Description("Include duplicate-safe itemId values. Default is true.")] bool includeItemIds = true,
        [Description("Overwrite outputPath if it already exists. Default is false.")] bool overwrite = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SavedViewpointsExportAsync(new SavedViewpointsExportRequest
        {
            OutputPath = outputPath,
            Format = format,
            IncludeItemIds = includeItemIds,
            Overwrite = overwrite,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Imports standard Navisworks Saved Viewpoints XML by parsing view/viewfolder nodes and creating folders/viewpoints in the active document. Defaults to dry-run. Supports camera/folder import and simple rlellipse/rlline redlines; unsupported XML details such as other redline types, clip planes, hide/material overrides are reported as warnings.")]
    public Task<SavedViewpointsImportResponse> SavedViewpointsImport(
        [Description("Path to a Navisworks Saved Viewpoints XML file exported from the Saved Viewpoints palette.")] string inputPath,
        [Description("Target folder path under Saved Viewpoints. Missing folders are created only when apply=true. Empty means root.")] string targetFolderPath = "",
        [Description("Preserve viewfolder nesting from the XML below the target folder. Default is true.")] bool preserveXmlFolders = true,
        [Description("Maximum planned/imported items to include in the response. Default is 200, maximum is 1000.")] int previewLimit = 200,
        [Description("False previews the import, true creates folders and viewpoints. Default is false/dry-run.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SavedViewpointsImportAsync(new SavedViewpointsImportRequest
        {
            InputPath = inputPath,
            TargetFolderPath = targetFolderPath,
            PreserveXmlFolders = preserveXmlFolders,
            PreviewLimit = previewLimit,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Creates/deletes/renames/moves Saved Viewpoints folders or viewpoints. delete_many removes up to 5000 explicitly listed viewpoints in one atomic plan. Defaults to dry-run; pass apply=true only after reviewing list_saved_viewpoints/export output. Supports duplicate names via current-tree itemId or occurrence.")]
    public Task<SavedViewpointsManageResponse> SavedViewpointsManage(
        [Description("Operation: create_folder, delete_folder, delete, delete_many, rename, or move. delete targets one viewpoint; delete_many uses items.")] string operation,
        [Description("Saved viewpoint/folder path or unique name. For create_folder this can be the full new folder path when name is empty.")] string pathOrName = "",
        [Description("Current-tree itemId from list_saved_viewpoints or saved_viewpoints_export. Prefer this when names/paths are duplicated; refresh it after any tree change.")] string itemId = "",
        [Description("1-based occurrence to select when pathOrName matches duplicates. Prefer itemId when available.")] int occurrence = 0,
        [Description("Folder name for create_folder.")] string name = "",
        [Description("New name for rename.")] string newName = "",
        [Description("Target folder path for create_folder parent or move destination. Missing folders are created only when apply=true. Empty means Saved Viewpoints root.")] string targetFolderPath = "",
        [Description("Targets for delete_many. Each item accepts pathOrName or current-tree itemId plus optional 1-based occurrence. The whole list is resolved before any deletion.")] List<SavedViewpointsManageTarget> items = null,
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("For delete_folder, allow deleting folders that contain children. Default is false.")] bool allowDeleteNonEmptyFolder = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SavedViewpointsManageAsync(new SavedViewpointsManageRequest
        {
            Operation = operation,
            PathOrName = pathOrName,
            ItemId = itemId,
            Occurrence = occurrence > 0 ? (int?)occurrence : null,
            Name = name,
            NewName = newName,
            TargetFolderPath = targetFolderPath,
            Items = items ?? new List<SavedViewpointsManageTarget>(),
            Apply = apply,
            AllowDeleteNonEmptyFolder = allowDeleteNonEmptyFolder,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Naturally sorts Saved Viewpoints folders/viewpoints so names containing numbers sort numerically (1, 2, 11). Defaults to dry-run. Can sort one folder or the full tree recursively.")]
    public Task<SavedViewpointsReorderResponse> SavedViewpointsReorder(
        [Description("Folder path to sort. Empty means the Saved Viewpoints root. Use itemId for duplicate folder names.")] string folderPath = "",
        [Description("Current-tree folder itemId from list_saved_viewpoints or saved_viewpoints_export. Overrides folderPath when provided; refresh it after any tree change.")] string itemId = "",
        [Description("Sort nested folders recursively. Default is true.")] bool recursive = true,
        [Description("Keep folders before viewpoints while sorting each group naturally. Default is true.")] bool foldersFirst = true,
        [Description("False previews the reorder, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SavedViewpointsReorderAsync(new SavedViewpointsReorderRequest
        {
            FolderPath = folderPath,
            ItemId = itemId,
            Recursive = recursive,
            FoldersFirst = foldersFirst,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Lists selection/search sets and folders in the active Navisworks document without changing selection. Supports offset paging and path/name filtering. Returns duplicate-safe itemId, path, parentPath, type, index, explicit/static count, and dynamic-search flags.")]
    public Task<ListSelectionSetsResponse> ListSelectionSets(
        [Description("Maximum selection set/folder records to return. Default is 200, maximum is 1000.")] int limit = 200,
        [Description("Zero-based item offset for paging through large Selection Sets trees. Default is 0.")] int offset = 0,
        [Description("Include duplicate-safe itemId values for manage/reorder tools. Default is true.")] bool includeItemIds = true,
        [Description("Optional path prefix filter, for example MCP_24_RVM_SearchSets or Parent/Child. Returns that folder and descendants.")] string pathPrefix = "",
        [Description("Optional case-insensitive substring filter applied to item names. Useful for mojibake cleanup or duplicate lookup.")] string nameContains = "",
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ListSelectionSetsAsync(new ListSelectionSetsRequest
        {
            Limit = limit,
            Offset = offset,
            IncludeItemIds = includeItemIds,
            PathPrefix = pathPrefix,
            NameContains = nameContains,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Selects an existing Navisworks Selection Set/Search Set by itemId, exact path, or unique name from list_selection_sets. Folder dry-runs return metadata without expanding child sets by default; folder apply requires allowFolderExpansion=true because large folders can be slow.")]
    public Task<SelectSelectionSetResponse> SelectSelectionSet(
        [Description("Exact selection set/folder path returned by list_selection_sets, or a unique selection set/folder name. Paths use '/' between folders. Optional when itemId is provided.")] string pathOrName = "",
        [Description("Current-tree itemId from list_selection_sets. Prefer this when names/paths are duplicated or contain mojibake.")] string itemId = "",
        [Description("1-based occurrence to select when pathOrName matches duplicates. Prefer itemId when available.")] int occurrence = 0,
        [Description("Allow expanding a folder into all child Selection Sets/Search Sets for count/apply. Default is false to avoid long-running folder selections.")] bool allowFolderExpansion = false,
        [Description("False previews the selection, true applies it to Navisworks current selection. Default is false/dry-run.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectSelectionSetAsync(new SelectSelectionSetRequest
        {
            PathOrName = pathOrName,
            ItemId = itemId,
            Occurrence = occurrence > 0 ? (int?)occurrence : null,
            AllowFolderExpansion = allowFolderExpansion,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Creates a dynamic Navisworks Search Set and optionally saves it inside a Selection Sets folder. Defaults to dry-run. Conditions use the same schema as find_items; persistable operators are equals, contains, wildcard, and defined. Each condition supports logicalOperator=and|or, negate, ignoreCase (default true), ignoreDiacritics, and ignoreCharWidth. Navisworks has no parentheses and AND binds more strongly than OR; express (A OR B) AND D as (A AND D) OR (B AND D) by repeating D in each branch.")]
    public Task<CreateSearchSetResponse> CreateSearchSet(
        [Description("Search set name.")] string name,
        [Description("Search conditions to persist. logicalOperator connects this condition to the previous one: and (default) or or. negate, ignoreCase=true, ignoreDiacritics, and ignoreCharWidth map to native Navisworks condition flags.")] List<FindItemsCondition> conditions,
        [Description("Optional folder path under Selection Sets, for example Packages/MEP. Missing folders are created when apply=true.")] string folderPath = "",
        [Description("Condition combine operator. Only all/AND is currently supported for persisted native Search Sets.")] string combineOperator = FindItemsCombineOperators.All,
        [Description("Replace an existing selection/search set with the same name in the target folder. Does not overwrite folders. Default is false.")] bool overwrite = false,
        [Description("After applying, also select the items currently matching the search. Default is false.")] bool selectAfterCreate = false,
        [Description("False previews the action, true creates folders and the dynamic Search Set. Default is false/dry-run.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.CreateSearchSetAsync(new CreateSearchSetRequest
        {
            Name = name,
            FolderPath = folderPath,
            CombineOperator = combineOperator,
            Conditions = conditions ?? new List<FindItemsCondition>(),
            Overwrite = overwrite,
            SelectAfterCreate = selectAfterCreate,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Creates/deletes/renames/moves Selection Sets folders and static/dynamic selection/search sets. Defaults to dry-run; pass apply=true only after reviewing list_selection_sets output. Supports duplicate names via current-tree itemId or occurrence.")]
    public Task<SelectionSetsManageResponse> SelectionSetsManage(
        [Description("Operation: create_folder, delete_folder, delete_set, delete, rename, or move. delete_set works for both static Selection Sets and dynamic Search Sets.")] string operation,
        [Description("Selection set/folder path or unique name. For create_folder this can be the full new folder path when name is empty.")] string pathOrName = "",
        [Description("Current-tree itemId from list_selection_sets. Prefer this when names/paths are duplicated; refresh it after any tree change.")] string itemId = "",
        [Description("1-based occurrence to select when pathOrName matches duplicates. Prefer itemId when available.")] int occurrence = 0,
        [Description("Folder name for create_folder.")] string name = "",
        [Description("New name for rename.")] string newName = "",
        [Description("Target folder path for create_folder parent or move destination. Missing folders are created only when apply=true. Empty means Selection Sets root.")] string targetFolderPath = "",
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("For delete_folder/delete, allow deleting folders that contain children. Default is false.")] bool allowDeleteNonEmptyFolder = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectionSetsManageAsync(new SelectionSetsManageRequest
        {
            Operation = operation,
            PathOrName = pathOrName,
            ItemId = itemId,
            Occurrence = occurrence > 0 ? (int?)occurrence : null,
            Name = name,
            NewName = newName,
            TargetFolderPath = targetFolderPath,
            Apply = apply,
            AllowDeleteNonEmptyFolder = allowDeleteNonEmptyFolder,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Naturally sorts Selection Sets folders and sets so names containing numbers sort numerically (1, 2, 11). Defaults to dry-run. Can sort one folder or the full tree recursively.")]
    public Task<SelectionSetsReorderResponse> SelectionSetsReorder(
        [Description("Folder path to sort. Empty means the Selection Sets root. Use itemId for duplicate folder names.")] string folderPath = "",
        [Description("Current-tree folder itemId from list_selection_sets. Overrides folderPath when provided; refresh it after any tree change.")] string itemId = "",
        [Description("Sort nested folders recursively. Default is true.")] bool recursive = true,
        [Description("Keep folders before sets while sorting each group naturally. Default is true.")] bool foldersFirst = true,
        [Description("False previews the reorder, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectionSetsReorderAsync(new SelectionSetsReorderRequest
        {
            FolderPath = folderPath,
            ItemId = itemId,
            Recursive = recursive,
            FoldersFirst = foldersFirst,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Activates an existing saved viewpoint by exact path or unique name from list_saved_viewpoints. Defaults to dry-run and returns the resolved path without changing the view unless apply=true.")]
    public Task<ActivateSavedViewpointResponse> ActivateSavedViewpoint(
        [Description("Exact saved viewpoint path returned by list_saved_viewpoints, or a unique saved viewpoint name. Paths use '/' between folders.")] string pathOrName,
        [Description("False previews the viewpoint activation, true applies it to the current Navisworks view. Default is false/dry-run.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ActivateSavedViewpointAsync(new ActivateSavedViewpointRequest
        {
            PathOrName = pathOrName,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Selects previously matched Navisworks items by opaque match handles.")]
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

    [McpServerTool]
    [Description("Hides every item except the current Navisworks selection. Dry-run returns affected root/source-file scope summaries before applying.")]
    public Task<HideUnselectedResponse> HideUnselected(
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Maximum affected item preview rows to return. Default is 10, maximum is 50.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.HideUnselectedAsync(new HideUnselectedRequest
        {
            Apply = apply,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Hides the current Navisworks selection. Dry-run returns affected root/source-file scope summaries before applying.")]
    public Task<HideSelectedResponse> HideSelected(
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Maximum affected item preview rows to return. Default is 10, maximum is 50.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.HideSelectedAsync(new HideSelectedRequest
        {
            Apply = apply,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Shows the current Navisworks selection if it is hidden. Dry-run returns affected root/source-file scope summaries before applying.")]
    public Task<UnhideSelectedResponse> UnhideSelected(
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Maximum affected item preview rows to return. Default is 10, maximum is 50.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.UnhideSelectedAsync(new UnhideSelectedRequest
        {
            Apply = apply,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Makes the current Navisworks selection actually visible by unhiding selected items and any hidden ancestors needed for visibility. Dry-run returns affected root/source-file scope summaries before applying.")]
    public Task<RevealSelectedResponse> RevealSelected(
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Maximum affected item preview rows to return. Default is 10, maximum is 50.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.RevealSelectedAsync(new RevealSelectedRequest
        {
            Apply = apply,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Shows all hidden items and then hides everything except the current Navisworks selection. Dry-run returns root/source-file scope summaries for the re-hide portion; review previouslyHiddenItemCount separately before applying.")]
    public Task<IsolateSelectedResponse> IsolateSelected(
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Maximum affected item preview rows to return. Default is 10, maximum is 50.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.IsolateSelectedAsync(new IsolateSelectedRequest
        {
            Apply = apply,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Shows all currently hidden Navisworks items. Dry-run returns affected root/source-file scope summaries before applying.")]
    public Task<ShowAllResponse> ShowAll(
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Maximum affected item preview rows to return. Default is 10, maximum is 50.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ShowAllAsync(new ShowAllRequest
        {
            Apply = apply,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Creates a static Navisworks Selection Set from the current selection or from find_items/find_root_items_by_name match handles, optionally inside a Selection Sets folder. This stores concrete model items, not a dynamic search rule. Defaults to dry-run.")]
    public Task<CreateSelectionSetResponse> CreateSelectionSet(
        [Description("Selection set name.")] string name,
        [Description("Optional opaque match handles returned by find_items/find_root_items_by_name/list_item_children. When provided, the static Selection Set is built from these matched items instead of the current selection.")] List<string> matchHandles = null,
        [Description("Optional folder path under Selection Sets, for example Packages/MEP. Missing folders are created when apply=true.")] string folderPath = "",
        [Description("Replace an existing selection/search set with the same name in the target folder. Does not overwrite folders. Default is false.")] bool overwrite = false,
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.CreateSelectionSetAsync(new CreateSelectionSetRequest
        {
            Name = name,
            MatchHandles = matchHandles ?? new List<string>(),
            FolderPath = folderPath,
            Overwrite = overwrite,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Saves the current Navisworks view as a saved viewpoint, optionally inside a folder path.")]
    public Task<CreateViewpointResponse> CreateViewpoint(
        [Description("Saved viewpoint name.")] string name,
        [Description("Optional folder path under Saved Viewpoints, for example Clash/Zone A. Missing folders are created when apply=true.")] string folderPath = "",
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.CreateViewpointAsync(new CreateViewpointRequest
        {
            Name = name,
            FolderPath = folderPath,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Saves the active Navisworks document to its current path. This writes model changes immediately and runs on the Navisworks UI thread.")]
    public Task<SaveDocumentResponse> SaveDocument(
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SaveDocumentAsync(new SaveDocumentRequest(), cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Saves the active Navisworks document to a specified .nwd or .nwf path. Existing files are protected unless overwrite=true. Use .nwd to produce a self-contained deliverable with geometry.")]
    public Task<SaveDocumentResponse> SaveDocumentAs(
        [Description("Absolute target path with a .nwd or .nwf extension.")] string path,
        [Description("Allow replacing an existing target file. Default is false.")] bool overwrite = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SaveDocumentAsAsync(new SaveDocumentAsRequest
        {
            Path = path,
            Overwrite = overwrite,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Builds configurable saved-viewpoint steps for every non-empty Search Set or Selection Set below a required folder prefix. Each overview, markup, or sectionBox step has its own label, clustering strategy, and optional persistent markup including arrow callouts. Defaults to dry-run.")]
    public Task<SelectionSetsBuildViewpointsResponse> SelectionSetsBuildViewpoints(
        [Description("Required Selection Sets folder prefix to process recursively. There is no domain-specific default.")] string folderPrefix,
        [Description("One or more configurable steps. step is overview, markup, or sectionBox; label is substituted into {step}. Each step supports whenItemCountMin/whenItemCountMax and clusterBy=none|distance|count|grid; clusterCount requests an exact count.")] List<SelectionSetViewpointStep> steps,
        [Description("Viewpoint base-name template containing both {set} and {step}. Default is '{set} - {step}'. Cluster suffixes are appended automatically.")] string nameTemplate = "{set} - {step}",
        [Description("Skip a whole step when any target viewpoint name already exists. Default is true.")] bool skipExisting = true,
        [Description("Maximum concrete sets to process in one call, from 1 to 1000. Default is 500.")] int maxSetCount = 500,
        [Description("Response detail: summary omits per-cluster preview item names; full includes cluster previews. Default is summary.")] string verbosity = "summary",
        [Description("False previews sets, steps, clusters, and names. True creates viewpoints. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectionSetsBuildViewpointsAsync(new SelectionSetsBuildViewpointsRequest
        {
            FolderPrefix = folderPrefix,
            NameTemplate = nameTemplate,
            Steps = steps ?? new List<SelectionSetViewpointStep>(),
            SkipExisting = skipExisting,
            MaxSetCount = maxSetCount,
            Verbosity = verbosity,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Deprecated compatibility alias for selection_sets_build_viewpoints. Preserves the legacy MTR defaults and fixed markup/sectionBox pair. New callers should use the neutral tool.")]
    public Task<BuildMtrViewpointsResponse> BuildMtrViewpoints(
        [Description("Selection Sets folder prefix to process recursively. Default is MTR.")] string folderPrefix = "MTR",
        [Description("Create top-view markup points named '<set name> — план'. Default is true.")] bool createPlan = true,
        [Description("Create ISO section-box points named '<set name> — бокс'. Default is true.")] bool createSectionBox = true,
        [Description("Maximum gap between item bounding boxes, in millimeters, for proximity clusters. Default is 10000.")] double clusterMaxDistanceMm = 10000,
        [Description("Extra frame around each markup plan as a fraction of its cluster size. Default is 0.10.")] double fitMarginFactor = 0.10,
        [Description("Optional RGB markup color with three values from 0 to 1. Default is [1, 0, 0].")] List<double> ellipseColor = null,
        [Description("Markup line thickness from 1 to 20. Default is 3.")] int thickness = 3,
        [Description("Extra projected frame size as a fraction of each group box, from 0 to 5. Default is 0.20.")] double paddingFactor = 0.20,
        [Description("Persistent markup shape: rectangle, target, arrow, or hatch. Default is rectangle.")] string markStyle = "rectangle",
        [Description("Add one arrow callout built from supported RedlineLine primitives to every mark, including section-box viewpoints. Default is false.")] bool arrowCallout = false,
        [Description("Arrow length in millimeters. 0 selects 8% of the final camera HeightField, clamped to 5-15%. Default is 0.")] double arrowLengthMm = 0,
        [Description("Draw a crosshair over target ellipses. Default is false.")] bool targetCrosshair = false,
        [Description("Hatch angle in degrees when markStyle=hatch. Default is 45.")] double hatchAngleDeg = 45,
        [Description("Hatch line spacing in millimeters when markStyle=hatch. Default is 500.")] double? hatchSpacingMm = null,
        [Description("Deprecated alias for hatchSpacingMm.")] int? hatchSpacingPx = null,
        [Description("Hatch line thickness from 1 to 20. Defaults to thickness (3). ")] int hatchThickness = 3,
        [Description("Minimum full markup size in millimeters. Default is 500.")] double? minMarkSizeMm = null,
        [Description("Horizontal bbox size in millimeters at which an item receives its own markup frame. Default is 1500.")] double markSoloMinSizeMm = 1500,
        [Description("Maximum horizontal bbox gap in millimeters for merging small items into one markup frame. Default is 1000.")] double markMergeGapMm = 1000,
        [Description("Extra section-box context around every cluster, in millimeters. Default is 1000.")] double boxOffsetMm = 1000,
        [Description("Skip a plan or box when any of its target viewpoint names already exists, avoiding partial duplicate cluster output. Default is true.")] bool skipExisting = true,
        [Description("Maximum concrete sets to process in one call, from 1 to 1000. Default is 500.")] int maxSetCount = 500,
        [Description("Maximum clusters retained per set, from 1 to 100. Default is 10. When capped, the largest clusters are marked with arrows.")] int maxClustersPerSet = 10,
        [Description("Skip distance clustering when a set has more than this many items, from 1 to 10000. Default is 500.")] int maxItemsForDistanceClustering = 500,
        [Description("False previews sets, item counts, clusters, and naming without changing Navisworks. True creates the viewpoints. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.BuildMtrViewpointsAsync(new BuildMtrViewpointsRequest
        {
            FolderPrefix = folderPrefix,
            CreatePlan = createPlan,
            CreateSectionBox = createSectionBox,
            ClusterMaxDistanceMm = clusterMaxDistanceMm,
            FitMarginFactor = fitMarginFactor,
            EllipseColor = ellipseColor,
            Thickness = thickness,
            PaddingFactor = paddingFactor,
            MarkStyle = markStyle,
            ArrowCallout = arrowCallout,
            ArrowLengthMm = arrowLengthMm,
            TargetCrosshair = targetCrosshair,
            HatchAngleDeg = hatchAngleDeg,
            HatchSpacingMm = hatchSpacingMm,
            HatchSpacingPx = hatchSpacingPx,
            HatchThickness = hatchThickness,
            MinMarkSizeMm = minMarkSizeMm,
            MarkSoloMinSizeMm = markSoloMinSizeMm,
            MarkMergeGapMm = markMergeGapMm,
            BoxOffsetMm = boxOffsetMm,
            SkipExisting = skipExisting,
            MaxSetCount = maxSetCount,
            MaxClustersPerSet = maxClustersPerSet,
            MaxItemsForDistanceClustering = maxItemsForDistanceClustering,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Creates one or more saved viewpoints with persistent rectangle, target, arrow, or hatch redline marks around hybrid groups in the current selection. Large items receive individual marks; nearby small items are merged. autoTopView=true creates a top view; false preserves the current orthographic or perspective camera and its section box. Defaults to rectangle and dry-run.")]
    public Task<MarkupSelectionResponse> MarkupSelection(
        [Description("Saved viewpoint name. Slashes are replaced with spaces so the name is safe inside a folder path.")] string name,
        [Description("Optional folder path under Saved Viewpoints, for example MTR/240103-ТХ. Missing folders are created only when apply=true.")] string folderPath = "",
        [Description("Selection source. Only current_selection is currently supported.")] string source = "current_selection",
        [Description("Switch to a strict orthographic top view and fit it to the combined selection box. Default is true.")] bool autoTopView = true,
        [Description("When auto_top_view=false, fit the current view to the combined selection box. Default is true.")] bool fitToSelection = true,
        [Description("Extra frame around the selection when fitting the markup plan, as a fraction of its size. 0 keeps a tight fit; 0.10 adds 10%. Default is 0.10. This does not affect section_box_viewpoint.")] double fitMarginFactor = 0.10,
        [Description("Optional RGB frame color with three values from 0 to 1. The legacy parameter name is retained for compatibility. Default is [1, 0, 0].")] List<double> ellipseColor = null,
        [Description("Rectangular frame line thickness from 1 to 20. Default is 3.")] int thickness = 3,
        [Description("Extra frame half-size as a fraction of each projected group box, from 0 to 5. Default is 0.20.")] double paddingFactor = 0.20,
        [Description("Minimum full markup size in millimeters. Default is 500.")] double? minMarkSizeMm = null,
        [Description("Deprecated alias for minMarkSizeMm.")] int? minRadiusPixels = null,
        [Description("Persistent markup shape: rectangle, target, arrow, or hatch. Default is rectangle.")] string markStyle = "rectangle",
        [Description("Add one arrow callout built from supported RedlineLine primitives to every mark without replacing markStyle. Default is false.")] bool arrowCallout = false,
        [Description("Arrow length in millimeters. 0 selects 8% of the final camera HeightField, clamped to 5-15%. Default is 0.")] double arrowLengthMm = 0,
        [Description("Draw a crosshair over target ellipses. Default is false.")] bool targetCrosshair = false,
        [Description("Hatch angle in degrees when markStyle=hatch. Default is 45.")] double hatchAngleDeg = 45,
        [Description("Hatch line spacing in millimeters when markStyle=hatch. Default is 500.")] double? hatchSpacingMm = null,
        [Description("Deprecated alias for hatchSpacingMm.")] int? hatchSpacingPx = null,
        [Description("Hatch line thickness from 1 to 20. Defaults to thickness.")] int hatchThickness = 3,
        [Description("Horizontal bbox size in millimeters at which an item receives its own frame. Default is 1500.")] double markSoloMinSizeMm = 1500,
        [Description("Maximum horizontal bbox gap in millimeters for merging small items into one frame. Default is 1000.")] double markMergeGapMm = 1000,
        [Description("Maximum gap between item bounding boxes, in millimeters, for proximity clusters. 0 keeps one viewpoint for the full selection; a positive value creates one viewpoint per connected cluster and appends (1), (2), and so on to name. Default is 0.")] double clusterMaxDistanceMm = 0,
        [Description("Clustering strategy: none, distance, count, or grid. Empty preserves legacy inference from clusterMaxDistanceMm.")] string clusterBy = "",
        [Description("Approximate items per cluster for clusterBy=count. Default is 100.")] int clusterTargetSize = 100,
        [Description("Exact number of clusters for clusterBy=count, from 1 to 100. When set, overrides clusterTargetSize.")] int? clusterCount = null,
        [Description("Plan-grid square size in millimeters for clusterBy=grid. Default is 50000.")] double clusterGridSizeMm = 50000,
        [Description("Optional cap from 1 to 100 for created clusters. Largest clusters are retained; droppedClusterCount/uncoveredItemCount report omitted coverage. The arrowCallout setting is never overridden.")] int? maxClusters = null,
        [Description("Distance-clustering safeguard from 1 to 10000. It does not limit count/grid modes. Default is 500.")] int? maxItemsForClustering = null,
        [Description("Deprecated alias for maxItemsForClustering.")] int? maxItemsForDistanceClustering = null,
        [Description("Update an existing viewpoint with the same generated name in place. Duplicate same-name viewpoints remain an error. Default is false.")] bool overwrite = false,
        [Description("False previews the planned viewpoint without changing the view or writing files. True applies the top view/redlines and saves the viewpoint. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.MarkupSelectionAsync(new MarkupSelectionRequest
        {
            Name = name,
            FolderPath = folderPath,
            Source = source,
            AutoTopView = autoTopView,
            FitToSelection = fitToSelection,
            FitMarginFactor = fitMarginFactor,
            EllipseColor = ellipseColor,
            Thickness = thickness,
            PaddingFactor = paddingFactor,
            MinMarkSizeMm = minMarkSizeMm,
            MinRadiusPixels = minRadiusPixels,
            MarkStyle = markStyle,
            ArrowCallout = arrowCallout,
            ArrowLengthMm = arrowLengthMm,
            TargetCrosshair = targetCrosshair,
            HatchAngleDeg = hatchAngleDeg,
            HatchSpacingMm = hatchSpacingMm,
            HatchSpacingPx = hatchSpacingPx,
            HatchThickness = hatchThickness,
            MarkSoloMinSizeMm = markSoloMinSizeMm,
            MarkMergeGapMm = markMergeGapMm,
            ClusterBy = clusterBy,
            ClusterMaxDistanceMm = clusterMaxDistanceMm,
            ClusterTargetSize = clusterTargetSize,
            ClusterCount = clusterCount,
            ClusterGridSizeMm = clusterGridSizeMm,
            MaxClusters = maxClusters,
            MaxItemsForClustering = maxItemsForClustering,
            MaxItemsForDistanceClustering = maxItemsForDistanceClustering,
            Overwrite = overwrite,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Plans or shows runtime-only overlay markers for hybrid groups in the current selection. The markers stay attached while the camera moves but are never stored in saved viewpoints or .nwd/.nwf files. Use markup_selection for persistent deliverables.")]
    public Task<LiveMarkersResponse> LiveMarkers(
        [Description("Overlay shape: rectangle, target, or arrow. Default is target.")] string style = "target",
        [Description("True shows or updates markers; false hides them when apply=true. Default is true.")] bool visible = true,
        [Description("Marker radius or padding in screen pixels, from 5 to 200. Default is 10.")] int markerRadiusPixels = 10,
        [Description("Horizontal bbox size in millimeters at which an item receives its own marker. Default is 1500.")] double markSoloMinSizeMm = 1500,
        [Description("Maximum horizontal bbox gap in millimeters for merging small items into one marker. Default is 1000.")] double markMergeGapMm = 1000,
        [Description("False previews marker groups without changing the active tool. True shows, updates, or hides the overlay. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.LiveMarkersAsync(new LiveMarkersRequest
        {
            Style = style,
            Visible = visible,
            MarkerRadiusPixels = markerRadiusPixels,
            MarkSoloMinSizeMm = markSoloMinSizeMm,
            MarkMergeGapMm = markMergeGapMm,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Creates one or more saved viewpoints with an enabled Navisworks section box around the current selection plus context. Optional persistent markup and line-based arrow callouts are calculated after the final ISO camera and clipping box. It never hides or isolates model items. Defaults to dry-run.")]
    public Task<SectionBoxViewpointResponse> SectionBoxViewpoint(
        [Description("Saved viewpoint base name. Slashes are replaced with spaces. When clustering creates multiple viewpoints, (1), (2), and so on are appended.")] string name,
        [Description("Optional folder path under Saved Viewpoints, for example MTR/240103-ТХ. Missing folders are created only when apply=true.")] string folderPath = "",
        [Description("Selection source. Only current_selection is currently supported.")] string source = "current_selection",
        [Description("Extra context around every cluster bounding box, in millimeters. Default is 1000.")] double boxOffsetMm = 1000,
        [Description("Optional markup style: rectangle, target, arrow, or hatch. Empty keeps the section-box viewpoint unmarked unless arrowCallout=true.")] string markStyle = "",
        [Description("Add one arrow callout built from supported RedlineLine primitives to every mark. Default is false.")] bool arrowCallout = false,
        [Description("Arrow length in millimeters. 0 selects 8% of the final camera HeightField, clamped to 5-15%. Default is 0.")] double arrowLengthMm = 0,
        [Description("Draw a crosshair over target ellipses. Default is false.")] bool targetCrosshair = false,
        [Description("Optional RGB markup color with three values from 0 to 1. Default is [1, 0, 0].")] List<double> ellipseColor = null,
        [Description("Markup line thickness from 1 to 20. Default is 3.")] int thickness = 3,
        [Description("Extra projected frame size as a fraction of each group box, from 0 to 5. Default is 0.20.")] double paddingFactor = 0.20,
        [Description("Minimum full markup size in millimeters. Default is 500.")] double? minMarkSizeMm = null,
        [Description("Hatch angle in degrees when markStyle=hatch. Default is 45.")] double hatchAngleDeg = 45,
        [Description("Hatch line spacing in millimeters when markStyle=hatch. Default is 500.")] double? hatchSpacingMm = null,
        [Description("Hatch line thickness from 1 to 20. Defaults to thickness.")] int hatchThickness = 3,
        [Description("Horizontal bbox size in millimeters at which an item receives its own frame. Default is 1500.")] double markSoloMinSizeMm = 1500,
        [Description("Maximum horizontal bbox gap in millimeters for merging small items into one frame. Default is 1000.")] double markMergeGapMm = 1000,
        [Description("Maximum gap between item bounding boxes, in millimeters, for proximity clusters. 0 keeps one viewpoint for the full selection. Default is 0.")] double clusterMaxDistanceMm = 0,
        [Description("Clustering strategy: none, distance, count, or grid. Empty preserves legacy inference from clusterMaxDistanceMm.")] string clusterBy = "",
        [Description("Approximate items per cluster for clusterBy=count. Default is 100.")] int clusterTargetSize = 100,
        [Description("Exact number of clusters for clusterBy=count, from 1 to 100. When set, overrides clusterTargetSize.")] int? clusterCount = null,
        [Description("Plan-grid square size in millimeters for clusterBy=grid. Default is 50000.")] double clusterGridSizeMm = 50000,
        [Description("Optional cap from 1 to 100 for created clusters. Largest clusters are retained.")] int? maxClusters = null,
        [Description("Distance-clustering safeguard from 1 to 10000. It does not limit count/grid modes. Default is 500.")] int? maxItemsForClustering = null,
        [Description("Deprecated alias for maxItemsForClustering.")] int? maxItemsForDistanceClustering = null,
        [Description("Update an existing viewpoint with the same generated name in place. Duplicate same-name viewpoints remain an error. Default is false.")] bool overwrite = false,
        [Description("False previews the planned viewpoints without changing the view. True creates section-box viewpoints without hiding or isolating items. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SectionBoxViewpointAsync(new SectionBoxViewpointRequest
        {
            Name = name,
            FolderPath = folderPath,
            Source = source,
            BoxOffsetMm = boxOffsetMm,
            MarkStyle = markStyle,
            ArrowCallout = arrowCallout,
            ArrowLengthMm = arrowLengthMm,
            TargetCrosshair = targetCrosshair,
            EllipseColor = ellipseColor,
            Thickness = thickness,
            PaddingFactor = paddingFactor,
            MinMarkSizeMm = minMarkSizeMm,
            HatchAngleDeg = hatchAngleDeg,
            HatchSpacingMm = hatchSpacingMm,
            HatchThickness = hatchThickness,
            MarkSoloMinSizeMm = markSoloMinSizeMm,
            MarkMergeGapMm = markMergeGapMm,
            ClusterBy = clusterBy,
            ClusterMaxDistanceMm = clusterMaxDistanceMm,
            ClusterTargetSize = clusterTargetSize,
            ClusterCount = clusterCount,
            ClusterGridSizeMm = clusterGridSizeMm,
            MaxClusters = maxClusters,
            MaxItemsForClustering = maxItemsForClustering,
            MaxItemsForDistanceClustering = maxItemsForDistanceClustering,
            Overwrite = overwrite,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Zooms the current Navisworks view to the bounding box of the current selection.")]
    public Task<ZoomToSelectionResponse> ZoomToSelection(
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ZoomToSelectionAsync(new ZoomToSelectionRequest(), cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Centers the current Navisworks view on the current selection without doing a bounding-box zoom.")]
    public Task<FocusOnSelectionResponse> FocusOnSelection(
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.FocusOnSelectionAsync(new FocusOnSelectionRequest(), cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Fits the current Navisworks view to the full model.")]
    public Task<FitAllResponse> FitAll(
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.FitAllAsync(new FitAllRequest(), cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    private static async Task RunHealthStep(McpHealthCheckResponse response, string name, Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var step = new McpHealthCheckStep
        {
            Name = name,
        };

        try
        {
            await action().ConfigureAwait(false);
            step.Ok = true;
        }
        catch (Exception ex)
        {
            step.Ok = false;
            step.ErrorCode = ExtractErrorCode(ex);
            step.ErrorMessage = ex.Message;
        }
        finally
        {
            stopwatch.Stop();
            step.ElapsedMs = stopwatch.ElapsedMilliseconds;
            response.Checks.Add(step);
        }
    }

    private static string ExtractErrorCode(Exception ex)
    {
        if (ex is HostCallException hostCallException)
            return hostCallException.ErrorCode;

        var message = ex == null ? string.Empty : ex.Message ?? string.Empty;
        var separatorIndex = message.IndexOf(':');
        if (separatorIndex > 0 && separatorIndex < 80)
            return message.Substring(0, separatorIndex);

        return ex == null ? string.Empty : ex.GetType().Name;
    }

}
