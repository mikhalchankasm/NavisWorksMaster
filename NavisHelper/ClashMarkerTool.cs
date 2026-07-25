using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Agent.Contracts;
using Application = Autodesk.Navisworks.Api.Application;

namespace NavisHelper
{
    /// <summary>
    /// Runtime-only overlay markers that stay attached to 3D points or boxes while the camera moves.
    /// Overlay state is intentionally not persisted in saved viewpoints or Navisworks files.
    /// </summary>
    [Plugin("NavisHelper.ClashMarker", "CBC")]
    public class ClashMarkerTool : ToolPlugin
    {
        public const string CenterDotStyle = "centerDot";
        private static readonly object Sync = new object();
        private static List<ClashMarkerVisual> _markers = new List<ClashMarkerVisual>();
        private static Point3D _markerPoint;
        private static Tool? _previousTool;

        public static Point3D MarkerPoint
        {
            get { return _markerPoint; }
            set
            {
                lock (Sync)
                {
                    _markerPoint = value;
                    MarkerStyle = MarkupRedlineJsonBuilder.TargetStyle;
                    _markers = value == null
                        ? new List<ClashMarkerVisual>()
                        : new List<ClashMarkerVisual> { new ClashMarkerVisual(value, null) };
                }
            }
        }

        public static double MarkerRadius { get; set; } = 10;
        public static string MarkerStyle { get; private set; } = MarkupRedlineJsonBuilder.TargetStyle;
        public static bool IsActive { get; private set; }
        public static string LastError { get; private set; }
        public static int MarkerCount
        {
            get
            {
                lock (Sync)
                    return _markers.Count;
            }
        }

        public static void Show(Point3D point, double screenRadius = 10)
        {
            ShowMany(new[] { new ClashMarkerVisual(point, null) }, MarkupRedlineJsonBuilder.TargetStyle, screenRadius);
        }

        public static void ShowMany(IEnumerable<ClashMarkerVisual> markers, string style, double screenRadius = 10)
        {
            var resolvedMarkers = (markers ?? Enumerable.Empty<ClashMarkerVisual>())
                .Where(marker => marker != null && marker.Center != null)
                .ToList();
            lock (Sync)
            {
                _markers = resolvedMarkers;
                _markerPoint = resolvedMarkers.Count == 1 ? resolvedMarkers[0].Center : null;
            }
            MarkerStyle = NormalizeOverlayStyle(style);
            MarkerRadius = Math.Max(5, Math.Min(screenRadius, 200));

            if (!IsActive)
            {
                try
                {
                    _previousTool = Application.MainDocument.Tool.Value;
                    var pluginRecord = Application.Plugins.FindPlugin("NavisHelper.ClashMarker.CBC");
                    if (pluginRecord == null)
                    {
                        LastError = "Plugin not found";
                        return;
                    }

                    var tool = pluginRecord.LoadedPlugin as ToolPlugin ?? pluginRecord.LoadPlugin() as ToolPlugin;
                    if (tool == null)
                    {
                        LastError = "LoadPlugin returned null";
                        return;
                    }

                    Application.MainDocument.Tool.SetCustomToolPlugin(tool);
                    IsActive = true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    return;
                }
            }

            LastError = null;
            RequestRedraw();
        }

        public static void Hide()
        {
            var wasActive = IsActive;
            lock (Sync)
            {
                _markerPoint = null;
                _markers.Clear();
            }
            IsActive = false;
            if (!wasActive)
            {
                RequestRedraw();
                return;
            }
            try
            {
                if (_previousTool.HasValue)
                    Application.MainDocument.Tool.Value = _previousTool.Value;
                else
                    Application.MainDocument.Tool.Value = Tool.Select;
                _previousTool = null;
                RequestRedraw();
            }
            catch
            {
            }
        }

        public override bool MouseDown(View view, KeyModifiers modifiers, ushort button, int x, int y, double timeOffset)
        {
            return false;
        }

        public override bool MouseUp(View view, KeyModifiers modifiers, ushort button, int x, int y, double timeOffset)
        {
            return false;
        }

