using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.DocumentParts;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        private const int DefaultSelectionPreviewLimit = 20;
        private const int MaxSelectionPreviewLimit = 100;
        private const int DefaultSelectionCopyNamesLimit = 10000;
        private const int MaxSelectionCopyNamesLimit = 100000;
        private const int DefaultSelectedItemsTreeMaxItems = 10000;
        private const int MaxSelectedItemsTreeMaxItems = 100000;
        private const string SelectedItemsTreeFormatTree = "tree";
        private const string SelectedItemsTreeFormatFlat = "flat";
        private const int DefaultPropertyItemLimit = 5;
        private const int MaxPropertyItemLimit = 20;
        private const int DefaultPropertyLimit = 50;
        private const int MaxPropertyLimit = 200;
        private const int DefaultSavedViewpointsLimit = 200;
        private const int MaxSavedViewpointsLimit = 1000;
        private const int DefaultVisibilityPreviewLimit = 10;
        private const int MaxVisibilityPreviewLimit = 50;
        private const int VisibilityRootSummaryLimit = 20;
        private const string ItemInternalCategory = "LcOaNode";
        private const string SourceFileInternalProperty = "LcOaNodeSourceFile";
        private static readonly Tuple<string, string>[] SourceFileDisplayProperties =
        {
            Tuple.Create("Item", "Source File"),
            Tuple.Create("Элемент", "Файл источника"),
            Tuple.Create(string.Empty, "Source File"),
            Tuple.Create(string.Empty, "Файл источника"),
        };


        private static void CollectItemsToHide(
            ModelItem item,
            ISet<string> itemsToKeepVisible,
            ICollection<ModelItem> itemsToHide,
            ModelItem rootItem,
            VisibilityRootSummaryAccumulator rootSummaries)
        {
            if (item == null)
                return;

            if (itemsToKeepVisible.Contains(BuildItemPath(item)))
            {
                foreach (ModelItem childItem in item.Children)
                {
                    CollectItemsToHide(childItem, itemsToKeepVisible, itemsToHide, rootItem, rootSummaries);
                }

                return;
            }

            itemsToHide.Add(item);
            rootSummaries.Add(rootItem, item);
        }

        private static void CollectVisibleSelectedItems(
            ModelItem item,
            ISet<string> selectedPaths,
            ICollection<ModelItem> itemsToHide,
            ModelItem rootItem,
            VisibilityRootSummaryAccumulator rootSummaries)
        {
            if (item == null)
                return;

            var path = BuildItemPath(item);
            if (selectedPaths.Add(path) && !item.IsHidden)
            {
                itemsToHide.Add(item);
                rootSummaries.Add(rootItem, item);
            }

            foreach (ModelItem childItem in item.Children)
            {
                CollectVisibleSelectedItems(childItem, selectedPaths, itemsToHide, rootItem, rootSummaries);
            }
        }

        private static void CollectHiddenSelectedItems(
            ModelItem item,
            ISet<string> selectedPaths,
            ICollection<ModelItem> itemsToReveal,
            bool includeHiddenAncestors,
            ModelItem rootItem,
            VisibilityRootSummaryAccumulator rootSummaries)
        {
            if (item == null)
                return;

            AddHiddenItem(item, selectedPaths, itemsToReveal, rootItem, rootSummaries);

            if (includeHiddenAncestors)
                AddHiddenAncestors(item, selectedPaths, itemsToReveal, rootItem, rootSummaries);

            foreach (ModelItem childItem in item.Children)
            {
                CollectHiddenSelectedItems(childItem, selectedPaths, itemsToReveal, includeHiddenAncestors, rootItem, rootSummaries);
            }
        }

        private static void AddHiddenItem(
            ModelItem item,
            ISet<string> selectedPaths,
            ICollection<ModelItem> itemsToReveal,
            ModelItem rootItem,
            VisibilityRootSummaryAccumulator rootSummaries)
        {
            if (item == null)
                return;

            var path = BuildItemPath(item);
            if (item.IsHidden && selectedPaths.Add(path))
            {
                itemsToReveal.Add(item);
                rootSummaries.Add(rootItem, item);
            }
        }

        private static void AddHiddenAncestors(
            ModelItem item,
            ISet<string> selectedPaths,
            ICollection<ModelItem> itemsToReveal,
            ModelItem rootItem,
            VisibilityRootSummaryAccumulator rootSummaries)
        {
            var current = item == null ? null : item.Parent;
            while (current != null)
            {
                var path = BuildItemPath(current);
                if (selectedPaths.Contains(path))
                    break;

                if (current.IsHidden && selectedPaths.Add(path))
                {
                    itemsToReveal.Add(current);
                    rootSummaries.Add(rootItem, current);
                }

                current = current.Parent;
            }
        }

        private static void CollectItems(ModelItem item, ICollection<ModelItem> items)
        {
            if (item == null)
                return;

            items.Add(item);
            foreach (ModelItem childItem in item.Children)
            {
                CollectItems(childItem, items);
            }
        }

        private static ModelItemCollection SnapshotSelection(ModelItemCollection selectedItems)
        {
            var snapshot = new ModelItemCollection();
            if (selectedItems != null && selectedItems.Count > 0)
                snapshot.AddRange(selectedItems);

            return snapshot;
        }

        private static void RestoreSelection(Document document, ModelItemCollection selectionSnapshot)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            document.CurrentSelection.CopyFrom(selectionSnapshot ?? new ModelItemCollection());
        }

        private static void ClearSelection(Document document)
        {
            RestoreSelection(document, new ModelItemCollection());
        }


        private static int ClampSelectionPreviewLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultSelectionPreviewLimit);
            if (value < 1)
                return 1;
            if (value > MaxSelectionPreviewLimit)
                return MaxSelectionPreviewLimit;
            return value;
        }

        private static int ClampSelectionCopyNamesLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultSelectionCopyNamesLimit);
            if (value < 1)
                return 1;
            if (value > MaxSelectionCopyNamesLimit)
                return MaxSelectionCopyNamesLimit;
            return value;
        }

        private static int ClampPropertyItemLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultPropertyItemLimit);
            if (value < 1)
                return 1;
            if (value > MaxPropertyItemLimit)
                return MaxPropertyItemLimit;
            return value;
        }

        private static int ClampPropertyLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultPropertyLimit);
            if (value < 1)
                return 1;
            if (value > MaxPropertyLimit)
                return MaxPropertyLimit;
            return value;
        }

        private static int ClampSavedViewpointsLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultSavedViewpointsLimit);
            if (value < 1)
                return 1;
            if (value > MaxSavedViewpointsLimit)
                return MaxSavedViewpointsLimit;
            return value;
        }

        private static int ClampListOffset(int? offset)
        {
            var value = offset.GetValueOrDefault(0);
            return value < 0 ? 0 : value;
        }

        private static int ClampVisibilityPreviewLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultVisibilityPreviewLimit);
            if (value < 1)
                return 1;
            if (value > MaxVisibilityPreviewLimit)
                return MaxVisibilityPreviewLimit;
            return value;
        }

        private static List<VisibilityPreviewItem> BuildVisibilityPreview(IEnumerable<ModelItem> items, int limit)
        {
            var result = new List<VisibilityPreviewItem>();
            if (items == null)
                return result;

            foreach (var item in items.Take(limit))
            {
                result.Add(new VisibilityPreviewItem
                {
                    DisplayName = item == null ? string.Empty : item.DisplayName ?? string.Empty,
                    Path = BuildItemPath(item),
                    SourceFile = TryGetSourceFile(item) ?? string.Empty,
                    IsHidden = item != null && item.IsHidden,
                });
            }

            return result;
        }

        private sealed class VisibilityRootSummaryAccumulator
        {
            private readonly Dictionary<string, VisibilityRootSummaryInput> _inputs =
                new Dictionary<string, VisibilityRootSummaryInput>(StringComparer.OrdinalIgnoreCase);

            public void Add(ModelItem rootItem, ModelItem fallbackItem)
            {
                if (rootItem == null)
                    return;

                var rootPath = BuildItemPath(rootItem);
                var rootDisplayName = rootItem.DisplayName ?? string.Empty;
                var key = string.IsNullOrWhiteSpace(rootPath) ? rootDisplayName : rootPath;
                VisibilityRootSummaryInput input;
                if (!_inputs.TryGetValue(key, out input))
                {
                    var sourceFile = TryGetSourceFile(rootItem);
                    if (string.IsNullOrWhiteSpace(sourceFile))
                        sourceFile = TryGetSourceFile(fallbackItem);

                    input = new VisibilityRootSummaryInput
                    {
                        RootDisplayName = rootDisplayName,
                        RootPath = rootPath,
                        SourceFile = sourceFile ?? string.Empty,
                        AffectedItemCount = 0,
                    };
                    _inputs.Add(key, input);
                }

                input.AffectedItemCount++;
            }

            public VisibilityRootSummaryResult Build()
            {
                return VisibilityRootSummaryHelper.Summarize(_inputs.Values, VisibilityRootSummaryLimit);
            }
        }

        private static ModelItem GetRootItem(ModelItem item)
        {
            var root = item;
            while (root != null && root.Parent != null)
                root = root.Parent;
            return root;
        }

        private static HashSet<string> NormalizeCategoryFilters(IEnumerable<string> filters)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (filters == null)
                return result;

            foreach (var filter in filters)
            {
                if (!string.IsNullOrWhiteSpace(filter))
                    result.Add(filter.Trim());
            }

            return result;
        }

        private static ItemPropertiesPreview BuildItemPropertiesPreview(
            ModelItem item,
            int propertyLimit,
            bool includeInternalNames,
            ISet<string> categoryFilters)
        {
            var preview = new ItemPropertiesPreview
            {
                DisplayName = item == null ? string.Empty : item.DisplayName ?? string.Empty,
                ClassDisplayName = item == null ? string.Empty : item.ClassDisplayName ?? string.Empty,
                Path = BuildItemPath(item),
                SourceFile = TryGetSourceFile(item) ?? string.Empty,
            };

            if (item == null || item.PropertyCategories == null)
                return preview;

            var remaining = propertyLimit;
            foreach (PropertyCategory category in item.PropertyCategories)
            {
                if (remaining <= 0)
                    break;
                if (!CategoryMatches(category, categoryFilters))
                    continue;

                var categoryInfo = new ItemPropertyCategoryInfo
                {
                    DisplayName = category.DisplayName ?? string.Empty,
                    InternalName = includeInternalNames ? category.Name ?? string.Empty : string.Empty,
                };

                foreach (DataProperty property in category.Properties)
                {
                    if (remaining <= 0)
                        break;
                    if (property == null)
                        continue;

                    categoryInfo.Properties.Add(new ItemPropertyInfo
                    {
                        DisplayName = property.DisplayName ?? string.Empty,
                        InternalName = includeInternalNames ? property.Name ?? string.Empty : string.Empty,
                        Value = GetPropertyDisplayValue(property),
                        ValueType = property.Value == null ? string.Empty : property.Value.GetType().Name,
                    });
                    remaining--;
                }

                if (categoryInfo.Properties.Count > 0)
                    preview.Categories.Add(categoryInfo);
            }

            return preview;
        }

        private static bool HasMoreProperties(ModelItem item, int propertyLimit, ISet<string> categoryFilters)
        {
            if (item == null || item.PropertyCategories == null)
                return false;

            var count = 0;
            foreach (PropertyCategory category in item.PropertyCategories)
            {
                if (!CategoryMatches(category, categoryFilters))
                    continue;

                foreach (DataProperty property in category.Properties)
                {
                    if (property == null)
                        continue;

                    count++;
                    if (count > propertyLimit)
                        return true;
                }
            }

            return false;
        }

        private static bool CategoryMatches(PropertyCategory category, ISet<string> categoryFilters)
        {
            if (category == null)
                return false;
            if (categoryFilters == null || categoryFilters.Count == 0)
                return true;

            return categoryFilters.Contains(category.DisplayName ?? string.Empty) ||
                   categoryFilters.Contains(category.Name ?? string.Empty);
        }

        private static IEnumerable<SelectionSetItemInfo> FilterSelectionSetItems(IEnumerable<SelectionSetItemInfo> items, ListSelectionSetsRequest request)
        {
            if (items == null)
                return Enumerable.Empty<SelectionSetItemInfo>();

            var result = items;
            var pathPrefix = NormalizeSavedItemPath(request == null ? null : request.PathPrefix);
            if (!string.IsNullOrWhiteSpace(pathPrefix))
            {
                result = result.Where(item =>
                {
                    var path = NormalizeSavedItemPath(item == null ? null : item.Path);
                    return string.Equals(path, pathPrefix, StringComparison.OrdinalIgnoreCase) ||
                           path.StartsWith(pathPrefix + "/", StringComparison.OrdinalIgnoreCase);
                });
            }

            var nameContains = request == null ? null : request.NameContains;
            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                var needle = nameContains.Trim();
                result = result.Where(item => (item == null ? string.Empty : item.Name ?? string.Empty)
                    .IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return result;
        }

        private static void CollectSavedViewpointItems(SavedItem item, string parentPath, int depth, ICollection<SavedViewpointItemInfo> items)
        {
            if (item == null || items == null)
                return;

            var name = item.DisplayName ?? string.Empty;
            var path = string.IsNullOrWhiteSpace(parentPath) ? name : parentPath + "/" + name;
            var groupItem = item as GroupItem;
            var savedViewpoint = item as SavedViewpoint;
            var childCount = groupItem == null ? 0 : groupItem.Children.Count();

            items.Add(new SavedViewpointItemInfo
            {
                ItemId = "sv:" + (items.Count + 1).ToString("D6"),
                Name = name,
                Path = path,
                ParentPath = parentPath ?? string.Empty,
                Type = item.GetType().Name,
                Depth = depth,
                Index = GetSavedItemIndex(item),
                ChildCount = childCount,
                ContainsVisibilityOverrides = savedViewpoint == null ? (bool?)null : savedViewpoint.ContainsVisibilityOverrides,
                ContainsAppearanceOverrides = savedViewpoint == null ? (bool?)null : savedViewpoint.ContainsAppearanceOverrides,
            });

            if (groupItem == null)
                return;

            foreach (SavedItem child in groupItem.Children)
                CollectSavedViewpointItems(child, path, depth + 1, items);
        }

        private static void CollectSelectionSetItems(SavedItem item, string parentPath, int depth, ICollection<SelectionSetItemInfo> items)
        {
            if (item == null || items == null)
                return;

            var name = item.DisplayName ?? string.Empty;
            var path = string.IsNullOrWhiteSpace(parentPath) ? name : parentPath + "/" + name;
            var groupItem = item as GroupItem;
            var selectionSet = item as SelectionSet;
            var childCount = GetSavedItemChildCount(groupItem);
            var explicitItemCount = GetExplicitModelItemCount(selectionSet);

            items.Add(new SelectionSetItemInfo
            {
                Name = name,
                Path = path,
                Type = item.GetType().Name,
                Depth = depth,
                ChildCount = childCount,
                ExplicitItemCount = explicitItemCount,
            });

            if (groupItem == null)
                return;

            foreach (SavedItem child in groupItem.Children)
                CollectSelectionSetItems(child, path, depth + 1, items);
        }

        private static ResolvedSavedItem ResolveSingleSavedItem(
            GroupItem root,
            string pathOrName,
            Func<SavedItem, bool> predicate,
            string notFoundCode,
            string notFoundMessage)
        {
            var matches = new List<ResolvedSavedItem>();
            foreach (SavedItem child in root.Children)
                CollectSavedItemMatches(child, string.Empty, predicate, matches);

            var requestedPath = NormalizeSavedItemPath(pathOrName);
            var pathMatches = matches
                .Where(match => string.Equals(NormalizeSavedItemPath(match.Path), requestedPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (pathMatches.Count == 1)
                return pathMatches[0];
            if (pathMatches.Count > 1)
                throw new AgentCommandException(ErrorCodes.SavedItemAmbiguous, "More than one saved item has the requested path.");

            var requestedName = (pathOrName ?? string.Empty).Trim();
            var nameMatches = matches
                .Where(match => string.Equals(match.Item.DisplayName ?? string.Empty, requestedName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (nameMatches.Count == 1)
                return nameMatches[0];
            if (nameMatches.Count > 1)
                throw new AgentCommandException(ErrorCodes.SavedItemAmbiguous, "More than one saved item has the requested name. Use the full path returned by the list tool.");

            throw new AgentCommandException(notFoundCode, notFoundMessage);
        }

        private static void CollectSavedItemMatches(
            SavedItem item,
            string parentPath,
            Func<SavedItem, bool> predicate,
            ICollection<ResolvedSavedItem> matches)
        {
            if (item == null || matches == null)
                return;

            var name = item.DisplayName ?? string.Empty;
            var path = string.IsNullOrWhiteSpace(parentPath) ? name : parentPath + "/" + name;
            if (predicate == null || predicate(item))
            {
                matches.Add(new ResolvedSavedItem
                {
                    Item = item,
                    Path = path,
                });
            }

            var groupItem = item as GroupItem;
            if (groupItem == null)
                return;

            foreach (SavedItem child in groupItem.Children)
                CollectSavedItemMatches(child, path, predicate, matches);
        }

        private static string NormalizeSavedItemPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim().Replace('\\', '/').Trim('/');
        }

        private static ModelItemCollection BuildSelectionSetItems(Document document, SavedItem savedItem)
        {
            var result = new ModelItemCollection();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddSelectionSetItems(document, savedItem, result, seen);
            return result;
        }

        private static void AddSelectionSetItems(Document document, SavedItem savedItem, ModelItemCollection result, ISet<string> seen)
        {
            if (savedItem == null || result == null || seen == null)
                return;

            var selectionSet = savedItem as SelectionSet;
            if (selectionSet != null)
            {
                var selectedItems = selectionSet.GetSelectedItems(document);
                foreach (ModelItem item in selectedItems)
                {
                    var path = BuildItemPath(item);
                    if (seen.Add(path))
                        result.Add(item);
                }

                return;
            }

            var groupItem = savedItem as GroupItem;
            if (groupItem == null)
                return;

            foreach (SavedItem child in groupItem.Children)
                AddSelectionSetItems(document, child, result, seen);
        }

        private static int CountSelectionSets(SavedItem savedItem)
        {
            if (savedItem == null)
                return 0;
            if (savedItem is SelectionSet)
                return 1;

            var groupItem = savedItem as GroupItem;
            if (groupItem == null)
                return 0;

            var count = 0;
            foreach (SavedItem child in groupItem.Children)
                count += CountSelectionSets(child);
            return count;
        }

        private sealed class ResolvedSavedItem
        {
            public SavedItem Item { get; set; }
            public string Path { get; set; }
        }

        private static int GetSavedItemChildCount(GroupItem groupItem)
        {
            if (groupItem == null)
                return 0;

            try
            {
                return groupItem.Children.Count();
            }
            catch
            {
                return 0;
            }
        }

        private static int GetExplicitModelItemCount(SelectionSet selectionSet)
        {
            if (selectionSet == null)
                return 0;

            try
            {
                return selectionSet.ExplicitModelItems == null ? 0 : selectionSet.ExplicitModelItems.Count;
            }
            catch
            {
                return 0;
            }
        }

        private static object GetObjectProperty(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            try
            {
                var property = instance.GetType().GetProperty(propertyName);
                return property == null ? null : property.GetValue(instance, null);
            }
            catch
            {
                return null;
            }
        }

        private static void AddViewpointProperty(IDictionary<string, string> properties, object viewpoint, string propertyName)
        {
            if (properties == null || viewpoint == null || string.IsNullOrWhiteSpace(propertyName))
                return;

            var value = GetObjectProperty(viewpoint, propertyName);
            if (value != null)
                properties[propertyName] = Convert.ToString(value);
        }

        private static BoundingBoxInfo ToBoundingBoxInfo(BoundingBox3D box)
        {
            if (box == null)
                return null;

            var min = box.Min;
            var max = box.Max;
            return new BoundingBoxInfo
            {
                Min = ToPoint3Info(min),
                Max = ToPoint3Info(max),
                Center = new Point3Info
                {
                    X = (min.X + max.X) / 2.0,
                    Y = (min.Y + max.Y) / 2.0,
                    Z = (min.Z + max.Z) / 2.0,
                },
                Size = new Point3Info
                {
                    X = max.X - min.X,
                    Y = max.Y - min.Y,
                    Z = max.Z - min.Z,
                },
            };
        }

        private static BoundingBoxInfo TryBuildBoundingBoxInfo(ModelItem item)
        {
            if (item == null)
                return null;

            try
            {
                return ToBoundingBoxInfo(item.BoundingBox());
            }
            catch
            {
                return null;
            }
        }

        private static int ClampSelectedItemsTreeMaxItems(int? value)
        {
            var requested = value.GetValueOrDefault(DefaultSelectedItemsTreeMaxItems);
            if (requested < 1)
                return 1;
            if (requested > MaxSelectedItemsTreeMaxItems)
                return MaxSelectedItemsTreeMaxItems;

            return requested;
        }

        private static int? NormalizeSelectedItemsTreeMaxDepth(int? value)
        {
            if (!value.HasValue)
                return null;
            if (value.Value < 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "maxDepth must be greater than or equal to 0.");

            return value.Value;
        }

        private static string NormalizeSelectedItemsTreeFormat(string format)
        {
            if (string.IsNullOrWhiteSpace(format))
                return SelectedItemsTreeFormatTree;

            var normalized = format.Trim().ToLowerInvariant();
            if (string.Equals(normalized, SelectedItemsTreeFormatTree, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, SelectedItemsTreeFormatFlat, StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            throw new AgentCommandException(ErrorCodes.SchemaViolation, "format must be either 'tree' or 'flat'.");
        }

        private static List<ModelItem> BuildAncestorChain(ModelItem item)
        {
            var ancestors = new List<ModelItem>();
            var current = item == null ? null : item.Parent;

            while (current != null)
            {
                ancestors.Add(current);
                current = current.Parent;
            }

            ancestors.Reverse();
            return ancestors;
        }

        private static List<ModelItem> BuildItemChain(ModelItem item)
        {
            var chain = BuildAncestorChain(item);
            if (item != null)
                chain.Add(item);

            return chain;
        }

        private static SelectedItemHierarchyNode BuildSelectedItemHierarchyNode(ModelItem item, int depth, bool isSelectedItem, bool includeBoundingBox)
        {
            if (item == null)
                return null;

            return new SelectedItemHierarchyNode
            {
                Depth = depth,
                IsSelectedItem = isSelectedItem,
                DisplayName = item.DisplayName ?? string.Empty,
                ClassDisplayName = item.ClassDisplayName ?? string.Empty,
                Path = BuildItemPath(item),
                SourceFile = TryGetSourceFile(item) ?? string.Empty,
                IsHidden = item.IsHidden,
                ChildCount = item.Children == null ? 0 : item.Children.Count(),
                BoundingBox = includeBoundingBox ? TryBuildBoundingBoxInfo(item) : null,
            };
        }

        private static SelectedItemsTreeFlatItem BuildSelectedItemsTreeFlatItem(
            int selectionIndex,
            ModelItem selectedItem,
            IList<ModelItem> chainItems,
            string rootName,
            string sourceFile,
            int selectedDepth,
            int? maxDepth,
            bool includeBoundingBoxes,
            SelectedItemsTreeResponse response)
        {
            var item = new SelectedItemsTreeFlatItem
            {
                SelectionIndex = selectionIndex,
                Name = GetItemDisplayName(selectedItem),
                DisplayName = selectedItem.DisplayName ?? string.Empty,
                Path = BuildItemPath(selectedItem),
                Depth = selectedDepth,
                RootName = rootName ?? string.Empty,
                SourceFile = sourceFile ?? string.Empty,
                IsSelectedLeaf = true,
                BoundingBox = includeBoundingBoxes ? TryBuildBoundingBoxInfo(selectedItem) : null,
            };

            for (var depth = 0; depth < chainItems.Count; depth++)
            {
                if (maxDepth.HasValue && depth > maxDepth.Value)
                {
                    response.DepthTruncated = true;
                    break;
                }

                var chainItem = chainItems[depth];
                item.Chain.Add(BuildSelectedItemsTreePathNode(
                    chainItem,
                    depth,
                    rootName,
                    sourceFile,
                    depth == selectedDepth,
                    includeBoundingBoxes));
            }

            return item;
        }

        private static void AddSelectedItemTreePath(
            IDictionary<string, SelectedItemsTreeBuildNode> rootBuilders,
            IList<ModelItem> chainItems,
            string rootName,
            string sourceFile,
            string selectedPath,
            int? maxDepth,
            bool includeBoundingBoxes,
            SelectedItemsTreeResponse response)
        {
            IDictionary<string, SelectedItemsTreeBuildNode> siblings = rootBuilders;
            SelectedItemsTreeBuildNode currentBuilder = null;

            for (var depth = 0; depth < chainItems.Count; depth++)
            {
                if (maxDepth.HasValue && depth > maxDepth.Value)
                {
                    response.DepthTruncated = true;
                    return;
                }

                var item = chainItems[depth];
                var path = BuildItemPath(item);
                SelectedItemsTreeBuildNode nextBuilder;
                if (!siblings.TryGetValue(path, out nextBuilder))
                {
                    var node = BuildSelectedItemsTreeNode(
                        item,
                        depth,
                        rootName,
                        sourceFile,
                        false,
                        includeBoundingBoxes);
                    nextBuilder = new SelectedItemsTreeBuildNode
                    {
                        Node = node,
                    };
                    siblings[path] = nextBuilder;

                    if (currentBuilder != null)
                        currentBuilder.Node.Children.Add(node);
                }

                if (string.Equals(path, selectedPath, StringComparison.OrdinalIgnoreCase))
                    nextBuilder.Node.IsSelectedLeaf = true;

                currentBuilder = nextBuilder;
                siblings = nextBuilder.ChildrenByPath;
            }
        }

        private static SelectedItemsTreeNode BuildSelectedItemsTreeNode(
            ModelItem item,
            int depth,
            string rootName,
            string sourceFile,
            bool isSelectedLeaf,
            bool includeBoundingBoxes)
        {
            var name = GetItemDisplayName(item);
            return new SelectedItemsTreeNode
            {
                Name = name,
                DisplayName = item == null ? string.Empty : item.DisplayName ?? string.Empty,
                Path = BuildItemPath(item),
                Depth = depth,
                RootName = rootName ?? string.Empty,
                SourceFile = sourceFile ?? string.Empty,
                IsSelectedLeaf = isSelectedLeaf,
                BoundingBox = includeBoundingBoxes ? TryBuildBoundingBoxInfo(item) : null,
            };
        }

        private static SelectedItemsTreePathNode BuildSelectedItemsTreePathNode(
            ModelItem item,
            int depth,
            string rootName,
            string sourceFile,
            bool isSelectedLeaf,
            bool includeBoundingBoxes)
        {
            var name = GetItemDisplayName(item);
            return new SelectedItemsTreePathNode
            {
                Name = name,
                DisplayName = item == null ? string.Empty : item.DisplayName ?? string.Empty,
                Path = BuildItemPath(item),
                Depth = depth,
                RootName = rootName ?? string.Empty,
                SourceFile = sourceFile ?? string.Empty,
                IsSelectedLeaf = isSelectedLeaf,
                BoundingBox = includeBoundingBoxes ? TryBuildBoundingBoxInfo(item) : null,
            };
        }

        private static string GetItemDisplayName(ModelItem item)
        {
            if (item == null)
                return string.Empty;

            return string.IsNullOrWhiteSpace(item.DisplayName)
                ? item.ClassDisplayName ?? string.Empty
                : item.DisplayName;
        }

        private static Point3Info ToPoint3InfoFromObject(object value)
        {
            if (value == null)
                return null;

            var x = GetDoubleProperty(value, "X");
            var y = GetDoubleProperty(value, "Y");
            var z = GetDoubleProperty(value, "Z");
            if (!x.HasValue || !y.HasValue || !z.HasValue)
                return null;

            return new Point3Info
            {
                X = x.Value,
                Y = y.Value,
                Z = z.Value,
            };
        }

        private static RotationInfo ToRotationInfoFromObject(object value)
        {
            if (value == null)
                return null;

            var a = GetDoubleProperty(value, "A");
            var b = GetDoubleProperty(value, "B");
            var c = GetDoubleProperty(value, "C");
            var d = GetDoubleProperty(value, "D");
            if (!a.HasValue || !b.HasValue || !c.HasValue || !d.HasValue)
                return null;

            return new RotationInfo
            {
                A = a.Value,
                B = b.Value,
                C = c.Value,
                D = d.Value,
            };
        }

        private static double? GetDoubleProperty(object instance, string propertyName)
        {
            var value = GetObjectProperty(instance, propertyName);
            if (value == null)
                return null;

            try
            {
                return Convert.ToDouble(value);
            }
            catch
            {
                return null;
            }
        }

        private static Point3Info ToPoint3Info(Point3D point)
        {
            return new Point3Info
            {
                X = point.X,
                Y = point.Y,
                Z = point.Z,
            };
        }

        private static string TryGetSourceFile(ModelItem item)
        {
            var current = item;
            while (current != null)
            {
                var sourceFileProperty = TryFindSourceFileProperty(current);
                if (sourceFileProperty != null)
                    return GetPropertyDisplayValue(sourceFileProperty);

                current = current.Parent;
            }

            return string.Empty;
        }

        private static DataProperty TryFindSourceFileProperty(ModelItem item)
        {
            if (item == null || item.PropertyCategories == null)
                return null;

            foreach (PropertyCategory category in item.PropertyCategories)
            {
                if (category == null || category.Properties == null)
                    continue;

                if (string.Equals(category.Name, ItemInternalCategory, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (DataProperty property in category.Properties)
                    {
                        if (property != null && string.Equals(property.Name, SourceFileInternalProperty, StringComparison.OrdinalIgnoreCase))
                            return property;
                    }
                }

                foreach (var alias in SourceFileDisplayProperties)
                {
                    if (!string.IsNullOrWhiteSpace(alias.Item1) &&
                        !string.Equals(category.DisplayName, alias.Item1, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (DataProperty property in category.Properties)
                    {
                        if (property != null && string.Equals(property.DisplayName, alias.Item2, StringComparison.OrdinalIgnoreCase))
                            return property;
                    }
                }
            }

            return null;
        }

        private static string GetPropertyDisplayValue(DataProperty property)
        {
            if (property == null || property.Value == null)
                return string.Empty;

            try
            {
                return property.Value.ToDisplayString();
            }
            catch
            {
                return property.Value.ToString();
            }
        }

        private static void CollectItemPaths(ModelItem item, ISet<string> items)
        {
            if (item == null)
                return;

            items.Add(BuildItemPath(item));
            foreach (ModelItem childItem in item.Children)
            {
                CollectItemPaths(childItem, items);
            }
        }

        private static int CountHiddenItems(ModelItem item)
        {
            if (item == null)
                return 0;

            var count = item.IsHidden ? 1 : 0;
            foreach (ModelItem childItem in item.Children)
            {
                count += CountHiddenItems(childItem);
            }

            return count;
        }

        private static ModelItemCollection FindHiddenItems(Document document)
        {
            var search = new Search();
            search.Selection.SelectAll();
            search.Locations = SearchLocations.DescendantsAndSelf;
            search.SearchConditions.Add(
                SearchCondition.HasPropertyByName(PropertyCategoryNames.Item, DataPropertyNames.ItemHidden)
                    .EqualValue(VariantData.FromBoolean(true)));

            return search.FindAll(document, false);
        }

        private static bool SelectionSetExists(DocumentSelectionSets selectionSets, string name)
        {
            return EnumerateSavedItems(selectionSets.RootItem)
                .Any(item => string.Equals(item.DisplayName, name, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool TryResolveSavedViewpointFolder(
            DocumentSavedViewpoints savedViewpoints,
            string folderPath,
            bool createIfMissing,
            out GroupItem targetFolder,
            out bool folderExists,
            out int createdFolderCount)
        {
            if (savedViewpoints == null)
                throw new ArgumentNullException(nameof(savedViewpoints));

            targetFolder = savedViewpoints.RootItem;
            folderExists = true;
            createdFolderCount = 0;

            var segments = SplitFolderPath(folderPath);
            if (segments.Length == 0)
                return true;

            GroupItem currentFolder = savedViewpoints.RootItem;
            foreach (var segment in segments)
            {
                var nextFolder = FindChildGroupByName(currentFolder, segment);
                if (nextFolder != null)
                {
                    currentFolder = nextFolder;
                    continue;
                }

                folderExists = false;
                if (!createIfMissing)
                {
                    targetFolder = savedViewpoints.RootItem;
                    return false;
                }

                savedViewpoints.AddCopy(currentFolder, new FolderItem
                {
                    DisplayName = segment,
                });

                nextFolder = FindChildGroupByName(currentFolder, segment);
                if (nextFolder == null)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Failed to create viewpoint folder: " + segment);

                createdFolderCount++;
                currentFolder = nextFolder;
            }

            targetFolder = currentFolder;
            return true;
        }

        private static GroupItem FindChildGroupByName(GroupItem parent, string name)
        {
            if (parent == null || string.IsNullOrWhiteSpace(name))
                return null;

            return parent.Children
                .OfType<GroupItem>()
                .FirstOrDefault(item => string.Equals(item.DisplayName, name, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool SavedItemExists(GroupItem parent, string name)
        {
            if (parent == null || string.IsNullOrWhiteSpace(name))
                return false;

            return parent.Children
                .OfType<SavedItem>()
                .Any(item => string.Equals(item.DisplayName, name, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeFolderPath(string folderPath)
        {
            var segments = SplitFolderPath(folderPath);
            return segments.Length == 0 ? string.Empty : string.Join("/", segments);
        }

        private static string[] SplitFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return new string[0];

            return folderPath
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment.Trim())
                .Where(segment => segment.Length > 0)
                .ToArray();
        }

        private static IEnumerable<SavedItem> EnumerateSavedItems(SavedItem item)
        {
            yield return item;

            var groupItem = item as GroupItem;
            if (groupItem == null)
                yield break;

            foreach (SavedItem child in groupItem.Children)
            {
                foreach (var descendant in EnumerateSavedItems(child))
                {
                    yield return descendant;
                }
            }
        }

        private static string BuildItemPath(ModelItem item)
        {
            if (item == null)
                return string.Empty;

            var stack = new Stack<string>();
            var current = item;

            while (current != null)
            {
                stack.Push(string.IsNullOrWhiteSpace(current.DisplayName)
                    ? current.ClassDisplayName
                    : current.DisplayName);
                current = current.Parent;
            }

            return string.Join(" / ", stack.ToArray());
        }

        private sealed class SelectedItemsTreeBuildNode
        {
            public SelectedItemsTreeNode Node { get; set; }
            public Dictionary<string, SelectedItemsTreeBuildNode> ChildrenByPath { get; private set; } = new Dictionary<string, SelectedItemsTreeBuildNode>(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal sealed class AgentCommandException : Exception
    {
        public AgentCommandException(string errorCode, string message, bool logAsWarning = false)
            : base(message)
        {
            ErrorCode = errorCode;
            LogAsWarning = logAsWarning;
        }

        public string ErrorCode { get; private set; }
        public bool LogAsWarning { get; private set; }
    }
}
