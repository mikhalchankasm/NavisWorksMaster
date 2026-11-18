using System;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashBoundsOverlapInfo
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Min { get; set; }
    }

    public static class ClashBoundsOverlapHelper
    {
        public static ClashBoundsOverlapInfo Compute(BoundingBoxInfo a, BoundingBoxInfo b)
        {
            if (a == null || b == null || a.Min == null || a.Max == null || b.Min == null || b.Max == null)
                return null;

            var x = Math.Min(a.Max.X, b.Max.X) - Math.Max(a.Min.X, b.Min.X);
            var y = Math.Min(a.Max.Y, b.Max.Y) - Math.Max(a.Min.Y, b.Min.Y);
            var z = Math.Min(a.Max.Z, b.Max.Z) - Math.Max(a.Min.Z, b.Min.Z);
            return new ClashBoundsOverlapInfo { X = x, Y = y, Z = z, Min = Math.Min(x, Math.Min(y, z)) };
        }
    }
}