        public override void OverlayRender(View view, Graphics graphics)
        {
            ClashMarkerVisual[] markers;
            lock (Sync)
                markers = _markers.ToArray();

            foreach (var marker in markers)
            {
                try
                {
                    double[] rectangle;
                    var hasRectangle = TryProjectBounds(view, marker.Bounds, out rectangle);
                    var projectedCenter = view.ProjectPoint(marker.Center, true, true);
                    if (projectedCenter == null)
                        continue;

                    var center = new Point2D(projectedCenter.X, projectedCenter.Y);
                    if (string.Equals(MarkerStyle, CenterDotStyle, StringComparison.Ordinal))
                        DrawCenterDot(graphics, center, MarkerRadius);
                    else if (string.Equals(MarkerStyle, MarkupRedlineJsonBuilder.RectangleStyle, StringComparison.Ordinal))
                        DrawRectangle(graphics, hasRectangle ? rectangle : AroundPoint(center, MarkerRadius));
                    else if (string.Equals(MarkerStyle, MarkupRedlineJsonBuilder.ArrowStyle, StringComparison.Ordinal))
                        DrawArrow(graphics, hasRectangle ? rectangle : AroundPoint(center, MarkerRadius));
                    else if (string.Equals(MarkerStyle, MarkupRedlineJsonBuilder.HatchStyle, StringComparison.Ordinal))
                        DrawHatchedPolygon(graphics, view, marker.OutlinePoints, marker.Bounds, hasRectangle ? rectangle : AroundPoint(center, MarkerRadius));
                    else
                        DrawTarget(graphics, center, MarkerRadius);
                }
                catch
                {
                }
            }
        }

        private static void DrawRectangle(Graphics graphics, double[] rectangle)
        {
            var padding = MarkerRadius;
            var left = rectangle[0] - padding;
            var top = rectangle[1] - padding;
            var right = rectangle[2] + padding;
            var bottom = rectangle[3] + padding;
            graphics.LineWidth(3);
            graphics.Color(Color.FromByteRGB(255, 0, 0), 1.0);
            graphics.Line(new Point2D(left, top), new Point2D(right, top));
            graphics.Line(new Point2D(right, top), new Point2D(right, bottom));
            graphics.Line(new Point2D(right, bottom), new Point2D(left, bottom));
            graphics.Line(new Point2D(left, bottom), new Point2D(left, top));
        }

        private static void DrawTarget(Graphics graphics, Point2D center, double radius)
        {
            graphics.Color(Color.FromByteRGB(255, 255, 0), 0.5);
            graphics.Circle(center, radius, true);
            graphics.LineWidth(3);
            graphics.Color(Color.FromByteRGB(255, 255, 0), 1.0);
            graphics.Circle(center, radius, false);
            graphics.LineWidth(1);
            graphics.Color(Color.FromByteRGB(255, 0, 0), 1.0);
            graphics.Circle(center, radius * 1.3, false);
            graphics.LineWidth(2);
            graphics.Color(Color.FromByteRGB(255, 50, 50), 1.0);
            graphics.Line(new Point2D(center.X - radius * 1.7, center.Y), new Point2D(center.X + radius * 1.7, center.Y));
            graphics.Line(new Point2D(center.X, center.Y - radius * 1.7), new Point2D(center.X, center.Y + radius * 1.7));
        }

        private static void DrawCenterDot(Graphics graphics, Point2D center, double radius)
        {
            graphics.DepthTest(false);
            graphics.DepthMask(false);
            graphics.Blend(true);
            graphics.Color(Color.FromByteRGB(255, 230, 0), 0.28);
            graphics.Circle(center, radius, true);
            graphics.LineWidth(3);
            graphics.Color(Color.FromByteRGB(255, 80, 0), 0.95);
            graphics.Circle(center, radius, false);
            graphics.LineWidth(1);
            graphics.Color(Color.FromByteRGB(255, 255, 255), 0.75);
            graphics.Circle(center, Math.Max(2, radius * 0.35), false);
        }

