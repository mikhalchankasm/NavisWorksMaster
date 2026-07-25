using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;
using NavisHelper.Interfaces;
using Application = Autodesk.Navisworks.Api.Application;

namespace NavisHelper.Dev
{
    /// <summary>
    /// Dev-кнопка для redline-контура плоскости по текущему выделению.
    /// </summary>
    public class SelectionPlaneScript : IDevScript
    {
        private const double ItemBoxScale = 1.0;
        private const double HullExpandFactor = 0.05;
        private static bool _isVisible;

        public string Name => "Плоскость по выделению";

        public void Run()
        {
            try
            {
                var doc = Application.ActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("Нет активного документа.", Name);
                    return;
                }

                var view = doc.ActiveView;
                if (view == null)
                {
                    MessageBox.Show("Нет активного вида.", Name);
                    return;
                }

                if (_isVisible)
                {
                    ClearRedlines(view);
                    _isVisible = false;
                    return;
                }

                var selection = doc.CurrentSelection.SelectedItems;
                if (selection == null || selection.Count == 0)
                {
                    MessageBox.Show("Нет выделенных объектов.", Name);
                    return;
                }

                var bbox = selection.BoundingBox();
                if (bbox == null)
                {
                    MessageBox.Show("Не удалось определить габариты выделения.", Name);
                    return;
                }

                var corners = BuildPlanePolygon(selection, bbox);
                var redlinePoints = new List<Point2D>();

                for (int i = 0; i < corners.Count; i++)
                    redlinePoints.Add(WorldToRedline(view, corners[i]));

                string json = BuildLineLoopJson(redlinePoints);
                view.SetRedlines(json);
                _isVisible = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Name);
            }
        }

        private static Point2D WorldToRedline(View view, Point3D point)
        {
            ProjectionResult projected = view.ProjectPoint(point, false, false);
            if (projected == null)
                throw new InvalidOperationException("ProjectPoint вернул null.");

            return LcOpRedline.ScreenToCameraSpace(view.Viewer, projected.X, projected.Y);
        }

        private static List<Point3D> BuildPlanePolygon(ModelItemCollection selection, BoundingBox3D selectionBox)
        {
            double dx = Math.Abs(selectionBox.Max.X - selectionBox.Min.X);
            double dy = Math.Abs(selectionBox.Max.Y - selectionBox.Min.Y);
            double dz = Math.Abs(selectionBox.Max.Z - selectionBox.Min.Z);
            PlaneAxis axis = ChooseAxis(dx, dy, dz);
            var points = new List<PlanePoint>();

            foreach (ModelItem item in selection)
            {
                BoundingBox3D box = null;
                try { box = item.BoundingBox(); } catch { }
                if (box == null) continue;

                AddBoxProjection(points, box, axis, ItemBoxScale);
            }

            if (points.Count < 3)
            {
                AddBoxProjection(points, selectionBox, axis, 1.0);
            }

            var hull = ConvexHull(points);
            if (hull.Count < 3)
            {
                hull = ConvexHull(BoxProjection(selectionBox, axis, 1.0));
            }

            ExpandHull(hull, HullExpandFactor);

            double fixedValue = FixedCoordinate(selectionBox.Center, axis);
            return hull.Select(p => ToPoint3D(p, fixedValue, axis)).ToList();
        }

        private static PlaneAxis ChooseAxis(double dx, double dy, double dz)
        {
            if (dx <= dy && dx <= dz) return PlaneAxis.X;
            if (dy <= dx && dy <= dz) return PlaneAxis.Y;
            return PlaneAxis.Z;
        }

        private static void AddBoxProjection(List<PlanePoint> target, BoundingBox3D box, PlaneAxis axis, double scale)
        {
            target.AddRange(BoxProjection(box, axis, scale));
        }

        private static List<PlanePoint> BoxProjection(BoundingBox3D box, PlaneAxis axis, double scale)
        {
            switch (axis)
            {
                case PlaneAxis.X:
                    return RectPoints(box.Min.Y, box.Min.Z, box.Max.Y, box.Max.Z, scale);
                case PlaneAxis.Y:
                    return RectPoints(box.Min.X, box.Min.Z, box.Max.X, box.Max.Z, scale);
                default:
                    return RectPoints(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y, scale);
            }
        }

        private static List<PlanePoint> RectPoints(double minU, double minV, double maxU, double maxV, double scale)
        {
            double centerU = (minU + maxU) / 2.0;
            double centerV = (minV + maxV) / 2.0;
            double halfU = Math.Abs(maxU - minU) * scale / 2.0;
            double halfV = Math.Abs(maxV - minV) * scale / 2.0;

            minU = centerU - halfU;
            maxU = centerU + halfU;
            minV = centerV - halfV;
            maxV = centerV + halfV;

            return new List<PlanePoint>
            {
                new PlanePoint(minU, minV),
                new PlanePoint(maxU, minV),
                new PlanePoint(maxU, maxV),
                new PlanePoint(minU, maxV)
            };
        }

        private static double FixedCoordinate(Point3D center, PlaneAxis axis)
        {
            switch (axis)
            {
                case PlaneAxis.X: return center.X;
                case PlaneAxis.Y: return center.Y;
                default: return center.Z;
            }
        }

        private static Point3D ToPoint3D(PlanePoint point, double fixedValue, PlaneAxis axis)
        {
            switch (axis)
            {
                case PlaneAxis.X:
                    return new Point3D(fixedValue, point.U, point.V);
                case PlaneAxis.Y:
                    return new Point3D(point.U, fixedValue, point.V);
                default:
                    return new Point3D(point.U, point.V, fixedValue);
            }
        }

        private static List<PlanePoint> ConvexHull(IEnumerable<PlanePoint> source)
        {
            var points = source
                .GroupBy(p => string.Format(CultureInfo.InvariantCulture, "{0:G12};{1:G12}", p.U, p.V))
                .Select(g => g.First())
                .OrderBy(p => p.U)
                .ThenBy(p => p.V)
                .ToList();

            if (points.Count <= 1) return points;

            var lower = new List<PlanePoint>();
            foreach (var p in points)
            {
                while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0)
                    lower.RemoveAt(lower.Count - 1);
                lower.Add(p);
            }

            var upper = new List<PlanePoint>();
            for (int i = points.Count - 1; i >= 0; i--)
            {
                var p = points[i];
                while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0)
                    upper.RemoveAt(upper.Count - 1);
                upper.Add(p);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        private static double Cross(PlanePoint o, PlanePoint a, PlanePoint b)
        {
            return (a.U - o.U) * (b.V - o.V) - (a.V - o.V) * (b.U - o.U);
        }

        private static void ExpandHull(List<PlanePoint> hull, double factor)
        {
            if (hull.Count == 0) return;

            double centerU = hull.Average(p => p.U);
            double centerV = hull.Average(p => p.V);

            for (int i = 0; i < hull.Count; i++)
            {
                var p = hull[i];
                hull[i] = new PlanePoint(
                    centerU + (p.U - centerU) * (1.0 + factor),
                    centerV + (p.V - centerV) * (1.0 + factor));
            }
        }

        private static string BuildLineLoopJson(List<Point2D> points)
        {
            if (points == null || points.Count < 3)
                throw new InvalidOperationException("Ожидалось не менее 3 точек плоскости.");

            var sb = new StringBuilder();
            sb.Append("{\"Type\":\"RedlineCollection\",\"Version\":1,\"Values\":[");

            bool hasLines = false;
            for (int i = 0; i < points.Count; i++)
            {
                AppendLine(sb, points[i], points[(i + 1) % points.Count], hasLines, 3, 0, 1, 1);
                hasLines = true;
            }

            foreach (var hatch in BuildHatchLines(points))
            {
                AppendLine(sb, hatch.Start, hatch.End, hasLines, 1, 0, 1, 1);
                hasLines = true;
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static List<HatchLine> BuildHatchLines(List<Point2D> polygon)
        {
            var result = new List<HatchLine>();
            if (polygon.Count < 3) return result;

            AddHorizontalHatches(polygon, result);
            AddVerticalHatches(polygon, result);
            return result;
        }

        private static void AddHorizontalHatches(List<Point2D> polygon, List<HatchLine> result)
        {
            double minY = polygon.Min(p => p.Y);
            double maxY = polygon.Max(p => p.Y);
            double height = maxY - minY;
            if (height <= 1e-9) return;

            int lineCount = 28;
            double step = height / (lineCount + 1);

            for (int i = 1; i <= lineCount; i++)
            {
                double y = minY + step * i;
                var xs = new List<double>();

                for (int j = 0; j < polygon.Count; j++)
                {
                    Point2D a = polygon[j];
                    Point2D b = polygon[(j + 1) % polygon.Count];

                    if (Math.Abs(a.Y - b.Y) < 1e-12) continue;

                    bool crosses = (a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y);
                    if (!crosses) continue;

                    double t = (y - a.Y) / (b.Y - a.Y);
                    xs.Add(a.X + t * (b.X - a.X));
                }

                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    if (Math.Abs(xs[k + 1] - xs[k]) < 1e-9) continue;
                    result.Add(new HatchLine(new Point2D(xs[k], y), new Point2D(xs[k + 1], y)));
                }
            }
        }

        private static void AddVerticalHatches(List<Point2D> polygon, List<HatchLine> result)
        {
            double minX = polygon.Min(p => p.X);
            double maxX = polygon.Max(p => p.X);
            double width = maxX - minX;
            if (width <= 1e-9) return;

            int lineCount = 28;
            double step = width / (lineCount + 1);

            for (int i = 1; i <= lineCount; i++)
            {
                double x = minX + step * i;
                var ys = new List<double>();

                for (int j = 0; j < polygon.Count; j++)
                {
                    Point2D a = polygon[j];
                    Point2D b = polygon[(j + 1) % polygon.Count];

                    if (Math.Abs(a.X - b.X) < 1e-12) continue;

                    bool crosses = (a.X <= x && b.X > x) || (b.X <= x && a.X > x);
                    if (!crosses) continue;

                    double t = (x - a.X) / (b.X - a.X);
                    ys.Add(a.Y + t * (b.Y - a.Y));
                }

                ys.Sort();
                for (int k = 0; k + 1 < ys.Count; k += 2)
                {
                    if (Math.Abs(ys[k + 1] - ys[k]) < 1e-9) continue;
                    result.Add(new HatchLine(new Point2D(x, ys[k]), new Point2D(x, ys[k + 1])));
                }
            }
        }

        private static void AppendLine(
            StringBuilder sb,
            Point2D start,
            Point2D end,
            bool prependComma,
            int thickness,
            int red,
            int green,
            int blue)
        {
            if (prependComma) sb.Append(",");

            sb.Append("{\"Type\":\"RedlineLine\",\"Version\":1,");
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"Thickness\":{0},", thickness);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"Color\":[{0},{1},{2}],", red, green, blue);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "\"Start\":[{0:G17},{1:G17}],",
                start.X,
                start.Y);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "\"End\":[{0:G17},{1:G17}]",
                end.X,
                end.Y);
            sb.Append("}");
        }

        private static void ClearRedlines(View view)
        {
            view.SetRedlines("{\"Type\":\"RedlineCollection\",\"Version\":1,\"Values\":[]}");
        }

        private enum PlaneAxis
        {
            X,
            Y,
            Z
        }

        private struct PlanePoint
        {
            public PlanePoint(double u, double v)
            {
                U = u;
                V = v;
            }

            public double U { get; }
            public double V { get; }
        }

        private struct HatchLine
        {
            public HatchLine(Point2D start, Point2D end)
            {
                Start = start;
                End = end;
            }

            public Point2D Start { get; }
            public Point2D End { get; }
        }
    }
}
