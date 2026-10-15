using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>Deterministic, runtime-independent enclosing spheres and clash zones.</summary>
    public static class ClashZoneGeometry
    {
        /// <summary>Finds the minimum enclosing sphere without changing the input collection.</summary>
        public static ClashZoneSphere MinimumEnclosingSphere(IList<ClashZonePoint> points)
        {
            if (points == null)
                throw new ArgumentNullException(nameof(points));
            if (points.Count == 0)
                throw new ArgumentException("At least one point is required.", nameof(points));
            foreach (var point in points)
                if (point == null || !InRange(point.X) || !InRange(point.Y) || !InRange(point.Z))
                    throw new ArgumentException("Point coordinates must be finite and within +/-1e12 mm.", nameof(points));
            var ordered = Shuffle(OrderPoints(points));
            return Solve(ordered, ordered.Count, new List<ClashZonePoint>(4));
        }

        /// <summary>Splits each cluster by enclosing radius, then numbers zones in canonical order.</summary>
        public static IList<ClashZone> BuildZones(
            IEnumerable<ClashZoneInputCluster> clusters, ClashZoneGeometryOptions options)
        {
            if (clusters == null)
                throw new ArgumentNullException(nameof(clusters));
            options = options ?? new ClashZoneGeometryOptions();
            if (!Finite(options.MaxZoneRadiusMm) || options.MaxZoneRadiusMm <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxZoneRadiusMm));
            if (!Finite(options.MinRadiusMm) || options.MinRadiusMm < 0)
                throw new ArgumentOutOfRangeException(nameof(options.MinRadiusMm));
            if (!Finite(options.PadMm) || options.PadMm < 0)
                throw new ArgumentOutOfRangeException(nameof(options.PadMm));
            var zones = new List<ClashZone>();
            foreach (var cluster in clusters)
            {
                if (cluster == null)
                    throw new ArgumentException("Clusters must not contain null entries.", nameof(clusters));
                if (cluster.Points != null && cluster.Points.Count > 0)
                    Split(cluster.PartitionKey, cluster.Points, options, zones);
            }
            zones = zones.OrderBy(z => z.PartitionKey, StringComparer.Ordinal)
                .ThenBy(z => z.CentroidX).ThenBy(z => z.CentroidY).ThenBy(z => z.CentroidZ)
                .ThenBy(z => z.Points[0].Id, StringComparer.Ordinal).ToList();
            for (var i = 0; i < zones.Count; i++)
                zones[i].ZoneId = "Z-" + (i + 1).ToString("D4", CultureInfo.InvariantCulture);
            return zones;
        }

        private static void Split(string key, IList<ClashZonePoint> points,
            ClashZoneGeometryOptions options, List<ClashZone> zones)
        {
            var sphere = MinimumEnclosingSphere(points);
            if (sphere.Radius > options.MaxZoneRadiusMm && points.Count >= 2)
            {
                var spans = new[]
                {
                    points.Max(p => p.X) - points.Min(p => p.X),
                    points.Max(p => p.Y) - points.Min(p => p.Y),
                    points.Max(p => p.Z) - points.Min(p => p.Z)
                };
                var axis = spans[0] >= spans[1] && spans[0] >= spans[2] ? 0
                    : spans[1] >= spans[2] ? 1 : 2;
                var ordered = points.OrderBy(p => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z)
                    .ThenBy(p => p.Id, StringComparer.Ordinal)
                    .ThenBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).ToList();
                var half = ordered.Count / 2;
                Split(key, ordered.GetRange(0, half), options, zones);
                Split(key, ordered.GetRange(half, ordered.Count - half), options, zones);
                return;
            }
            var members = OrderPoints(points);
            zones.Add(new ClashZone
            {
                PartitionKey = key, Points = members,
                CenterX = sphere.X, CenterY = sphere.Y, CenterZ = sphere.Z,
                EnclosingRadiusMm = sphere.Radius,
                RadiusMm = Math.Max(options.MinRadiusMm, sphere.Radius + options.PadMm),
                CentroidX = members.Average(p => p.X), CentroidY = members.Average(p => p.Y),
                CentroidZ = members.Average(p => p.Z)
            });
        }

        private static List<ClashZonePoint> OrderPoints(IEnumerable<ClashZonePoint> points) =>
            points.OrderBy(p => p.Id, StringComparer.Ordinal)
                .ThenBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).ToList();

        // Welzl is expected-linear only for a random order: a chain with sequential ids would cost
        // n^2/2 containment checks. A fixed-seed permutation of the sorted points keeps the result
        // independent of input order while restoring the expected bound.
        private static List<ClashZonePoint> Shuffle(List<ClashZonePoint> ordered)
        {
            var state = 0x9E3779B97F4A7C15UL;
            for (var i = ordered.Count - 1; i > 0; i--)
            {
                state ^= state << 13;
                state ^= state >> 7;
                state ^= state << 17;
                var j = (int)(state % (ulong)(i + 1));
                var swap = ordered[i];
                ordered[i] = ordered[j];
                ordered[j] = swap;
            }
            return ordered;
        }

        // Welzl's move-to-front variant: recursion follows support points, not input size.
        private static ClashZoneSphere Solve(List<ClashZonePoint> points, int count,
            List<ClashZonePoint> boundary)
        {
            var sphere = SupportSphere(boundary);
            if (boundary.Count == 4)
                return sphere;
            for (var i = 0; i < count; i++)
            {
                var point = points[i];
                if (Contains(sphere, point))
                    continue;
                boundary.Add(point);
                sphere = Solve(points, i, boundary);
                boundary.RemoveAt(boundary.Count - 1);
                points.RemoveAt(i);
                points.Insert(0, point);
            }
            return sphere;
        }

        // Enumerating the <= 15 nonempty subsets handles coincident, collinear and coplanar supports.
        private static ClashZoneSphere SupportSphere(IList<ClashZonePoint> boundary)
        {
            ClashZoneSphere best = null;
            for (var mask = 1; mask < (1 << boundary.Count); mask++)
            {
                var support = new List<ClashZonePoint>(4);
                for (var i = 0; i < boundary.Count; i++)
                    if ((mask & (1 << i)) != 0)
                        support.Add(boundary[i]);
                var candidate = Through(support);
                if (candidate != null && (best == null || candidate.Radius < best.Radius)
                    && boundary.All(p => Contains(candidate, p)))
                    best = candidate;
            }
            return best;
        }

        private static ClashZoneSphere Through(IList<ClashZonePoint> points)
        {
            var origin = new Vector(points[0]);
            var offset = new Vector(0, 0, 0);
            if (points.Count >= 2)
            {
                var u = new Vector(points[1]) - origin;
                offset = u * 0.5;
                if (points.Count >= 3)
                {
                    var v = new Vector(points[2]) - origin;
                    var normal = Cross(u, v);
                    if (points.Count == 3)
                    {
                        var denominator = 2 * Dot(normal, normal);
                        if (denominator == 0)
                            return null;
                        offset = (Cross(normal, u) * Dot(v, v) + Cross(v, normal) * Dot(u, u))
                            * (1 / denominator);
                    }
                    else
                    {
                        var w = new Vector(points[3]) - origin;
                        var denominator = 2 * Dot(u, Cross(v, w));
                        if (denominator == 0)
                            return null;
                        offset = (Cross(v, w) * Dot(u, u) + Cross(w, u) * Dot(v, v)
                            + normal * Dot(w, w)) * (1 / denominator);
                    }
                }
            }
            var center = origin + offset;
            if (!Finite(center.X) || !Finite(center.Y) || !Finite(center.Z))
                return null;
            var sphere = new ClashZoneSphere { X = center.X, Y = center.Y, Z = center.Z };
            sphere.Radius = Math.Sqrt(points.Max(p => DistanceSquared(sphere, p)));
            return sphere;
        }

        private static bool Contains(ClashZoneSphere sphere, ClashZonePoint point)
        {
            if (sphere == null)
                return false;
            var distance = DistanceSquared(sphere, point);
            var radius = sphere.Radius * sphere.Radius;
            // Relative tolerance absorbs roundoff without imposing an absolute minimum radius.
            return distance <= radius + 1e-12 * Math.Max(distance, radius);
        }

        private static double DistanceSquared(ClashZoneSphere sphere, ClashZonePoint point)
        {
            var dx = point.X - sphere.X;
            var dy = point.Y - sphere.Y;
            var dz = point.Z - sphere.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        // 1e12 mm (a million kilometres) keeps every squared distance far from double overflow.
        private static bool InRange(double value) => Finite(value) && Math.Abs(value) <= 1e12;
        private static double Dot(Vector u, Vector v) => u.X * v.X + u.Y * v.Y + u.Z * v.Z;
        private static Vector Cross(Vector u, Vector v) => new Vector(
            u.Y * v.Z - u.Z * v.Y, u.Z * v.X - u.X * v.Z, u.X * v.Y - u.Y * v.X);

        private struct Vector
        {
            public readonly double X, Y, Z;
            public Vector(double x, double y, double z) { X = x; Y = y; Z = z; }
            public Vector(ClashZonePoint point) : this(point.X, point.Y, point.Z) { }
            public static Vector operator +(Vector u, Vector v) => new Vector(u.X + v.X, u.Y + v.Y, u.Z + v.Z);
            public static Vector operator -(Vector u, Vector v) => new Vector(u.X - v.X, u.Y - v.Y, u.Z - v.Z);
            public static Vector operator *(Vector u, double scale) => new Vector(u.X * scale, u.Y * scale, u.Z * scale);
        }
    }
}
