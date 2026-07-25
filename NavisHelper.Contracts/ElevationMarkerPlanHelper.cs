using System;
using System.Collections.Generic;
using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    public static class ElevationMarkerPlanHelper
    {
        public const int DefaultMaximumItemCount = 100;

        public static IReadOnlyList<ElevationMarkerPlanItem> Build(
            IReadOnlyList<ElevationBounds> bounds,
            double targetZ,
            int maximumItemCount = DefaultMaximumItemCount)
        {
            if (bounds == null)
                throw new ArgumentNullException(nameof(bounds));
            if (bounds.Count == 0)
                throw new ArgumentException("At least one object is required.", nameof(bounds));
            if (maximumItemCount < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumItemCount));
            if (bounds.Count > maximumItemCount)
                throw new InvalidOperationException(
                    "The elevation batch contains " + bounds.Count +
                    " objects, which exceeds the safety limit of " + maximumItemCount + ".");
            if (!IsFinite(targetZ))
                throw new ArgumentOutOfRangeException(nameof(targetZ));

            var result = new List<ElevationMarkerPlanItem>(bounds.Count);
            for (var index = 0; index < bounds.Count; index++)
            {
                var item = bounds[index];
                ValidateBounds(item, index);
                var topZ = item.MaxZ;
                result.Add(new ElevationMarkerPlanItem
                {
                    SourceIndex = index,
                    TopX = (item.MinX + item.MaxX) / 2.0,
                    TopY = (item.MinY + item.MaxY) / 2.0,
                    TopZ = topZ,
                    TargetZ = targetZ,
                    Difference = topZ - targetZ,
                });
            }

            return result;
        }

        public static string FormatElevationLabel(double topZMillimeters)
        {
            if (!IsFinite(topZMillimeters))
                throw new ArgumentOutOfRangeException(nameof(topZMillimeters));

            var roundedMillimeters = Math.Round(
                topZMillimeters,
                0,
                MidpointRounding.AwayFromZero);
            return "Z=" +
                   roundedMillimeters.ToString("+0;-0;0", CultureInfo.InvariantCulture) +
                   " MM";
        }

        private static void ValidateBounds(ElevationBounds bounds, int index)
        {
            if (bounds == null)
                throw new ArgumentException("Object bounds at index " + index + " are missing.");
            if (!IsFinite(bounds.MinX) ||
                !IsFinite(bounds.MinY) ||
                !IsFinite(bounds.MinZ) ||
                !IsFinite(bounds.MaxX) ||
                !IsFinite(bounds.MaxY) ||
                !IsFinite(bounds.MaxZ))
            {
                throw new ArgumentException("Object bounds at index " + index + " are not finite.");
            }
            if (bounds.MaxX < bounds.MinX ||
                bounds.MaxY < bounds.MinY ||
                bounds.MaxZ < bounds.MinZ)
            {
                throw new ArgumentException("Object bounds at index " + index + " are inverted.");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public sealed class ElevationBounds
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }
    }

    public sealed class ElevationMarkerPlanItem
    {
        public int SourceIndex { get; set; }
        public double TopX { get; set; }
        public double TopY { get; set; }
        public double TopZ { get; set; }
        public double TargetZ { get; set; }
        public double Difference { get; set; }
    }
}
