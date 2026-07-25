using System;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using Application = Autodesk.Navisworks.Api.Application;

namespace NavisHelper
{
    /// <summary>
    /// Dev overlay-tool для быстрой визуализации плоскости по текущему выделению.
    /// </summary>
    [Plugin("NavisHelper.SelectionPlane", "CBC")]
    public class SelectionPlaneTool : ToolPlugin
    {
        private static Tool? _previousTool;

        public static Point3D[] Corners { get; private set; }
        public static string LastError { get; private set; }
        public static bool IsActive { get; private set; }
        public static string PlaneInfo { get; private set; }

        public static bool ShowFromCurrentSelection()
        {
            LastError = null;
            PlaneInfo = null;

            try
            {
                var doc = Application.ActiveDocument;
                if (doc == null)
                {
                    LastError = "Нет активного документа";
                    return false;
                }

                var selection = doc.CurrentSelection.SelectedItems;
                if (selection == null || selection.Count == 0)
                {
                    LastError = "Нет выделенных объектов";
                    return false;
                }

                var bbox = selection.BoundingBox();
                if (bbox == null)
                {
                    LastError = "Не удалось определить габариты выделения";
                    return false;
                }

                Corners = BuildPlaneCorners(bbox, out string axisName);
                PlaneInfo = string.Format(
                    "Плоскость: {0} объектов, нормаль {1}",
                    selection.Count,
                    axisName);

                EnsureToolActive();
                RequestRedraw();
                return LastError == null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public static void Hide()
        {
            Corners = null;
            PlaneInfo = null;
            IsActive = false;

            try
            {
                if (_previousTool.HasValue)
                    Application.MainDocument.Tool.Value = _previousTool.Value;
                else
                    Application.MainDocument.Tool.Value = Tool.Select;

                _previousTool = null;
                RequestRedraw();
            }
            catch { }
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
            if (Corners == null || Corners.Length != 4) return;

            try
            {
                var p0 = Project(view, Corners[0]);
                var p1 = Project(view, Corners[1]);
                var p2 = Project(view, Corners[2]);
                var p3 = Project(view, Corners[3]);
                if (p0 == null || p1 == null || p2 == null || p3 == null) return;

                graphics.Color(Color.FromByteRGB(0, 180, 255), 0.22);
                graphics.Triangle(p0, p1, p2, true);
                graphics.Triangle(p0, p2, p3, true);

                graphics.LineWidth(3);
                graphics.Color(Color.FromByteRGB(0, 120, 255), 1.0);
                graphics.Line(p0, p1);
                graphics.Line(p1, p2);
                graphics.Line(p2, p3);
                graphics.Line(p3, p0);

                graphics.LineWidth(1);
                graphics.Color(Color.FromByteRGB(255, 255, 255), 0.9);
                graphics.Line(p0, p2);
                graphics.Line(p1, p3);
            }
            catch { }
        }

        private static void EnsureToolActive()
        {
            if (IsActive) return;

            try
            {
                _previousTool = Application.MainDocument.Tool.Value;

                var pluginRecord = Application.Plugins.FindPlugin("NavisHelper.SelectionPlane.CBC");
                if (pluginRecord == null)
                {
                    LastError = "SelectionPlane plugin not found";
                    return;
                }

                var tool = pluginRecord.LoadedPlugin as ToolPlugin ??
                           pluginRecord.LoadPlugin() as ToolPlugin;

                if (tool == null)
                {
                    LastError = "SelectionPlane LoadPlugin returned null";
                    return;
                }

                Application.MainDocument.Tool.SetCustomToolPlugin(tool);
                IsActive = true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        private static Point2D Project(View view, Point3D point)
        {
            ProjectionResult projection = view.ProjectPoint(point, false, false);
            if (projection == null) return null;
            return new Point2D(projection.X, projection.Y);
        }

        private static Point3D[] BuildPlaneCorners(BoundingBox3D bbox, out string axisName)
        {
            double dx = Math.Abs(bbox.Max.X - bbox.Min.X);
            double dy = Math.Abs(bbox.Max.Y - bbox.Min.Y);
            double dz = Math.Abs(bbox.Max.Z - bbox.Min.Z);

            var center = bbox.Center;
            double padX = Math.Max(dx * 0.05, 0.05);
            double padY = Math.Max(dy * 0.05, 0.05);
            double padZ = Math.Max(dz * 0.05, 0.05);

            if (dx <= dy && dx <= dz)
            {
                axisName = "X";
                double x = center.X;
                return new[]
                {
                    new Point3D(x, bbox.Min.Y - padY, bbox.Min.Z - padZ),
                    new Point3D(x, bbox.Max.Y + padY, bbox.Min.Z - padZ),
                    new Point3D(x, bbox.Max.Y + padY, bbox.Max.Z + padZ),
                    new Point3D(x, bbox.Min.Y - padY, bbox.Max.Z + padZ)
                };
            }

            if (dy <= dx && dy <= dz)
            {
                axisName = "Y";
                double y = center.Y;
                return new[]
                {
                    new Point3D(bbox.Min.X - padX, y, bbox.Min.Z - padZ),
                    new Point3D(bbox.Max.X + padX, y, bbox.Min.Z - padZ),
                    new Point3D(bbox.Max.X + padX, y, bbox.Max.Z + padZ),
                    new Point3D(bbox.Min.X - padX, y, bbox.Max.Z + padZ)
                };
            }

            axisName = "Z";
            double z = center.Z;
            return new[]
            {
                new Point3D(bbox.Min.X - padX, bbox.Min.Y - padY, z),
                new Point3D(bbox.Max.X + padX, bbox.Min.Y - padY, z),
                new Point3D(bbox.Max.X + padX, bbox.Max.Y + padY, z),
                new Point3D(bbox.Min.X - padX, bbox.Max.Y + padY, z)
            };
        }

        private static void RequestRedraw()
        {
            try { Application.ActiveDocument.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All); }
            catch { }
        }
    }
}
