using System;
using System.Collections.Generic;
using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    public sealed class SectionBoxRequest
    {
        public string Operation { get; set; }
        public SectionBoxGeometry Box { get; set; }
        public BoxVector3 Min { get; set; }
        public BoxVector3 Max { get; set; }
        public BoxVector3 OffsetMm { get; set; }
        public SectionBoxFaceOffsets GrowMm { get; set; }
        public double? RotateDegrees { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class SectionBoxResponse
    {
        public string Operation { get; set; }
        public bool Apply { get; set; }
        public bool Applied { get; set; }
        public bool Enabled { get; set; }
        public SectionBoxGeometry Box { get; set; }
        public SectionBoxGeometry Readback { get; set; }
        public bool? ReadbackMatches { get; set; }
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public static class SectionBoxEditRules
    {
        internal static bool HasFaces(SectionBoxFaceOffsets faces) => faces != null &&
            (faces.East.HasValue || faces.West.HasValue || faces.North.HasValue ||
             faces.South.HasValue || faces.Top.HasValue || faces.Bottom.HasValue);

        public static SectionBoxGeometry Move(SectionBoxGeometry box, double dxMm, double dyMm, double dzMm, double mmToUnits)
        {
            ValidateFactor(mmToUnits);
            var result = Copy(box);
            result.Center.X += dxMm * mmToUnits;
            result.Center.Y += dyMm * mmToUnits;
            result.Center.Z += dzMm * mmToUnits;
            SectionBoxGeometryRules.Validate(result);
            return result;
        }

        public static SectionBoxGeometry Grow(SectionBoxGeometry box, SectionBoxFaceOffsets faces, double mmToUnits)
        {
            ValidateFactor(mmToUnits);
            if (!HasFaces(faces)) throw new ArgumentException("grow needs growMm with at least one face.");
            var result = Copy(box);
            var positive = new[] { faces.East, faces.North, faces.Top };
            var negative = new[] { faces.West, faces.South, faces.Bottom };
            var extents = new[] { box.HalfExtents.X, box.HalfExtents.Y, box.HalfExtents.Z };
            for (var i = 0; i < 3; i++)
            {
                var plus = positive[i].GetValueOrDefault();
                var minus = negative[i].GetValueOrDefault();
                if (!Finite(plus) || !Finite(minus)) throw new ArgumentException("growMm faces must be finite.");
                plus *= mmToUnits * 0.5;
                minus *= mmToUnits * 0.5;
                extents[i] += plus + minus;
                if (extents[i] <= 0)
                    throw new ArgumentException("grow would leave the box with no volume along its " + "xyz"[i] + " axis.");
                var axis = box.Axes[i];
                result.Center.X += axis.X * (plus - minus);
                result.Center.Y += axis.Y * (plus - minus);
                result.Center.Z += axis.Z * (plus - minus);
            }
            result.HalfExtents = new BoxVector3 { X = extents[0], Y = extents[1], Z = extents[2] };
            SectionBoxGeometryRules.Validate(result);
            return result;
        }

        public static SectionBoxGeometry RotateZ(SectionBoxGeometry box, double degrees)
        {
            if (!Finite(degrees)) throw new ArgumentException("rotate needs a finite rotateDegrees.");
            var result = Copy(box);
            var radians = (degrees % 360) * Math.PI / 180;
            var cosine = Math.Cos(radians);
            var sine = Math.Sin(radians);
            foreach (var axis in result.Axes)
            {
                var x = axis.X;
                axis.X = cosine * x - sine * axis.Y;
                axis.Y = sine * x + cosine * axis.Y;
            }
            SectionBoxGeometryRules.Validate(result);
            return result;
        }

        private static SectionBoxGeometry Copy(SectionBoxGeometry box)
        {
            SectionBoxGeometryRules.Validate(box);
            return new SectionBoxGeometry
            {
                FormatVersion = box.FormatVersion, CoordinateSpace = box.CoordinateSpace, DocumentUnits = box.DocumentUnits,
                Center = CopyVector(box.Center), HalfExtents = CopyVector(box.HalfExtents),
                Axes = box.Axes.ConvertAll(CopyVector),
            };
        }

        private static BoxVector3 CopyVector(BoxVector3 v) => new BoxVector3 { X = v.X, Y = v.Y, Z = v.Z };
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static void ValidateFactor(double value)
        {
            if (!Finite(value) || value <= 0) throw new ArgumentException("mmToUnits must be finite and positive.");
        }
    }

    public static class SectionBoxWriteRules
    {
        public static string Describe(SectionBoxRequest request)
        {
            var set = string.Equals(request?.Operation, "set", StringComparison.OrdinalIgnoreCase);
            var clear = string.Equals(request?.Operation, "clear", StringComparison.OrdinalIgnoreCase);
            var move = string.Equals(request?.Operation, "move", StringComparison.OrdinalIgnoreCase);
            var grow = string.Equals(request?.Operation, "grow", StringComparison.OrdinalIgnoreCase);
            var rotate = string.Equals(request?.Operation, "rotate", StringComparison.OrdinalIgnoreCase);
            if (!set && !clear && !move && !grow && !rotate) return "operation must be set, clear, move, grow or rotate.";
            if (move || grow || rotate)
            {
                if (request.Box != null || request.Min != null || request.Max != null)
                    return "move, grow and rotate take no box, min or max.";
                if (move && request.OffsetMm == null) return "move needs offsetMm.";
                if (grow && !SectionBoxEditRules.HasFaces(request.GrowMm)) return "grow needs growMm with at least one face.";
                if (rotate && (!request.RotateDegrees.HasValue || double.IsNaN(request.RotateDegrees.Value) || double.IsInfinity(request.RotateDegrees.Value)))
                    return "rotate needs a finite rotateDegrees.";
                return null;
            }
            if (clear)
                return request.Box != null || request.Min != null || request.Max != null
                    ? "clear takes no box, min or max." : null;
            if (request.Box != null ? request.Min != null || request.Max != null : request.Min == null || request.Max == null)
                return "set needs either box or both min and max.";
            return null;
        }

        public static SectionBoxGeometry FromMinMax(BoxVector3 min, BoxVector3 max, string units)
        {
            // Validate each endpoint independently before checking strict ordering.
            SectionBoxGeometryRules.ValidateBounds(min, min);
            SectionBoxGeometryRules.ValidateBounds(max, max);
            if (max.X <= min.X || max.Y <= min.Y || max.Z <= min.Z)
                throw new ArgumentException("max must be greater than min on every axis.");
            var box = new SectionBoxGeometry
            {
                DocumentUnits = units,
                Center = Vector(min.X * 0.5 + max.X * 0.5, min.Y * 0.5 + max.Y * 0.5, min.Z * 0.5 + max.Z * 0.5),
                HalfExtents = Vector(max.X * 0.5 - min.X * 0.5, max.Y * 0.5 - min.Y * 0.5, max.Z * 0.5 - min.Z * 0.5),
                Axes = SectionBoxGeometryRules.AxesFromQuaternion(0, 0, 0, 1),
            };
            SectionBoxGeometryRules.Validate(box);
            return box;
        }

        public static double[] QuaternionFromAxes(IList<BoxVector3> axes)
        {
            SectionBoxGeometryRules.Validate(new SectionBoxGeometry
            {
                DocumentUnits = "meters", Center = Vector(0, 0, 0), HalfExtents = Vector(1, 1, 1),
                Axes = axes == null ? null : new List<BoxVector3>(axes),
            });
            var a = axes[0]; var b = axes[1]; var c = axes[2];
            // Shepperd: select the largest squared component to avoid division by w near 180 degrees.
            // Each row is a rotated basis vector, the transpose of the usual column rotation matrix.
            var candidates = new[] { 1 + a.X - b.Y - c.Z, 1 - a.X + b.Y - c.Z,
                1 - a.X - b.Y + c.Z, 1 + a.X + b.Y + c.Z };
            var largest = 0;
            for (var i = 1; i < 4; i++)
                if (candidates[i] > candidates[largest]) largest = i;
            var s = 2 * Math.Sqrt(candidates[largest]);
            double[] q;
            switch (largest)
            {
                case 0: q = new[] { s / 4, (a.Y + b.X) / s, (a.Z + c.X) / s, (b.Z - c.Y) / s }; break;
                case 1: q = new[] { (a.Y + b.X) / s, s / 4, (b.Z + c.Y) / s, (c.X - a.Z) / s }; break;
                case 2: q = new[] { (a.Z + c.X) / s, (b.Z + c.Y) / s, s / 4, (a.Y - b.X) / s }; break;
                default: q = new[] { (b.Z - c.Y) / s, (c.X - a.Z) / s, (a.Y - b.X) / s, s / 4 }; break;
            }
            var length = Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
            for (var i = 0; i < 4; i++) q[i] /= length;
            return q;
        }

        public static string BuildClippingJson(SectionBoxGeometry box, double ex, double ey, double ez)
        {
            SectionBoxGeometryRules.Validate(box);
            var min = Vector(box.Center.X - box.HalfExtents.X, box.Center.Y - box.HalfExtents.Y, box.Center.Z - box.HalfExtents.Z);
            var max = Vector(box.Center.X + box.HalfExtents.X, box.Center.Y + box.HalfExtents.Y, box.Center.Z + box.HalfExtents.Z);
            SectionBoxGeometryRules.ValidateBounds(min, max);
            if (min.X == max.X || min.Y == max.Y || min.Z == max.Z)
                throw new ArgumentException("The box extents cannot be represented at this center.");
            return "{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"OrientedBox\":{\"Type\":\"OrientedBox3D\",\"Version\":1,\"Box\":[" +
                JsonVector(min) + "," + JsonVector(max) + "],\"Rotation\":" + JsonVector(Vector(ex, ey, ez)) + "},\"Enabled\":true}";
        }

        private static BoxVector3 Vector(double x, double y, double z) => new BoxVector3 { X = x, Y = y, Z = z };
        private static string JsonVector(BoxVector3 v) => "[" + Number(v.X) + "," + Number(v.Y) + "," + Number(v.Z) + "]";
        private static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("Clipping coordinates must be finite.");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