        private static void DrawArrow(Graphics graphics, double[] rectangle)
        {
            var tip = new Point2D(rectangle[2], rectangle[1]);
            var offset = Math.Max(MarkerRadius * 4, 20);
            var tail = new Point2D(tip.X + offset, tip.Y - offset);
            var wing = Math.Max(MarkerRadius * 1.2, 8);
            graphics.LineWidth(3);
            graphics.Color(Color.FromByteRGB(255, 0, 0), 1.0);
            graphics.Line(tail, tip);
            graphics.Line(tip, new Point2D(tip.X + wing, tip.Y));
            graphics.Line(tip, new Point2D(tip.X, tip.Y - wing));
        }

        private static void DrawHatchedPolygon(Graphics graphics, View view, IList<Point3D> outlinePoints, BoundingBox3D bounds, double[] fallbackRectangle)
        {
            var polygon = TryProjectConvexHull(view, outlinePoints);
            if (polygon == null || polygon.Count < 3)
                polygon = TryProjectConvexHull(view, bounds);
            if (polygon == null || polygon.Count < 3)
                polygon = RectanglePolygon(fallbackRectangle);

            // This is the native ToolPlugin graphics overlay used by the clash
            // marker: it is deliberately a view marker, not a saved redline.
            graphics.DepthTest(false);
            graphics.DepthMask(false);
            graphics.Blend(true);
            graphics.Color(Color.FromByteRGB(255, 230, 0), 0.18);
            for (var index = 1; index < polygon.Count - 1; index++)
                graphics.Triangle(polygon[0], polygon[index], polygon[index + 1], true);

            graphics.LineWidth(3);
            graphics.Color(Color.FromByteRGB(255, 0, 0), 1.0);
            for (var index = 0; index < polygon.Count; index++)
                graphics.Line(polygon[index], polygon[(index + 1) % polygon.Count]);

            DrawPolygonHatch(graphics, polygon, 12);
        }

        private static void DrawPolygonHatch(Graphics graphics, IList<Point2D> polygon, double spacing)
        {
            if (polygon == null || polygon.Count < 3)
                return;

            const double directionX = 0.7071067811865476;
            const double directionY = 0.7071067811865476;
            const double normalX = -directionY;
            const double normalY = directionX;
            var minimum = polygon.Min(point => normalX * point.X + normalY * point.Y);
            var maximum = polygon.Max(point => normalX * point.X + normalY * point.Y);
            graphics.LineWidth(1);
            graphics.Color(Color.FromByteRGB(255, 50, 50), 0.85);
            for (var offset = Math.Floor(minimum / spacing) * spacing; offset <= maximum + 0.1; offset += spacing)
            {
                var intersections = new List<Point2D>();
                for (var index = 0; index < polygon.Count; index++)
                {
                    var from = polygon[index];
                    var to = polygon[(index + 1) % polygon.Count];
                    var fromProjection = normalX * from.X + normalY * from.Y;
                    var toProjection = normalX * to.X + normalY * to.Y;
                    var denominator = toProjection - fromProjection;
                    if (Math.Abs(denominator) < 1e-9)
                        continue;
                    var ratio = (offset - fromProjection) / denominator;
                    if (ratio < -1e-9 || ratio > 1 + 1e-9)
                        continue;
                    intersections.Add(new Point2D(from.X + (to.X - from.X) * ratio, from.Y + (to.Y - from.Y) * ratio));
                }

                if (intersections.Count < 2)
                    continue;
                var endpoints = intersections
                    .OrderBy(point => directionX * point.X + directionY * point.Y)
                    .ToList();
                graphics.Line(endpoints.First(), endpoints.Last());
            }
        }

        private static List<Point2D> TryProjectConvexHull(View view, IEnumerable<Point3D> points)
        {
            if (view == null)
                return null;
            var projectedPoints = new List<Point2D>();
            foreach (var point in points ?? Enumerable.Empty<Point3D>())
            {
                if (point == null)
                    continue;
                var projection = view.ProjectPoint(point, true, true);
                if (projection != null)
                    projectedPoints.Add(new Point2D(projection.X, projection.Y));
            }
            return BuildConvexHull(projectedPoints);
        }

