using System;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Core;
using NavisHelper.Core.Localization;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisHelper.WPF
{
    public partial class NavisHelperPanel
    {
        private void SaveClashCameraDiagnostic()
        {
            var doc = NwApplication.ActiveDocument;
            if (doc == null || doc.IsClear || doc.CurrentViewpoint == null)
            {
                SetGlobalStatusResource("Panel_Clash_Camera_NoActiveDocument", Brushes.Orange);
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(_clashCameraDiagDir))
                    _clashCameraDiagDir = Path.Combine(Path.GetTempPath(), "NavisHelper-ClashCameraDiag-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

                Directory.CreateDirectory(_clashCameraDiagDir);
                _clashCameraDiagIndex++;

                dynamic row = _clashGrid?.SelectedItem;
                ClashResult clash = null;
                try { clash = row?.Result as ClashResult; } catch { }

                var viewpoint = doc.CurrentViewpoint.CreateCopy();
                var cameraJson = string.Empty;
                try { cameraJson = viewpoint.GetCamera() ?? string.Empty; } catch (Exception ex) { cameraJson = "GetCamera error: " + ex.Message; }

                ProjectionResult projected = null;
                var center = _clashMgr.LastClashCenter ?? clash?.Center;
                if (center != null && doc.ActiveView != null)
                {
                    try { projected = doc.ActiveView.ProjectPoint(center, false, false); } catch { }
                }

                var focalEstimate = EstimateFocalPoint(viewpoint);
                var path = Path.Combine(_clashCameraDiagDir, "camera_" + _clashCameraDiagIndex.ToString("000", CultureInfo.InvariantCulture) + ".json");
                File.WriteAllText(path, BuildClashCameraDiagnosticJson(row, clash, viewpoint, center, projected, focalEstimate, cameraJson), System.Text.Encoding.UTF8);

                SetGlobalStatusResource(
                    "Panel_Clash_Camera_Saved_Format",
                    Brushes.DarkGreen,
                    _clashCameraDiagIndex,
                    path);
            }
            catch (Exception ex)
            {
                SetGlobalStatusResource("Panel_Clash_Camera_Failed_Format", Brushes.Red, ex.Message);
            }
        }

        private string BuildClashCameraDiagnosticJson(dynamic row, ClashResult clash, Viewpoint viewpoint, Point3D center, ProjectionResult projected, Point3D focalEstimate, string cameraJson)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            AppendJson(sb, "capturedAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture), true);
            AppendJson(sb, "index", _clashCameraDiagIndex.ToString(CultureInfo.InvariantCulture), false);
            AppendJson(sb, "document", NwApplication.ActiveDocument?.FileName ?? string.Empty, true);
            AppendJson(sb, "selectedRowName", SafeDynamicString(() => row?.Name), true);
            AppendJson(sb, "selectedRowStatus", SafeDynamicString(() => row?.Status), true);
            AppendJson(sb, "selectedRowDistance", SafeDynamicString(() => row?.Distance), true);
            AppendJson(sb, "clashDisplayName", clash?.DisplayName ?? string.Empty, true);
            AppendJson(sb, "clashCenter", FormatPoint(clash?.Center), false);
            AppendJson(sb, "lastClashCenter", FormatPoint(center), false);
            AppendJson(sb, "lastExpandedBoxMin", FormatPoint(_clashMgr.LastExpandedBox?.Min), false);
            AppendJson(sb, "lastExpandedBoxMax", FormatPoint(_clashMgr.LastExpandedBox?.Max), false);
            AppendJson(sb, "viewWidth", (NwApplication.ActiveDocument?.ActiveView?.Width ?? 0).ToString(CultureInfo.InvariantCulture), false);
            AppendJson(sb, "viewHeight", (NwApplication.ActiveDocument?.ActiveView?.Height ?? 0).ToString(CultureInfo.InvariantCulture), false);
            AppendJson(sb, "projectedClash", FormatProjection(projected), false);
            AppendJson(sb, "position", FormatPoint(viewpoint.Position), false);
            AppendJson(sb, "rotation", FormatRotation(viewpoint.Rotation), false);
            AppendJson(sb, "projection", viewpoint.Projection.ToString(), true);
            AppendJson(sb, "hasFocalDistance", viewpoint.HasFocalDistance.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(), false);
            AppendJson(sb, "focalDistance", GetFocalDistanceOrNull(viewpoint), false);
            AppendJson(sb, "rightOffsetAtFocalDistance", SafeDoubleOrNull(() => viewpoint.RightOffsetAtFocalDistance), false);
            AppendJson(sb, "upOffsetAtFocalDistance", SafeDoubleOrNull(() => viewpoint.UpOffsetAtFocalDistance), false);
            AppendJson(sb, "rightOffsetFactor", SafeDoubleOrNull(() => viewpoint.RightOffsetFactor), false);
            AppendJson(sb, "upOffsetFactor", SafeDoubleOrNull(() => viewpoint.UpOffsetFactor), false);
            AppendJson(sb, "focalPointEstimate", FormatPoint(focalEstimate), false);
            AppendJson(sb, "worldUpVector", SafeToString(() => viewpoint.WorldUpVector), true);
            AppendJson(sb, "cameraJson", cameraJson, true, false);
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static Point3D EstimateFocalPoint(Viewpoint viewpoint)
        {
            if (viewpoint == null || !viewpoint.HasFocalDistance)
                return null;

            var p = viewpoint.Position;
            var r = viewpoint.Rotation;
            var d = viewpoint.FocalDistance;
            var fx = -2.0 * (r.B * r.D + r.A * r.C);
            var fy = -2.0 * (r.C * r.D - r.A * r.B);
            var fz = -(1.0 - 2.0 * (r.B * r.B + r.C * r.C));
            return new Point3D(p.X + fx * d, p.Y + fy * d, p.Z + fz * d);
        }

        private static string GetFocalDistanceOrNull(Viewpoint viewpoint)
        {
            try
            {
                if (viewpoint == null || !viewpoint.HasFocalDistance)
                    return "null";

                return viewpoint.FocalDistance.ToString("G17", CultureInfo.InvariantCulture);
            }
            catch
            {
                return "null";
            }
        }

        private static string SafeDoubleOrNull(Func<double> getter)
        {
            try
            {
                var value = getter();
                return (double.IsNaN(value) || double.IsInfinity(value))
                    ? "null"
                    : value.ToString("G17", CultureInfo.InvariantCulture);
            }
            catch
            {
                return "null";
            }
        }

        private static void AppendJson(System.Text.StringBuilder sb, string name, string value, bool quote, bool comma = true)
        {
            sb.Append("  \"").Append(EscapeJson(name)).Append("\": ");
            if (quote)
            {
                sb.Append("\"").Append(EscapeJson(value ?? string.Empty)).Append("\"");
            }
            else
            {
                sb.Append(value ?? "null");
            }

            if (comma)
                sb.Append(",");
            sb.AppendLine();
        }

        private static string FormatPoint(Point3D point)
        {
            if (point == null)
                return "null";

            return string.Format(CultureInfo.InvariantCulture, "{{\"x\":{0:G17},\"y\":{1:G17},\"z\":{2:G17}}}", point.X, point.Y, point.Z);
        }

        private static string FormatRotation(Rotation3D rotation)
        {
            return string.Format(CultureInfo.InvariantCulture, "{{\"w\":{0:G17},\"x\":{1:G17},\"y\":{2:G17},\"z\":{3:G17}}}", rotation.A, rotation.B, rotation.C, rotation.D);
        }

        private static string FormatProjection(ProjectionResult projection)
        {
            if (projection == null)
                return "null";

            return string.Format(CultureInfo.InvariantCulture, "{{\"x\":{0:G17},\"y\":{1:G17},\"depth\":{2:G17}}}", projection.X, projection.Y, projection.Depth);
        }

        private static string SafeDynamicString(Func<object> getter)
        {
            try
            {
                return Convert.ToString(getter(), CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeToString<T>(Func<T> getter)
        {
            try
            {
                var value = getter();
                return value == null ? string.Empty : value.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }
}
