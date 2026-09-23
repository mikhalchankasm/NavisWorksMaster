using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksHostTools : NavisworksToolBase
{
    public NavisworksHostTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Lists running Navisworks MCP host instances. Use instance_id from this tool when multiple Navisworks windows are open, or navisworks_version when exactly one host of that version is running.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = false, RequiresDocument = false)]
    public ListNavisworksHostsResponse ListNavisworksHosts()
    {
        return _hostBridgeClient.ListNavisworksHosts();
    }

    [McpServerTool]
    [Description("Returns MCP diagnostics: JSONL log file path, discovery instances directory, and currently running Navisworks host records.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = false, RequiresDocument = false)]
    public McpDiagnosticsResponse McpDiagnostics()
    {
        return _hostBridgeClient.GetDiagnostics();
    }

    [McpServerTool]
    [Description("Returns the last MCP JSONL call log lines. Use after failures or long runs to confirm which tools were invoked, their target Navisworks instance, elapsed time, status, and error code.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = false, RequiresDocument = false)]
    public McpRecentCallsResponse McpRecentCalls(
        [Description("Number of recent JSONL log lines to return. Default is 50, maximum is 200.")] int lineCount = 50)
    {
        return _hostBridgeClient.GetRecentCalls(lineCount);
    }

    [McpServerTool]
    [Description("Returns the NavisHelper MCP error contract: stable error codes, meanings, retryability, and recommended client actions.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = false, RequiresDocument = false)]
    public McpErrorContractResponse McpErrorContract()
    {
        return HostBridgeClient.GetErrorContract();
    }

    [McpServerTool]
    [Description("Runs a read-only MCP/Navisworks health check and returns a verdict instead of throwing on partial failures. Use after long runs, timeouts, or suspected host hangs.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = false)]
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
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
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
    [Description("Returns current Navisworks MCP host status: active document, process id, memory use, model count, and indexed root item count.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = false)]
    public Task<HostStatusResponse> HostStatus(
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.HostStatusAsync(new HostStatusRequest(), cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Returns host-side status for a recent request_id, or for the most recent host operation when no request_id is given. Use after request_timeout, transport disconnect, or oversized-response suspicion to determine whether the Navisworks-side command eventually completed, failed, or is still running.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = false)]
    public Task<LastOperationStatusResponse> LastOperationStatus(
        [Description("Optional request_id from mcp_recent_calls or the MCP server error log. Leave empty to ask about the most recent host operation, which is what a caller has after a timeout dropped the reply before it carried a request_id. Check the returned command matches the call you lost.")] string requestId = "",
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.LastOperationStatusAsync(new LastOperationStatusRequest
        {
            RequestId = requestId,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
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
