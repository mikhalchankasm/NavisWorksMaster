using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;
using Newtonsoft.Json;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {

        private static string NormalizeClashReportOutputDirectory(Document document, string outputDirectory)
        {
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputDirectory.Trim()));

            var baseDirectory = string.Empty;
            try
            {
                if (!string.IsNullOrWhiteSpace(document.FileName))
                    baseDirectory = Path.GetDirectoryName(document.FileName);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to resolve clash report output directory from document file name: " + ex.Message, "ClashMcp");
            }

            if (string.IsNullOrWhiteSpace(baseDirectory))
                baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NavisHelper", "ClashReports");

            return Path.Combine(baseDirectory, "NavisHelper_ClashReport_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        }

        private static string NormalizeClashViewpointFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return "NavisHelper Clash Viewpoints " + DateTime.Now.ToString("yyyyMMdd HHmmss", CultureInfo.InvariantCulture);

            var normalized = folderPath.Trim().Replace('\\', '/').Trim('/');
            return string.IsNullOrWhiteSpace(normalized)
                ? "NavisHelper Clash Viewpoints " + DateTime.Now.ToString("yyyyMMdd HHmmss", CultureInfo.InvariantCulture)
                : normalized;
        }

        private static void ClearClashReportOutputDirectory(string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            var markerPath = Path.Combine(outputDirectory, ClashReportMarkerFileName);
            var imagesDirectory = Path.Combine(outputDirectory, ClashReportOutputHelper.ImagesDirectoryName);
            var existingProtectedFiles = ClashReportOutputHelper.GetProtectedExistingFileNames(
                Directory.EnumerateFiles(outputDirectory).Select(Path.GetFileName));

            if (!File.Exists(markerPath) && ClashReportOutputHelper.RequiresMarkerForOverwrite(Directory.Exists(imagesDirectory), existingProtectedFiles))
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    "Refusing to overwrite NavisHelper clash report files without a NavisHelper clash report marker file. Choose an empty outputDirectory or a directory created by NavisHelper.");

            foreach (var fileName in ClashReportOutputHelper.ProtectedFileNames)
            {
                var path = Path.Combine(outputDirectory, fileName);
                if (File.Exists(path))
                    File.Delete(path);
            }

            if (Directory.Exists(imagesDirectory))
                Directory.Delete(imagesDirectory, true);
        }

        private static void EnsureClashReportOutputMarker(string outputDirectory)
        {
            File.WriteAllText(
                Path.Combine(outputDirectory, ClashReportMarkerFileName),
                "NavisHelper clash report output directory." + Environment.NewLine,
                Encoding.UTF8);
        }

        private static double NormalizePositiveDouble(double? value, double defaultValue, string name)
        {
            double result;
            if (!ClashNumericOptionHelper.TryNormalizePositiveDouble(value, defaultValue, out result))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, name + " must be greater than 0.");
            return result;
        }

        private static double NormalizeNonNegativeDouble(double value, string name)
        {
            double result;
            if (!ClashNumericOptionHelper.TryNormalizeNonNegativeDouble(value, out result))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, name + " must be greater than or equal to 0.");
            return result;
        }

        private static double NormalizeUnitDouble(double? value, double defaultValue, string name)
        {
            double result;
            if (!ClashNumericOptionHelper.TryNormalizeUnitDouble(value, defaultValue, out result))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, name + " must be between 0 and 1.");
            return result;
        }

        private static ClashTestType? NormalizeClashTestType(string value)
        {
            var normalized = ClashTestTypeHelper.NormalizeTestType(value);
            switch (normalized)
            {
                case null:
                    if (string.IsNullOrWhiteSpace(value))
                        return null;
                    break;
                case ClashTestTypeHelper.Hard:
                    return ClashTestType.Hard;
                case ClashTestTypeHelper.HardConservative:
                    return ClashTestType.HardConservative;
                case ClashTestTypeHelper.Clearance:
                    return ClashTestType.Clearance;
                case ClashTestTypeHelper.Duplicate:
                    return ClashTestType.Duplicate;
            }

            throw new AgentCommandException(ErrorCodes.SchemaViolation, "testType must be one of: hard/intersection, hard_conservative/conservative, clearance, duplicate.");
        }

        private static double? SafeDouble(Func<double> read)
        {
            if (read == null)
                return null;

            try
            {
                return read();
            }
            catch
            {
                return null;
            }
        }

        private static double? DocUnitsToMm(double? value)
        {
            if (!value.HasValue)
                return null;

            return DocUnitsToMm(value.Value);
        }

        private static double DocUnitsToMm(double value)
        {
            try
            {
                var units = Application.ActiveDocument.Units;
                switch (units)
                {
                    case Units.Centimeters: return value * 10.0;
                    case Units.Meters: return value * 1000.0;
                    case Units.Kilometers: return value * 1000000.0;
                    case Units.Inches: return value * 25.4;
                    case Units.Feet: return value * 304.8;
                    case Units.Yards: return value * 914.4;
                    case Units.Miles: return value * 1609344.0;
                    case Units.Millimeters: return value;
                    default: return value * 1000.0;
                }
            }
            catch
            {
                return value * 1000.0;
            }
        }

        private static Autodesk.Navisworks.Api.Color ParseReportColor(string text, Autodesk.Navisworks.Api.Color fallback, string name)
        {
            ClashRgbColor color;
            if (!ClashReportColorHelper.TryParseHexRgb(text, out color))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, name + " must be #RRGGBB.");

            if (color == null)
                return fallback;

            return Autodesk.Navisworks.Api.Color.FromByteRGB(color.R, color.G, color.B);
        }

        private static string NormalizeClashBoxMode(string boxMode)
        {
            var value = ClashReportOptionHelper.NormalizeBoxMode(boxMode);
            if (value != null)
                return value;
            throw new AgentCommandException(ErrorCodes.SchemaViolation, "boxMode must be 'point' or 'items'.");
        }

        private static BoundingBox3D BuildClashResultBox(ClashResult result, double offsetMm, string boxMode)
        {
            if (result == null)
                return null;

            var allItems = new ModelItemCollection();
            if (result.Selection1 != null)
                allItems.AddRange(result.Selection1);
            if (result.Selection2 != null)
                allItems.AddRange(result.Selection2);
            if (allItems.Count == 0)
                return null;

            var bbox = allItems.BoundingBox();
            if (bbox == null)
                return null;

            var offsetUnits = SectionBoxHelper.MmToDocUnits(offsetMm);
            if (string.Equals(boxMode, ClashBoxModePoint, StringComparison.OrdinalIgnoreCase))
            {
                var center = result.Center ?? bbox.Center;
                var halfSize = Math.Max(offsetUnits, 0.1);
                return new BoundingBox3D(
                    new Point3D(center.X - halfSize, center.Y - halfSize, center.Z - halfSize),
                    new Point3D(center.X + halfSize, center.Y + halfSize, center.Z + halfSize));
            }

            var itemCenter = bbox.Center;
            var halfX = Math.Max((bbox.Max.X - bbox.Min.X) / 2.0, 0.1) + offsetUnits;
            var halfY = Math.Max((bbox.Max.Y - bbox.Min.Y) / 2.0, 0.1) + offsetUnits;
            var halfZ = Math.Max((bbox.Max.Z - bbox.Min.Z) / 2.0, 0.1) + offsetUnits;
            return new BoundingBox3D(
                new Point3D(itemCenter.X - halfX, itemCenter.Y - halfY, itemCenter.Z - halfZ),
                new Point3D(itemCenter.X + halfX, itemCenter.Y + halfY, itemCenter.Z + halfZ));
        }

        private static ClashReportItem BuildClashReportItem(
            ClashReportWorkItem row,
            int index,
            BoundingBox3D box,
            double boxOffsetMm,
            string boxMode,
            string viewpointName,
            bool viewpointCreated,
            string viewpointPath,
            bool screenshotCaptured,
            string screenshotPath,
            bool topViewScreenshotCaptured = false,
            string topViewScreenshotPath = "",
            int fullBoxTransparencyItemCount = 0,
            string errorMessage = "",
            ClashReportClusterAssignment clusterAssignment = null)
        {
            var result = row.Result;
            var item1 = result == null ? null : result.Item1;
            var item2 = result == null ? null : result.Item2;
            var item1Names = result == null ? new List<string>() : GetClashItemNames(result.Selection1, result.Item1, 1);
            var item2Names = result == null ? new List<string>() : GetClashItemNames(result.Selection2, result.Item2, 1);
            var normalizedBoxMode = NormalizeClashBoxMode(boxMode);
            var actualBox = box ?? BuildClashResultBox(result, boxOffsetMm, normalizedBoxMode);
            return ClashReportResponseFactory.BuildReportItem(new ClashReportItemValues
            {
                Index = index,
                TestIndex = row.TestIndex,
                ResultIndex = row.ResultIndex,
                TestName = row.TestName ?? string.Empty,
                GroupPath = row.GroupPath ?? string.Empty,
                ResultName = row.ResultName ?? string.Empty,
                Status = row.Status ?? string.Empty,
                AssignedTo = row.AssignedTo ?? string.Empty,
                Description = SafeString(() => result.Description),
                Distance = TryGetClashDistance(result),
                BoxOffsetMm = boxOffsetMm,
                BoxMode = normalizedBoxMode,
                ClashPoint = result == null || result.Center == null ? null : ToPoint3Info(result.Center),
                ClashBox = ToBoundingBoxInfo(actualBox),
                Item1Name = item1Names.Count > 0 ? item1Names[0] : string.Empty,
                Item2Name = item2Names.Count > 0 ? item2Names[0] : string.Empty,
                Item1Path = BuildItemPath(item1),
                Item2Path = BuildItemPath(item2),
                Item1ItemCount = result == null ? 0 : GetClashItemCount(result.Selection1, result.Item1),
                Item2ItemCount = result == null ? 0 : GetClashItemCount(result.Selection2, result.Item2),
                ViewpointName = viewpointName ?? string.Empty,
                ViewpointPath = viewpointPath ?? string.Empty,
                ViewpointCreated = viewpointCreated,
                ScreenshotPath = screenshotPath ?? string.Empty,
                ScreenshotCaptured = screenshotCaptured,
                TopViewScreenshotPath = topViewScreenshotPath ?? string.Empty,
                TopViewScreenshotCaptured = topViewScreenshotCaptured,
                FullBoxTransparencyItemCount = fullBoxTransparencyItemCount,
                ClusterIndex = clusterAssignment == null ? 0 : clusterAssignment.ClusterIndex,
                ClusterId = clusterAssignment == null ? string.Empty : clusterAssignment.ClusterId ?? string.Empty,
                ClusterName = clusterAssignment == null ? string.Empty : clusterAssignment.ClusterName ?? string.Empty,
                ErrorMessage = errorMessage ?? string.Empty,
            });
        }

        private static ClashSavedViewpointItem BuildClashSavedViewpointItem(
            ClashReportWorkItem row,
            int index,
            BoundingBoxInfo clashBox,
            double boxOffsetMm,
            string boxMode,
            string viewpointName,
            bool viewpointCreated,
            string viewpointPath,
            int fullBoxTransparencyItemCount,
            string errorMessage = "")
        {
            var result = row == null ? null : row.Result;
            return ClashReportResponseFactory.BuildSavedViewpointItem(new ClashSavedViewpointItemValues
            {
                Index = index,
                TestIndex = row == null ? 0 : row.TestIndex,
                ResultIndex = row == null ? 0 : row.ResultIndex,
                TestName = row == null ? string.Empty : row.TestName ?? string.Empty,
                GroupPath = row == null ? string.Empty : row.GroupPath ?? string.Empty,
                ResultName = row == null ? string.Empty : row.ResultName ?? string.Empty,
                Status = row == null ? string.Empty : row.Status ?? string.Empty,
                AssignedTo = row == null ? string.Empty : row.AssignedTo ?? string.Empty,
                Distance = TryGetClashDistance(result),
                BoxOffsetMm = boxOffsetMm,
                BoxMode = boxMode ?? string.Empty,
                ClashPoint = result == null || result.Center == null ? null : ToPoint3Info(result.Center),
                ClashBox = clashBox,
                Item1Name = GetClashItemName(result == null ? null : result.Item1),
                Item2Name = GetClashItemName(result == null ? null : result.Item2),
                Item1ItemCount = result == null ? 0 : GetClashItemCount(result.Selection1, result.Item1),
                Item2ItemCount = result == null ? 0 : GetClashItemCount(result.Selection2, result.Item2),
                ViewpointName = viewpointName ?? string.Empty,
                ViewpointPath = viewpointPath ?? string.Empty,
                ViewpointCreated = viewpointCreated,
                FullBoxTransparencyItemCount = fullBoxTransparencyItemCount,
                ErrorMessage = errorMessage ?? string.Empty,
            });
        }

        private static double? TryGetClashDistance(ClashResult result)
        {
            if (result == null)
                return null;

            try
            {
                return result.Distance;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildSafeViewpointName(int index, ClashReportWorkItem row)
        {
            return ClashReportResponseFactory.BuildSafeViewpointName(index, row == null ? string.Empty : row.ResultName);
        }

        private static string BuildSafeClusterViewpointName(int index, ClashClusterSummary summary)
        {
            var left = summary == null ? string.Empty : summary.DisplayNameA;
            var right = summary == null ? string.Empty : summary.DisplayNameB;
            return ClashReportResponseFactory.BuildSafeViewpointName(index, "Cluster " + left + " - " + right);
        }

        private static void ApplyClusterArtifactResult(ClashClusterSummary summary, ClashReportItemProcessingResult processed)
        {
            if (summary == null || processed == null)
                return;

            summary.ArtifactBox = ToBoundingBoxInfo(processed.Box);
            summary.ViewpointName = processed.ViewpointName ?? string.Empty;
            summary.ViewpointPath = processed.ViewpointPath ?? string.Empty;
            summary.ViewpointCreated = processed.ViewpointCreated;
            summary.ScreenshotPath = processed.ScreenshotPath ?? string.Empty;
            summary.ScreenshotCaptured = processed.ScreenshotCaptured;
            summary.TopViewScreenshotPath = processed.TopViewScreenshotPath ?? string.Empty;
            summary.TopViewScreenshotCaptured = processed.TopViewScreenshotCaptured;
            summary.FullBoxTransparencyItemCount = processed.FullBoxTransparencyItemCount;
            summary.ArtifactErrorMessage = processed.ErrorMessage ?? string.Empty;
        }

        private static ClashReportScreenshotOptions NormalizeScreenshotExportOptions(ClashGenerateReportRequest request)
        {
            string errorMessage;
            var options = ClashReportOptionHelper.NormalizeScreenshotOptions(
                request == null ? null : request.ScreenshotProfile,
                request == null ? null : request.ScreenshotFormat,
                request == null ? null : request.ScreenshotMaxWidth,
                request == null ? null : request.ScreenshotMaxHeight,
                request == null ? null : request.ScreenshotJpegQuality,
                out errorMessage);

            if (options == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, errorMessage);

            return options;
        }
    }
}
