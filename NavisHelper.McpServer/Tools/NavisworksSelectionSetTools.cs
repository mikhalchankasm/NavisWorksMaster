using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksSelectionSetTools : NavisworksToolBase
{
    public NavisworksSelectionSetTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Lists selection/search sets and folders in the active Navisworks document without changing selection. Supports offset paging and path/name filtering. Returns duplicate-safe itemId, path, parentPath, type, index, explicit/static count, and dynamic-search flags.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
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
    [ToolCapabilities(ToolEffects.View, RequiresHost = true, RequiresDocument = true)]
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
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
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
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
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
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
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
    [Description("Creates a static Navisworks Selection Set from the current selection or from find_items/find_root_items_by_name match handles, optionally inside a Selection Sets folder. This stores concrete model items, not a dynamic search rule. Defaults to dry-run.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
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

}
