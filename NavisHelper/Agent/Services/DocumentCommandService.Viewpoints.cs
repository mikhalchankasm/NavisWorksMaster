using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        private static List<SavedViewpointNode> BuildSavedViewpointNodes(GroupItem root)
        {
            var nodes = new List<SavedViewpointNode>();
            if (root == null)
                return nodes;

            var index = 0;
            foreach (SavedItem child in root.Children)
            {
                CollectSavedViewpointNodes(child, root, string.Empty, 0, FormatSavedViewpointItemId(index), nodes);
                index++;
            }
            return nodes;
        }

        private static void CollectSavedViewpointNodes(SavedItem item, GroupItem parent, string parentPath, int depth, string itemId, IList<SavedViewpointNode> nodes)
        {
            if (item == null)
                return;

            var name = item.DisplayName ?? string.Empty;
            var path = string.IsNullOrWhiteSpace(parentPath) ? name : parentPath + "/" + name;
            var groupItem = item as GroupItem;
            var savedViewpoint = item as SavedViewpoint;
            var childCount = GetSavedItemChildCount(groupItem);
            var info = new SavedViewpointItemInfo
            {
                ItemId = itemId,
                Name = name,
                Path = path,
                ParentPath = parentPath ?? string.Empty,
                Type = item.GetType().Name,
                Depth = depth,
                Index = GetSavedItemIndex(item),
                ChildCount = childCount,
                ContainsVisibilityOverrides = savedViewpoint == null ? (bool?)null : savedViewpoint.ContainsVisibilityOverrides,
                ContainsAppearanceOverrides = savedViewpoint == null ? (bool?)null : savedViewpoint.ContainsAppearanceOverrides,
            };

            nodes.Add(new SavedViewpointNode
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
                CollectSavedViewpointNodes(child, groupItem, path, depth + 1, itemId + "." + childIndex.ToString("D4"), nodes);
                childIndex++;
            }
        }

        private static string FormatSavedViewpointItemId(int index)
        {
            return "sv:" + index.ToString("D4");
        }

        private static int GetSavedItemIndex(SavedItem item)
        {
            if (item == null || item.Parent == null)
                return -1;

            try
            {
                return item.Parent.Children.IndexOf(item);
            }
            catch
            {
                return -1;
            }
        }

        private static string BuildSavedItemPath(SavedItem item)
        {
            if (item == null)
                return string.Empty;

            var names = new Stack<string>();
            var current = item;
            while (current != null && current.Parent != null)
            {
                names.Push(current.DisplayName ?? string.Empty);
                current = current.Parent;
            }

            return string.Join("/", names.ToArray());
        }

        private static bool IsDescendantFolder(GroupItem possibleDescendant, GroupItem ancestor)
        {
            var current = possibleDescendant as SavedItem;
            while (current != null)
            {
                if (current == ancestor)
                    return true;
                current = current.Parent;
            }

            return false;
        }

        private static int CountSavedItemTree(SavedItem item)
        {
            if (item == null)
                return 0;

            var count = 1;
            var groupItem = item as GroupItem;
            if (groupItem == null)
                return count;

            foreach (SavedItem child in groupItem.Children)
                count += CountSavedItemTree(child);

            return count;
        }

        private static IEnumerable<SavedItem> SortSavedItems(IEnumerable<SavedItem> items, bool foldersFirst)
        {
            var query = items ?? Enumerable.Empty<SavedItem>();
            if (foldersFirst)
            {
                return query
                    .OrderBy(item => item is GroupItem ? 0 : 1)
                    .ThenBy(item => item.DisplayName ?? string.Empty, NaturalStringComparer.Instance)
                    .ThenBy(item => item.GetType().Name, StringComparer.OrdinalIgnoreCase);
            }

            return query
                .OrderBy(item => item.DisplayName ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(item => item is GroupItem ? 0 : 1)
                .ThenBy(item => item.GetType().Name, StringComparer.OrdinalIgnoreCase);
        }

        private static int CountChangedPositions(IList<string> before, IList<string> after)
        {
            if (before == null || after == null)
                return 0;

            var count = 0;
            var max = Math.Min(before.Count, after.Count);
            for (var i = 0; i < max; i++)
            {
                if (!string.Equals(before[i], after[i], StringComparison.Ordinal))
                    count++;
            }

            return count + Math.Abs(before.Count - after.Count);
        }

        private sealed class SavedViewpointNode
        {
            public SavedItem Item { get; set; }
            public GroupItem Parent { get; set; }
            public string Path { get; set; }
            public string ParentPath { get; set; }
            public SavedViewpointItemInfo Info { get; set; }
        }
    }
}
