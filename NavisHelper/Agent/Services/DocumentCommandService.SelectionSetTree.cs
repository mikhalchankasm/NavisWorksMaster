using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        private static SelectionSetNode ResolveSelectionSetNode(GroupItem root, SelectionSetsManageRequest request, Func<SavedItem, bool> predicate, string notFoundMessage)
        {
            var nodes = BuildSelectionSetNodes(root)
                .Where(node => predicate == null || predicate(node.Item))
                .ToList();

            if (!string.IsNullOrWhiteSpace(request.ItemId))
            {
                var idMatches = nodes.Where(node => string.Equals(node.Info.ItemId, request.ItemId.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                return SelectSelectionSetNodeMatch(idMatches, request.Occurrence, notFoundMessage);
            }

            var requested = NormalizeSavedItemPath(request.PathOrName);
            if (string.IsNullOrWhiteSpace(requested))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "PathOrName or itemId is required.");

            var pathMatches = nodes
                .Where(node => string.Equals(NormalizeSavedItemPath(node.Path), requested, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (pathMatches.Count > 0)
                return SelectSelectionSetNodeMatch(pathMatches, request.Occurrence, notFoundMessage);

            var nameMatches = nodes
                .Where(node => string.Equals(node.Item.DisplayName ?? string.Empty, request.PathOrName.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            return SelectSelectionSetNodeMatch(nameMatches, request.Occurrence, notFoundMessage);
        }

        private static SelectionSetNode SelectSelectionSetNodeMatch(IList<SelectionSetNode> matches, int? occurrence, string notFoundMessage)
        {
            if (matches == null || matches.Count == 0)
                throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, notFoundMessage);

            if (occurrence.HasValue)
            {
                var index = occurrence.Value - 1;
                if (index < 0 || index >= matches.Count)
                    throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, "Occurrence is outside the matched item range.");
                return matches[index];
            }

            if (matches.Count == 1)
                return matches[0];

            throw new AgentCommandException(ErrorCodes.SavedItemAmbiguous, "More than one selection set item matched. Pass itemId from list_selection_sets or an occurrence number.");
        }

        private static List<SelectionSetNode> BuildSelectionSetNodes(GroupItem root)
        {
            var nodes = new List<SelectionSetNode>();
            if (root == null)
                return nodes;

            var index = 0;
            foreach (SavedItem child in root.Children)
            {
                CollectSelectionSetNodes(child, root, string.Empty, 0, FormatSelectionSetItemId(index), nodes);
                index++;
            }
            return nodes;
        }

        private static void CollectSelectionSetNodes(SavedItem item, GroupItem parent, string parentPath, int depth, string itemId, IList<SelectionSetNode> nodes)
        {
            if (item == null)
                return;

            var name = item.DisplayName ?? string.Empty;
            var path = string.IsNullOrWhiteSpace(parentPath) ? name : parentPath + "/" + name;
            var groupItem = item as GroupItem;
            var selectionSet = item as SelectionSet;
            var childCount = GetSavedItemChildCount(groupItem);
            var info = new SelectionSetItemInfo
            {
                ItemId = itemId,
                Name = name,
                Path = path,
                ParentPath = parentPath ?? string.Empty,
                Type = item.GetType().Name,
                Depth = depth,
                Index = GetSavedItemIndex(item),
                ChildCount = childCount,
                ExplicitItemCount = GetExplicitModelItemCount(selectionSet),
                HasExplicitModelItems = selectionSet != null && selectionSet.HasExplicitModelItems,
                HasSearch = selectionSet != null && selectionSet.HasSearch,
                SearchConditionCount = GetSelectionSetSearchConditionCount(selectionSet),
            };

            nodes.Add(new SelectionSetNode
            {
                Item = item,
                Parent = parent,
                Path = path,
                ParentPath = parentPath ?? string.Empty,
                Info = info,
            });

            if (groupItem == null)
                return;

            var childIndex = 0;
            foreach (SavedItem child in groupItem.Children)
            {
                CollectSelectionSetNodes(child, groupItem, path, depth + 1, itemId + "." + childIndex.ToString("D4", CultureInfo.InvariantCulture), nodes);
                childIndex++;
            }
        }

        private static string FormatSelectionSetItemId(int index)
        {
            return "ss:" + index.ToString("D4", CultureInfo.InvariantCulture);
        }

        private static int GetSelectionSetSearchConditionCount(SelectionSet selectionSet)
        {
            if (selectionSet == null || !selectionSet.HasSearch || selectionSet.Search == null || selectionSet.Search.SearchConditions == null)
                return 0;

            try
            {
                return selectionSet.Search.SearchConditions.Count;
            }
            catch
            {
                return 0;
            }
        }

        private static bool TryResolveSelectionSetFolder(
            DocumentSelectionSets selectionSets,
            string folderPath,
            bool createIfMissing,
            out GroupItem targetFolder,
            out bool folderExists,
            out int createdFolderCount)
        {
            if (selectionSets == null)
                throw new ArgumentNullException(nameof(selectionSets));

            targetFolder = selectionSets.RootItem;
            folderExists = true;
            createdFolderCount = 0;

            var segments = SplitFolderPath(folderPath);
            if (segments.Length == 0)
                return true;

            GroupItem currentFolder = selectionSets.RootItem;
            for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                var segment = segments[segmentIndex];
                var nextFolder = FindChildGroupByName(currentFolder, segment);
                if (nextFolder != null)
                {
                    currentFolder = nextFolder;
                    continue;
                }

                folderExists = false;
                if (!createIfMissing)
                {
                    targetFolder = selectionSets.RootItem;
                    return false;
                }

                selectionSets.InsertCopy(currentFolder, currentFolder.Children.Count, new FolderItem
                {
                    DisplayName = segment,
                });

                // InsertCopy replaces Navisworks saved-item snapshots. Re-resolve the path from
                // the live root instead of reading the stale parent object used for the insert.
                currentFolder = selectionSets.RootItem;
                for (var resolvedIndex = 0; resolvedIndex <= segmentIndex; resolvedIndex++)
                {
                    currentFolder = FindChildGroupByName(currentFolder, segments[resolvedIndex]);
                    if (currentFolder == null)
                        throw new AgentCommandException(ErrorCodes.CommandFailed, "Failed to create selection set folder: " + segment);
                }

                nextFolder = currentFolder;
                if (nextFolder == null)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Failed to create selection set folder: " + segment);

                createdFolderCount++;
                currentFolder = nextFolder;
            }

            targetFolder = currentFolder;
            return true;
        }

        private static SavedItem FindChildSavedItemByName(GroupItem parent, string name)
        {
            if (parent == null || string.IsNullOrWhiteSpace(name))
                return null;

            return parent.Children
                .OfType<SavedItem>()
                .FirstOrDefault(item => string.Equals(item.DisplayName ?? string.Empty, name, StringComparison.OrdinalIgnoreCase));
        }

        private sealed class SelectionSetNode
        {
            public SavedItem Item { get; set; }
            public GroupItem Parent { get; set; }
            public string Path { get; set; }
            public string ParentPath { get; set; }
            public SelectionSetItemInfo Info { get; set; }
        }
    }
}
