using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    public static class MarkupRedlineJsonBuilder
    {
        public const string RectangleStyle = "rectangle";
        public const string TargetStyle = "target";
        public const string ArrowStyle = "arrow";
        public const string HatchStyle = "hatch";

        public static string Build(
            string style,
            IEnumerable<MarkupRedlineRect> rectangles,
            double red,
            double green,
            double blue,
            int thickness,
            double arrowOffsetX,
            double arrowOffsetY)
        {
            int arrowCount;
            return Build(style, rectangles, red, green, blue, thickness, arrowOffsetX, arrowOffsetY, null, out arrowCount);
        }

        public static string Build(
            string style,
            IEnumerable<MarkupRedlineRect> rectangles,
            double red,
            double green,
            double blue,
            int thickness,
            double arrowOffsetX,
            double arrowOffsetY,
            MarkupRedlineBuildOptions options)
        {
            int arrowCount;
            return Build(style, rectangles, red, green, blue, thickness, arrowOffsetX, arrowOffsetY, options, out arrowCount);
        }

        public static string Build(
            string style,
            IEnumerable<MarkupRedlineRect> rectangles,
            double red,
            double green,
            double blue,
            int thickness,
            double arrowOffsetX,
            double arrowOffsetY,
            MarkupRedlineBuildOptions options,
            out int arrowCount)
        {
            var normalizedStyle = NormalizeStyle(style);
            options = options ?? new MarkupRedlineBuildOptions();
            var values = new List<string>();
            arrowCount = 0;
            var rectangleList = (rectangles ?? Enumerable.Empty<MarkupRedlineRect>()).ToList();
            foreach (var rectangle in rectangleList)
            {
                ValidateRectangle(rectangle);
                if (string.Equals(normalizedStyle, TargetStyle, StringComparison.Ordinal))
                    AddTarget(values, rectangle, red, green, blue, thickness, options.TargetCrosshair);
                else if (string.Equals(normalizedStyle, ArrowStyle, StringComparison.Ordinal))
                {
                    // Compatibility mode: markStyle=arrow emits only callouts.
                }
                else if (string.Equals(normalizedStyle, HatchStyle, StringComparison.Ordinal))
                    AddHatch(values, rectangle, red, green, blue, thickness, options);
                else
                    AddRectangle(values, rectangle, red, green, blue, thickness);
            }

            if (string.Equals(normalizedStyle, ArrowStyle, StringComparison.Ordinal) || options.ArrowCallout)
            {
                foreach (var rectangle in rectangleList)
                    if (AddArrow(
                        values,
                        rectangle,
                        rectangleList,
                        string.Equals(normalizedStyle, TargetStyle, StringComparison.Ordinal),
                        red,
                        green,
                        blue,
                        thickness,
                        arrowOffsetX,
                        arrowOffsetY,
                        options))
                        arrowCount++;
            }

            return "{\"Type\":\"RedlineCollection\",\"Version\":1,\"Values\":[" + string.Join(",", values) + "]}";
        }

        public static string NormalizeStyle(string style)
        {
            var normalized = string.IsNullOrWhiteSpace(style) ? RectangleStyle : style.Trim().ToLowerInvariant();
            if (normalized != RectangleStyle && normalized != TargetStyle && normalized != ArrowStyle && normalized != HatchStyle)
                throw new ArgumentException("markStyle must be rectangle, target, arrow, or hatch.", nameof(style));
            return normalized;
        }

        public static double ResolveArrowLength(double heightField, double requestedDocumentUnits)
        {
            if (!IsFinite(heightField) || heightField <= 1e-9)
                throw new ArgumentOutOfRangeException(nameof(heightField), "heightField must be finite and greater than zero.");
            if (!IsFinite(requestedDocumentUnits) || requestedDocumentUnits < 0)
                throw new ArgumentOutOfRangeException(nameof(requestedDocumentUnits), "requestedDocumentUnits must be finite and non-negative.");
            var minimum = heightField * 0.05;
            var maximum = heightField * 0.15;
            var requested = requestedDocumentUnits <= 1e-9 ? heightField * 0.08 : requestedDocumentUnits;
            return Math.Max(minimum, Math.Min(maximum, requested));
        }

        private static void AddRectangle(ICollection<string> values, MarkupRedlineRect rectangle, double red, double green, double blue, int thickness)
        {
            AddLine(values, rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Top, red, green, blue, thickness);
            AddLine(values, rectangle.Right, rectangle.Top, rectangle.Right, rectangle.Bottom, red, green, blue, thickness);
            AddLine(values, rectangle.Right, rectangle.Bottom, rectangle.Left, rectangle.Bottom, red, green, blue, thickness);
            AddLine(values, rectangle.Left, rectangle.Bottom, rectangle.Left, rectangle.Top, red, green, blue, thickness);
        }

        private static void AddTarget(ICollection<string> values, MarkupRedlineRect rectangle, double red, double green, double blue, int thickness, bool targetCrosshair)
        {
            var culture = CultureInfo.InvariantCulture;
            values.Add(string.Format(
                culture,
                "{{\"Type\":\"RedlineEllipse\",\"Version\":1,\"Thickness\":{0},\"Color\":[{1:F6},{2:F6},{3:F6}],\"MinPoint\":[{4:G17},{5:G17}],\"MaxPoint\":[{6:G17},{7:G17}]}}",
                thickness,
                red,
                green,
                blue,
                Math.Min(rectangle.Left, rectangle.Right),
                Math.Min(rectangle.Top, rectangle.Bottom),
                Math.Max(rectangle.Left, rectangle.Right),
                Math.Max(rectangle.Top, rectangle.Bottom)));

            if (targetCrosshair)
            {
                var centerX = (rectangle.Left + rectangle.Right) / 2;
                var centerY = (rectangle.Top + rectangle.Bottom) / 2;
                var radiusX = Math.Abs(rectangle.Right - rectangle.Left) / 2;
                var radiusY = Math.Abs(rectangle.Top - rectangle.Bottom) / 2;
                AddLine(values, centerX - radiusX * 1.7, centerY, centerX + radiusX * 1.7, centerY, red, green, blue, thickness);
                AddLine(values, centerX, centerY - radiusY * 1.7, centerX, centerY + radiusY * 1.7, red, green, blue, thickness);
            }
        }

        private static bool AddArrow(
            ICollection<string> values,
            MarkupRedlineRect rectangle,
            IList<MarkupRedlineRect> allRectangles,
            bool useEllipseBoundary,
            double red,
            double green,
            double blue,
            int thickness,
            double arrowOffsetX,
            double arrowOffsetY,
            MarkupRedlineBuildOptions options)
        {
            var arrowLength = Math.Abs(options.ArrowLength);
            if (arrowLength < 1e-9)
            {
                arrowLength = Math.Max(Math.Abs(arrowOffsetX), Math.Abs(arrowOffsetY));
                if (arrowLength < 1e-9)
                    arrowLength = Math.Max(Math.Max(Math.Abs(rectangle.Right - rectangle.Left), Math.Abs(rectangle.Top - rectangle.Bottom)), 1e-3);
            }

            var centerX = (rectangle.Left + rectangle.Right) / 2.0;
            var centerY = (rectangle.Top + rectangle.Bottom) / 2.0;
            var left = Math.Min(rectangle.Left, rectangle.Right);
            var right = Math.Max(rectangle.Left, rectangle.Right);
            var bottom = Math.Min(rectangle.Bottom, rectangle.Top);
            var top = Math.Max(rectangle.Bottom, rectangle.Top);
            var radiusX = Math.Max((right - left) / 2.0, 1e-9);
            var radiusY = Math.Max((top - bottom) / 2.0, 1e-9);
            var halfWidth = options.CameraHalfWidth.GetValueOrDefault();
            var halfHeight = options.CameraHalfHeight.GetValueOrDefault();
            var hasCameraBounds = halfWidth > 1e-9 && halfHeight > 1e-9;
            var frameLimitX = halfWidth * 0.97;
            var frameLimitY = halfHeight * 0.97;
            var otherRectangles = (allRectangles ?? new List<MarkupRedlineRect>()).Where(item => !ReferenceEquals(item, rectangle)).ToList();
            var centroidX = allRectangles == null || allRectangles.Count == 0 ? centerX : allRectangles.Average(item => (item.Left + item.Right) / 2.0);
            var centroidY = allRectangles == null || allRectangles.Count == 0 ? centerY : allRectangles.Average(item => (item.Top + item.Bottom) / 2.0);
            ArrowCandidate best = null;
            ArrowCandidate bestShortened = null;
            for (var directionIndex = 0; directionIndex < 16; directionIndex++)
            {
                var angle = directionIndex * Math.PI / 8.0;
                var ux = Math.Cos(angle);
                var uy = Math.Sin(angle);
                var boundaryScale = useEllipseBoundary
                    ? 1.0 / Math.Sqrt((ux * ux) / (radiusX * radiusX) + (uy * uy) / (radiusY * radiusY))
                    : Math.Min(radiusX / Math.Max(Math.Abs(ux), 1e-12), radiusY / Math.Max(Math.Abs(uy), 1e-12));
                var tipX = centerX + boundaryScale * ux;
                var tipY = centerY + boundaryScale * uy;
                var tailX = tipX + arrowLength * ux;
                var tailY = tipY + arrowLength * uy;

                var clearance = otherRectangles.Count == 0
                    ? 0
                    : otherRectangles.Min(item => DistanceSegmentToRectangle(tipX, tipY, tailX, tailY, item));
                var awayX = centerX - centroidX;
                var awayY = centerY - centroidY;
                var awayLength = Math.Sqrt(awayX * awayX + awayY * awayY);
                var awayScore = awayLength <= 1e-12 ? 0 : (ux * awayX + uy * awayY) / awayLength;
                var candidate = new ArrowCandidate(tailX, tailY, tipX, tipY, clearance, awayScore, directionIndex);
                if (hasCameraBounds && (tailX < -frameLimitX || tailX > frameLimitX || tailY < -frameLimitY || tailY > frameLimitY))
                {
                    var shortened = candidate.ClampTail(-frameLimitX, frameLimitX, -frameLimitY, frameLimitY);
                    if (bestShortened == null || shortened.Length > bestShortened.Length + 1e-9 ||
                        (Math.Abs(shortened.Length - bestShortened.Length) <= 1e-9 && shortened.IsBetterThan(bestShortened)))
                        bestShortened = shortened;
                    continue;
                }
                if (best == null || candidate.IsBetterThan(best))
                    best = candidate;
            }

            if (best == null && bestShortened != null && bestShortened.Length >= arrowLength * 0.35)
                best = bestShortened;
            if (best == null)
                return false;
            AddArrowLines(values, best.TailX, best.TailY, best.TipX, best.TipY, red, green, blue, thickness);
            return true;
        }

        private static double DistanceSegmentToRectangle(double startX, double startY, double endX, double endY, MarkupRedlineRect rectangle)
        {
            var left = Math.Min(rectangle.Left, rectangle.Right);
            var right = Math.Max(rectangle.Left, rectangle.Right);
            var bottom = Math.Min(rectangle.Bottom, rectangle.Top);
            var top = Math.Max(rectangle.Bottom, rectangle.Top);
            double lower;
            double upper;
            if (TryClipLine(left, right, bottom, top, startX, startY, endX - startX, endY - startY, out lower, out upper) && upper >= 0 && lower <= 1)
                return 0;

            var distance = Math.Min(PointToRectangleDistance(startX, startY, left, right, bottom, top), PointToRectangleDistance(endX, endY, left, right, bottom, top));
            distance = Math.Min(distance, PointToSegmentDistance(left, bottom, startX, startY, endX, endY));
            distance = Math.Min(distance, PointToSegmentDistance(left, top, startX, startY, endX, endY));
            distance = Math.Min(distance, PointToSegmentDistance(right, bottom, startX, startY, endX, endY));
            return Math.Min(distance, PointToSegmentDistance(right, top, startX, startY, endX, endY));
        }

        private static double PointToRectangleDistance(double x, double y, double left, double right, double bottom, double top)
        {
            var dx = Math.Max(Math.Max(left - x, 0), x - right);
            var dy = Math.Max(Math.Max(bottom - y, 0), y - top);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double PointToSegmentDistance(double x, double y, double startX, double startY, double endX, double endY)
        {
            var dx = endX - startX;
            var dy = endY - startY;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 1e-18)
                return Math.Sqrt((x - startX) * (x - startX) + (y - startY) * (y - startY));
            var t = Math.Max(0, Math.Min(1, ((x - startX) * dx + (y - startY) * dy) / lengthSquared));
            var projectedX = startX + t * dx;
            var projectedY = startY + t * dy;
            return Math.Sqrt((x - projectedX) * (x - projectedX) + (y - projectedY) * (y - projectedY));
        }

        private static void AddArrowLines(ICollection<string> values, double tailX, double tailY, double tipX, double tipY, double red, double green, double blue, int thickness)
        {
            foreach (var line in ArrowLineGeometryHelper.Build(tailX, tailY, tipX, tipY))
                AddLine(values, line.StartX, line.StartY, line.EndX, line.EndY, red, green, blue, thickness);
        }

        private static void AddHatch(ICollection<string> values, MarkupRedlineRect rectangle, double red, double green, double blue, int thickness, MarkupRedlineBuildOptions options)
        {
            AddRectangle(values, rectangle, red, green, blue, thickness);
            var angleRadians = (options.HatchAngleDeg ?? 45.0) * Math.PI / 180.0;
            var dx = Math.Cos(angleRadians);
            var dy = Math.Sin(angleRadians);
            var nx = -dy;
            var ny = dx;
            var left = Math.Min(rectangle.Left, rectangle.Right);
            var right = Math.Max(rectangle.Left, rectangle.Right);
            var bottom = Math.Min(rectangle.Bottom, rectangle.Top);
            var top = Math.Max(rectangle.Bottom, rectangle.Top);
            var projections = new[]
            {
                nx * left + ny * bottom, nx * left + ny * top,
                nx * right + ny * bottom, nx * right + ny * top,
            };
            var projectionSpan = projections.Max() - projections.Min();
            var spacing = options.HatchSpacing <= 1e-9
                ? Math.Max(projectionSpan / 40.0, 1e-3)
                : options.HatchSpacing;
            var maxHatchLines = options.MaxHatchLines.GetValueOrDefault(2000);
            if (maxHatchLines < 1)
                maxHatchLines = 1;
            if (projectionSpan > 0 && projectionSpan / spacing > maxHatchLines)
                spacing = projectionSpan / maxHatchLines;
            var start = Math.Floor(projections.Min() / spacing) * spacing;
            var end = projections.Max();
            var hatchThickness = options.HatchThickness.GetValueOrDefault(thickness);
            for (var t = start; t <= end + spacing * 0.25; t += spacing)
            {
                double s0;
                double s1;
                if (!TryClipLine(left, right, bottom, top, nx * t, ny * t, dx, dy, out s0, out s1))
                    continue;
                AddLine(values, nx * t + dx * s0, ny * t + dy * s0, nx * t + dx * s1, ny * t + dy * s1, red, green, blue, hatchThickness);
            }
        }

        private static bool TryClipLine(double left, double right, double bottom, double top, double x, double y, double dx, double dy, out double lower, out double upper)
        {
            lower = double.NegativeInfinity;
            upper = double.PositiveInfinity;
            return ClipAxis(x, dx, left, right, ref lower, ref upper) && ClipAxis(y, dy, bottom, top, ref lower, ref upper) && lower <= upper;
        }

        private static bool ClipAxis(double origin, double direction, double minimum, double maximum, ref double lower, ref double upper)
        {
            if (Math.Abs(direction) < 1e-12)
                return origin >= minimum && origin <= maximum;
            var first = (minimum - origin) / direction;
            var second = (maximum - origin) / direction;
            if (first > second)
            {
                var temporary = first;
                first = second;
                second = temporary;
            }
            lower = Math.Max(lower, first);
            upper = Math.Min(upper, second);
            return lower <= upper;
        }

        private static void AddLine(
            ICollection<string> values,
            double startX,
            double startY,
            double endX,
            double endY,
            double red,
            double green,
            double blue,
            int thickness)
        {
            values.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{{\"Type\":\"RedlineLine\",\"Version\":1,\"Thickness\":{0},\"Color\":[{1:F6},{2:F6},{3:F6}],\"Start\":[{4:G17},{5:G17}],\"End\":[{6:G17},{7:G17}]}}",
                thickness,
                ToLineColor(red),
                ToLineColor(green),
                ToLineColor(blue),
                startX,
                startY,
                endX,
                endY));
        }

        private static double ToLineColor(double value)
        {
            return Math.Max(0, Math.Min(1, value));
        }

        private static void ValidateRectangle(MarkupRedlineRect rectangle)
        {
            if (rectangle == null)
                throw new ArgumentException("Markup rectangles cannot contain null values.", nameof(rectangle));
            if (!IsFinite(rectangle.Left) || !IsFinite(rectangle.Top) || !IsFinite(rectangle.Right) || !IsFinite(rectangle.Bottom))
                throw new ArgumentException("Markup rectangle coordinates must be finite.", nameof(rectangle));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private sealed class ArrowCandidate
        {
            public ArrowCandidate(double tailX, double tailY, double tipX, double tipY, double clearance, double awayScore, int directionIndex)
            {
                TailX = tailX;
                TailY = tailY;
                TipX = tipX;
                TipY = tipY;
                Clearance = clearance;
                AwayScore = awayScore;
                DirectionIndex = directionIndex;
            }

            public double TailX { get; private set; }
            public double TailY { get; private set; }
            public double TipX { get; private set; }
            public double TipY { get; private set; }
            public double Clearance { get; private set; }
            public double AwayScore { get; private set; }
            public int DirectionIndex { get; private set; }
            public double Length
            {
                get
                {
                    var dx = TailX - TipX;
                    var dy = TailY - TipY;
                    return Math.Sqrt(dx * dx + dy * dy);
                }
            }

            public bool IsBetterThan(ArrowCandidate other)
            {
                if (Clearance > other.Clearance + 1e-9) return true;
                if (Clearance < other.Clearance - 1e-9) return false;
                if (AwayScore > other.AwayScore + 1e-9) return true;
                if (AwayScore < other.AwayScore - 1e-9) return false;
                return DirectionIndex < other.DirectionIndex;
            }

            public ArrowCandidate ClampTail(double minimumX, double maximumX, double minimumY, double maximumY)
            {
                return new ArrowCandidate(
                    Math.Max(minimumX, Math.Min(maximumX, TailX)),
                    Math.Max(minimumY, Math.Min(maximumY, TailY)),
                    TipX,
                    TipY,
                    Clearance,
                    AwayScore,
                    DirectionIndex);
            }
        }
    }

    public sealed class MarkupRedlineRect
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Right { get; set; }
        public double Bottom { get; set; }
    }

    public sealed class MarkupRedlineBuildOptions
    {
        public double? HatchAngleDeg { get; set; }
        public double HatchSpacing { get; set; }
        public int? HatchThickness { get; set; }
        public int? MaxHatchLines { get; set; }
        public double? CameraHalfWidth { get; set; }
        public double? CameraHalfHeight { get; set; }
        public bool ArrowCallout { get; set; }
        public double ArrowLength { get; set; }
        public bool TargetCrosshair { get; set; }
    }
}
