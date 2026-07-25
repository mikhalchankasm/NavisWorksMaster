using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal static class ClashReportItemProcessingService
    {
        public static ClashReportItemProcessingResult Process<TWorkItem>(
            Document document,
            TWorkItem row,
            int globalIndex,
            ClashDocumentStateSnapshot originalState,
            ClashPreviewManager preview,
            bool createViewpoints,
            ClashSavedViewpointTarget targetViewpointFolder,
            double offsetMm,
            string boxMode,
            bool useFullBoxTransparency,
            double contextTransparency,
            bool captureScreenshots,
            string imagesDirectory,
            ClashReportScreenshotOptions screenshotOptions,
            bool captureTopViewScreenshots,
            bool includeClashPointMarker,
            Func<TWorkItem, ClashResult> getResult,
            Func<ClashResult, double, string, BoundingBox3D> buildClashResultBox,
            Func<int, TWorkItem, string> buildSafeViewpointName)
        {
            var processed = new ClashReportItemProcessingResult();
            var clashResult = getResult == null ? null : getResult(row);

            try
            {
                ClashDocumentStateService.RestoreViewpoint(document, originalState);

                preview.ShowClashResult(clashResult);
                var box = buildClashResultBox == null ? null : buildClashResultBox(clashResult, offsetMm, boxMode);
                box = box ?? preview.LastExpandedBox;
                processed.Box = box;
                if (box != null)
                {
                    ViewpointCameraHelper.ApplyIso1ViewToBox(document, box, clashResult == null ? null : clashResult.Center);
                    SectionBoxHelper.SetSectionBox(box);
                }

                if (includeClashPointMarker && clashResult != null && clashResult.Center != null)
                {
                    string markerWarning;
                    if (!ClashReportMarkerViewService.TrySetClashPointMarker(document, clashResult.Center, out markerWarning) && !string.IsNullOrWhiteSpace(markerWarning))
                        processed.Warnings.Add("Clash " + globalIndex + " marker: " + markerWarning);
                }

                if (createViewpoints && targetViewpointFolder != null && targetViewpointFolder.Folder != null)
                {
                    var savedViewpoint = ClashSavedViewpointCreationService.SaveCurrentViewpointWithAppearance(
                        document,
                        targetViewpointFolder,
                        buildSafeViewpointName == null ? string.Empty : buildSafeViewpointName(globalIndex, row));
                    processed.ViewpointName = savedViewpoint.ViewpointName;
                    processed.ViewpointPath = savedViewpoint.ViewpointPath;
                    processed.ViewpointCreated = savedViewpoint.Created;
                    processed.CreatedViewpointCount++;
                }

                if (useFullBoxTransparency && contextTransparency > 0 && box != null)
                {
                    processed.FullBoxTransparencyItemCount = preview.ApplyClashRootContextTransparency();
                }

                if (captureScreenshots)
                {
                    var screenshotSet = ClashReportScreenshotSetService.Capture(
                        document,
                        clashResult,
                        box,
                        globalIndex,
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
                processed.Box = null;
                processed.Warnings.Add("Clash " + globalIndex + " failed: " + ex.Message);
            }

            return processed;
        }
    }

    internal sealed class ClashReportItemProcessingResult
    {
        public BoundingBox3D Box;
        public string ViewpointName = string.Empty;
        public string ViewpointPath = string.Empty;
        public bool ViewpointCreated;
        public int CreatedViewpointCount;
        public string ScreenshotPath = string.Empty;
        public bool ScreenshotCaptured;
        public string TopViewScreenshotPath = string.Empty;
        public bool TopViewScreenshotCaptured;
        public int ScreenshotCount;
        public int FullBoxTransparencyItemCount;
        public string ErrorMessage = string.Empty;
        public readonly List<string> Warnings = new List<string>();
    }
}
