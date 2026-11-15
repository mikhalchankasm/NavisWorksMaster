using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksFbxLevelExportTools : NavisworksToolBase
{
    public NavisworksFbxLevelExportTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Starts an FBX export job for matchHandles from find_items or other queries, or the current selection with useSelection=true, at the requested depth, and returns at once with operationId and instanceId. Depth 0 exports each item itself, 1 its children, and so on, up to 20. For each item the job hides everything else, exports, and restores visibility; hidden items are skipped. Files are named NNN_<name>.fbx in tree order. FBX options come from Options Editor > File Exporters > FBX. Refused when a target exists unless overwrite is true, or when more than 5000 items would be exported. One export can keep Navisworks busy for minutes; poll fbx_export_status and stop with cancel_fbx_export, passing the returned instanceId to both.")]
    [ToolCapabilities(ToolEffects.Document | ToolEffects.Files, RequiresHost = true, RequiresDocument = true)]
    public Task<FbxLevelExportStatusResponse> StartFbxExport(
        [Description("Fully qualified output folder, created if missing.")] string folder,
        [Description("Match handles from find_items or other queries; pass these or useSelection=true, not both.")] List<string> matchHandles = null,
        [Description("Export the current selection instead of matchHandles. Default false.")] bool useSelection = false,
        [Description("0 exports each item itself, 1 its children, and so on, up to 20.")] int depth = 0,
        [Description("Allow replacing existing target FBX files. Defaults to false.")] bool overwrite = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.StartFbxLevelExportAsync(
            new StartFbxLevelExportRequest
            {
                MatchHandles = matchHandles ?? new List<string>(),
                UseSelection = useSelection,
                Depth = depth,
                Folder = folder,
                Overwrite = overwrite
            },
            cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Returns running, completed, failed, or canceled for a job started by start_fbx_export, with counts, the current item and the last 20 exported files. Read-only, bypasses host_busy, and answers without the Navisworks UI thread while an export runs. Pass the instanceId returned by start_fbx_export.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = false)]
    public Task<FbxLevelExportStatusResponse> FbxExportStatus(
        [Description("Operation id returned by start_fbx_export.")] string operationId,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.FbxLevelExportStatusAsync(
            new FbxLevelExportStatusRequest { OperationId = operationId },
            cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Requests cancellation of a job started by start_fbx_export before the next item. The running FBX export cannot be interrupted; visibility is restored after every item. Pass the instanceId returned by start_fbx_export and poll fbx_export_status until the job stops.")]
    [ToolCapabilities(ToolEffects.LocalState, RequiresHost = true, RequiresDocument = false)]
    public Task<FbxLevelExportStatusResponse> CancelFbxExport(
        [Description("Operation id returned by start_fbx_export.")] string operationId,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.CancelFbxLevelExportAsync(
            new CancelFbxLevelExportRequest { OperationId = operationId },
            cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
