using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        public SelectionSetsReorderResponse SelectionSetsReorder(Document document, SelectionSetsReorderRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (document.SelectionSets == null || document.SelectionSets.RootItem == null)
                throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, "The active document has no selection sets collection.");

            var apply = request.Apply == true;
            var recursive = request.Recursive.GetValueOrDefault(true);
            var foldersFirst = request.FoldersFirst.GetValueOrDefault(true);
            var targetFolder = ResolveSelectionSetFolderForReorder(document.SelectionSets.RootItem, request);
            var folderPath = targetFolder == document.SelectionSets.RootItem ? string.Empty : BuildSavedItemPath(targetFolder);
            var folders = new List<GroupItem>();
            CollectSelectionSetFoldersForReorder(targetFolder, recursive, folders);

            var response = new SelectionSetsReorderResponse
            {
                Apply = apply,
                FolderPath = folderPath,
                Recursive = recursive,
                FoldersFirst = foldersFirst,
                ProcessedFolderCount = folders.Count,
            };

            foreach (var folder in folders)
            {
                var plan = BuildSelectionSetReorderPlan(folder, foldersFirst);
                response.Folders.Add(plan);
                if (!plan.WouldChange)
                    continue;

                response.ReorderedFolderCount++;
                response.MovedItemCount += CountChangedPositions(plan.Before, plan.After);
                if (apply)
                    ApplySelectionSetFolderReorder(document.SelectionSets, folder, foldersFirst);
            }

            response.Changed = apply ? (bool?)(response.ReorderedFolderCount > 0) : null;
            return response;
        }

        private static GroupItem ResolveSelectionSetFolderForReorder(GroupItem root, SelectionSetsReorderRequest request)
        {
            if (root == null)
                throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, "The active document has no selection set root.");
            if (!string.IsNullOrWhiteSpace(request.ItemId))
                return ResolveSelectionSetNode(root, new SelectionSetsManageRequest { ItemId = request.ItemId }, item => item is GroupItem, "Folder was not found.").Item as GroupItem;

            var folderPath = NormalizeSavedItemPath(request.FolderPath);
            if (string.IsNullOrWhiteSpace(folderPath))
                return root;

            return ResolveSelectionSetNode(root, new SelectionSetsManageRequest { PathOrName = folderPath }, item => item is GroupItem, "Folder was not found.").Item as GroupItem;
        }

        private static void CollectSelectionSetFoldersForReorder(GroupItem folder, bool recursive, IList<GroupItem> folders)
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
                    CollectSelectionSetFoldersForReorder(childFolder, true, folders);
            }
        }

        private static SelectionSetsReorderFolderPlan BuildSelectionSetReorderPlan(GroupItem folder, bool foldersFirst)
        {
            var children = folder.Children.Cast<SavedItem>().ToList();
            var sorted = SortSavedItems(children, foldersFirst).ToList();
            var before = children.Select(item => item.DisplayName ?? string.Empty).ToList();
            var after = sorted.Select(item => item.DisplayName ?? string.Empty).ToList();

            return new SelectionSetsReorderFolderPlan
            {
                FolderPath = folder.Parent == null ? string.Empty : BuildSavedItemPath(folder),
                ChildCount = children.Count,
                WouldChange = !before.SequenceEqual(after),
                Before = before,
                After = after,
            };
        }

        private static void ApplySelectionSetFolderReorder(DocumentSelectionSets selectionSets, GroupItem folder, bool foldersFirst)
        {
            var desired = SortSavedItems(folder.Children.Cast<SavedItem>().ToList(), foldersFirst).ToList();
            for (var targetIndex = 0; targetIndex < desired.Count; targetIndex++)
            {
                var item = desired[targetIndex];
                var currentIndex = folder.Children.IndexOf(item);
                if (currentIndex >= 0 && currentIndex != targetIndex)
                    selectionSets.Move(folder, currentIndex, folder, targetIndex);
            }
        }
    }
}
