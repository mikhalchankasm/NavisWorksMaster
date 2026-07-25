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
        public SelectItemsResponse SelectItems(Document document, SelectItemsRequest request, MatchSessionStore sessionStore)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (sessionStore == null)
                throw new ArgumentNullException(nameof(sessionStore));
            if (request.MatchHandles == null || request.MatchHandles.Count == 0)
                throw new AgentCommandException(ErrorCodes.EmptyMatchHandles, "At least one match handle is required.");

            var response = new SelectItemsResponse();
            var itemsToSelect = new List<ModelItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var handle in request.MatchHandles.Where(h => !string.IsNullOrWhiteSpace(h)))
            {
                IList<ModelItem> items;
                if (!sessionStore.TryGet(handle, out items))
                {
                    response.Partial = true;
                    response.Results.Add(new SelectItemsHandleResult
                    {
                        MatchHandle = handle,
                        Status = SelectHandleStatuses.Stale,
                        SelectedItemCount = 0,
                    });
                    continue;
                }

                var selectedCount = 0;
                foreach (var item in items)
                {
                    var identity = BuildItemPath(item);
                    if (seen.Add(identity))
                    {
                        itemsToSelect.Add(item);
                        selectedCount++;
                    }
                }

                response.Results.Add(new SelectItemsHandleResult
                {
                    MatchHandle = handle,
                    Status = SelectHandleStatuses.Selected,
                    SelectedItemCount = selectedCount,
                });
            }

            if (itemsToSelect.Count > 0)
            {
                var selectedItemsCollection = new ModelItemCollection();
                selectedItemsCollection.AddRange(itemsToSelect);
                document.CurrentSelection.CopyFrom(selectedItemsCollection);
            }

            response.SelectedHandleCount = response.Results.Count(r => string.Equals(r.Status, SelectHandleStatuses.Selected, StringComparison.OrdinalIgnoreCase));
            response.SelectedItemCount = itemsToSelect.Count;
            return response;
        }

        public SelectionStatusResponse SelectionStatus(Document document, SelectionStatusRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new SelectionStatusRequest();
            var selectedItems = document.CurrentSelection.SelectedItems;
            var selectedCount = selectedItems == null ? 0 : selectedItems.Count;
            var response = new SelectionStatusResponse
            {
                HasSelection = selectedCount > 0,
                SelectedItemCount = selectedCount,
            };

            if (request.IncludeBoundingBox.GetValueOrDefault(true) && selectedCount > 0)
                response.BoundingBox = ToBoundingBoxInfo(selectedItems.BoundingBox());

            return response;
        }

        public SelectionCopyNamesResponse SelectionCopyNames(Document document, SelectionCopyNamesRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new SelectionCopyNamesRequest();
            var selectedItems = document.CurrentSelection.SelectedItems;
            var selectedCount = selectedItems == null ? 0 : selectedItems.Count;
            var limit = ClampSelectionCopyNamesLimit(request.Limit);
            var includePaths = request.IncludePaths.GetValueOrDefault(false);
            var includeSourceFiles = request.IncludeSourceFiles.GetValueOrDefault(false);

            var response = new SelectionCopyNamesResponse
            {
                SelectedItemCount = selectedCount,
                ReturnedItemCount = Math.Min(selectedCount, limit),
                Truncated = selectedCount > limit,
            };

            if (selectedItems == null || selectedCount == 0)
                return response;

            foreach (ModelItem item in selectedItems.Take(limit))
            {
                var name = GetItemDisplayName(item);
                response.Names.Add(name);
                response.Items.Add(new SelectionCopyNameItem
                {
                    DisplayName = name,
                    Path = includePaths ? BuildItemPath(item) : string.Empty,
                    SourceFile = includeSourceFiles ? TryGetSourceFile(item) ?? string.Empty : string.Empty,
                });
            }

            return response;
        }

        public SelectedItemsPreviewResponse SelectedItemsPreview(Document document, SelectedItemsPreviewRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new SelectedItemsPreviewRequest();
            var selectedItems = document.CurrentSelection.SelectedItems;
            var selectedCount = selectedItems == null ? 0 : selectedItems.Count;
            var limit = ClampSelectionPreviewLimit(request.Limit);
            var includeBoundingBoxes = request.IncludeBoundingBoxes.GetValueOrDefault(false);

            var response = new SelectedItemsPreviewResponse
            {
                SelectedItemCount = selectedCount,
                Truncated = selectedCount > limit,
            };

            if (selectedItems == null || selectedCount == 0)
                return response;

            foreach (ModelItem item in selectedItems.Take(limit))
            {
                response.Items.Add(new SelectedItemPreview
                {
                    DisplayName = item.DisplayName ?? string.Empty,
                    ClassDisplayName = item.ClassDisplayName ?? string.Empty,
                    Path = BuildItemPath(item),
                    SourceFile = TryGetSourceFile(item) ?? string.Empty,
                    IsHidden = item.IsHidden,
                    ChildCount = item.Children == null ? 0 : item.Children.Count(),
                    BoundingBox = includeBoundingBoxes ? ToBoundingBoxInfo(item.BoundingBox()) : null,
                });
            }

            return response;
        }

        public SelectedItemsAncestryResponse SelectedItemsAncestry(Document document, SelectedItemsAncestryRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new SelectedItemsAncestryRequest();
            var selectedItems = document.CurrentSelection.SelectedItems;
            var selectedCount = selectedItems == null ? 0 : selectedItems.Count;
            var limit = ClampSelectionPreviewLimit(request.Limit);
            var includeBoundingBoxes = request.IncludeBoundingBoxes.GetValueOrDefault(false);

            var response = new SelectedItemsAncestryResponse
            {
                SelectedItemCount = selectedCount,
                Truncated = selectedCount > limit,
            };

            if (selectedItems == null || selectedCount == 0)
                return response;

            var selectionIndex = 0;
            foreach (ModelItem item in selectedItems.Take(limit))
            {
                var ancestors = BuildAncestorChain(item);
                var chain = new List<SelectedItemHierarchyNode>();
                var depth = 0;

                foreach (var ancestor in ancestors)
                {
                    var node = BuildSelectedItemHierarchyNode(ancestor, depth, false, includeBoundingBoxes);
                    chain.Add(node);
                    depth++;
                }

                var selectedNode = BuildSelectedItemHierarchyNode(item, depth, true, includeBoundingBoxes);
                chain.Add(selectedNode);

                response.Items.Add(new SelectedItemAncestry
                {
                    SelectionIndex = selectionIndex,
                    Item = selectedNode,
                    Ancestors = chain.Where(node => !node.IsSelectedItem).ToList(),
                    Chain = chain,
                });

                selectionIndex++;
            }

            return response;
        }

        public SelectedItemsTreeResponse SelectedItemsTree(Document document, SelectedItemsTreeRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new SelectedItemsTreeRequest();
            var format = NormalizeSelectedItemsTreeFormat(request.Format);
            var maxItems = ClampSelectedItemsTreeMaxItems(request.MaxItems);
            var maxDepth = NormalizeSelectedItemsTreeMaxDepth(request.MaxDepth);
            var includeBoundingBoxes = request.IncludeBoundingBoxes.GetValueOrDefault(false);
            var selectedItems = document.CurrentSelection.SelectedItems;
            var selectedCount = selectedItems == null ? 0 : selectedItems.Count;
            var returnedCount = Math.Min(selectedCount, maxItems);

            var response = new SelectedItemsTreeResponse
            {
                DocumentTitle = string.IsNullOrWhiteSpace(document.FileName) ? string.Empty : Path.GetFileName(document.FileName),
                Format = format,
                SelectedItemCount = selectedCount,
                ReturnedItemCount = returnedCount,
                Truncated = selectedCount > maxItems,
            };

            if (selectedItems == null || selectedCount == 0)
                return response;

            var rootBuilders = new Dictionary<string, SelectedItemsTreeBuildNode>(StringComparer.OrdinalIgnoreCase);
            var selectionIndex = 0;

            foreach (ModelItem item in selectedItems.Take(maxItems))
            {
                var chainItems = BuildItemChain(item);
                if (chainItems.Count == 0)
                    continue;

                var rootItem = chainItems[0];
                var rootName = GetItemDisplayName(rootItem);
                var sourceFile = TryGetSourceFile(rootItem) ?? string.Empty;
                var selectedDepth = chainItems.Count - 1;
                var selectedPath = BuildItemPath(item);

                if (string.Equals(format, SelectedItemsTreeFormatFlat, StringComparison.OrdinalIgnoreCase))
                {
                    response.Items.Add(BuildSelectedItemsTreeFlatItem(
                        selectionIndex,
                        item,
                        chainItems,
                        rootName,
                        sourceFile,
                        selectedDepth,
                        maxDepth,
                        includeBoundingBoxes,
                        response));
                }
                else
                {
                    AddSelectedItemTreePath(
                        rootBuilders,
                        chainItems,
                        rootName,
                        sourceFile,
                        selectedPath,
                        maxDepth,
                        includeBoundingBoxes,
                        response);
                }

                selectionIndex++;
            }

            if (string.Equals(format, SelectedItemsTreeFormatTree, StringComparison.OrdinalIgnoreCase))
                response.Roots = rootBuilders.Values.Select(builder => builder.Node).ToList();

            return response;
        }

        public ItemPropertiesByHandleResponse ItemPropertiesByHandle(Document document, ItemPropertiesByHandleRequest request, MatchSessionStore sessionStore)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (sessionStore == null)
                throw new ArgumentNullException(nameof(sessionStore));
            if (request.MatchHandles == null || request.MatchHandles.Count == 0)
                throw new AgentCommandException(ErrorCodes.EmptyMatchHandles, "At least one match handle is required.");

            var itemLimit = ClampPropertyItemLimit(request.ItemLimit);
            var propertyLimit = ClampPropertyLimit(request.PropertyLimit);
            var includeInternalNames = request.IncludeInternalNames.GetValueOrDefault(false);
            var categoryFilters = NormalizeCategoryFilters(request.CategoryFilters);
            var response = new ItemPropertiesByHandleResponse();

            foreach (var handle in request.MatchHandles.Where(h => !string.IsNullOrWhiteSpace(h)))
            {
                IList<ModelItem> items;
                if (!sessionStore.TryGet(handle, out items))
                {
                    response.Partial = true;
                    response.Results.Add(new ItemPropertiesHandleResult
                    {
                        MatchHandle = handle,
                        Status = SelectHandleStatuses.Stale,
                    });
                    continue;
                }

                var result = new ItemPropertiesHandleResult
                {
                    MatchHandle = handle,
                    Status = SelectHandleStatuses.Selected,
                    ItemCount = items.Count,
                    ReturnedItemCount = Math.Min(items.Count, itemLimit),
                    ItemsTruncated = items.Count > itemLimit,
                };

                foreach (var item in items.Take(itemLimit))
                {
                    var itemPreview = BuildItemPropertiesPreview(item, propertyLimit, includeInternalNames, categoryFilters);
                    if (itemPreview.Categories.Sum(category => category.Properties.Count) >= propertyLimit)
                        result.PropertiesTruncated = HasMoreProperties(item, propertyLimit, categoryFilters);

                    result.Items.Add(itemPreview);
                }

                if (result.ItemsTruncated || result.PropertiesTruncated)
                    response.Partial = true;

                response.Results.Add(result);
            }

            return response;
        }
    }
}
