using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksViewpointTools : NavisworksToolBase
{
    public NavisworksViewpointTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Returns read-only information about the current Navisworks viewpoint, including position, rotation, and common viewpoint properties when available.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<CurrentViewpointInfoResponse> CurrentViewpointInfo(
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.CurrentViewpointInfoAsync(new CurrentViewpointInfoRequest(), cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Lists saved viewpoints and folders in the active Navisworks document without changing the current view. Returns name, path, type, depth, and child count.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
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
    [ToolCapabilities(ToolEffects.Files, RequiresHost = true, RequiresDocument = true)]
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
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
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
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
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
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
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
    [Description("Activates an existing saved viewpoint by exact path or unique name from list_saved_viewpoints. Defaults to dry-run and returns the resolved path without changing the view unless apply=true.")]
    [ToolCapabilities(ToolEffects.View, RequiresHost = true, RequiresDocument = true)]
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
    [Description("Saves the current Navisworks view as a saved viewpoint, optionally inside a folder path.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
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

}
