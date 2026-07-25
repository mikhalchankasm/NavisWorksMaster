using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal static class ClashReportScreenshotSetService
    {
        public static ClashReportScreenshotSetResult Capture(
            Document document,
            ClashResult result,
            BoundingBox3D box,
            int globalIndex,
            string imagesDirectory,
            ClashReportScreenshotOptions screenshotOptions,
            bool captureTopViewScreenshots,
            bool includeClashPointMarker)
        {
            return CaptureCore(
                document,
                result == null ? null : result.Center,
                box,
                globalIndex,
                imagesDirectory,
                screenshotOptions,
                captureTopViewScreenshots,
                includeClashPointMarker,
                false);
        }

        public static ClashReportScreenshotSetResult CaptureCluster(
            Document document,
            Point3D markerPoint,
            BoundingBox3D box,
            int globalIndex,
            string imagesDirectory,
            ClashReportScreenshotOptions screenshotOptions,
            bool captureTopViewScreenshots,
            bool includeClashPointMarker)
        {
            return CaptureCore(
                document,
                markerPoint,
                box,
                globalIndex,
                imagesDirectory,
                screenshotOptions,
                captureTopViewScreenshots,
                includeClashPointMarker,
                true);
        }

        private static ClashReportScreenshotSetResult CaptureCore(
            Document document,
            Point3D markerPoint,
            BoundingBox3D box,
            int globalIndex,
            string imagesDirectory,
            ClashReportScreenshotOptions screenshotOptions,
            bool captureTopViewScreenshots,
            bool includeClashPointMarker,
            bool clusterArtifact)
        {
            var capture = new ClashReportScreenshotSetResult();
            var fileName = clusterArtifact
                ? ClashReportOutputHelper.BuildClusterScreenshotFileName(globalIndex, screenshotOptions.FileExtension, false)
                : ClashReportOutputHelper.BuildScreenshotFileName(globalIndex, screenshotOptions.FileExtension, false);
            var absoluteScreenshotPath = ClashReportOutputHelper.BuildScreenshotAbsolutePath(imagesDirectory, fileName);
            string warning;
            if (ClashReportScreenshotCaptureService.TryCaptureCurrentViewImage(absoluteScreenshotPath, screenshotOptions, out warning))
            {
                capture.ScreenshotPath = ClashReportOutputHelper.BuildScreenshotRelativePath(fileName);
                capture.ScreenshotCaptured = true;
                capture.CapturedCount++;
            }
            else if (!string.IsNullOrWhiteSpace(warning))
            {
                capture.Warnings.Add("Screenshot " + globalIndex + ": " + warning);
            }

            if (!captureTopViewScreenshots || box == null)
                return capture;

            if (includeClashPointMarker)
                ClashReportMarkerViewService.ClearActiveViewRedlines(document);

            ClashReportMarkerViewService.ApplyTopViewToBox(document, box);
            if (includeClashPointMarker && markerPoint != null)
            {
                string markerWarning;
                if (!ClashReportMarkerViewService.TrySetClashPointMarker(document, markerPoint, out markerWarning) && !string.IsNullOrWhiteSpace(markerWarning))
                    capture.Warnings.Add("Clash " + globalIndex + " top marker: " + markerWarning);
            }

            var topFileName = clusterArtifact
                ? ClashReportOutputHelper.BuildClusterScreenshotFileName(globalIndex, screenshotOptions.FileExtension, true)
                : ClashReportOutputHelper.BuildScreenshotFileName(globalIndex, screenshotOptions.FileExtension, true);
            var absoluteTopScreenshotPath = ClashReportOutputHelper.BuildScreenshotAbsolutePath(imagesDirectory, topFileName);
            if (ClashReportScreenshotCaptureService.TryCaptureCurrentViewImage(absoluteTopScreenshotPath, screenshotOptions, out warning))
            {
                capture.TopViewScreenshotPath = ClashReportOutputHelper.BuildScreenshotRelativePath(topFileName);
                capture.TopViewScreenshotCaptured = true;
                capture.CapturedCount++;
            }
            else if (!string.IsNullOrWhiteSpace(warning))
            {
                capture.Warnings.Add("Top screenshot " + globalIndex + ": " + warning);
            }

            return capture;
        }
    }

    internal sealed class ClashReportScreenshotSetResult
    {
        public string ScreenshotPath = string.Empty;
        public bool ScreenshotCaptured;
        public string TopViewScreenshotPath = string.Empty;
        public bool TopViewScreenshotCaptured;
        public int CapturedCount;
        public readonly List<string> Warnings = new List<string>();
    }
}
