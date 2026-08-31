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
        internal const int DefaultPreviewLimit = 10;
        internal const int MaximumPreviewLimit = 50;

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
            var maxScannedItems = ValidateMaxScannedItems(request.MaxScannedItems);
            var maxDurationSeconds = ValidateMaxDurationSeconds(request.MaxDurationSeconds);
            var previewLimit = Clamp(request.PreviewLimit, DefaultPreviewLimit, MaximumPreviewLimit);
            var selectionSnapshot = applyRequested ? SnapshotSelection(document) : null;
            var durationStopwatch = Stopwatch.StartNew();
            var durationGuard = new SectionBoxIsolationDurationGuard(
                maxDurationSeconds,
                new StopwatchIsolationClock(durationStopwatch));
            var traversal = Traverse(document, request.Box, maxScannedItems, durationGuard);
            BoxIsolationPlanningResult planning;
            try
            {
                planning = traversal.TimedOut
                    ? new BoxIsolationPlanningResult { TimedOut = true }
                    : BoxIsolationPlanner.BuildBounded(
                        request.Box,
                        traversal.Candidates.Select(candidate => candidate.PlanItem).ToList(),
                        durationGuard);
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(
                    ErrorCodes.CommandFailed,
                    "Box isolation planning failed before any visibility change: " + ex.Message);
            }

            var timedOut = traversal.TimedOut || planning.TimedOut;
            var partial = BoxIsolationExecutionRules.IsPartial(
                traversal.TraversalTruncated,
                timedOut);
            var response = BuildResponse(
                document,
                applyRequested,
                selectionSnapshot,
                traversal,
                planning.Plan,
                previewLimit,
                partial,
                timedOut,
                maxDurationSeconds,
                durationGuard.ElapsedMilliseconds,
                planning);

            if (!applyRequested)
            {
                response.ElapsedMilliseconds = durationGuard.ElapsedMilliseconds;
                return response;
            }
            if (!BoxIsolationExecutionRules.ShouldApply(
                    applyRequested,
                    partial,
                    traversal.ClassificationErrorCount))
            {
                response.ApplyRejected = true;
                response.ApplyRejectionCode = partial
                    ? "incomplete_box_traversal"
                    : "box_classification_errors";
                response.Warnings.Add(partial
                    ? "apply=true was rejected because bounded traversal or visibility planning did not complete; visibility was not changed."
                    : "apply=true was rejected because one or more geometry items could not be classified; visibility was not changed.");
                response.ElapsedMilliseconds = durationGuard.ElapsedMilliseconds;
                return response;
            }

            try
            {
                SetHidden(document, traversal.Candidates, planning.Plan.RevealIndices, false);
                SetHidden(document, traversal.Candidates, planning.Plan.NewlyHiddenIndices, true);
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
                    if (document.ActiveView != null)
                        document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
                }
                catch (Exception rollbackException)
                {
                    throw new AgentCommandException(
                        ErrorCodes.CommandFailed,
                        "Box isolation failed (" + applyException.Message +
                        ") and visibility rollback also failed: " + rollbackException.Message);
                }

                throw new AgentCommandException(
                    ErrorCodes.CommandFailed,
                    "Box isolation failed; the previous visibility state was restored: " + applyException.Message);
            }

            response.Applied = true;
            response.VisibleItemCount = planning.Plan.KeepVisibleIndices.Count;
            response.HiddenItemCount = planning.Plan.HideIndices.Count;
            response.SelectionPreserved = SelectionMatches(document, selectionSnapshot);
            response.SectionBoxPreserved = true;
            response.ElapsedMilliseconds = durationGuard.ElapsedMilliseconds;
            return response;
        }

        private static TraversalResult Traverse(
            Document document,
            SectionBoxGeometry box,
            int maxScannedItems,
            SectionBoxIsolationDurationGuard durationGuard)
        {
            var result = new TraversalResult(maxScannedItems);
            var intersectionTester = new SectionBoxGeometryRules.SectionBoxIntersectionTester(box);
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
                if (durationGuard.ShouldStop(hasRemainingWork: true))
                {
                    result.TimedOut = true;
                    break;
                }

                var pending = stack.Pop();
                var item = pending.Item;
                if (item == null)
                    continue;
                if (!result.Accounting.TryRegisterScannedItem())
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Box isolation traversal accounting exceeded its reviewed limit.");
                var candidateIndex = result.Candidates.Count;
                BoxVector3 minimum;
                BoxVector3 maximum;
                var boundsReadable = false;
                var intersects = false;
                var boundsErrorMessage = string.Empty;
                try
                {
                    var bounds = item.BoundingBox();
                    if (bounds == null)
                        throw new InvalidOperationException("BoundingBox returned null.");
                    minimum = ToVector(bounds.Min);
                    maximum = ToVector(bounds.Max);
                    SectionBoxGeometryRules.ValidateBounds(minimum, maximum);
                    intersects = intersectionTester.Intersects(minimum, maximum);
                    boundsReadable = true;
                }
                catch (Exception ex)
                {
                    boundsErrorMessage = ex.Message;
                    minimum = new BoxVector3();
                    maximum = new BoxVector3();
                }

                ModelItemEnumerableCollection children = null;
                var childCount = 0;
                var hierarchyStatusKnown = false;
                try
                {
                    children = item.Children;
                    childCount = children == null ? 0 : children.Count();
                    hierarchyStatusKnown = true;
                }
                catch (Exception ex)
                {
                    boundsErrorMessage += " Child hierarchy was unavailable: " + ex.Message;
                }
                var geometryStatusKnown = false;
                var hasGeometry = false;
                if (!boundsReadable)
                {
                    try
                    {
                        hasGeometry = item.HasGeometry;
                        geometryStatusKnown = true;
                    }
                    catch (Exception ex)
                    {
                        boundsErrorMessage += " Geometry status was unavailable: " + ex.Message;
                    }
                }
                var disposition = BoxIsolationTraversalPolicy.Classify(
                    boundsReadable,
                    intersects,
                    geometryStatusKnown,
                    hasGeometry,
                    childCount > 0);
                if (!hierarchyStatusKnown && disposition != BoxIsolationNodeDisposition.OutsideSubtree)
                    disposition = BoxIsolationNodeDisposition.Unclassified;
                var unclassified = BoxIsolationTraversalPolicy.IsRealClassificationError(disposition);
                var preserveCurrentVisibility = BoxIsolationTraversalPolicy.ShouldPreserveCurrentVisibility(disposition);

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
                        PreserveCurrentVisibility = preserveCurrentVisibility,
                        HasPrecomputedIntersection = boundsReadable,
                        PrecomputedIntersects = intersects,
                    },
                });
                if (unclassified)
                {
                    result.ClassificationErrorCount++;
                    result.UnclassifiedIndices.Add(candidateIndex);
                    result.AddWarning("Preserved a geometry item with an unreadable bounding box visible: " + boundsErrorMessage);
                }
                else if (disposition == BoxIsolationNodeDisposition.StructuralContainer)
                {
                    result.StructuralContainerItemCount++;
                }
                else if (disposition == BoxIsolationNodeDisposition.EmptyLeaf)
                {
                    result.EmptyItemCount++;
                }
                else if (disposition == BoxIsolationNodeDisposition.Intersecting)
                {
                    result.IntersectingItemCount++;
                }
                else
                {
                    result.OutsideItemCount++;
                }

                // Autodesk's API contract defines ModelItem.BoundingBox() as the box of the
                // item and its children. A readable outside box therefore excludes the whole
                // subtree. We intentionally count only the skipped direct child branches;
                // enumerating every pruned descendant would defeat the bounded traversal.
                if (!BoxIsolationTraversalPolicy.ShouldDescend(disposition, childCount > 0))
                {
                    result.Accounting.RecordPrunedSubtree(childCount);
                }
                else if (children != null)
                {
                    foreach (ModelItem child in children)
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
            bool partial,
            bool timedOut,
            int maxDurationSeconds,
            long elapsedMilliseconds,
            BoxIsolationPlanningResult planning)
        {
            var classifiedCount = traversal.IntersectingItemCount +
                                  traversal.OutsideItemCount +
                                  traversal.UnclassifiedIndices.Count +
                                  traversal.StructuralContainerItemCount +
                                  traversal.EmptyItemCount;
            if (classifiedCount != traversal.Candidates.Count)
            {
                throw new AgentCommandException(
                    ErrorCodes.CommandFailed,
                    "Box isolation classification counts are inconsistent; visibility was not changed.");
            }
            if (!planning.TimedOut &&
                (plan.IntersectingIndices.Count != traversal.IntersectingItemCount ||
                 plan.OutsideIndices.Count != traversal.OutsideItemCount ||
                 plan.UnclassifiedIndices.Count != traversal.UnclassifiedIndices.Count ||
                 planning.ClassificationProcessedItemCount != traversal.Candidates.Count ||
                 planning.VisibilityProcessedItemCount != traversal.Candidates.Count))
            {
                throw new AgentCommandException(
                    ErrorCodes.CommandFailed,
                    "Box isolation planning counts are inconsistent; visibility was not changed.");
            }

            var response = new IsolateByBoxResponse
            {
                ApplyRequested = applyRequested,
                Applied = false,
                Partial = partial,
                TraversalTruncated = traversal.TraversalTruncated,
                TimedOut = timedOut,
                MaxDurationSeconds = maxDurationSeconds,
                ElapsedMilliseconds = elapsedMilliseconds,
                ClassificationErrorCount = traversal.ClassificationErrorCount,
                ScannedItemCount = traversal.Accounting.ScannedItemCount,
                IntersectingItemCount = traversal.IntersectingItemCount,
                OutsideItemCount = traversal.OutsideItemCount,
                ConservativeUnclassifiedItemCount = traversal.UnclassifiedIndices.Count,
                StructuralContainerItemCount = traversal.StructuralContainerItemCount,
                EmptyItemCount = traversal.EmptyItemCount,
                PrunedSubtreeRootCount = traversal.Accounting.PrunedSubtreeRootCount,
                PrunedDirectChildBranchCount = traversal.Accounting.PrunedDirectChildBranchCount,
                WouldKeepVisibleItemCount = plan.KeepVisibleIndices.Count,
                WouldHideItemCount = plan.HideIndices.Count,
                PreviouslyHiddenItemCount = plan.PreviouslyHiddenItemCount,
                WouldRevealItemCount = plan.WouldRevealItemCount,
                WouldChangeVisibilityItemCount = plan.WouldChangeVisibilityItemCount,
                AffectedItemsPreviewTruncated = plan.WouldChangeVisibilityItemCount > previewLimit,
                PreservedUnclassifiedPreviewTruncated = traversal.UnclassifiedIndices.Count > previewLimit,
                SelectionPreserved = !applyRequested || SelectionMatches(document, selectionSnapshot),
                SectionBoxPreserved = true,
                Warnings = traversal.Warnings.ToList(),
            };
            var affectedIndices = plan.RevealIndices
                .Concat(plan.NewlyHiddenIndices)
                .OrderBy(index => index)
                .Take(previewLimit);
            foreach (var candidateIndex in affectedIndices)
            {
                var candidate = traversal.Candidates[candidateIndex];
                var targetHidden = plan.NewlyHiddenIndices.Contains(candidateIndex);
                response.AffectedItemsPreview.Add(new BoxIsolationPreviewItem
                {
                    DisplayName = candidate.Item.DisplayName ?? string.Empty,
                    Path = BuildItemPath(candidate.Item),
                    WasHidden = candidate.PlanItem.WasHidden,
                    TargetHidden = targetHidden,
                });
            }
            foreach (var candidateIndex in traversal.UnclassifiedIndices.Take(previewLimit))
            {
                var candidate = traversal.Candidates[candidateIndex];
                response.PreservedUnclassifiedPreview.Add(new BoxIsolationPreviewItem
                {
                    DisplayName = candidate.Item.DisplayName ?? string.Empty,
                    Path = BuildItemPath(candidate.Item),
                    WasHidden = candidate.PlanItem.WasHidden,
                    TargetHidden = false,
                });
            }
            if (traversal.UnclassifiedIndices.Count > 0)
            {
                response.Warnings.Add(
                    traversal.UnclassifiedIndices.Count +
                    " item(s) had unreadable bounding boxes and were conservatively preserved visible with their ancestors.");
            }
            if (traversal.StructuralContainerItemCount > 0)
            {
                response.Warnings.Add(
                    traversal.StructuralContainerItemCount +
                    " non-geometry container(s) had no readable bounding box; they were preserved and their children were inspected.");
            }
            if (traversal.EmptyItemCount > 0)
            {
                response.Warnings.Add(
                    traversal.EmptyItemCount +
                    " empty non-geometry leaf item(s) had no readable bounding box and were preserved without a classification error.");
            }
            if (planning.TimedOut && !traversal.TimedOut)
            {
                response.Warnings.Add(
                    "The bounded visibility-planning phase did not complete; apply=true is rejected and no visibility was changed.");
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

        private static int ValidateMaxScannedItems(int? requested)
        {
            var value = requested.GetValueOrDefault(SectionBoxIsolationLimits.DefaultMaxScannedItems);
            if (value < 1 || value > SectionBoxIsolationLimits.MaximumMaxScannedItems)
            {
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    "maxScannedItems must be between 1 and " +
                    SectionBoxIsolationLimits.MaximumMaxScannedItems + ".");
            }
            return value;
        }

        private static int ValidateMaxDurationSeconds(int? requested)
        {
            try
            {
                return SectionBoxIsolationLimits.ValidateMaxDurationSeconds(requested);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }
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
            public TraversalResult(int maximumScannedItems)
            {
                Accounting = new BoxIsolationTraversalAccounting(maximumScannedItems);
            }

            public List<Candidate> Candidates { get; } = new List<Candidate>();
            public BoxIsolationTraversalAccounting Accounting { get; }
            public bool TraversalTruncated { get; set; }
            public bool TimedOut { get; set; }
            public int ClassificationErrorCount { get; set; }
            public int IntersectingItemCount { get; set; }
            public int OutsideItemCount { get; set; }
            public int StructuralContainerItemCount { get; set; }
            public int EmptyItemCount { get; set; }
            public List<int> UnclassifiedIndices { get; } = new List<int>();
            public List<string> Warnings { get; } = new List<string>();

            public void AddWarning(string warning)
            {
                if (Warnings.Count < 10)
                    Warnings.Add(warning);
            }
        }

        private sealed class StopwatchIsolationClock : ISectionBoxIsolationClock
        {
            private readonly Stopwatch _stopwatch;

            public StopwatchIsolationClock(Stopwatch stopwatch)
            {
                _stopwatch = stopwatch ?? throw new ArgumentNullException(nameof(stopwatch));
            }

            public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;
        }
    }
}
