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
    [Description("Starts exporting every Selection Tree item at one level of the active model to its own FBX file in folder, as a job, and returns at once with operationId and instanceId. Level 1 is the root items (the files at the top of the Selection Tree), level 2 their children. For each item the job hides everything else, exports, and restores the visibility; items the user hid are skipped. Files are named NNN_<name>.fbx in tree order. FBX options come from Options Editor > File Exporters > FBX. Refused when a target exists unless overwrite is true, or when the level holds more than 5000 items. One export can keep Navisworks busy for minutes; poll fbx_level_export_status and stop with cancel_fbx_level_export, passing the returned instanceId to both.")]
    [ToolCapabilities(ToolEffects.Document | ToolEffects.Files, RequiresHost = true, RequiresDocument = true)]
    public Task<FbxLevelExportStatusResponse> StartFbxLevelExport(
        [Description("Selection Tree level, from 1 to 20; 1 means root items and 2 their children.")] int level,
        [Description("Fully qualified output folder, created if missing.")] string folder,
        [Description("Allow replacing existing target FBX files. Defaults to false.")] bool overwrite = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.StartFbxLevelExportAsync(
            new StartFbxLevelExportRequest { Level = level, Folder = folder, Overwrite = overwrite },
            cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Returns running, completed, failed, or canceled for a job started by start_fbx_level_export, with counts, the current item and the last 20 exported files. Read-only, bypasses host_busy, and answers without the Navisworks UI thread while an export runs. Pass the instanceId returned by start_fbx_level_export.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = false)]
    public Task<FbxLevelExportStatusResponse> FbxLevelExportStatus(
        [Description("Operation id returned by start_fbx_level_export.")] string operationId,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.FbxLevelExportStatusAsync(
            new FbxLevelExportStatusRequest { OperationId = operationId },
            cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Requests cancellation of a job started by start_fbx_level_export before the next item. The running FBX export cannot be interrupted; visibility is restored after every item. Pass the instanceId returned by start_fbx_level_export and poll fbx_level_export_status until the job stops.")]
    [ToolCapabilities(ToolEffects.LocalState, RequiresHost = true, RequiresDocument = false)]
    public Task<FbxLevelExportStatusResponse> CancelFbxLevelExport(
        [Description("Operation id returned by start_fbx_level_export.")] string operationId,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.CancelFbxLevelExportAsync(
            new CancelFbxLevelExportRequest { OperationId = operationId },
            cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
