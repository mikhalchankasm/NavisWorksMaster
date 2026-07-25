using System;
using System.Globalization;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal static class ClashReportMarkerViewService
    {
        public static bool TrySetClashPointMarker(Document document, Point3D clashPoint, out string warning)
        {
            warning = string.Empty;
            try
            {
                if (document == null || document.ActiveView == null)
                {
                    warning = "Active view is unavailable.";
                    return false;
                }

                var view = document.ActiveView;
                var projected = view.ProjectPoint(clashPoint, false, false);
                if (projected == null)
                {
                    warning = "ProjectPoint returned null.";
                    return false;
                }

                var cameraSpace = LcOpRedline.ScreenToCameraSpace(view.Viewer, projected.X, projected.Y);
                if (!IsFinite(cameraSpace.X) || !IsFinite(cameraSpace.Y))
                {
                    warning = "Projected marker point is not finite.";
                    return false;
                }

                var radii = GetRedlineMarkerRadii(view, 14);
                var rx = radii[0];
                var ry = radii[1];
                var lineX = rx * 1.7;
                var lineY = ry * 1.7;
                var c = CultureInfo.InvariantCulture;
                var redline = string.Format(c,
                    "{{\"Type\":\"RedlineCollection\",\"Version\":1,\"Values\":[" +
                    "{{\"Type\":\"RedlineEllipse\",\"Version\":1,\"Thickness\":4,\"Color\":[1.0,0.0,0.0]," +
                    "\"MinPoint\":[{0},{1}],\"MaxPoint\":[{2},{3}]}}," +
                    "{{\"Type\":\"RedlineLine\",\"Version\":1,\"Thickness\":3,\"Color\":[1,0,0]," +
                    "\"Start\":[{4},{5}],\"End\":[{6},{7}]}}," +
                    "{{\"Type\":\"RedlineLine\",\"Version\":1,\"Thickness\":3,\"Color\":[1,0,0]," +
                    "\"Start\":[{8},{9}],\"End\":[{10},{11}]}}" +
                    "]}}",
                    cameraSpace.X - rx, cameraSpace.Y - ry,
                    cameraSpace.X + rx, cameraSpace.Y + ry,
                    cameraSpace.X - lineX, cameraSpace.Y,
                    cameraSpace.X + lineX, cameraSpace.Y,
                    cameraSpace.X, cameraSpace.Y - lineY,
                    cameraSpace.X, cameraSpace.Y + lineY);

                RedlineJsonSanitizer.SetSupportedRedlines(view, redline, null);
                view.RequestDelayedRedraw(ViewRedrawRequests.All);
                return true;
            }
            catch (Exception ex)
            {
                warning = ex.Message;
                return false;
            }
        }

        public static void ClearActiveViewRedlines(Document document)
        {
            try
            {
                if (document == null || document.ActiveView == null)
                    return;

                RedlineJsonSanitizer.SetSupportedRedlines(document.ActiveView, "{\"Type\":\"RedlineCollection\",\"Version\":1,\"Values\":[]}", null);
                document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to clear active view redlines after clash report capture: " + ex.Message, "ClashMcp");
            }
        }

        public static void ApplyTopViewToBox(Document document, BoundingBox3D box)
        {
            ApplyTopViewToBox(document, box, true);
        }

        public static void ApplyTopViewToBox(Document document, BoundingBox3D box, bool fitToBox)
        {
            ApplyTopViewToBox(document, box, fitToBox, true);
        }

        public static void ApplyTopViewToBox(Document document, BoundingBox3D box, bool fitToBox, bool applySectionBox)
        {
            if (document == null || document.CurrentViewpoint == null || box == null)
                return;

            var viewpoint = document.CurrentViewpoint.CreateCopy();
            viewpoint.Rotation = new Rotation3D(0, 0, 0, -1);
            viewpoint.Projection = ViewpointProjection.Orthographic;
            if (fitToBox)
                viewpoint.ZoomBox(box);
            document.CurrentViewpoint.CopyFrom(viewpoint);
            if (applySectionBox)
                SectionBoxHelper.SetSectionBox(box);
        }

        private static double[] GetRedlineMarkerRadii(View activeView, int pixels)
        {
            try
            {
                var centerX = Math.Max(activeView.Width / 2, 1);
                var centerY = Math.Max(activeView.Height / 2, 1);
                var rightX = Math.Min(centerX + 1, Math.Max(activeView.Width - 1, 1));
                var downY = Math.Min(centerY + 1, Math.Max(activeView.Height - 1, 1));

                var center = LcOpRedline.ScreenToCameraSpace(activeView.Viewer, centerX, centerY);
                var right = LcOpRedline.ScreenToCameraSpace(activeView.Viewer, rightX, centerY);
                var down = LcOpRedline.ScreenToCameraSpace(activeView.Viewer, centerX, downY);

                var unitX = Math.Abs(right.X - center.X);
                var unitY = Math.Abs(down.Y - center.Y);
                if (!IsFinite(unitX) || unitX < 1e-9)
                    unitX = 0.002;
                if (!IsFinite(unitY) || unitY < 1e-9)
                    unitY = 0.002;

                return new[] { unitX * pixels, unitY * pixels };
            }
            catch
            {
                return new[] { 0.03, 0.03 };
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
