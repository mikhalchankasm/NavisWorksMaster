using System;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        public SelectionSetsManageResponse SelectionSetsManage(Document document, SelectionSetsManageRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (document.SelectionSets == null || document.SelectionSets.RootItem == null)
                throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, "The active document has no selection sets collection.");

            var operation = NormalizeSelectionSetsManageOperation(request.Operation);
            var apply = request.Apply == true;

            if (string.Equals(operation, "create_folder", StringComparison.OrdinalIgnoreCase))
                return SelectionSetsManageCreateFolder(document.SelectionSets, request, apply, operation);
            if (string.Equals(operation, "delete_folder", StringComparison.OrdinalIgnoreCase))
                return SelectionSetsManageDelete(document.SelectionSets, request, apply, operation, item => item is GroupItem, "Folder was not found.");
            if (string.Equals(operation, "delete_set", StringComparison.OrdinalIgnoreCase))
                return SelectionSetsManageDelete(document.SelectionSets, request, apply, operation, item => item is SelectionSet, "Selection/search set was not found.");
            if (string.Equals(operation, "delete", StringComparison.OrdinalIgnoreCase))
                return SelectionSetsManageDelete(document.SelectionSets, request, apply, operation, item => item is GroupItem || item is SelectionSet, "Selection set item was not found.");
            if (string.Equals(operation, "rename", StringComparison.OrdinalIgnoreCase))
                return SelectionSetsManageRename(document.SelectionSets, request, apply, operation);
            if (string.Equals(operation, "move", StringComparison.OrdinalIgnoreCase))
                return SelectionSetsManageMove(document.SelectionSets, request, apply, operation);

            throw new AgentCommandException(ErrorCodes.SchemaViolation, "Unsupported selection set operation: " + request.Operation);
        }

        private static SelectionSetsManageResponse SelectionSetsManageCreateFolder(DocumentSelectionSets selectionSets, SelectionSetsManageRequest request, bool apply, string operation)
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
            if (!TryResolveSelectionSetFolder(selectionSets, targetFolderPath, false, out targetFolder, out folderExists, out createdFolderCount))
                targetFolder = selectionSets.RootItem;

            var newPath = string.IsNullOrWhiteSpace(targetFolderPath) ? name : targetFolderPath + "/" + name;
            var alreadyExists = folderExists && targetFolder != null && targetFolder.Children.OfType<GroupItem>().Any(item => string.Equals(item.DisplayName ?? string.Empty, name, StringComparison.OrdinalIgnoreCase));
            if (apply && alreadyExists)
                throw new AgentCommandException(ErrorCodes.SelectionSetNameConflict, "A folder with the same name already exists in the target folder.");

            if (apply)
            {
                if (!TryResolveSelectionSetFolder(selectionSets, targetFolderPath, true, out targetFolder, out folderExists, out createdFolderCount))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to resolve the target folder.");
                selectionSets.InsertCopy(targetFolder, targetFolder.Children.Count, new FolderItem { DisplayName = name });
            }

            var response = new SelectionSetsManageResponse
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

        private static SelectionSetsManageResponse SelectionSetsManageDelete(
            DocumentSelectionSets selectionSets,
            SelectionSetsManageRequest request,
            bool apply,
            string operation,
            Func<SavedItem, bool> predicate,
            string notFoundMessage)
        {
            var node = ResolveSelectionSetNode(selectionSets.RootItem, request, predicate, notFoundMessage);
            var folder = node.Item as GroupItem;
            var childCount = GetSavedItemChildCount(folder);
            if (apply && folder != null && childCount > 0 && request.AllowDeleteNonEmptyFolder != true)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Folder is not empty. Pass allowDeleteNonEmptyFolder=true to delete it with all children.");

            if (apply)
                RemoveSelectionSetItem(selectionSets, node);

            var response = BuildSelectionSetsManageResponse(apply, operation, node);
            response.Changed = apply ? (bool?)true : null;
            if (!apply && folder != null && childCount > 0)
                response.Warnings.Add("Folder contains " + childCount.ToString(CultureInfo.InvariantCulture) + " direct child item(s). Pass allowDeleteNonEmptyFolder=true before applying a delete.");
            return response;
        }

        private static SelectionSetsManageResponse SelectionSetsManageRename(DocumentSelectionSets selectionSets, SelectionSetsManageRequest request, bool apply, string operation)
        {
            var newName = (request.NewName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newName))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "NewName is required.");

            var node = ResolveSelectionSetNode(selectionSets.RootItem, request, item => item is GroupItem || item is SelectionSet, "Selection set item was not found.");
            var oldPath = node.Path;
            var newPath = string.IsNullOrWhiteSpace(node.ParentPath) ? newName : node.ParentPath + "/" + newName;
            var duplicateSibling = node.Parent != null && node.Parent.Children
                .Cast<SavedItem>()
                .Any(item => item != node.Item && string.Equals(item.DisplayName ?? string.Empty, newName, StringComparison.OrdinalIgnoreCase));

            if (apply)
            {
                var parent = node.Parent ?? selectionSets.RootItem;
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

                selectionSets.ReplaceWithCopy(parent, currentIndex, replacement);
            }

            var response = BuildSelectionSetsManageResponse(apply, operation, node);
            response.NewName = newName;
            response.NewPath = newPath;
            response.Path = oldPath;
            response.Changed = apply ? (bool?)true : null;
            if (duplicateSibling)
                response.Warnings.Add("The target folder already contains another item with the new name. Navisworks permits duplicates, but use itemId for later operations.");
            return response;
        }

        private static SelectionSetsManageResponse SelectionSetsManageMove(DocumentSelectionSets selectionSets, SelectionSetsManageRequest request, bool apply, string operation)
        {
            var targetFolderPath = NormalizeFolderPath(request.TargetFolderPath);
            var node = ResolveSelectionSetNode(selectionSets.RootItem, request, item => item is GroupItem || item is SelectionSet, "Selection set item was not found.");

            GroupItem targetFolder;
            bool folderExists;
            int createdFolderCount;
            if (!TryResolveSelectionSetFolder(selectionSets, targetFolderPath, false, out targetFolder, out folderExists, out createdFolderCount))
                targetFolder = selectionSets.RootItem;

            if (node.Parent == null)
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Cannot move the selection sets root.");
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
            var duplicateSibling = folderExists && targetFolder != null && targetFolder.Children
                .Cast<SavedItem>()
                .Any(item => item != node.Item && string.Equals(item.DisplayName ?? string.Empty, node.Item.DisplayName ?? string.Empty, StringComparison.OrdinalIgnoreCase));

            if (apply)
            {
                if (!TryResolveSelectionSetFolder(selectionSets, targetFolderPath, true, out targetFolder, out folderExists, out createdFolderCount))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to resolve the target folder.");

                var sourceIndex = node.Parent.Children.IndexOf(node.Item);
                if (sourceIndex < 0)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to determine source item index for move.");
                selectionSets.Move(node.Parent, sourceIndex, targetFolder, targetFolder.Children.Count);
            }

            var response = BuildSelectionSetsManageResponse(apply, operation, node);
            response.TargetFolderPath = targetFolderPath;
            response.NewPath = newPath;
            response.TargetFolderExists = folderExists;
            response.CreatedFolderCount = apply ? (int?)createdFolderCount : null;
            response.Changed = apply ? (bool?)true : null;
            if (duplicateSibling)
                response.Warnings.Add("The target folder already contains an item with the same name. Navisworks permits duplicates, but use itemId for later operations.");
            return response;
        }

        private static SelectionSetsManageResponse BuildSelectionSetsManageResponse(bool apply, string operation, SelectionSetNode node)
        {
            return new SelectionSetsManageResponse
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

        private static void RemoveSelectionSetItem(DocumentSelectionSets selectionSets, SelectionSetNode node)
        {
            if (selectionSets == null)
                throw new ArgumentNullException(nameof(selectionSets));
            if (node == null || node.Item == null)
                throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, "Selection set item was not found.");

            var parent = node.Parent ?? selectionSets.RootItem;
            var index = parent == null || parent.Children == null ? -1 : parent.Children.IndexOf(node.Item);
            if (parent == null || index < 0)
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to determine parent/index for delete: " + node.Path);

            if (selectionSets.Remove(parent, node.Item))
                return;

            selectionSets.RemoveAt(parent, index);
        }

        private static string NormalizeSelectionSetsManageOperation(string operation)
        {
            var value = (operation ?? string.Empty).Trim().ToLowerInvariant();
            if (value == "create-folder")
                return "create_folder";
            if (value == "delete-folder")
                return "delete_folder";
            if (value == "delete-set")
                return "delete_set";
            return value;
        }
    }
}
