using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        public HideUnselectedResponse HideUnselected(Document document, HideUnselectedRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var apply = request.Apply == true;
            var previewLimit = ClampVisibilityPreviewLimit(request.PreviewLimit);

            var selectedItems = document.CurrentSelection.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0)
                throw new AgentCommandException(ErrorCodes.NoSelection, "There is no active selection.");
            var selectedCount = selectedItems.Count;
            var selectionSnapshot = SnapshotSelection(selectedItems);

            var itemsToKeepVisible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModelItem item in selectedItems)
            {
                CollectItemPaths(item, itemsToKeepVisible);

                var current = item;
                while (current != null)
                {
                    itemsToKeepVisible.Add(BuildItemPath(current));
                    current = current.Parent;
                }
            }

            var itemsToHide = new List<ModelItem>();
            var rootSummaries = new VisibilityRootSummaryAccumulator();
            foreach (ModelItem rootItem in document.Models.CreateCollectionFromRootItems())
            {
                CollectItemsToHide(rootItem, itemsToKeepVisible, itemsToHide, rootItem, rootSummaries);
            }
            var rootSummaryResult = rootSummaries.Build();

            if (apply && itemsToHide.Count > 0)
            {
                var hiddenItems = new ModelItemCollection();
                hiddenItems.AddRange(itemsToHide);
                document.Models.SetHidden(hiddenItems, true);
            }

            if (apply)
            {
                RestoreSelection(document, selectionSnapshot);

                if (document.ActiveView != null)
                    document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            }

            return new HideUnselectedResponse
            {
                Apply = apply,
                SelectedItemCount = selectedCount,
                WouldHideItemCount = itemsToHide.Count,
                WouldKeepVisibleItemCount = itemsToKeepVisible.Count,
                HiddenItemCount = apply ? (int?)itemsToHide.Count : null,
                AffectedRootCount = rootSummaryResult.TotalRootCount,
                AffectedRootSummariesTruncated = rootSummaryResult.Truncated,
                AffectedRootSummaries = rootSummaryResult.Summaries,
                AffectedItemsPreviewTruncated = itemsToHide.Count > previewLimit,
                AffectedItemsPreview = BuildVisibilityPreview(itemsToHide, previewLimit),
            };
        }

        public HideSelectedResponse HideSelected(Document document, HideSelectedRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var apply = request.Apply == true;
            var previewLimit = ClampVisibilityPreviewLimit(request.PreviewLimit);

            var selectedItems = document.CurrentSelection.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0)
                throw new AgentCommandException(ErrorCodes.NoSelection, "There is no active selection.");
            var selectedCount = selectedItems.Count;

            var selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var itemsToHide = new List<ModelItem>();
            var rootSummaries = new VisibilityRootSummaryAccumulator();

            foreach (ModelItem item in selectedItems)
            {
                CollectVisibleSelectedItems(item, selectedPaths, itemsToHide, GetRootItem(item), rootSummaries);
            }
            var rootSummaryResult = rootSummaries.Build();

            if (apply && itemsToHide.Count > 0)
            {
                var hiddenItems = new ModelItemCollection();
                hiddenItems.AddRange(itemsToHide);
                document.Models.SetHidden(hiddenItems, true);
            }

            if (apply)
            {
                ClearSelection(document);

                if (document.ActiveView != null)
                    document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            }

            return new HideSelectedResponse
            {
                Apply = apply,
                SelectedItemCount = selectedCount,
                WouldHideItemCount = itemsToHide.Count,
                HiddenItemCount = apply ? (int?)itemsToHide.Count : null,
                AffectedRootCount = rootSummaryResult.TotalRootCount,
                AffectedRootSummariesTruncated = rootSummaryResult.Truncated,
                AffectedRootSummaries = rootSummaryResult.Summaries,
                AffectedItemsPreviewTruncated = itemsToHide.Count > previewLimit,
                AffectedItemsPreview = BuildVisibilityPreview(itemsToHide, previewLimit),
            };
        }

        public UnhideSelectedResponse UnhideSelected(Document document, UnhideSelectedRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var apply = request.Apply == true;
            var previewLimit = ClampVisibilityPreviewLimit(request.PreviewLimit);

            var selectedItems = document.CurrentSelection.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0)
                throw new AgentCommandException(ErrorCodes.NoSelection, "There is no active selection.");
            var selectedCount = selectedItems.Count;
            var selectionSnapshot = SnapshotSelection(selectedItems);

            var selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var itemsToReveal = new List<ModelItem>();
            var rootSummaries = new VisibilityRootSummaryAccumulator();

            foreach (ModelItem item in selectedItems)
            {
                CollectHiddenSelectedItems(item, selectedPaths, itemsToReveal, false, GetRootItem(item), rootSummaries);
            }
            var rootSummaryResult = rootSummaries.Build();

            if (apply && itemsToReveal.Count > 0)
            {
                var revealedItems = new ModelItemCollection();
                revealedItems.AddRange(itemsToReveal);
                document.Models.SetHidden(revealedItems, false);
            }

            if (apply)
            {
                RestoreSelection(document, selectionSnapshot);

                if (document.ActiveView != null)
                    document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            }

            return new UnhideSelectedResponse
            {
                Apply = apply,
                SelectedItemCount = selectedCount,
                WouldRevealItemCount = itemsToReveal.Count,
                RevealedItemCount = apply ? (int?)itemsToReveal.Count : null,
                AffectedRootCount = rootSummaryResult.TotalRootCount,
                AffectedRootSummariesTruncated = rootSummaryResult.Truncated,
                AffectedRootSummaries = rootSummaryResult.Summaries,
                AffectedItemsPreviewTruncated = itemsToReveal.Count > previewLimit,
                AffectedItemsPreview = BuildVisibilityPreview(itemsToReveal, previewLimit),
            };
        }

        public RevealSelectedResponse RevealSelected(Document document, RevealSelectedRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var apply = request.Apply == true;
            var previewLimit = ClampVisibilityPreviewLimit(request.PreviewLimit);

            var selectedItems = document.CurrentSelection.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0)
                throw new AgentCommandException(ErrorCodes.NoSelection, "There is no active selection.");
            var selectedCount = selectedItems.Count;
            var selectionSnapshot = SnapshotSelection(selectedItems);

            var selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var itemsToReveal = new List<ModelItem>();
            var rootSummaries = new VisibilityRootSummaryAccumulator();

            foreach (ModelItem item in selectedItems)
            {
                CollectHiddenSelectedItems(item, selectedPaths, itemsToReveal, true, GetRootItem(item), rootSummaries);
            }
            var rootSummaryResult = rootSummaries.Build();

            if (apply && itemsToReveal.Count > 0)
            {
                var revealedItems = new ModelItemCollection();
                revealedItems.AddRange(itemsToReveal);
                document.Models.SetHidden(revealedItems, false);
            }

            if (apply)
            {
                RestoreSelection(document, selectionSnapshot);

                if (document.ActiveView != null)
                    document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            }

            return new RevealSelectedResponse
            {
                Apply = apply,
                SelectedItemCount = selectedCount,
                WouldRevealItemCount = itemsToReveal.Count,
                RevealedItemCount = apply ? (int?)itemsToReveal.Count : null,
                AffectedRootCount = rootSummaryResult.TotalRootCount,
                AffectedRootSummariesTruncated = rootSummaryResult.Truncated,
                AffectedRootSummaries = rootSummaryResult.Summaries,
                AffectedItemsPreviewTruncated = itemsToReveal.Count > previewLimit,
                AffectedItemsPreview = BuildVisibilityPreview(itemsToReveal, previewLimit),
            };
        }

        public IsolateSelectedResponse IsolateSelected(Document document, IsolateSelectedRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var apply = request.Apply == true;
            var previewLimit = ClampVisibilityPreviewLimit(request.PreviewLimit);

            var selectedItems = document.CurrentSelection.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0)
                throw new AgentCommandException(ErrorCodes.NoSelection, "There is no active selection.");
            var selectedCount = selectedItems.Count;
            var selectionSnapshot = SnapshotSelection(selectedItems);

            var hiddenItems = FindHiddenItems(document);
            var hiddenCount = hiddenItems.Count;
            var hideResponse = HideUnselected(document, new HideUnselectedRequest
            {
                Apply = false,
                PreviewLimit = previewLimit,
            });

            if (apply)
            {
                if (hiddenCount > 0)
                    document.Models.SetHidden(hiddenItems, false);

                RestoreSelection(document, selectionSnapshot);

                var hiddenItemCount = 0;
                if (hideResponse.WouldHideItemCount > 0)
                {
                    var reappliedHideResponse = HideUnselected(document, new HideUnselectedRequest
                    {
                        Apply = true,
                        PreviewLimit = previewLimit,
                    });
                    hiddenItemCount = reappliedHideResponse.HiddenItemCount.GetValueOrDefault();
                }
                else if (document.ActiveView != null)
                {
                    document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
                }

                return new IsolateSelectedResponse
                {
                    Apply = true,
                    SelectedItemCount = selectedCount,
                    PreviouslyHiddenItemCount = hiddenCount,
                    WouldHideItemCount = hideResponse.WouldHideItemCount,
                    WouldKeepVisibleItemCount = hideResponse.WouldKeepVisibleItemCount,
                    RevealedItemCount = hiddenCount,
                    HiddenItemCount = hiddenItemCount,
                    AffectedRootCount = hideResponse.AffectedRootCount,
                    AffectedRootSummariesTruncated = hideResponse.AffectedRootSummariesTruncated,
                    AffectedRootSummaries = hideResponse.AffectedRootSummaries,
                    AffectedItemsPreviewTruncated = hideResponse.AffectedItemsPreviewTruncated,
                    AffectedItemsPreview = hideResponse.AffectedItemsPreview,
                };
            }

            return new IsolateSelectedResponse
            {
                Apply = apply,
                SelectedItemCount = selectedCount,
                PreviouslyHiddenItemCount = hiddenCount,
                WouldHideItemCount = hideResponse.WouldHideItemCount,
                WouldKeepVisibleItemCount = hideResponse.WouldKeepVisibleItemCount,
                RevealedItemCount = apply ? (int?)hiddenCount : null,
                HiddenItemCount = apply ? (int?)0 : null,
                AffectedRootCount = hideResponse.AffectedRootCount,
                AffectedRootSummariesTruncated = hideResponse.AffectedRootSummariesTruncated,
                AffectedRootSummaries = hideResponse.AffectedRootSummaries,
                AffectedItemsPreviewTruncated = hideResponse.AffectedItemsPreviewTruncated,
                AffectedItemsPreview = hideResponse.AffectedItemsPreview,
            };
        }

        public ShowAllResponse ShowAll(Document document, ShowAllRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var apply = request.Apply == true;
            var previewLimit = ClampVisibilityPreviewLimit(request.PreviewLimit);

            var selectionSnapshot = SnapshotSelection(document.CurrentSelection.SelectedItems);
            var hiddenItems = FindHiddenItems(document);
            var hiddenCount = hiddenItems.Count;
            var rootSummaries = new VisibilityRootSummaryAccumulator();
            foreach (ModelItem hiddenItem in hiddenItems)
                rootSummaries.Add(GetRootItem(hiddenItem), hiddenItem);
            var rootSummaryResult = rootSummaries.Build();

            if (apply && hiddenCount > 0)
            {
                document.Models.SetHidden(hiddenItems, false);
            }

            if (apply)
            {
                RestoreSelection(document, selectionSnapshot);

                if (document.ActiveView != null)
                    document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            }

            return new ShowAllResponse
            {
                Apply = apply,
                CurrentlyHiddenItemCount = hiddenCount,
                WouldRevealItemCount = hiddenCount,
                RevealedItemCount = apply ? (int?)hiddenCount : null,
                AffectedRootCount = rootSummaryResult.TotalRootCount,
                AffectedRootSummariesTruncated = rootSummaryResult.Truncated,
                AffectedRootSummaries = rootSummaryResult.Summaries,
                AffectedItemsPreviewTruncated = hiddenItems.Count > previewLimit,
                AffectedItemsPreview = BuildVisibilityPreview(hiddenItems, previewLimit),
            };
        }
    }
}
