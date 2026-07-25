using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        public ListSelectionSetsResponse ListSelectionSets(Document document, ListSelectionSetsRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ListSelectionSetsRequest();
            var limit = ClampSavedViewpointsLimit(request.Limit);
            var offset = ClampListOffset(request.Offset);
            var includeItemIds = request.IncludeItemIds.GetValueOrDefault(true);
            var allItems = document.SelectionSets != null && document.SelectionSets.RootItem != null
                ? BuildSelectionSetNodes(document.SelectionSets.RootItem).Select(node => node.Info).ToList()
                : new List<SelectionSetItemInfo>();
            var filteredItems = FilterSelectionSetItems(allItems, request).ToList();
            var pageItems = filteredItems.Skip(offset).Take(limit).ToList();

            if (!includeItemIds)
            {
                foreach (var item in pageItems)
                    item.ItemId = null;
            }

            var nextOffset = offset + pageItems.Count;
            return new ListSelectionSetsResponse
            {
                TotalItemCount = allItems.Count,
                FilteredItemCount = filteredItems.Count,
                Offset = offset,
                NextOffset = nextOffset < filteredItems.Count ? (int?)nextOffset : null,
                ReturnedItemCount = pageItems.Count,
                Truncated = nextOffset < filteredItems.Count,
                Items = pageItems,
            };
        }

        public SelectSelectionSetResponse SelectSelectionSet(Document document, SelectSelectionSetRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (document.SelectionSets == null || document.SelectionSets.RootItem == null)
                throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, "The active document has no selection sets.");
            if (string.IsNullOrWhiteSpace(request.PathOrName) && string.IsNullOrWhiteSpace(request.ItemId))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Selection set path/name or itemId is required.");

            var apply = request.Apply == true;
            var resolvedItem = ResolveSelectionSetNode(
                document.SelectionSets.RootItem,
                new SelectionSetsManageRequest
                {
                    PathOrName = request.PathOrName,
                    ItemId = request.ItemId,
                    Occurrence = request.Occurrence,
                },
                item => item is SelectionSet || item is GroupItem,
                "Selection set or folder was not found.");

            var groupItem = resolvedItem.Item as GroupItem;
            var allowFolderExpansion = request.AllowFolderExpansion == true;
            if (groupItem != null && !allowFolderExpansion)
            {
                if (apply)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Selecting folders is disabled by default because large folders can be slow. Pass allowFolderExpansion=true or select a concrete Selection Set/Search Set.");

                var folderResponse = new SelectSelectionSetResponse
                {
                    Apply = false,
                    Name = resolvedItem.Item.DisplayName ?? string.Empty,
                    Path = resolvedItem.Path,
                    Type = resolvedItem.Item.GetType().Name,
                    SelectionSetCount = CountSelectionSets(resolvedItem.Item),
                    SelectedItemCount = 0,
                    FolderExpansionSkipped = true,
                    Selected = null,
                };
                folderResponse.Warnings.Add("Folder matched. Dry-run did not expand it to model items; pass allowFolderExpansion=true only when you intentionally want to count/select all child sets.");
                return folderResponse;
            }

            var selectedItems = BuildSelectionSetItems(document, resolvedItem.Item);
            if (apply)
            {
                document.CurrentSelection.CopyFrom(selectedItems);
            }

            return new SelectSelectionSetResponse
            {
                Apply = apply,
                Name = resolvedItem.Item.DisplayName ?? string.Empty,
                Path = resolvedItem.Path,
                Type = resolvedItem.Item.GetType().Name,
                SelectionSetCount = CountSelectionSets(resolvedItem.Item),
                SelectedItemCount = selectedItems.Count,
                FolderExpansionSkipped = false,
                Selected = apply ? (bool?)true : null,
            };
        }

        public CreateSelectionSetResponse CreateSelectionSet(Document document, CreateSelectionSetRequest request, MatchSessionStore sessionStore)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var apply = request.Apply == true;
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Selection set name is required.");
            if (document.SelectionSets == null || document.SelectionSets.RootItem == null)
                throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, "The active document has no selection sets collection.");

            var matchHandles = request.MatchHandles == null
                ? new List<string>()
                : request.MatchHandles.Where(handle => !string.IsNullOrWhiteSpace(handle)).ToList();
            var source = matchHandles.Count > 0 ? "match_handles" : "current_selection";
            var matchHandleResults = new List<SelectItemsHandleResult>();
            var partial = false;
            ModelItemCollection itemsToSave;

            if (matchHandles.Count > 0)
            {
                if (sessionStore == null)
                    throw new ArgumentNullException(nameof(sessionStore));

                itemsToSave = ResolveModelItemsFromMatchHandles(matchHandles, sessionStore, matchHandleResults, out partial);
                if (itemsToSave.Count == 0)
                    throw new AgentCommandException(ErrorCodes.EmptyMatchHandles, "None of the supplied match handles resolved to live model items. Re-run find_items or find_root_items_by_name and retry.");
            }
            else
            {
                var selectedItems = document.CurrentSelection.SelectedItems;
                if (selectedItems == null || selectedItems.Count == 0)
                    throw new AgentCommandException(ErrorCodes.NoSelection, "There is no active selection.");

                itemsToSave = new ModelItemCollection();
                itemsToSave.AddRange(selectedItems);
            }

            var selectedCount = itemsToSave.Count;

            var sanitizedName = request.Name.Trim();
            var folderPath = NormalizeFolderPath(request.FolderPath);
            GroupItem targetFolder;
            bool folderExists;
            int createdFolderCount;
            if (!TryResolveSelectionSetFolder(document.SelectionSets, folderPath, false, out targetFolder, out folderExists, out createdFolderCount))
                targetFolder = document.SelectionSets.RootItem;
            var targetFolderExistedBeforeApply = folderExists;

            var existing = folderExists && targetFolder != null ? FindChildSavedItemByName(targetFolder, sanitizedName) : null;
            var nameConflict = existing != null;

            if (apply && nameConflict && request.Overwrite != true)
                throw new AgentCommandException(ErrorCodes.SelectionSetNameConflict, "Selection set with the same name already exists in the target folder.");
            if (apply && existing != null && !(existing is SelectionSet))
                throw new AgentCommandException(ErrorCodes.SelectionSetNameConflict, "A folder or unsupported saved item with the same name already exists in the target folder.");

            if (apply)
            {
                if (!TryResolveSelectionSetFolder(document.SelectionSets, folderPath, true, out targetFolder, out folderExists, out createdFolderCount))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to resolve the target selection sets folder.");

                existing = FindChildSavedItemByName(targetFolder, sanitizedName);
                var selectionSet = new SelectionSet
                {
                    DisplayName = sanitizedName,
                };
                selectionSet.ExplicitModelItems.AddRange(itemsToSave);

                if (existing != null)
                {
                    if (!(existing is SelectionSet))
                        throw new AgentCommandException(ErrorCodes.SelectionSetNameConflict, "A folder or unsupported saved item with the same name already exists in the target folder.");

                    var existingIndex = targetFolder.Children.IndexOf(existing);
                    if (existingIndex < 0)
                        throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to determine existing selection set index.");
                    document.SelectionSets.ReplaceWithCopy(targetFolder, existingIndex, selectionSet);
                }
                else
                {
                    document.SelectionSets.InsertCopy(targetFolder, targetFolder.Children.Count, selectionSet);
                }
            }

            var path = string.IsNullOrWhiteSpace(folderPath) ? sanitizedName : folderPath + "/" + sanitizedName;
            return new CreateSelectionSetResponse
            {
                Apply = apply,
                Name = sanitizedName,
                FolderPath = folderPath,
                Path = path,
                SelectedItemCount = selectedCount,
                Source = source,
                Partial = partial,
                MatchHandleResults = matchHandleResults,
                NameConflict = nameConflict,
                FolderExists = targetFolderExistedBeforeApply,
                CreatedFolderCount = apply ? (int?)createdFolderCount : null,
                Created = apply ? (bool?)(existing == null) : null,
                Overwritten = apply ? (bool?)(existing != null) : null,
            };
        }

        private static ModelItemCollection ResolveModelItemsFromMatchHandles(
            IEnumerable<string> matchHandles,
            MatchSessionStore sessionStore,
            IList<SelectItemsHandleResult> results,
            out bool partial)
        {
            if (sessionStore == null)
                throw new ArgumentNullException(nameof(sessionStore));

            partial = false;
            var itemsToSave = new ModelItemCollection();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var handle in matchHandles.Where(h => !string.IsNullOrWhiteSpace(h)))
            {
                IList<ModelItem> items;
                if (!sessionStore.TryGet(handle, out items))
                {
                    partial = true;
                    if (results != null)
                    {
                        results.Add(new SelectItemsHandleResult
                        {
                            MatchHandle = handle,
                            Status = SelectHandleStatuses.Stale,
                            SelectedItemCount = 0,
                        });
                    }
                    continue;
                }

                var selectedCount = 0;
                foreach (var item in items)
                {
                    var identity = BuildItemPath(item);
                    if (seen.Add(identity))
                    {
                        itemsToSave.Add(item);
                        selectedCount++;
                    }
                }

                if (results != null)
                {
                    results.Add(new SelectItemsHandleResult
                    {
                        MatchHandle = handle,
                        Status = SelectHandleStatuses.Selected,
                        SelectedItemCount = selectedCount,
                    });
                }
            }

            return itemsToSave;
        }
    }
}
