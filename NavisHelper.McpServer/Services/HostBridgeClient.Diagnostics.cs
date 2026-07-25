using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed partial class HostBridgeClient
{
    public ListNavisworksHostsResponse ListNavisworksHosts()
    {
        var directory = GetInstancesDirectory();
        Directory.CreateDirectory(directory);

        var response = new ListNavisworksHostsResponse
        {
            Hosts = InstanceDiscoveryStore.LoadAliveRecords(directory, JsonOptions)
                .Select(record => new NavisworksHostInfo
                {
                    ProtocolVersion = record.ProtocolVersion,
                    InstanceId = record.InstanceId,
                    PipeName = record.PipeName,
                    Pid = record.Pid,
                    NavisworksVersion = record.NavisworksVersion,
                    DocumentTitle = record.DocumentTitle,
                    StartedAtUtc = record.StartedAtUtc,
                    ProcessStartedAtUtc = record.ProcessStartedAtUtc,
                    PluginVersion = record.PluginVersion,
                    PluginAssemblyPath = record.PluginAssemblyPath,
                    PluginAssemblyLastWriteUtc = record.PluginAssemblyLastWriteUtc,
                    PluginAssemblyLength = record.PluginAssemblyLength,
                    HostLogFilePath = record.HostLogFilePath,
                })
                .OrderBy(host => host.NavisworksVersion)
                .ThenBy(host => host.Pid)
                .ToList(),
        };

        _callLogger.Log(new
        {
            event_name = "list_navisworks_hosts",
            timestamp_utc = DateTime.UtcNow,
            host_count = response.Hosts.Count,
        });

        return response;
    }

    public McpDiagnosticsResponse GetDiagnostics()
    {
        var hosts = ListNavisworksHosts();
        return new McpDiagnosticsResponse
        {
            McpServerVersion = McpServerVersion,
            ProtocolVersion = ProtocolConstants.CurrentProtocolVersion,
            LogFilePath = _callLogger.LogFilePath,
            InstancesDirectory = GetInstancesDirectory(),
            Hosts = hosts.Hosts,
        };
    }

    public McpRecentCallsResponse GetRecentCalls(int lineCount)
    {
        return _callLogger.GetRecentCalls(lineCount);
    }

    public static McpErrorContractResponse GetErrorContract()
    {
        return new McpErrorContractResponse
        {
            Errors = new List<McpErrorContractItem>
            {
                Error(ErrorCodes.NoActiveDocument, "No document is open in the targeted Navisworks host.", "Open a model in Navisworks, then retry host_status or the intended tool.", false),
                Error(ErrorCodes.NoActiveView, "The active document has no active view.", "Activate a view in Navisworks or call fit_all after a document is ready.", true),
                Error(ErrorCodes.HostUiContextUnavailable, "The Navisworks plugin has not attached a UI dispatcher.", "Restart Navisworks or reload the plugin; retry after host_status succeeds.", true),
                Error(ErrorCodes.MultipleHostsDetected, "More than one Navisworks host matches the request.", "Call list_navisworks_hosts and pass instanceId to subsequent tools.", false),
                Error(ErrorCodes.InstanceNotFound, "No running host matches the requested instanceId/version, or discovery is stale.", "Call list_navisworks_hosts; start Navisworks if no hosts are returned.", true),
                Error(ErrorCodes.HostBusy, "The host is already processing another request.", "Retry after a short delay. Avoid parallel calls to the same Navisworks instance.", true),
                Error(ErrorCodes.InteractiveBusy, "The Navisworks UI is busy with a manual interactive operation.", "Wait for the manual NavisHelper/Navisworks operation to finish, then retry.", false),
                Error(ErrorCodes.SchemaViolation, "The request payload is invalid or unsupported.", "Fix arguments before retrying. Check tool descriptions and enum values.", false),
                Error(ErrorCodes.EmptyMatchHandles, "A command requiring match handles received none.", "Run find_items or find_root_items_by_name first and pass returned handles.", false),
                Error(ErrorCodes.NoSelection, "A selection-dependent command was called with no active selection.", "Run select_items first or select items manually in Navisworks.", false),
                Error(ErrorCodes.SelectionSetNotFound, "The requested selection set or folder was not found.", "Call list_selection_sets and pass an exact path returned by that tool.", false),
                Error(ErrorCodes.SavedViewpointNotFound, "The requested saved viewpoint was not found.", "Call list_saved_viewpoints and pass an exact path returned by that tool.", false),
                Error(ErrorCodes.SavedItemAmbiguous, "More than one saved item matched the provided name/path.", "Use the full path returned by list_selection_sets or list_saved_viewpoints.", false),
                Error(ErrorCodes.SelectionSetNameConflict, "A selection set with the requested name already exists.", "Choose another name or inspect existing selection sets manually.", false),
                Error(ErrorCodes.ViewpointNameConflict, "A saved viewpoint with the requested name already exists in the target folder.", "Choose another viewpoint name or folder.", false),
                Error(ErrorCodes.StaleMatchReference, "A match handle no longer resolves in the host session.", "Re-run find_items, find_root_items_by_name, or list_item_children and retry with fresh handles.", false),
                Error(ErrorCodes.QueryTooAmbiguous, "A broad query would scan or return too much model data.", "Use a more specific name/path/source-file filter or split the request.", false),
                Error(ErrorCodes.RequestTimeout, "The host did not complete the request within the timeout or the search guarded itself before timeout.", "Split large requests into smaller batches and retry remaining work.", true),
                Error(ErrorCodes.TransportConnectFailed, "The MCP server could not connect to the host named pipe.", "Call list_navisworks_hosts; if the process is gone, start Navisworks again.", true),
                Error(ErrorCodes.CommandFailed, "Navisworks rejected or failed the requested operation.", "Inspect the error message and current model/view state before retrying.", false),
                Error("scenario_invalid", "A saved or proposed scenario failed strict schema, privacy, path, allowlist, or safety validation.", "Review the scenario validation errors and correct the draft or hand-edited file; invalid files are never rewritten automatically.", false),
                Error("scenario_not_found", "The requested scenario_id does not exist in the current user's scenario library.", "Call list_scenarios and use the returned scenario_id.", false),
                Error("scenario_conflict", "The scenario SHA-256 changed after it was read.", "Call get_scenario, review the current content, and retry with its latest sha256.", true),
                Error("scenario_name_conflict", "An exactReplay scenario already uses this case-insensitive name.", "Choose a unique exact-replay name so no-question replay remains unambiguous.", false),
                Error("scenario_store_full", "The per-user scenario store reached its 500-file limit.", "Delete or export unneeded scenarios before creating another one; updates remain available.", false),
                Error("scenario_context_mismatch", "Strict exactReplay context did not strongly match the supplied Navisworks version/root files/project label.", "Do not execute the scenario. Inspect or update it explicitly; never fall back silently to template mode.", false),
                Error("scenario_tool_contract_changed", "A saved exactReplay step targets an older scenario-visible tool contract.", "Review and resave the scenario against the current tool contract before replaying it.", false),
                Error("scenario_parameters_required", "A template scenario is missing one or more required runtime parameter values.", "Supply the listed parameters and resolve again.", false),
            },
        };
    }
}
