using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal static class ClashReportClusterProcessingService
    {
        public static ClashReportItemProcessingResult Process<TWorkItem>(
            Document document,
            IList<TWorkItem> rows,
            ClashClusterSummary summary,
            int clusterIndex,
            ClashDocumentStateSnapshot originalState,
            ClashPreviewManager preview,
            bool createViewpoints,
            ClashSavedViewpointTarget targetViewpointFolder,
            bool useFullBoxTransparency,
            double contextTransparency,
            bool captureScreenshots,
            string imagesDirectory,
            ClashReportScreenshotOptions screenshotOptions,
            bool captureTopViewScreenshots,
            bool includeClashPointMarker,
            Func<TWorkItem, ClashResult> getResult,
            Func<int, ClashClusterSummary, string> buildSafeViewpointName)
        {
            var processed = new ClashReportItemProcessingResult();
            var results = (rows ?? new List<TWorkItem>())
                .Select(item => getResult == null ? null : getResult(item))
                .Where(result => result != null)
                .ToList();

            try
            {
                ClashDocumentStateService.RestoreViewpoint(document, originalState);
                preview.ShowClashResults(results, BuildDisplayName(summary));
                if (!preview.LastSuccess)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(preview.LastStatus) ? "Cluster preview could not be prepared." : preview.LastStatus);

                processed.Box = preview.LastExpandedBox;
                var markerPoint = preview.LastClashCenter;
                if (includeClashPointMarker && markerPoint != null)
                {
                    string markerWarning;
                    if (!ClashReportMarkerViewService.TrySetClashPointMarker(document, markerPoint, out markerWarning) && !string.IsNullOrWhiteSpace(markerWarning))
                        processed.Warnings.Add("Cluster " + clusterIndex + " marker: " + markerWarning);
                }

                if (createViewpoints && targetViewpointFolder != null && targetViewpointFolder.Folder != null)
                {
                    var savedViewpoint = ClashSavedViewpointCreationService.SaveCurrentViewpointWithAppearance(
                        document,
                        targetViewpointFolder,
                        buildSafeViewpointName == null ? string.Empty : buildSafeViewpointName(clusterIndex, summary));
                    processed.ViewpointName = savedViewpoint.ViewpointName;
                    processed.ViewpointPath = savedViewpoint.ViewpointPath;
                    processed.ViewpointCreated = savedViewpoint.Created;
                    processed.CreatedViewpointCount++;
                }

                if (useFullBoxTransparency && contextTransparency > 0 && processed.Box != null)
                    processed.FullBoxTransparencyItemCount = preview.ApplyClashRootContextTransparency();

                if (captureScreenshots)
                {
                    var screenshotSet = ClashReportScreenshotSetService.CaptureCluster(
                        document,
                        markerPoint,
                        processed.Box,
                        clusterIndex,
                        imagesDirectory,
                        screenshotOptions,
                        captureTopViewScreenshots,
                        includeClashPointMarker);
                    processed.ScreenshotPath = screenshotSet.ScreenshotPath;
                    processed.ScreenshotCaptured = screenshotSet.ScreenshotCaptured;
                    processed.TopViewScreenshotPath = screenshotSet.TopViewScreenshotPath;
                    processed.TopViewScreenshotCaptured = screenshotSet.TopViewScreenshotCaptured;
                    processed.ScreenshotCount += screenshotSet.CapturedCount;
                    processed.Warnings.AddRange(screenshotSet.Warnings);
                }

                if (includeClashPointMarker)
                    ClashReportMarkerViewService.ClearActiveViewRedlines(document);
            }
            catch (Exception ex)
            {
                if (includeClashPointMarker)
                    ClashReportMarkerViewService.ClearActiveViewRedlines(document);
                processed.ErrorMessage = ex.Message;
                processed.Warnings.Add("Cluster " + clusterIndex + " failed: " + ex.Message);
            }

            return processed;
        }

        private static string BuildDisplayName(ClashClusterSummary summary)
        {
            if (summary == null)
                return "Clash cluster";

            var left = string.IsNullOrWhiteSpace(summary.DisplayNameA) ? "A" : summary.DisplayNameA;
            var right = string.IsNullOrWhiteSpace(summary.DisplayNameB) ? "B" : summary.DisplayNameB;
            return "Cluster " + summary.Index + ": " + left + " / " + right;
        }
    }
}
