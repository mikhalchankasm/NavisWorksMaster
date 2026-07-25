using System;
using System.Globalization;
using System.IO;
using System.Linq;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal static class ClashReportResponseFactory
    {
        public static ClashReportItem BuildReportItem(ClashReportItemValues values)
        {
            values = values ?? new ClashReportItemValues();
            return new ClashReportItem
            {
                Index = values.Index,
                TestIndex = values.TestIndex,
                ResultIndex = values.ResultIndex,
                TestName = values.TestName ?? string.Empty,
                GroupPath = values.GroupPath ?? string.Empty,
                ResultName = values.ResultName ?? string.Empty,
                Status = values.Status ?? string.Empty,
                AssignedTo = values.AssignedTo ?? string.Empty,
                Description = values.Description ?? string.Empty,
                Distance = values.Distance,
                BoxOffsetMm = values.BoxOffsetMm,
                BoxMode = values.BoxMode ?? string.Empty,
                ClashPoint = values.ClashPoint,
                ClashBox = values.ClashBox,
                Item1Name = values.Item1Name ?? string.Empty,
                Item2Name = values.Item2Name ?? string.Empty,
                Item1Path = values.Item1Path ?? string.Empty,
                Item2Path = values.Item2Path ?? string.Empty,
                Item1ItemCount = values.Item1ItemCount,
                Item2ItemCount = values.Item2ItemCount,
                ViewpointName = values.ViewpointName ?? string.Empty,
                ViewpointPath = values.ViewpointPath ?? string.Empty,
                ViewpointCreated = values.ViewpointCreated,
                ScreenshotPath = values.ScreenshotPath ?? string.Empty,
                ScreenshotCaptured = values.ScreenshotCaptured,
                TopViewScreenshotPath = values.TopViewScreenshotPath ?? string.Empty,
                TopViewScreenshotCaptured = values.TopViewScreenshotCaptured,
                FullBoxTransparencyItemCount = values.FullBoxTransparencyItemCount,
                ClusterIndex = values.ClusterIndex,
                ClusterId = values.ClusterId ?? string.Empty,
                ClusterName = values.ClusterName ?? string.Empty,
                ErrorMessage = values.ErrorMessage ?? string.Empty,
            };
        }

        public static ClashSavedViewpointItem BuildSavedViewpointItem(ClashSavedViewpointItemValues values)
        {
            values = values ?? new ClashSavedViewpointItemValues();
            return new ClashSavedViewpointItem
            {
                Index = values.Index,
                TestIndex = values.TestIndex,
                ResultIndex = values.ResultIndex,
                TestName = values.TestName ?? string.Empty,
                GroupPath = values.GroupPath ?? string.Empty,
                ResultName = values.ResultName ?? string.Empty,
                Status = values.Status ?? string.Empty,
                AssignedTo = values.AssignedTo ?? string.Empty,
                Distance = values.Distance,
                BoxOffsetMm = values.BoxOffsetMm,
                BoxMode = values.BoxMode ?? string.Empty,
                ClashPoint = values.ClashPoint,
                ClashBox = values.ClashBox,
                Item1Name = values.Item1Name ?? string.Empty,
                Item2Name = values.Item2Name ?? string.Empty,
                Item1ItemCount = values.Item1ItemCount,
                Item2ItemCount = values.Item2ItemCount,
                ViewpointName = values.ViewpointName ?? string.Empty,
                ViewpointPath = values.ViewpointPath ?? string.Empty,
                ViewpointCreated = values.ViewpointCreated,
                FullBoxTransparencyItemCount = values.FullBoxTransparencyItemCount,
                ErrorMessage = values.ErrorMessage ?? string.Empty,
            };
        }

        public static string BuildSafeViewpointName(int index, string resultName)
        {
            var raw = index.ToString("0000", CultureInfo.InvariantCulture) + " " + (resultName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(resultName))
                raw = index.ToString("0000", CultureInfo.InvariantCulture) + " Clash";

            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(raw.Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch).ToArray()).Trim();
            return sanitized.Length > 80 ? sanitized.Substring(0, 80).Trim() : sanitized;
        }
    }

    internal sealed class ClashReportItemValues
    {
        public int Index;
        public int TestIndex;
        public int ResultIndex;
        public string TestName;
        public string GroupPath;
        public string ResultName;
        public string Status;
        public string AssignedTo;
        public string Description;
        public double? Distance;
        public double BoxOffsetMm;
        public string BoxMode;
        public Point3Info ClashPoint;
        public BoundingBoxInfo ClashBox;
        public string Item1Name;
        public string Item2Name;
        public string Item1Path;
        public string Item2Path;
        public int Item1ItemCount;
        public int Item2ItemCount;
        public string ViewpointName;
        public string ViewpointPath;
        public bool ViewpointCreated;
        public string ScreenshotPath;
        public bool ScreenshotCaptured;
        public string TopViewScreenshotPath;
        public bool TopViewScreenshotCaptured;
        public int FullBoxTransparencyItemCount;
        public int ClusterIndex;
        public string ClusterId;
        public string ClusterName;
        public string ErrorMessage;
    }

    internal sealed class ClashSavedViewpointItemValues
    {
        public int Index;
        public int TestIndex;
        public int ResultIndex;
        public string TestName;
        public string GroupPath;
        public string ResultName;
        public string Status;
        public string AssignedTo;
        public double? Distance;
        public double BoxOffsetMm;
        public string BoxMode;
        public Point3Info ClashPoint;
        public BoundingBoxInfo ClashBox;
        public string Item1Name;
        public string Item2Name;
        public int Item1ItemCount;
        public int Item2ItemCount;
        public string ViewpointName;
        public string ViewpointPath;
        public bool ViewpointCreated;
        public int FullBoxTransparencyItemCount;
        public string ErrorMessage;
    }
}