        private static List<Point2D> TryProjectConvexHull(View view, BoundingBox3D bounds)
        {
            if (view == null || bounds == null)
                return null;
            var points = new List<Point3D>();
            foreach (var x in new[] { bounds.Min.X, bounds.Max.X })
            foreach (var y in new[] { bounds.Min.Y, bounds.Max.Y })
            foreach (var z in new[] { bounds.Min.Z, bounds.Max.Z })
                points.Add(new Point3D(x, y, z));
            return TryProjectConvexHull(view, points);
        }

        private static List<Point2D> RectanglePolygon(double[] rectangle)
        {
            if (rectangle == null || rectangle.Length < 4)
                return new List<Point2D>();
            return new List<Point2D>
            {
                new Point2D(rectangle[0], rectangle[1]),
                new Point2D(rectangle[2], rectangle[1]),
                new Point2D(rectangle[2], rectangle[3]),
                new Point2D(rectangle[0], rectangle[3]),
            };
        }

        private static List<Point2D> BuildConvexHull(IEnumerable<Point2D> points)
        {
            var sorted = (points ?? Enumerable.Empty<Point2D>())
                .OrderBy(point => point.X)
                .ThenBy(point => point.Y)
                .ToList();
            if (sorted.Count < 3)
                return sorted;

            var lower = new List<Point2D>();
            foreach (var point in sorted)
            {
                while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], point) <= 0)
                    lower.RemoveAt(lower.Count - 1);
                lower.Add(point);
            }
            var upper = new List<Point2D>();
            for (var index = sorted.Count - 1; index >= 0; index--)
            {
                var point = sorted[index];
                while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], point) <= 0)
                    upper.RemoveAt(upper.Count - 1);
                upper.Add(point);
            }
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        private static double Cross(Point2D origin, Point2D left, Point2D right)
        {
            return (left.X - origin.X) * (right.Y - origin.Y) - (left.Y - origin.Y) * (right.X - origin.X);
        }

        private static bool TryProjectBounds(View view, BoundingBox3D bounds, out double[] rectangle)
        {
            rectangle = null;
            if (bounds == null)
                return false;

            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;
            var count = 0;
            foreach (var x in new[] { bounds.Min.X, bounds.Max.X })
            foreach (var y in new[] { bounds.Min.Y, bounds.Max.Y })
            foreach (var z in new[] { bounds.Min.Z, bounds.Max.Z })
            {
                var projected = view.ProjectPoint(new Point3D(x, y, z), true, true);
                if (projected == null)
                    continue;
                minX = Math.Min(minX, projected.X);
                minY = Math.Min(minY, projected.Y);
                maxX = Math.Max(maxX, projected.X);
                maxY = Math.Max(maxY, projected.Y);
                count++;
            }

            if (count == 0)
                return false;
            rectangle = new[] { minX, minY, maxX, maxY };
            return true;
        }

        private static double[] AroundPoint(Point2D center, double radius)
        {
            return new[] { center.X - radius, center.Y - radius, center.X + radius, center.Y + radius };
        }

        private static string NormalizeOverlayStyle(string style)
        {
            if (string.Equals(style, CenterDotStyle, StringComparison.OrdinalIgnoreCase))
                return CenterDotStyle;
            return MarkupRedlineJsonBuilder.NormalizeStyle(style);
        }

        private static void RequestRedraw()
        {
            try
            {
                Application.ActiveDocument.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            }
            catch
            {
            }
        }
    }

    public sealed class ClashMarkerVisual
    {
        public ClashMarkerVisual(Point3D center, BoundingBox3D bounds, IEnumerable<Point3D> outlinePoints = null)
        {
            Center = center;
            Bounds = bounds;
            OutlinePoints = (outlinePoints ?? Enumerable.Empty<Point3D>()).Where(point => point != null).ToList();
        }

        public Point3D Center { get; private set; }
        public BoundingBox3D Bounds { get; private set; }
        public IList<Point3D> OutlinePoints { get; private set; }
    }
}
