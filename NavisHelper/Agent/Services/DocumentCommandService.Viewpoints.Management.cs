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
        public SavedViewpointsManageResponse SavedViewpointsManage(Document document, SavedViewpointsManageRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (document.SavedViewpoints == null || document.SavedViewpoints.RootItem == null)
                throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "The active document has no saved viewpoints collection.");

            var operation = NormalizeSavedViewpointsManageOperation(request.Operation);
            var apply = request.Apply == true;

            if (string.Equals(operation, "create_folder", StringComparison.OrdinalIgnoreCase))
                return ManageCreateFolder(document.SavedViewpoints, request, apply, operation);
            if (string.Equals(operation, "delete_folder", StringComparison.OrdinalIgnoreCase))
                return ManageDeleteFolder(document.SavedViewpoints, request, apply, operation);
            if (string.Equals(operation, "delete", StringComparison.OrdinalIgnoreCase))
                return ManageDeleteViewpoint(document.SavedViewpoints, request, apply, operation);
            if (string.Equals(operation, "delete_many", StringComparison.OrdinalIgnoreCase))
                return ManageDeleteManyViewpoints(document.SavedViewpoints, request, apply, operation);
            if (string.Equals(operation, "rename", StringComparison.OrdinalIgnoreCase))
                return ManageRename(document.SavedViewpoints, request, apply, operation);
            if (string.Equals(operation, "move", StringComparison.OrdinalIgnoreCase))
                return ManageMove(document.SavedViewpoints, request, apply, operation);

            throw new AgentCommandException(ErrorCodes.SchemaViolation, "Unsupported saved viewpoint operation: " + request.Operation);
        }

        public SavedViewpointsReorderResponse SavedViewpointsReorder(Document document, SavedViewpointsReorderRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (document.SavedViewpoints == null || document.SavedViewpoints.RootItem == null)
                throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "The active document has no saved viewpoints collection.");

            var apply = request.Apply == true;
            var recursive = request.Recursive.GetValueOrDefault(true);
            var foldersFirst = request.FoldersFirst.GetValueOrDefault(true);
            var targetFolder = ResolveSavedViewpointFolderForReorder(document.SavedViewpoints.RootItem, request);
            var folderPath = targetFolder == document.SavedViewpoints.RootItem ? string.Empty : BuildSavedItemPath(targetFolder);
            var folders = new List<GroupItem>();
            CollectFoldersForReorder(targetFolder, recursive, folders);

            var response = new SavedViewpointsReorderResponse
            {
                Apply = apply,
                FolderPath = folderPath,
                Recursive = recursive,
                FoldersFirst = foldersFirst,
                ProcessedFolderCount = folders.Count,
            };

            foreach (var folder in folders)
            {
                var plan = BuildReorderPlan(folder, foldersFirst);
                response.Folders.Add(plan);
                if (!plan.WouldChange)
                    continue;

                response.ReorderedFolderCount++;
                response.MovedItemCount += CountChangedPositions(plan.Before, plan.After);
                if (apply)
                    ApplyFolderReorder(document.SavedViewpoints, folder, foldersFirst);
            }

            response.Changed = apply ? (bool?)(response.ReorderedFolderCount > 0) : null;
            return response;
        }

        private static SavedViewpointsManageResponse ManageCreateFolder(DocumentSavedViewpoints savedViewpoints, SavedViewpointsManageRequest request, bool apply, string operation)
        {
            var targetFolderPath = NormalizeFolderPath(request.TargetFolderPath);
            var name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(request.PathOrName))
            {
                var segments = SplitFolderPath(request.PathOrName);
                if (segments.Length > 0)
                {
                    name = segments[segments.Length - 1];
                    targetFolderPath = string.Join("/", segments.Take(segments.Length - 1).ToArray());
                }
            }
            if (string.IsNullOrWhiteSpace(name))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Folder name is required.");

            GroupItem targetFolder;
            bool folderExists;
            int createdFolderCount;
            if (!TryResolveSavedViewpointFolder(savedViewpoints, targetFolderPath, false, out targetFolder, out folderExists, out createdFolderCount))
                targetFolder = savedViewpoints.RootItem;

            var newPath = string.IsNullOrWhiteSpace(targetFolderPath) ? name : targetFolderPath + "/" + name;
            var alreadyExists = targetFolder != null && targetFolder.Children.OfType<GroupItem>().Any(item => string.Equals(item.DisplayName ?? string.Empty, name, StringComparison.OrdinalIgnoreCase));
            if (apply && alreadyExists)
                throw new AgentCommandException(ErrorCodes.ViewpointNameConflict, "A folder with the same name already exists in the target folder.");

            if (apply)
            {
                if (!TryResolveSavedViewpointFolder(savedViewpoints, targetFolderPath, true, out targetFolder, out folderExists, out createdFolderCount))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to resolve the target folder.");
                savedViewpoints.InsertCopy(targetFolder, targetFolder.Children.Count, new FolderItem { DisplayName = name });
            }

            var response = new SavedViewpointsManageResponse
            {
                Apply = apply,
                Operation = operation,
                Name = name,
                Path = newPath,
                NewPath = newPath,
                TargetFolderPath = targetFolderPath,
                Type = typeof(FolderItem).Name,
                MatchedItemCount = alreadyExists ? 1 : 0,
                TargetFolderExists = folderExists,
                CreatedFolderCount = apply ? (int?)createdFolderCount : null,
                Changed = apply ? (bool?)true : null,
            };
            if (alreadyExists)
                response.Warnings.Add("A folder with the same name already exists in the target folder; apply=true will fail.");
            return response;
        }

        private static SavedViewpointsManageResponse ManageDeleteFolder(DocumentSavedViewpoints savedViewpoints, SavedViewpointsManageRequest request, bool apply, string operation)
        {
            var node = ResolveSavedViewpointNode(savedViewpoints.RootItem, request, item => item is GroupItem, "Folder was not found.");
            var folder = node.Item as GroupItem;
            var childCount = GetSavedItemChildCount(folder);
            if (apply && childCount > 0 && request.AllowDeleteNonEmptyFolder != true)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Folder is not empty. Pass allowDeleteNonEmptyFolder=true to delete it with all children.");

            var response = BuildManageResponse(apply, operation, node);
            if (apply)
                RemoveSavedViewpointItem(savedViewpoints, node);
            response.Changed = apply ? (bool?)true : null;
            if (!apply && childCount > 0)
                response.Warnings.Add("Folder contains " + childCount.ToString() + " direct child item(s). Pass allowDeleteNonEmptyFolder=true before applying a delete.");
            return response;
        }

        private static SavedViewpointsManageResponse ManageDeleteViewpoint(DocumentSavedViewpoints savedViewpoints, SavedViewpointsManageRequest request, bool apply, string operation)
        {
            var node = ResolveSavedViewpointNode(savedViewpoints.RootItem, request, item => item is SavedViewpoint, "Saved viewpoint was not found.");
            var response = BuildManageResponse(apply, operation, node);
            response.Items.Add(BuildManageItemResponse(node, apply));
            if (apply)
                RemoveSavedViewpointItem(savedViewpoints, node);
            response.DeletedItemCount = apply ? 1 : 0;
            response.Changed = apply ? (bool?)true : null;
            return response;
        }

        private static SavedViewpointsManageResponse ManageDeleteManyViewpoints(DocumentSavedViewpoints savedViewpoints, SavedViewpointsManageRequest request, bool apply, string operation)
        {
            var targets = request.Items ?? new List<SavedViewpointsManageTarget>();
            if (targets.Count == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "items must contain at least one saved viewpoint target for delete_many.");
            if (targets.Count > 5000)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "delete_many accepts at most 5000 saved viewpoint targets per call.");

            // Resolve the complete plan before mutating the tree so an invalid or
            // ambiguous target cannot leave a partially deleted batch.
            var nodes = new List<SavedViewpointNode>();
            var seenItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets)
            {
                if (target == null)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "delete_many items cannot contain null values.");
                var node = ResolveSavedViewpointNode(
                    savedViewpoints.RootItem,
                    new SavedViewpointsManageRequest
                    {
                        PathOrName = target.PathOrName,
                        ItemId = target.ItemId,
                        Occurrence = target.Occurrence,
                    },
                    item => item is SavedViewpoint,
                    "Saved viewpoint was not found.");
                if (seenItemIds.Add(node.Info.ItemId))
                    nodes.Add(node);
            }

            var response = new SavedViewpointsManageResponse
            {
                Apply = apply,
                Operation = operation,
                MatchedItemCount = nodes.Count,
                DeletedItemCount = apply ? nodes.Count : 0,
                Changed = apply ? (bool?)(nodes.Count > 0) : null,
                Type = typeof(SavedViewpoint).Name,
                TargetFolderExists = true,
            };
            foreach (var node in nodes)
                response.Items.Add(BuildManageItemResponse(node, apply));
            if (apply)
            {
                var deletePlan = nodes
                    .Select(node => new
                    {
                        Node = node,
                        ChildIndex = node.Parent == null ? -1 : node.Parent.Children.IndexOf(node.Item),
                    })
                    .ToList();
                if (deletePlan.Any(item => item.ChildIndex < 0))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to determine every parent/index for delete_many before mutation.");

                // Delete high indexes first so lower planned indexes remain stable.
                // Re-resolve the parent before every mutation because Navisworks may
                // rebuild SavedItem wrappers after a tree change.
                var deletedCount = 0;
                try
                {
                    // Fixed-width itemId segments make descending full-id order
                    // stable: removing a later/deeper item cannot renumber any
                    // still-pending earlier item or parent folder.
                    foreach (var item in deletePlan.OrderByDescending(value => value.Node.Info.ItemId, StringComparer.Ordinal))
                    {
                        var liveNode = ResolveSavedViewpointNode(
                            savedViewpoints.RootItem,
                            new SavedViewpointsManageRequest { ItemId = item.Node.Info.ItemId },
                            savedItem => savedItem is SavedViewpoint,
                            "Saved viewpoint identity changed during delete_many.");
                        var liveParent = liveNode.Parent ?? savedViewpoints.RootItem;
                        var liveIndex = liveParent.Children.IndexOf(liveNode.Item);
                        if (liveIndex != item.ChildIndex ||
                            !string.Equals(liveNode.Path, item.Node.Path, StringComparison.Ordinal) ||
                            liveNode.Item.GetType() != item.Node.Item.GetType())
                        {
                            throw new AgentCommandException(ErrorCodes.CommandFailed, "Saved viewpoint identity changed during delete_many; deletion was stopped before removing: " + item.Node.Path);
                        }
                        savedViewpoints.RemoveAt(liveParent, liveIndex);
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "delete_many stopped after deleting " + deletedCount.ToString() + " of " + deletePlan.Count.ToString() + " planned viewpoint(s): " + ex.Message);
                }
            }
            if (nodes.Count < targets.Count)
                response.Warnings.Add((targets.Count - nodes.Count).ToString() + " duplicate target(s) referred to viewpoints already present in this delete plan.");
            return response;
        }

        private static SavedViewpointsManageItemResponse BuildManageItemResponse(SavedViewpointNode node, bool deleted)
        {
            return new SavedViewpointsManageItemResponse
            {
                ItemId = node.Info.ItemId,
                Name = node.Item.DisplayName ?? string.Empty,
                Path = node.Path,
                Type = node.Item.GetType().Name,
                Deleted = deleted,
            };
        }

        private static void RemoveSavedViewpointItem(DocumentSavedViewpoints savedViewpoints, SavedViewpointNode node)
        {
            if (savedViewpoints == null)
                throw new ArgumentNullException(nameof(savedViewpoints));
            if (node == null || node.Item == null)
                throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "Saved viewpoint item was not found.");

            var parent = node.Parent ?? savedViewpoints.RootItem;
            var index = parent == null || parent.Children == null ? -1 : parent.Children.IndexOf(node.Item);
            if (parent == null || index < 0)
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to determine parent/index for delete: " + node.Path);

            if (savedViewpoints.Remove(parent, node.Item))
                return;

            savedViewpoints.RemoveAt(parent, index);
        }

        private static SavedViewpointsManageResponse ManageRename(DocumentSavedViewpoints savedViewpoints, SavedViewpointsManageRequest request, bool apply, string operation)
        {
            var newName = (request.NewName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newName))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "NewName is required.");

            var node = ResolveSavedViewpointNode(savedViewpoints.RootItem, request, item => item is GroupItem || item is SavedViewpoint, "Saved viewpoint item was not found.");
            var oldPath = node.Path;
            var newPath = string.IsNullOrWhiteSpace(node.ParentPath) ? newName : node.ParentPath + "/" + newName;
            var duplicateSibling = node.Parent != null && node.Parent.Children
                .Cast<SavedItem>()
                .Any(item => item != node.Item && string.Equals(item.DisplayName ?? string.Empty, newName, StringComparison.OrdinalIgnoreCase));

            if (apply)
            {
                var parent = node.Parent ?? savedViewpoints.RootItem;
                var currentIndex = parent.Children.IndexOf(node.Item);
                if (currentIndex < 0)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to determine item index for rename.");

                var replacement = node.Item.CreateCopy();
                replacement.DisplayName = newName;
                var sourceFolder = node.Item as GroupItem;
                if (sourceFolder != null)
                {
                    var replacementFolder = replacement as GroupItem;
                    if (replacementFolder == null || CountSavedItemTree(sourceFolder) != CountSavedItemTree(replacementFolder))
                        throw new AgentCommandException(ErrorCodes.CommandFailed, "Folder copy did not preserve children; rename was aborted before changing the model.");
                }
                savedViewpoints.ReplaceWithCopy(parent, currentIndex, replacement);
            }

            var response = BuildManageResponse(apply, operation, node);
            response.NewName = newName;
            response.NewPath = newPath;
            response.Path = oldPath;
            response.Changed = apply ? (bool?)true : null;
            if (duplicateSibling)
                response.Warnings.Add("The target folder already contains another item with the new name. Navisworks permits duplicates, but use itemId for later operations.");
            return response;
        }

        private static SavedViewpointsManageResponse ManageMove(DocumentSavedViewpoints savedViewpoints, SavedViewpointsManageRequest request, bool apply, string operation)
        {
            var targetFolderPath = NormalizeFolderPath(request.TargetFolderPath);
            var node = ResolveSavedViewpointNode(savedViewpoints.RootItem, request, item => item is GroupItem || item is SavedViewpoint, "Saved viewpoint item was not found.");

            GroupItem targetFolder;
            bool folderExists;
            int createdFolderCount;
            if (!TryResolveSavedViewpointFolder(savedViewpoints, targetFolderPath, false, out targetFolder, out folderExists, out createdFolderCount))
                targetFolder = savedViewpoints.RootItem;

            if (node.Parent == null)
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Cannot move the saved viewpoint root.");
            if (targetFolder == node.Item)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Cannot move an item into itself.");
            if (node.Item is GroupItem && IsDescendantFolder(targetFolder, node.Item as GroupItem))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Cannot move a folder into its own descendant.");
            if (node.Item is GroupItem &&
                !string.IsNullOrWhiteSpace(targetFolderPath) &&
                (string.Equals(NormalizeSavedItemPath(targetFolderPath), NormalizeSavedItemPath(node.Path), StringComparison.OrdinalIgnoreCase) ||
                 NormalizeSavedItemPath(targetFolderPath).StartsWith(NormalizeSavedItemPath(node.Path) + "/", StringComparison.OrdinalIgnoreCase)))
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Cannot move a folder into itself or its own descendant path.");
            }

            var newPath = string.IsNullOrWhiteSpace(targetFolderPath) ? (node.Item.DisplayName ?? string.Empty) : targetFolderPath + "/" + (node.Item.DisplayName ?? string.Empty);
            var duplicateSibling = targetFolder != null && targetFolder.Children
                .Cast<SavedItem>()
                .Any(item => item != node.Item && string.Equals(item.DisplayName ?? string.Empty, node.Item.DisplayName ?? string.Empty, StringComparison.OrdinalIgnoreCase));

            if (apply)
            {
                if (!TryResolveSavedViewpointFolder(savedViewpoints, targetFolderPath, true, out targetFolder, out folderExists, out createdFolderCount))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to resolve the target folder.");

                var sourceIndex = node.Parent.Children.IndexOf(node.Item);
                if (sourceIndex < 0)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to determine source item index for move.");
                savedViewpoints.Move(node.Parent, sourceIndex, targetFolder, targetFolder.Children.Count);
            }

            var response = BuildManageResponse(apply, operation, node);
            response.TargetFolderPath = targetFolderPath;
            response.NewPath = newPath;
            response.TargetFolderExists = folderExists;
            response.CreatedFolderCount = apply ? (int?)createdFolderCount : null;
            response.Changed = apply ? (bool?)true : null;
            if (duplicateSibling)
                response.Warnings.Add("The target folder already contains an item with the same name. Navisworks permits duplicates, but use itemId for later operations.");
            return response;
        }

        private static SavedViewpointsManageResponse BuildManageResponse(bool apply, string operation, SavedViewpointNode node)
        {
            return new SavedViewpointsManageResponse
            {
                Apply = apply,
                Operation = operation,
                Name = node.Item.DisplayName ?? string.Empty,
                Path = node.Path,
                NewPath = node.Path,
                Type = node.Item.GetType().Name,
                MatchedItemCount = 1,
                TargetFolderExists = true,
            };
        }

        private static SavedViewpointNode ResolveSavedViewpointNode(GroupItem root, SavedViewpointsManageRequest request, Func<SavedItem, bool> predicate, string notFoundMessage)
        {
            var nodes = BuildSavedViewpointNodes(root)
                .Where(node => predicate == null || predicate(node.Item))
                .ToList();

            if (!string.IsNullOrWhiteSpace(request.ItemId))
            {
                var idMatches = nodes.Where(node => string.Equals(node.Info.ItemId, request.ItemId.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                return SelectSavedViewpointNodeMatch(idMatches, request.Occurrence, notFoundMessage);
            }

            var requested = NormalizeSavedItemPath(request.PathOrName);
            if (string.IsNullOrWhiteSpace(requested))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "PathOrName or itemId is required.");

            var pathMatches = nodes
                .Where(node => string.Equals(NormalizeSavedItemPath(node.Path), requested, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (pathMatches.Count > 0)
                return SelectSavedViewpointNodeMatch(pathMatches, request.Occurrence, notFoundMessage);

            var nameMatches = nodes
                .Where(node => string.Equals(node.Item.DisplayName ?? string.Empty, request.PathOrName.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            return SelectSavedViewpointNodeMatch(nameMatches, request.Occurrence, notFoundMessage);
        }

        private static SavedViewpointNode SelectSavedViewpointNodeMatch(IList<SavedViewpointNode> matches, int? occurrence, string notFoundMessage)
        {
            if (matches == null || matches.Count == 0)
                throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, notFoundMessage);

            if (occurrence.HasValue)
            {
                var index = occurrence.Value - 1;
                if (index < 0 || index >= matches.Count)
                    throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "Occurrence is outside the matched item range.");
                return matches[index];
            }

            if (matches.Count == 1)
                return matches[0];

            throw new AgentCommandException(ErrorCodes.SavedItemAmbiguous, "More than one saved viewpoint item matched. Pass itemId from list_saved_viewpoints/export or an occurrence number.");
        }

        private static GroupItem ResolveSavedViewpointFolderForReorder(GroupItem root, SavedViewpointsReorderRequest request)
        {
            if (root == null)
                throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "The active document has no saved viewpoint root.");
            if (!string.IsNullOrWhiteSpace(request.ItemId))
                return ResolveSavedViewpointNode(root, new SavedViewpointsManageRequest { ItemId = request.ItemId }, item => item is GroupItem, "Folder was not found.").Item as GroupItem;

            var folderPath = NormalizeSavedItemPath(request.FolderPath);
            if (string.IsNullOrWhiteSpace(folderPath))
                return root;

            return ResolveSavedViewpointNode(root, new SavedViewpointsManageRequest { PathOrName = folderPath }, item => item is GroupItem, "Folder was not found.").Item as GroupItem;
        }

        private static void CollectFoldersForReorder(GroupItem folder, bool recursive, IList<GroupItem> folders)
        {
            if (folder == null || folders == null)
                return;

            folders.Add(folder);
            if (!recursive)
                return;

            foreach (SavedItem child in folder.Children)
            {
                var childFolder = child as GroupItem;
                if (childFolder != null)
                    CollectFoldersForReorder(childFolder, true, folders);
            }
        }

        private static SavedViewpointsReorderFolderPlan BuildReorderPlan(GroupItem folder, bool foldersFirst)
        {
            var children = folder.Children.Cast<SavedItem>().ToList();
            var sorted = SortSavedItems(children, foldersFirst).ToList();
            var before = children.Select(item => item.DisplayName ?? string.Empty).ToList();
            var after = sorted.Select(item => item.DisplayName ?? string.Empty).ToList();

            return new SavedViewpointsReorderFolderPlan
            {
                FolderPath = folder.Parent == null ? string.Empty : BuildSavedItemPath(folder),
                ChildCount = children.Count,
                WouldChange = !before.SequenceEqual(after),
                Before = before,
                After = after,
            };
        }

        private static void ApplyFolderReorder(DocumentSavedViewpoints savedViewpoints, GroupItem folder, bool foldersFirst)
        {
            var desired = SortSavedItems(folder.Children.Cast<SavedItem>().ToList(), foldersFirst).ToList();
            for (var targetIndex = 0; targetIndex < desired.Count; targetIndex++)
            {
                var item = desired[targetIndex];
                var currentIndex = folder.Children.IndexOf(item);
                if (currentIndex >= 0 && currentIndex != targetIndex)
                    savedViewpoints.Move(folder, currentIndex, folder, targetIndex);
            }
        }

        private static string NormalizeSavedViewpointsManageOperation(string operation)
        {
            var value = (operation ?? string.Empty).Trim().ToLowerInvariant();
            if (value == "create-folder")
                return "create_folder";
            if (value == "delete-folder")
                return "delete_folder";
            if (value == "delete-many")
                return "delete_many";
            return value;
        }
    }
}
