using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class SectionPlaneSpec
    {
        public int Index { get; set; }
        public bool? Enabled { get; set; }
        public string Direction { get; set; }
        public BoxVector3 Normal { get; set; }
        public BoxVector3 Point { get; set; }
        public string FromSelection { get; set; }
        public double? OffsetMm { get; set; }
        public double? MoveByMm { get; set; }
    }

    public sealed class SectionPlaneState
    {
        public int Index { get; set; }
        public bool Enabled { get; set; }
        public BoxVector3 Normal { get; set; }
        public double Distance { get; set; }
    }

    public sealed class SectionPlaneInfo
    {
        public int Index { get; set; }
        public bool Enabled { get; set; }
        public BoxVector3 Normal { get; set; }
        public double Distance { get; set; }
        public string Direction { get; set; }
    }

    public sealed class SectionPlanesRequest
    {
        public string Operation { get; set; }
        public List<SectionPlaneSpec> Planes { get; set; } = new List<SectionPlaneSpec>();
        public bool? Linked { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class SectionPlanesResponse
    {
        public string Operation { get; set; }
        public string Mode { get; set; }
        public bool Enabled { get; set; }
        public bool Linked { get; set; }
        public List<SectionPlaneInfo> Planes { get; set; } = new List<SectionPlaneInfo>();
        public bool Applied { get; set; }
        public bool? ReadbackMatches { get; set; }
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public static class SectionPlaneRules
    {
        public const int MaxPlanes = 6;
        private static readonly string[] Directions = { "top", "bottom", "north", "south", "east", "west" };

        public static bool TryDirectionNormal(string direction, out BoxVector3 normal)
        {
            normal = null;
            switch ((direction ?? "").ToLowerInvariant())
            {
                case "top": normal = Vector(0, 0, 1); break;
                case "bottom": normal = Vector(0, 0, -1); break;
                case "north": normal = Vector(0, 1, 0); break;
                case "south": normal = Vector(0, -1, 0); break;
                case "east": normal = Vector(1, 0, 0); break;
                case "west": normal = Vector(-1, 0, 0); break;
            }
            return normal != null;
        }

        public static string DirectionName(BoxVector3 normal)
        {
            if (normal == null) return null;
            foreach (var direction in Directions)
            {
                BoxVector3 axis;
                TryDirectionNormal(direction, out axis);
                if (Math.Abs(normal.X - axis.X) <= 1e-9 &&
                    Math.Abs(normal.Y - axis.Y) <= 1e-9 && Math.Abs(normal.Z - axis.Z) <= 1e-9)
                    return direction;
            }
            return null;
        }

        public static SectionPlaneState Resolve(SectionPlaneSpec spec, SectionPlaneState current,
            BoxVector3 selectionMin, BoxVector3 selectionMax, double docUnitsPerMm, out string error)
        {
            error = null;
            if (spec == null || spec.Index < 1 || spec.Index > MaxPlanes)
                return Fail("Specify index 1-6.", out error);
            var hasDirection = spec.Direction != null;
            if (hasDirection && spec.Normal != null)
                return Fail("Specify direction or normal, not both.", out error);
            BoxVector3 normal = spec.Normal;
            if (hasDirection && !TryDirectionNormal(spec.Direction, out normal))
                return Fail("direction must be top, bottom, north, south, east or west", out error);
            if (normal != null && normal.X == 0 && normal.Y == 0 && normal.Z == 0)
                return Fail("normal must not be zero", out error);
            if (spec.FromSelection != null && !string.Equals(spec.FromSelection, "max", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(spec.FromSelection, "min", StringComparison.OrdinalIgnoreCase))
                return Fail("fromSelection must be max or min", out error);

            double distance;
            if (normal != null)
            {
                if ((spec.Point != null) == (spec.FromSelection != null) || spec.MoveByMm.HasValue)
                    return Fail("Specify exactly one of point, fromSelection or moveByMm.", out error);
                if (spec.FromSelection != null && (selectionMin == null || selectionMax == null))
                    return Fail("fromSelection needs a non-empty selection", out error);
                if (!Finite(normal) || (spec.Point != null && !Finite(spec.Point)))
                    return Fail("normal and point must be finite", out error);
                // Scale first so large finite normals can be normalised without overflow.
                var scale = Math.Max(Math.Abs(normal.X), Math.Max(Math.Abs(normal.Y), Math.Abs(normal.Z)));
                normal = Vector(normal.X / scale, normal.Y / scale, normal.Z / scale);
                var length = Math.Sqrt(Dot(normal, normal));
                normal = Vector(normal.X / length, normal.Y / length, normal.Z / length);
                if (spec.Point != null)
                    distance = Dot(normal, spec.Point);
                else
                {
                    var maximum = string.Equals(spec.FromSelection, "max", StringComparison.OrdinalIgnoreCase);
                    // Choose each coordinate independently: the resulting corner maximises/minimises n.p.
                    distance = Dot(normal, Vector(
                        (normal.X >= 0) == maximum ? selectionMax.X : selectionMin.X,
                        (normal.Y >= 0) == maximum ? selectionMax.Y : selectionMin.Y,
                        (normal.Z >= 0) == maximum ? selectionMax.Z : selectionMin.Z));
                }
                distance += (spec.OffsetMm ?? 0) * docUnitsPerMm;
            }
            else
            {
                if (spec.Point != null || spec.FromSelection != null || spec.OffsetMm.HasValue ||
                    (!spec.MoveByMm.HasValue && !spec.Enabled.HasValue))
                    return Fail("Specify point, fromSelection or moveByMm.", out error);
                if (current == null)
                    return Fail("This update needs an existing plane.", out error);
                normal = current.Normal;
                distance = current.Distance + (spec.MoveByMm ?? 0) * docUnitsPerMm;
            }
            if (!Finite(docUnitsPerMm) || docUnitsPerMm <= 0 || !Finite(normal) || !Finite(distance))
                return Fail("Plane coordinates and offsets must be finite; document units per mm must be positive.", out error);
            return new SectionPlaneState
            {
                Index = spec.Index, Normal = normal, Distance = distance,
                Enabled = spec.Enabled ?? current?.Enabled ?? true,
            };
        }

        private static SectionPlaneState Fail(string message, out string error) { error = message; return null; }
        private static BoxVector3 Vector(double x, double y, double z) => new BoxVector3 { X = x, Y = y, Z = z };
        private static double Dot(BoxVector3 a, BoxVector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Finite(BoxVector3 value) => value != null && Finite(value.X) && Finite(value.Y) && Finite(value.Z);
    }
}
