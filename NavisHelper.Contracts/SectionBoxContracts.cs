using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class BoxVector3
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public sealed class SectionBoxGeometry
    {
        public int FormatVersion { get; set; } = 1;
        public string CoordinateSpace { get; set; } = SectionBoxGeometryRules.DocumentGlobal;
        public string DocumentUnits { get; set; }
        public BoxVector3 Center { get; set; }
        public BoxVector3 HalfExtents { get; set; }
        public List<BoxVector3> Axes { get; set; } = new List<BoxVector3>();
    }

    public sealed class GetCurrentSectionBoxRequest
    {
    }

    public sealed class GetCurrentSectionBoxResponse
    {
        public bool Enabled { get; set; }
        public string Mode { get; set; }
        public SectionBoxGeometry Box { get; set; }
    }

    public sealed class IsolateByBoxRequest
    {
        public SectionBoxGeometry Box { get; set; }
        public bool? Apply { get; set; }
        public int? MaxScannedItems { get; set; }
        public int? MaxDurationSeconds { get; set; }
        public int? PreviewLimit { get; set; }
    }

    public sealed class IsolateByBoxResponse
    {
        public bool ApplyRequested { get; set; }
        public bool Applied { get; set; }
        public bool ApplyRejected { get; set; }
        public string ApplyRejectionCode { get; set; }
        public bool Partial { get; set; }
        public bool TraversalTruncated { get; set; }
        public bool TimedOut { get; set; }
        public int MaxDurationSeconds { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public int ClassificationErrorCount { get; set; }
        public int ScannedItemCount { get; set; }
        public int IntersectingItemCount { get; set; }
        public int OutsideItemCount { get; set; }
        public int ConservativeUnclassifiedItemCount { get; set; }
        public int StructuralContainerItemCount { get; set; }
        public int EmptyItemCount { get; set; }
        public int PrunedSubtreeRootCount { get; set; }
        public int PrunedDirectChildBranchCount { get; set; }
        public int WouldKeepVisibleItemCount { get; set; }
        public int WouldHideItemCount { get; set; }
        public int PreviouslyHiddenItemCount { get; set; }
        public int WouldRevealItemCount { get; set; }
        public int WouldChangeVisibilityItemCount { get; set; }
        public int? VisibleItemCount { get; set; }
        public int? HiddenItemCount { get; set; }
        public bool AffectedItemsPreviewTruncated { get; set; }
        public List<BoxIsolationPreviewItem> AffectedItemsPreview { get; set; } = new List<BoxIsolationPreviewItem>();
        public bool PreservedUnclassifiedPreviewTruncated { get; set; }
        public List<BoxIsolationPreviewItem> PreservedUnclassifiedPreview { get; set; } = new List<BoxIsolationPreviewItem>();
        public bool SelectionPreserved { get; set; }
        public bool SectionBoxPreserved { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class BoxIsolationPreviewItem
    {
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public bool WasHidden { get; set; }
        public bool TargetHidden { get; set; }
    }

    public sealed class BoxIsolationPlanItem
    {
        public int ParentIndex { get; set; } = -1;
        public BoxVector3 BoundsMin { get; set; }
        public BoxVector3 BoundsMax { get; set; }
        public bool WasHidden { get; set; }
        public bool Unclassified { get; set; }
        public bool PreserveCurrentVisibility { get; set; }
        public bool HasPrecomputedIntersection { get; set; }
        public bool PrecomputedIntersects { get; set; }
    }

    public sealed class BoxIsolationPlan
    {
        public List<int> UnclassifiedIndices { get; set; } = new List<int>();
        public List<int> IntersectingIndices { get; set; } = new List<int>();
        public List<int> OutsideIndices { get; set; } = new List<int>();
        public List<int> KeepVisibleIndices { get; set; } = new List<int>();
        public List<int> HideIndices { get; set; } = new List<int>();
        public List<int> RevealIndices { get; set; } = new List<int>();
        public List<int> NewlyHiddenIndices { get; set; } = new List<int>();
        public int PreviouslyHiddenItemCount { get; set; }
        public int WouldRevealItemCount { get; set; }
        public int WouldChangeVisibilityItemCount { get; set; }
    }

    public sealed class BoxIsolationPlanningResult
    {
        public BoxIsolationPlan Plan { get; set; } = new BoxIsolationPlan();
        public bool TimedOut { get; set; }
        public int ClassificationProcessedItemCount { get; set; }
        public int VisibilityProcessedItemCount { get; set; }
    }

    public enum BoxIsolationNodeDisposition
    {
        Intersecting,
        OutsideSubtree,
        StructuralContainer,
        EmptyLeaf,
        Unclassified,
    }

    public static class BoxIsolationTraversalPolicy
    {
        public static BoxIsolationNodeDisposition Classify(
            bool boundsReadable,
            bool intersects,
            bool geometryStatusKnown,
            bool hasGeometry,
            bool hasChildren)
        {
            if (boundsReadable)
                return intersects
                    ? BoxIsolationNodeDisposition.Intersecting
                    : BoxIsolationNodeDisposition.OutsideSubtree;
            if (geometryStatusKnown && !hasGeometry)
                return hasChildren
                    ? BoxIsolationNodeDisposition.StructuralContainer
                    : BoxIsolationNodeDisposition.EmptyLeaf;
            return BoxIsolationNodeDisposition.Unclassified;
        }

        public static bool ShouldDescend(BoxIsolationNodeDisposition disposition, bool hasChildren)
        {
            if (!hasChildren)
                return false;
            return disposition != BoxIsolationNodeDisposition.OutsideSubtree;
        }

        public static bool IsRealClassificationError(BoxIsolationNodeDisposition disposition)
        {
            return disposition == BoxIsolationNodeDisposition.Unclassified;
        }

        public static bool ShouldPreserveCurrentVisibility(BoxIsolationNodeDisposition disposition)
        {
            return disposition == BoxIsolationNodeDisposition.StructuralContainer ||
                   disposition == BoxIsolationNodeDisposition.EmptyLeaf;
        }
    }

    public sealed class BoxIsolationTraversalAccounting
    {
        private readonly int _maximumScannedItems;

        public BoxIsolationTraversalAccounting(int maximumScannedItems)
        {
            if (maximumScannedItems < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumScannedItems));
            _maximumScannedItems = maximumScannedItems;
        }

        public int ScannedItemCount { get; private set; }
        public int PrunedSubtreeRootCount { get; private set; }
        public int PrunedDirectChildBranchCount { get; private set; }

        public bool TryRegisterScannedItem()
        {
            if (ScannedItemCount >= _maximumScannedItems)
                return false;
            ScannedItemCount++;
            return true;
        }

        public void RecordPrunedSubtree(int directChildCount)
        {
            if (directChildCount < 1)
                return;
            PrunedSubtreeRootCount++;
            PrunedDirectChildBranchCount = checked(PrunedDirectChildBranchCount + directChildCount);
        }
    }

    public static class SectionBoxIsolationLimits
    {
        public const int DefaultMaxScannedItems = 500000;
        public const int MaximumMaxScannedItems = 500000;
        public const int DefaultMaxDurationSeconds = 60;
        public const int MaximumMaxDurationSeconds = 480;
        public const int PostTraversalHostReserveSeconds = 90;
        public const int BridgeSetupReserveSeconds = 5;
        public const int RecommendedClientResponseReserveSeconds = 5;

        public static int ValidateMaxDurationSeconds(int? requested)
        {
            var value = requested.GetValueOrDefault(DefaultMaxDurationSeconds);
            if (value < 1 || value > MaximumMaxDurationSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requested),
                    "maxDurationSeconds must be between 1 and " + MaximumMaxDurationSeconds + ".");
            }
            return value;
        }

        public static int GetBridgeRequestTimeoutMilliseconds(int maxDurationSeconds)
        {
            var validated = ValidateMaxDurationSeconds(maxDurationSeconds);
            return checked(
                (validated + PostTraversalHostReserveSeconds + BridgeSetupReserveSeconds) * 1000 +
                ProtocolConstants.HostTransportResponseMarginMilliseconds);
        }

        public static int GetRecommendedClientTimeoutMilliseconds(int maxDurationSeconds)
        {
            return checked(
                GetBridgeRequestTimeoutMilliseconds(maxDurationSeconds) +
                RecommendedClientResponseReserveSeconds * 1000);
        }
    }

    public interface ISectionBoxIsolationClock
    {
        long ElapsedMilliseconds { get; }
    }

    public sealed class SectionBoxIsolationDurationGuard
    {
        private readonly ISectionBoxIsolationClock _clock;
        private readonly long _limitMilliseconds;

        public SectionBoxIsolationDurationGuard(int maxDurationSeconds, ISectionBoxIsolationClock clock)
        {
            MaxDurationSeconds = SectionBoxIsolationLimits.ValidateMaxDurationSeconds(maxDurationSeconds);
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _limitMilliseconds = checked((long)MaxDurationSeconds * 1000L);
        }

        public int MaxDurationSeconds { get; }

        public long ElapsedMilliseconds => _clock.ElapsedMilliseconds;

        public bool ShouldStop(bool hasRemainingWork)
        {
            return hasRemainingWork && ElapsedMilliseconds >= _limitMilliseconds;
        }
    }

    public static class SectionBoxGeometryRules
    {
        public const int CurrentFormatVersion = 1;
        public const string DocumentGlobal = "document_global";
        private const double AxisTolerance = 1e-7;
        private const double QuaternionTolerance = 1e-12;

        public static void Validate(SectionBoxGeometry box)
        {
            if (box == null)
                throw new ArgumentException("box is required.");
            if (box.FormatVersion != CurrentFormatVersion)
                throw new ArgumentException("box.formatVersion must be 1.");
            if (!string.Equals(box.CoordinateSpace, DocumentGlobal, StringComparison.Ordinal))
                throw new ArgumentException("box.coordinateSpace must be document_global.");
            if (string.IsNullOrWhiteSpace(box.DocumentUnits))
                throw new ArgumentException("box.documentUnits is required.");

            ValidateFiniteVector(box.Center, "box.center");
            ValidateFiniteVector(box.HalfExtents, "box.halfExtents");
            if (box.HalfExtents.X <= 0 || box.HalfExtents.Y <= 0 || box.HalfExtents.Z <= 0)
                throw new ArgumentException("box.halfExtents components must be greater than zero.");
            if (box.Axes == null || box.Axes.Count != 3)
                throw new ArgumentException("box.axes must contain exactly three vectors.");

            for (var index = 0; index < box.Axes.Count; index++)
            {
                ValidateFiniteVector(box.Axes[index], "box.axes[" + index + "]");
                var length = Length(box.Axes[index]);
                if (Math.Abs(length - 1.0) > AxisTolerance)
                    throw new ArgumentException("box.axes must be unit vectors.");
            }

            if (Math.Abs(Dot(box.Axes[0], box.Axes[1])) > AxisTolerance ||
                Math.Abs(Dot(box.Axes[0], box.Axes[2])) > AxisTolerance ||
                Math.Abs(Dot(box.Axes[1], box.Axes[2])) > AxisTolerance)
            {
                throw new ArgumentException("box.axes must be mutually orthogonal.");
            }

            var handedness = Dot(Cross(box.Axes[0], box.Axes[1]), box.Axes[2]);
            if (Math.Abs(handedness - 1.0) > AxisTolerance)
                throw new ArgumentException("box.axes must form a right-handed rotation basis.");
        }

        public static List<BoxVector3> AxesFromQuaternion(double x, double y, double z, double w)
        {
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z) || !IsFinite(w))
                throw new ArgumentException("rotation quaternion must contain finite numbers.");
            var scale = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Max(Math.Abs(z), Math.Abs(w)));
            if (scale == 0)
                throw new ArgumentException("rotation quaternion must not be degenerate.");
            var scaledX = x / scale;
            var scaledY = y / scale;
            var scaledZ = z / scale;
            var scaledW = w / scale;
            var scaledMagnitude = Math.Sqrt(
                scaledX * scaledX + scaledY * scaledY + scaledZ * scaledZ + scaledW * scaledW);
            if (scale <= QuaternionTolerance / scaledMagnitude)
                throw new ArgumentException("rotation quaternion must not be degenerate.");
            x = scaledX / scaledMagnitude;
            y = scaledY / scaledMagnitude;
            z = scaledZ / scaledMagnitude;
            w = scaledW / scaledMagnitude;

            return new List<BoxVector3>
            {
                new BoxVector3
                {
                    X = 1 - 2 * (y * y + z * z),
                    Y = 2 * (x * y + z * w),
                    Z = 2 * (x * z - y * w),
                },
                new BoxVector3
                {
                    X = 2 * (x * y - z * w),
                    Y = 1 - 2 * (x * x + z * z),
                    Z = 2 * (y * z + x * w),
                },
                new BoxVector3
                {
                    X = 2 * (x * z + y * w),
                    Y = 2 * (y * z - x * w),
                    Z = 1 - 2 * (x * x + y * y),
                },
            };
        }

        public static bool IntersectsAabb(SectionBoxGeometry box, BoxVector3 minimum, BoxVector3 maximum)
        {
            Validate(box);
            return new SectionBoxIntersectionTester(box).Intersects(minimum, maximum);
        }

        public sealed class SectionBoxIntersectionTester
        {
            private readonly SectionBoxGeometry _box;
            private readonly double[,] _rotation = new double[3, 3];
            private readonly double[,] _absolute = new double[3, 3];
            private readonly double _extent0;
            private readonly double _extent1;
            private readonly double _extent2;

            public SectionBoxIntersectionTester(SectionBoxGeometry box)
            {
                _box = box;
                _extent0 = box.HalfExtents.X;
                _extent1 = box.HalfExtents.Y;
                _extent2 = box.HalfExtents.Z;
                const double epsilon = 1e-12;
                for (var row = 0; row < 3; row++)
                {
                    for (var column = 0; column < 3; column++)
                    {
                        _rotation[row, column] = Component(box.Axes[column], row);
                        _absolute[row, column] = Math.Abs(_rotation[row, column]) + epsilon;
                    }
                }
            }

            public bool Intersects(BoxVector3 minimum, BoxVector3 maximum)
            {
            ValidateBounds(minimum, maximum);
            var a0 = (maximum.X - minimum.X) * 0.5;
            var a1 = (maximum.Y - minimum.Y) * 0.5;
            var a2 = (maximum.Z - minimum.Z) * 0.5;
            var t0 = _box.Center.X - (minimum.X + maximum.X) * 0.5;
            var t1 = _box.Center.Y - (minimum.Y + maximum.Y) * 0.5;
            var t2 = _box.Center.Z - (minimum.Z + maximum.Z) * 0.5;

            for (var row = 0; row < 3; row++)
            {
                var radiusA = Value(row, a0, a1, a2);
                var radiusB = _extent0 * _absolute[row, 0] + _extent1 * _absolute[row, 1] + _extent2 * _absolute[row, 2];
                if (Math.Abs(Value(row, t0, t1, t2)) > radiusA + radiusB)
                    return false;
            }

            for (var column = 0; column < 3; column++)
            {
                var radiusA = a0 * _absolute[0, column] + a1 * _absolute[1, column] + a2 * _absolute[2, column];
                var radiusB = Value(column, _extent0, _extent1, _extent2);
                var distance = Math.Abs(t0 * _rotation[0, column] + t1 * _rotation[1, column] + t2 * _rotation[2, column]);
                if (distance > radiusA + radiusB)
                    return false;
            }

            for (var row = 0; row < 3; row++)
            {
                var row1 = (row + 1) % 3;
                var row2 = (row + 2) % 3;
                for (var column = 0; column < 3; column++)
                {
                    var column1 = (column + 1) % 3;
                    var column2 = (column + 2) % 3;
                    var radiusA = Value(row1, a0, a1, a2) * _absolute[row2, column] +
                                  Value(row2, a0, a1, a2) * _absolute[row1, column];
                    var radiusB = Value(column1, _extent0, _extent1, _extent2) * _absolute[row, column2] +
                                  Value(column2, _extent0, _extent1, _extent2) * _absolute[row, column1];
                    var distance = Math.Abs(
                        Value(row2, t0, t1, t2) * _rotation[row1, column] -
                        Value(row1, t0, t1, t2) * _rotation[row2, column]);
                    if (distance > radiusA + radiusB)
                        return false;
                }
            }

            return true;
            }

            private static double Value(int index, double value0, double value1, double value2)
            {
                return index == 0 ? value0 : index == 1 ? value1 : value2;
            }
        }

        public static void ValidateBounds(BoxVector3 minimum, BoxVector3 maximum)
        {
            ValidateFiniteVector(minimum, "bounds minimum");
            ValidateFiniteVector(maximum, "bounds maximum");
            if (minimum.X > maximum.X || minimum.Y > maximum.Y || minimum.Z > maximum.Z)
                throw new ArgumentException("bounds minimum must not exceed maximum.");
        }

        private static void ValidateFiniteVector(BoxVector3 vector, string name)
        {
            if (vector == null)
                throw new ArgumentException(name + " is required.");
            if (!IsFinite(vector.X) || !IsFinite(vector.Y) || !IsFinite(vector.Z))
                throw new ArgumentException(name + " must contain finite numbers.");
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double Component(BoxVector3 vector, int index)
        {
            return index == 0 ? vector.X : index == 1 ? vector.Y : vector.Z;
        }

        private static double Dot(BoxVector3 left, BoxVector3 right)
        {
            return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        }

        private static BoxVector3 Cross(BoxVector3 left, BoxVector3 right)
        {
            return new BoxVector3
            {
                X = left.Y * right.Z - left.Z * right.Y,
                Y = left.Z * right.X - left.X * right.Z,
                Z = left.X * right.Y - left.Y * right.X,
            };
        }

        private static double Length(BoxVector3 vector)
        {
            return Math.Sqrt(Dot(vector, vector));
        }
    }

    public static class BoxIsolationPlanner
    {
        public static BoxIsolationPlan Build(SectionBoxGeometry box, IList<BoxIsolationPlanItem> items)
        {
            var result = BuildBounded(box, items, null);
            if (result.TimedOut)
                throw new InvalidOperationException("Unbounded box isolation planning unexpectedly timed out.");
            return result.Plan;
        }

        public static BoxIsolationPlanningResult BuildBounded(
            SectionBoxGeometry box,
            IList<BoxIsolationPlanItem> items,
            SectionBoxIsolationDurationGuard durationGuard)
        {
            SectionBoxGeometryRules.Validate(box);
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            var result = new BoxIsolationPlanningResult();
            var plan = result.Plan;
            var keep = new bool[items.Count];
            var tester = new SectionBoxGeometryRules.SectionBoxIntersectionTester(box);
            for (var index = 0; index < items.Count; index++)
            {
                if (durationGuard != null && durationGuard.ShouldStop(hasRemainingWork: true))
                {
                    result.TimedOut = true;
                    return result;
                }
                var item = items[index] ?? throw new ArgumentException("plan items must not contain null values.");
                if (item.ParentIndex >= index || item.ParentIndex < -1)
                    throw new ArgumentException("plan item parentIndex must refer to an earlier item or -1.");
                if (item.WasHidden)
                    plan.PreviouslyHiddenItemCount++;
                if (item.Unclassified)
                {
                    plan.UnclassifiedIndices.Add(index);
                    KeepItemAndAncestors(items, keep, index);
                    result.ClassificationProcessedItemCount = index + 1;
                    continue;
                }
                if (item.PreserveCurrentVisibility)
                {
                    result.ClassificationProcessedItemCount = index + 1;
                    continue;
                }
                var intersects = item.HasPrecomputedIntersection
                    ? item.PrecomputedIntersects
                    : tester.Intersects(item.BoundsMin, item.BoundsMax);
                if (!intersects)
                {
                    plan.OutsideIndices.Add(index);
                    result.ClassificationProcessedItemCount = index + 1;
                    continue;
                }

                plan.IntersectingIndices.Add(index);
                KeepItemAndAncestors(items, keep, index);
                result.ClassificationProcessedItemCount = index + 1;
            }

            for (var index = 0; index < items.Count; index++)
            {
                if (durationGuard != null && durationGuard.ShouldStop(hasRemainingWork: true))
                {
                    result.TimedOut = true;
                    return result;
                }
                if (keep[index])
                {
                    plan.KeepVisibleIndices.Add(index);
                    if (items[index].WasHidden)
                    {
                        plan.RevealIndices.Add(index);
                        plan.WouldRevealItemCount++;
                        plan.WouldChangeVisibilityItemCount++;
                    }
                }
                else if (items[index].PreserveCurrentVisibility)
                {
                    if (items[index].WasHidden)
                        plan.HideIndices.Add(index);
                    else
                        plan.KeepVisibleIndices.Add(index);
                }
                else
                {
                    plan.HideIndices.Add(index);
                    if (!items[index].WasHidden)
                    {
                        plan.NewlyHiddenIndices.Add(index);
                        plan.WouldChangeVisibilityItemCount++;
                    }
                }
                result.VisibilityProcessedItemCount = index + 1;
            }

            return result;
        }

        private static void KeepItemAndAncestors(IList<BoxIsolationPlanItem> items, bool[] keep, int index)
        {
            var current = index;
            while (current >= 0 && !keep[current])
            {
                keep[current] = true;
                current = items[current].ParentIndex;
            }
        }
    }

    public static class BoxIsolationExecutionRules
    {
        public static bool IsPartial(bool traversalTruncated, bool timedOut)
        {
            return traversalTruncated || timedOut;
        }

        public static bool ShouldApply(bool applyRequested, bool partial, int classificationErrorCount)
        {
            return applyRequested && !partial && classificationErrorCount == 0;
        }

        public static bool HasTimedOut(long elapsedMilliseconds, int limitMilliseconds)
        {
            return elapsedMilliseconds >= limitMilliseconds;
        }
    }
}
