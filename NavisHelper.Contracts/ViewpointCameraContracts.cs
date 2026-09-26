using System;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ViewpointSetCameraRequest
    {
        public Point3Info Position { get; set; }
        public Point3Info Target { get; set; }
        public Point3Info Direction { get; set; }
        public Point3Info Up { get; set; }
        public string Projection { get; set; }
        public double? HeightField { get; set; }
        public ViewpointCameraZoomTo ZoomTo { get; set; }
        public string SaveName { get; set; }
        public string SaveFolderPath { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class ViewpointCameraZoomTo
    {
        public Point3Info Point { get; set; }
        public double? PointHalfSize { get; set; }
        public BoundingBoxInfo BoundingBox { get; set; }
    }

    public sealed class ViewpointSetCameraResponse
    {
        public bool Apply { get; set; }
        public bool Applied { get; set; }
        public Point3Info Position { get; set; }
        public Point3Info Target { get; set; }
        public Point3Info Direction { get; set; }
        public Point3Info Up { get; set; }
        public double FocalDistance { get; set; }
        public string Projection { get; set; }
        public double? HeightField { get; set; }
        public BoundingBoxInfo ZoomBox { get; set; }
        public string SaveName { get; set; }
        public string SaveFolderPath { get; set; }
        public bool SaveRequested { get; set; }
        public bool SaveNameConflict { get; set; }
        public bool Saved { get; set; }
        public int? CreatedFolderCount { get; set; }
    }

    public sealed class ViewpointCameraPlan
    {
        public Point3Info Position { get; set; }
        public Point3Info Target { get; set; }
        public Point3Info Direction { get; set; }
        public Point3Info Up { get; set; }
        public double FocalDistance { get; set; }
        public string Projection { get; set; }
        public double? HeightField { get; set; }
        public BoundingBoxInfo ZoomBox { get; set; }
        public string SaveName { get; set; }
        public string SaveFolderPath { get; set; }
        public bool SaveRequested { get; set; }
    }

    public static class ViewpointCameraPlanHelper
    {
        public const string Perspective = "perspective";
        public const string Orthographic = "orthographic";
        private const double MinimumVectorLength = 1e-12;
        private const double ParallelSinEpsilon = 1e-6;
        private const double CoordinateMatchEpsilon = 1e-9;

        public static ViewpointCameraPlan Build(ViewpointSetCameraRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!IsFinitePoint(request.Position))
                throw new ArgumentException("position must contain three finite document-coordinate values.", nameof(request));
            if (!IsFinitePoint(request.Up))
                throw new ArgumentException("up must contain three finite vector components.", nameof(request));

            var hasTarget = request.Target != null;
            var hasDirection = request.Direction != null;
            if (hasTarget == hasDirection)
                throw new ArgumentException("Exactly one of target or direction must be supplied.", nameof(request));
            if (hasTarget && !IsFinitePoint(request.Target))
                throw new ArgumentException("target must contain three finite document-coordinate values.", nameof(request));
            if (hasDirection && !IsFinitePoint(request.Direction))
                throw new ArgumentException("direction must contain three finite vector components.", nameof(request));

            var projection = NormalizeProjection(request.Projection);
            if (projection == null)
                throw new ArgumentException("projection must be perspective, orthographic, or ortho.", nameof(request));

            var direction = hasTarget
                ? Subtract(request.Target, request.Position)
                : CopyPoint(request.Direction);
            if (!IsFinitePoint(direction))
                throw new ArgumentException("The derived camera direction is outside the finite numeric range.", nameof(request));
            double directionLength;
            double upLength;
            if (!TryGetFiniteLength(direction, out directionLength) || directionLength <= MinimumVectorLength)
                throw new ArgumentException("Camera target and position must differ, and direction must be non-zero.", nameof(request));
            if (!TryGetFiniteLength(request.Up, out upLength) || upLength <= MinimumVectorLength)
                throw new ArgumentException("up must be non-zero.", nameof(request));
            var normalizedDirection = Scale(direction, 1 / directionLength);
            var normalizedUp = Scale(request.Up, 1 / upLength);
            double crossLength;
            if (!TryGetFiniteLength(Cross(normalizedDirection, normalizedUp), out crossLength) || crossLength <= ParallelSinEpsilon)
                throw new ArgumentException("up must not be parallel to the camera direction.", nameof(request));

            if (request.HeightField.HasValue)
            {
                var value = request.HeightField.Value;
                if (!IsFinite(value) || value <= 0)
                    throw new ArgumentException("heightField must be finite and positive.", nameof(request));
                if (projection == Perspective && value >= Math.PI)
                    throw new ArgumentException("Perspective heightField is a vertical angle in radians and must be less than pi.", nameof(request));
            }

            var target = hasTarget ? CopyPoint(request.Target) : Add(request.Position, request.Direction);
            if (!IsFinitePoint(target))
                throw new ArgumentException("position plus direction must remain inside the finite numeric range.", nameof(request));

            var zoomBox = BuildZoomBox(request.ZoomTo, projection, target);
            var saveName = (request.SaveName ?? string.Empty).Trim();
            var saveFolderPath = (request.SaveFolderPath ?? string.Empty).Trim();
            if (saveName.Length == 0 && saveFolderPath.Length > 0)
                throw new ArgumentException("saveFolderPath requires saveName.", nameof(request));

            return new ViewpointCameraPlan
            {
                Position = CopyPoint(request.Position),
                Target = target,
                Direction = direction,
                Up = CopyPoint(request.Up),
                FocalDistance = directionLength,
                Projection = projection,
                HeightField = request.HeightField,
                ZoomBox = zoomBox,
                SaveName = saveName,
                SaveFolderPath = saveFolderPath,
                SaveRequested = saveName.Length > 0,
            };
        }

        public static string NormalizeProjection(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == Perspective)
                return Perspective;
            if (normalized == Orthographic || normalized == "ortho")
                return Orthographic;
            return null;
        }

        public static bool IsFinitePoint(Point3Info point)
        {
            return point != null && IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);
        }

        private static BoundingBoxInfo BuildZoomBox(ViewpointCameraZoomTo zoomTo, string projection, Point3Info target)
        {
            if (zoomTo == null)
                return null;
            if (projection != Orthographic)
                throw new ArgumentException("zoomTo is supported only for orthographic projection; use heightField for an exact-position perspective camera.", nameof(zoomTo));

            var hasPoint = zoomTo.Point != null;
            var hasBox = zoomTo.BoundingBox != null;
            if (hasPoint == hasBox)
                throw new ArgumentException("zoomTo must contain exactly one of point or boundingBox.", nameof(zoomTo));

            if (hasPoint)
            {
                if (!IsFinitePoint(zoomTo.Point))
                    throw new ArgumentException("zoomTo.point must contain finite document-coordinate values.", nameof(zoomTo));
                if (!zoomTo.PointHalfSize.HasValue || !IsFinite(zoomTo.PointHalfSize.Value) || zoomTo.PointHalfSize.Value <= 0)
                    throw new ArgumentException("zoomTo.pointHalfSize must be finite and positive for point zoom.", nameof(zoomTo));
                if (!PointsNearlyEqual(zoomTo.Point, target))
                    throw new ArgumentException("zoomTo.point must equal the camera target so exact orientation and framing do not conflict.", nameof(zoomTo));
                var half = zoomTo.PointHalfSize.Value;
                return CreateBox(
                    new Point3Info { X = zoomTo.Point.X - half, Y = zoomTo.Point.Y - half, Z = zoomTo.Point.Z - half },
                    new Point3Info { X = zoomTo.Point.X + half, Y = zoomTo.Point.Y + half, Z = zoomTo.Point.Z + half });
            }

            if (zoomTo.PointHalfSize.HasValue)
                throw new ArgumentException("zoomTo.pointHalfSize is valid only with zoomTo.point.", nameof(zoomTo));
            var min = zoomTo.BoundingBox.Min;
            var max = zoomTo.BoundingBox.Max;
            if (!IsFinitePoint(min) || !IsFinitePoint(max))
                throw new ArgumentException("zoomTo.boundingBox min and max must contain finite document-coordinate values.", nameof(zoomTo));
            if (min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z)
                throw new ArgumentException("zoomTo.boundingBox min must be strictly less than max on every axis.", nameof(zoomTo));
            var box = CreateBox(min, max);
            if (!PointsNearlyEqual(box.Center, target))
                throw new ArgumentException("zoomTo.boundingBox center must equal the camera target so exact orientation and framing do not conflict.", nameof(zoomTo));
            return box;
        }

        private static BoundingBoxInfo CreateBox(Point3Info min, Point3Info max)
        {
            if (!IsFinitePoint(min) || !IsFinitePoint(max))
                throw new ArgumentException("zoomTo coordinates and extents must remain inside the finite numeric range.");
            var size = Subtract(max, min);
            if (!IsFinitePoint(size))
                throw new ArgumentException("zoomTo bounding-box extents must remain inside the finite numeric range.");
            return new BoundingBoxInfo
            {
                Min = CopyPoint(min),
                Max = CopyPoint(max),
                Center = new Point3Info
                {
                    X = min.X + size.X / 2,
                    Y = min.Y + size.Y / 2,
                    Z = min.Z + size.Z / 2,
                },
                Size = size,
            };
        }

        private static Point3Info CopyPoint(Point3Info value)
        {
            return value == null ? null : new Point3Info { X = value.X, Y = value.Y, Z = value.Z };
        }

        private static Point3Info Add(Point3Info left, Point3Info right)
        {
            return new Point3Info { X = left.X + right.X, Y = left.Y + right.Y, Z = left.Z + right.Z };
        }

        private static Point3Info Subtract(Point3Info left, Point3Info right)
        {
            return new Point3Info { X = left.X - right.X, Y = left.Y - right.Y, Z = left.Z - right.Z };
        }

        private static Point3Info Cross(Point3Info left, Point3Info right)
        {
            return new Point3Info
            {
                X = left.Y * right.Z - left.Z * right.Y,
                Y = left.Z * right.X - left.X * right.Z,
                Z = left.X * right.Y - left.Y * right.X,
            };
        }

        private static Point3Info Scale(Point3Info value, double factor)
        {
            return new Point3Info { X = value.X * factor, Y = value.Y * factor, Z = value.Z * factor };
        }

        private static bool PointsNearlyEqual(Point3Info left, Point3Info right)
        {
            var scale = Math.Max(
                1,
                Math.Max(
                    Math.Max(Math.Abs(left.X), Math.Abs(right.X)),
                    Math.Max(
                        Math.Max(Math.Abs(left.Y), Math.Abs(right.Y)),
                        Math.Max(Math.Abs(left.Z), Math.Abs(right.Z)))));
            var tolerance = CoordinateMatchEpsilon * scale;
            return Math.Abs(left.X - right.X) <= tolerance &&
                   Math.Abs(left.Y - right.Y) <= tolerance &&
                   Math.Abs(left.Z - right.Z) <= tolerance;
        }

        private static bool TryGetFiniteLength(Point3Info value, out double length)
        {
            var scale = Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
            if (!IsFinite(scale) || scale == 0)
            {
                length = scale;
                return scale == 0;
            }
            var x = value.X / scale;
            var y = value.Y / scale;
            var z = value.Z / scale;
            length = scale * Math.Sqrt(x * x + y * y + z * z);
            return IsFinite(length);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
