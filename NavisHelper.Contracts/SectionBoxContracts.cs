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
        public int ClassificationErrorCount { get; set; }
        public int ScannedItemCount { get; set; }
        public int IntersectingItemCount { get; set; }
        public int WouldKeepVisibleItemCount { get; set; }
        public int WouldHideItemCount { get; set; }
        public int PreviouslyHiddenItemCount { get; set; }
        public int WouldRevealItemCount { get; set; }
        public int WouldChangeVisibilityItemCount { get; set; }
        public int? VisibleItemCount { get; set; }
        public int? HiddenItemCount { get; set; }
        public bool AffectedItemsPreviewTruncated { get; set; }
        public List<BoxIsolationPreviewItem> AffectedItemsPreview { get; set; } = new List<BoxIsolationPreviewItem>();
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
    }

    public sealed class BoxIsolationPlan
    {
        public List<int> UnclassifiedIndices { get; set; } = new List<int>();
        public List<int> IntersectingIndices { get; set; } = new List<int>();
        public List<int> KeepVisibleIndices { get; set; } = new List<int>();
        public List<int> HideIndices { get; set; } = new List<int>();
        public int PreviouslyHiddenItemCount { get; set; }
        public int WouldRevealItemCount { get; set; }
        public int WouldChangeVisibilityItemCount { get; set; }
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
            var magnitude = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (magnitude <= QuaternionTolerance)
                throw new ArgumentException("rotation quaternion must not be degenerate.");
            x /= magnitude;
            y /= magnitude;
            z /= magnitude;
            w /= magnitude;

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

        internal sealed class SectionBoxIntersectionTester
        {
            private readonly SectionBoxGeometry _box;
            private readonly double[,] _rotation = new double[3, 3];
            private readonly double[,] _absolute = new double[3, 3];
            private readonly double _extent0;
            private readonly double _extent1;
            private readonly double _extent2;

            internal SectionBoxIntersectionTester(SectionBoxGeometry box)
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

            internal bool Intersects(BoxVector3 minimum, BoxVector3 maximum)
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
            SectionBoxGeometryRules.Validate(box);
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            var plan = new BoxIsolationPlan();
            var keep = new bool[items.Count];
            var tester = new SectionBoxGeometryRules.SectionBoxIntersectionTester(box);
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index] ?? throw new ArgumentException("plan items must not contain null values.");
                if (item.ParentIndex >= index || item.ParentIndex < -1)
                    throw new ArgumentException("plan item parentIndex must refer to an earlier item or -1.");
                if (item.WasHidden)
                    plan.PreviouslyHiddenItemCount++;
                if (item.Unclassified)
                {
                    plan.UnclassifiedIndices.Add(index);
                    continue;
                }
                if (!tester.Intersects(item.BoundsMin, item.BoundsMax))
                    continue;

                plan.IntersectingIndices.Add(index);
                var current = index;
                while (current >= 0 && !keep[current])
                {
                    keep[current] = true;
                    current = items[current].ParentIndex;
                }
            }

            for (var index = 0; index < items.Count; index++)
            {
                if (items[index].Unclassified)
                    continue;
                if (keep[index])
                {
                    plan.KeepVisibleIndices.Add(index);
                    if (items[index].WasHidden)
                    {
                        plan.WouldRevealItemCount++;
                        plan.WouldChangeVisibilityItemCount++;
                    }
                }
                else
                {
                    plan.HideIndices.Add(index);
                    if (!items[index].WasHidden)
                        plan.WouldChangeVisibilityItemCount++;
                }
            }

            return plan;
        }
    }

    public static class BoxIsolationExecutionRules
    {
        public static bool ShouldApply(bool applyRequested, bool partial)
        {
            return applyRequested && !partial;
        }
    }
}
