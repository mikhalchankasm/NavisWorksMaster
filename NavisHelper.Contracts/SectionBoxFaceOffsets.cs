using System;

namespace NavisHelper.Agent.Contracts
{
    public sealed class SectionBoxFaceOffsets
    {
        public double? Top { get; set; }
        public double? Bottom { get; set; }
        public double? North { get; set; }
        public double? South { get; set; }
        public double? East { get; set; }
        public double? West { get; set; }
    }

    public static class SectionBoxFaceOffsetPlanner
    {
        public const double Open = -1;

        public static bool TryPlan(double[] clusterMin, double[] clusterMax, SectionBoxFaceOffsets offsets,
            double defaultOffsetMm, double unitsPerMm, double[] modelMin, double[] modelMax,
            out double[] min, out double[] max, out string error)
        {
            min = null;
            max = null;
            error = null;
            var values = new[] { offsets?.West, offsets?.East, offsets?.South, offsets?.North, offsets?.Bottom, offsets?.Top };
            var names = new[] { "west", "east", "south", "north", "bottom", "top" };
            for (var face = 0; face < values.Length; face++)
            {
                var value = values[face].GetValueOrDefault(defaultOffsetMm);
                if (double.IsNaN(value) || double.IsInfinity(value) || (value < 0 && value != Open))
                {
                    error = names[face] + " must be >= 0 or -1 (open).";
                    return false;
                }
            }

            min = new double[3];
            max = new double[3];
            var margin = defaultOffsetMm * unitsPerMm;
            for (var axis = 0; axis < 3; axis++)
            {
                var lower = values[axis * 2].GetValueOrDefault(defaultOffsetMm);
                var upper = values[axis * 2 + 1].GetValueOrDefault(defaultOffsetMm);
                min[axis] = lower == Open
                    ? Math.Min(modelMin[axis], clusterMin[axis]) - margin
                    : clusterMin[axis] - lower * unitsPerMm;
                max[axis] = upper == Open
                    ? Math.Max(modelMax[axis], clusterMax[axis]) + margin
                    : clusterMax[axis] + upper * unitsPerMm;
            }
            return true;
        }
    }
}
