using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed class BoxIsolationService
    {
        internal const int DefaultMaxScannedItems = 100000;
        internal const int MaximumMaxScannedItems = 500000;
        internal const int DefaultPreviewLimit = 10;
        internal const int MaximumPreviewLimit = 50;
        private const int MaxTraversalMilliseconds = 10000;

        public IsolateByBoxResponse Isolate(Document document, IsolateByBoxRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            try
            {
                SectionBoxGeometryRules.Validate(request.Box);
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }

            var activeUnits = SectionBoxCaptureService.NormalizeUnits(document.Units);
            if (!string.Equals(request.Box.DocumentUnits, activeUnits, StringComparison.Ordinal))
            {
                throw new AgentCommandException(
                    ErrorCodes.SectionBoxUnitsMismatch,
                    "box.documentUnits does not match the active document units (" + activeUnits + ").");
            }

            var applyRequested = request.Apply == true;
            var maxScannedItems = Clamp(request.MaxScannedItems, DefaultMaxScannedItems, MaximumMaxScannedItems);
            var previewLimit = Clamp(request.PreviewLimit, DefaultPreviewLimit, MaximumPreviewLimit);
            var selectionSnapshot = applyRequested ? SnapshotSelection(document) : null;
            var traversal = Traverse(document, request.Box, maxScannedItems);
            BoxIsolationPlan plan;
            try
            {
                plan = BoxIsolationPlanner.Build(request.Box, traversal.Candidates.Select(candidate => candidate.PlanItem).ToList());
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(
                    ErrorCodes.CommandFailed,
                    "Box isolation planning failed before any visibility change: " + ex.Message);
            }

            var partial = traversal.TraversalTruncated || traversal.TimedOut || traversal.ClassificationErrorCount > 0;
            var response = BuildResponse(
                document,
                applyRequested,
                selectionSnapshot,
                traversal,
                plan,
                previewLimit,
                partial);

            if (!applyRequested)
                return response;
            if (!BoxIsolationExecutionRules.ShouldApply(applyRequested, partial))
            {
                response.ApplyRejected = true;
                response.ApplyRejectionCode = "incomplete_box_classification";
                response.Warnings.Add("apply=true was rejected because the full model classification did not complete; visibility was not changed.");
                return response;
            }

            try
            {
                SetHidden(document, traversal.Candidates, plan.KeepVisibleIndices, false);
                SetHidden(document, traversal.Candidates, plan.HideIndices, true);
                if (!SelectionMatches(document, selectionSnapshot))
                    RestoreSelection(document, selectionSnapshot);
                if (document.ActiveView != null)
                    document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            }
            catch (Exception applyException)
            {
                try
                {
                    RestoreVisibility(document, traversal.Candidates);
                    if (!SelectionMatches(document, selectionSnapshot))
                        RestoreSelection(document, selectionSnapshot);
                }
                catch (Exception rollbackException)
                {
                    throw new AgentCommandException(
                        ErrorCodes.CommandFailed,
                        "Box isolation failed and visibility rollback also failed: " + rollbackException.Message);
                }

                throw new AgentCommandException(
                    ErrorCodes.CommandFailed,
                    "Box isolation failed; the previous visibility state was restored: " + applyException.Message);
            }

            response.Applied = true;
            response.VisibleItemCount = plan.KeepVisibleIndices.Count;
            response.HiddenItemCount = plan.HideIndices.Count;
            response.SelectionPreserved = SelectionMatches(document, selectionSnapshot);
            response.SectionBoxPreserved = true;
            return response;
        }

        private static TraversalResult Traverse(Document document, SectionBoxGeometry box, int maxScannedItems)
        {
            var result = new TraversalResult();
            var stopwatch = Stopwatch.StartNew();
            var stack = new Stack<PendingItem>();
            var roots = document.Models == null
                ? new List<ModelItem>()
                : document.Models.CreateCollectionFromRootItems().Cast<ModelItem>().ToList();
            for (var index = roots.Count - 1; index >= 0; index--)
                stack.Push(new PendingItem(roots[index], -1));

            while (stack.Count > 0)
            {
                if (result.Candidates.Count >= maxScannedItems)
                {
                    result.TraversalTruncated = true;
                    break;
                }
                if (stopwatch.ElapsedMilliseconds >= MaxTraversalMilliseconds)
                {
                    result.TimedOut = true;
                    break;
                }

                var pending = stack.Pop();
                var item = pending.Item;
                if (item == null)
                    continue;
                var candidateIndex = result.Candidates.Count;
                BoxVector3 minimum;
                BoxVector3 maximum;
                var unclassified = false;
                try
                {
                    var bounds = item.BoundingBox();
                    if (bounds == null)
                        throw new InvalidOperationException("BoundingBox returned null.");
                    minimum = ToVector(bounds.Min);
                    maximum = ToVector(bounds.Max);
                    SectionBoxGeometryRules.ValidateBounds(minimum, maximum);
                }
                catch (Exception ex)
                {
                    unclassified = true;
                    result.ClassificationErrorCount++;
                    result.AddWarning("Skipped an unreadable model-item bounding box: " + ex.Message);
                    minimum = new BoxVector3();
                    maximum = new BoxVector3();
                }

                result.Candidates.Add(new Candidate
                {
                    Item = item,
                    PlanItem = new BoxIsolationPlanItem
                    {
                        ParentIndex = pending.ParentIndex,
                        BoundsMin = minimum,
                        BoundsMax = maximum,
                        WasHidden = item.IsHidden,
                        Unclassified = unclassified,
                    },
                });

                if (item.Children != null)
                {
                    foreach (ModelItem child in item.Children)
                        stack.Push(new PendingItem(child, candidateIndex));
                }
            }

            return result;
        }

        private static IsolateByBoxResponse BuildResponse(
            Document document,
            bool applyRequested,
            ModelItemCollection selectionSnapshot,
            TraversalResult traversal,
            BoxIsolationPlan plan,
            int previewLimit,
            bool partial)
        {
            var response = new IsolateByBoxResponse
            {
                ApplyRequested = applyRequested,
                Applied = false,
                Partial = partial,
                TraversalTruncated = traversal.TraversalTruncated,
                TimedOut = traversal.TimedOut,
                ClassificationErrorCount = traversal.ClassificationErrorCount,
                ScannedItemCount = traversal.Candidates.Count,
                IntersectingItemCount = plan.IntersectingIndices.Count,
                WouldKeepVisibleItemCount = plan.KeepVisibleIndices.Count,
                WouldHideItemCount = plan.HideIndices.Count,
                PreviouslyHiddenItemCount = plan.PreviouslyHiddenItemCount,
                WouldRevealItemCount = plan.WouldRevealItemCount,
                WouldChangeVisibilityItemCount = plan.WouldChangeVisibilityItemCount,
                AffectedItemsPreviewTruncated = plan.HideIndices.Count > previewLimit,
                SelectionPreserved = !applyRequested || SelectionMatches(document, selectionSnapshot),
                SectionBoxPreserved = true,
                Warnings = traversal.Warnings.ToList(),
            };
            foreach (var candidateIndex in plan.HideIndices.Take(previewLimit))
            {
                var candidate = traversal.Candidates[candidateIndex];
                response.AffectedItemsPreview.Add(new BoxIsolationPreviewItem
                {
                    DisplayName = candidate.Item.DisplayName ?? string.Empty,
                    Path = BuildItemPath(candidate.Item),
                    WasHidden = candidate.PlanItem.WasHidden,
                    TargetHidden = true,
                });
            }
            return response;
        }

        private static void SetHidden(Document document, IList<Candidate> candidates, IEnumerable<int> indices, bool hidden)
        {
            var collection = new ModelItemCollection();
            foreach (var index in indices)
                collection.Add(candidates[index].Item);
            if (collection.Count > 0)
                document.Models.SetHidden(collection, hidden);
        }

        private static void RestoreVisibility(Document document, IList<Candidate> candidates)
        {
            var all = new ModelItemCollection();
            var previouslyHidden = new ModelItemCollection();
            foreach (var candidate in candidates)
            {
                all.Add(candidate.Item);
                if (candidate.PlanItem.WasHidden)
                    previouslyHidden.Add(candidate.Item);
            }
            if (all.Count > 0)
                document.Models.SetHidden(all, false);
            if (previouslyHidden.Count > 0)
                document.Models.SetHidden(previouslyHidden, true);
        }

        private static ModelItemCollection SnapshotSelection(Document document)
        {
            var snapshot = new ModelItemCollection();
            if (document.CurrentSelection != null && document.CurrentSelection.SelectedItems != null)
                snapshot.CopyFrom(document.CurrentSelection.SelectedItems);
            return snapshot;
        }

        private static void RestoreSelection(Document document, ModelItemCollection snapshot)
        {
            if (document.CurrentSelection != null)
                document.CurrentSelection.CopyFrom(snapshot ?? new ModelItemCollection());
        }

        private static bool SelectionMatches(Document document, ModelItemCollection snapshot)
        {
            var current = document.CurrentSelection == null ? null : document.CurrentSelection.SelectedItems;
            if (current == null)
                return snapshot == null || snapshot.Count == 0;
            if (snapshot == null || current.Count != snapshot.Count)
                return false;
            for (var index = 0; index < current.Count; index++)
            {
                if (!Equals(current[index], snapshot[index]))
                    return false;
            }
            return true;
        }

        private static BoxVector3 ToVector(Point3D point)
        {
            return new BoxVector3 { X = point.X, Y = point.Y, Z = point.Z };
        }

        private static int Clamp(int? requested, int defaultValue, int maximum)
        {
            var value = requested.GetValueOrDefault(defaultValue);
            if (value < 1)
                return 1;
            return value > maximum ? maximum : value;
        }

        private static string BuildItemPath(ModelItem item)
        {
            var names = new Stack<string>();
            var current = item;
            while (current != null)
            {
                names.Push(string.IsNullOrWhiteSpace(current.DisplayName) ? current.ClassDisplayName ?? string.Empty : current.DisplayName);
                current = current.Parent;
            }
            return string.Join(" / ", names.ToArray());
        }

        private sealed class PendingItem
        {
            public PendingItem(ModelItem item, int parentIndex)
            {
                Item = item;
                ParentIndex = parentIndex;
            }

            public ModelItem Item { get; private set; }
            public int ParentIndex { get; private set; }
        }

        private sealed class Candidate
        {
            public ModelItem Item { get; set; }
            public BoxIsolationPlanItem PlanItem { get; set; }
        }

        private sealed class TraversalResult
        {
            public List<Candidate> Candidates { get; } = new List<Candidate>();
            public bool TraversalTruncated { get; set; }
            public bool TimedOut { get; set; }
            public int ClassificationErrorCount { get; set; }
            public List<string> Warnings { get; } = new List<string>();

            public void AddWarning(string warning)
            {
                if (Warnings.Count < 10)
                    Warnings.Add(warning);
            }
        }
    }
}
