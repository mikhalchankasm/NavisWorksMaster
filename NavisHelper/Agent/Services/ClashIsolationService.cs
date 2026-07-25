using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed class ClashIsolationService
    {
        private Document _document;
        private ClashIsolationDocumentIdentity _documentIdentity;
        private ClashPreviewManager _preview;
        private ClashDocumentStateSnapshot _originalState;
        private bool _hasActiveIsolation;

        public ClashIsolateResultResponse Isolate(Document document, ClashIsolateResultRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            DiscardStaleState(document);
            request = request ?? new ClashIsolateResultRequest();
            var resultHandle = (request.ResultHandle ?? string.Empty).Trim();
            int testIndex;
            int resultIndex;
            if (!ClashHandleHelper.TryParseResultHandle(resultHandle, out testIndex, out resultIndex))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "resultHandle must be a clash result handle such as clash-result:1:1.");

            var clash = document.GetClash();
            if (clash == null)
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Clash Detective is not available for the active document.");
            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            if (testIndex < 1 || testIndex > tests.Count)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Clash test was not found for resultHandle: " + resultHandle);

            var test = tests[testIndex - 1];
            string groupPath;
            var result = ResolveResult(test, resultIndex, out groupPath);
            if (result == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Clash result was not found for resultHandle: " + resultHandle);

            var boxMode = ClashIsolationOptionHelper.NormalizeBoxMode(request.BoxMode);
            if (boxMode == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "boxMode must be point or items.");
            var cameraMode = ClashIsolationOptionHelper.NormalizeCameraMode(request.CameraMode);
            if (cameraMode == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "cameraMode must be current, iso, iso_opposite, top, front, back, left, right, or custom.");
            var projection = ClashIsolationOptionHelper.NormalizeProjection(request.Projection);
            if (projection == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "projection must be current, orthographic, or perspective.");
            if (cameraMode == ClashIsolationOptionHelper.CameraCustom &&
                !ClashIsolationOptionHelper.IsFinitePoint(request.CameraPosition))
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "cameraPosition with finite X, Y, and Z is required when cameraMode=custom.");
            }
            if (request.CameraTarget != null && !ClashIsolationOptionHelper.IsFinitePoint(request.CameraTarget))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "cameraTarget must contain finite X, Y, and Z values.");
            if (request.CameraUp != null && !ClashIsolationOptionHelper.IsFinitePoint(request.CameraUp))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "cameraUp must contain finite X, Y, and Z values.");
            if (cameraMode == ClashIsolationOptionHelper.CameraCustom)
                ValidateCustomCamera(request, result.Center);

            var offsetMm = request.BoxOffsetMm.GetValueOrDefault(1000);
            if (!ClashIsolationOptionHelper.IsValidBoxOffset(boxMode, offsetMm))
            {
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    boxMode == ClashIsolationOptionHelper.BoxModeItems
                        ? "boxOffsetMm must be a finite number greater than or equal to 0 for boxMode=items."
                        : "boxOffsetMm must be a finite number greater than 0 for boxMode=point.");
            }
            var contextTransparency = request.ContextTransparency.GetValueOrDefault(0.7);
            if (double.IsNaN(contextTransparency) || double.IsInfinity(contextTransparency) ||
                contextTransparency < 0 || contextTransparency > 1)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "contextTransparency must be between 0 and 1.");
            }

            var plannedBox = ClashPreviewManager.PlanClashBox(result, offsetMm, boxMode);
            var response = new ClashIsolateResultResponse
            {
                Apply = request.Apply == true,
                ResultHandle = ClashHandleHelper.BuildResultHandle(testIndex, resultIndex),
                TestHandle = ClashHandleHelper.BuildTestHandle(testIndex),
                TestName = test.DisplayName ?? string.Empty,
                ResultName = result.DisplayName ?? string.Empty,
                GroupPath = groupPath ?? string.Empty,
                BoxMode = boxMode,
                BoxOffsetMm = offsetMm,
                UseSectionBox = request.UseSectionBox.GetValueOrDefault(true),
                IsolatePair = request.IsolatePair.GetValueOrDefault(false),
                CameraMode = cameraMode,
                Projection = projection,
                ClashPoint = ToPointInfo(result.Center),
                ClashBox = ToBoundingBoxInfo(plannedBox),
                Item1Name = ClashItemNameService.GetFirstName(result.Selection1, result.Item1),
                Item2Name = ClashItemNameService.GetFirstName(result.Selection2, result.Item2),
                ScreenshotPath = NormalizeOptionalScreenshotPath(request.ScreenshotPath),
                CanReset = HasActiveIsolationFor(document),
            };
            var screenshotOptions = ValidateOptionalScreenshot(request, response.ScreenshotPath);

            if (!response.Apply)
            {
                response.Message = "Dry-run only. Pass apply=true to isolate the clash result.";
                return response;
            }

            EnsureDocumentState(document);
            if (!_hasActiveIsolation)
            {
                _originalState = ClashDocumentStateService.Capture(document, "MCP clash isolation");
                if (!string.IsNullOrWhiteSpace(_originalState.Warning))
                {
                    var warning = _originalState.Warning;
                    _originalState = null;
                    throw new AgentCommandException(
                        ErrorCodes.CommandFailed,
                        "Clash isolation was not applied because the original view state could not be captured safely: " + warning);
                }
            }
            var preview = _preview;
            var originalProjection = document.CurrentViewpoint == null
                ? ViewpointProjection.Perspective
                : document.CurrentViewpoint.CreateCopy().Projection;
            preview.OffsetMm = offsetMm;
            preview.BoxMode = boxMode;
            preview.UseSectionBox = response.UseSectionBox;
            preview.UsePairIsolation = response.IsolatePair;
            preview.UseContextTransparency = request.UseContextTransparency.GetValueOrDefault(false);
            preview.ContextTransparency = contextTransparency;
            preview.UseFixedIsoView = false;
            preview.ColorA = ParseColor(request.ColorAHex, Autodesk.Navisworks.Api.Color.FromByteRGB(255, 38, 38), "colorAHex");
            preview.ColorB = ParseColor(request.ColorBHex, Autodesk.Navisworks.Api.Color.FromByteRGB(38, 102, 255), "colorBHex");
            try
            {
                preview.ShowClashResult(result);
                if (!preview.LastSuccess)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, preview.LastStatus);
                if (cameraMode != ClashIsolationOptionHelper.CameraCurrent && preview.LastExpandedBox == null)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "The requested camera could not be framed because the clash has no usable bounding box.");
                if (cameraMode != ClashIsolationOptionHelper.CameraCurrent)
                {
                    var cameraProjection = ResolveCameraProjection(cameraMode, projection, originalProjection);
                    ClashIsolationCameraService.Apply(
                        document,
                        preview.LastExpandedBox,
                        preview.LastClashCenter,
                        cameraMode,
                        request.CameraPosition,
                        request.CameraTarget,
                        request.CameraUp,
                        cameraProjection);
                }
                else
                {
                    ClashIsolationCameraService.ApplyProjection(document, projection);
                }

                _hasActiveIsolation = true;
                response.Applied = true;
                response.CanReset = true;
                response.ClashBox = ToBoundingBoxInfo(preview.LastExpandedBox);
                response.HiddenBranchCount = preview.LastPairIsolationHiddenBranchCount;
                response.IsolationElapsedMilliseconds = preview.LastPairIsolationElapsedMilliseconds;
                response.IsolationStatus = preview.LastPairIsolationStatus ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(response.ScreenshotPath))
                    CaptureScreenshot(response, screenshotOptions, request.OverwriteScreenshot == true);

                response.Message = response.ScreenshotCaptured
                    ? "Clash result isolated and screenshot captured. Use clash_reset_isolation to restore the original view."
                    : "Clash result isolated. Use clash_reset_isolation to restore the original view.";
                return response;
            }
            catch
            {
                RestoreOriginalState(document, response.Warnings);
                _hasActiveIsolation = false;
                throw;
            }
        }

        public ClashResetIsolationResponse Reset(Document document, ClashResetIsolationRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            DiscardStaleState(document);
            request = request ?? new ClashResetIsolationRequest();
            var response = new ClashResetIsolationResponse
            {
                Apply = request.Apply == true,
                HadActiveIsolation = HasActiveIsolationFor(document),
            };

            if (!response.Apply)
            {
                response.Message = response.HadActiveIsolation
                    ? "Dry-run only. Pass apply=true to restore the original view."
                    : "There is no active MCP clash isolation in this document.";
                return response;
            }

            if (!response.HadActiveIsolation)
            {
                response.Message = "There is no active MCP clash isolation in this document.";
                return response;
            }

            RestoreOriginalState(document, response.Warnings);
            _hasActiveIsolation = false;
            response.Reset = true;
            response.Message = "Original viewpoint, section box, appearance overrides, and temporary pair visibility were restored.";
            return response;
        }

        public void ResetForDocumentChange(Document document)
        {
            if (!_hasActiveIsolation || !HasActiveIsolationFor(document))
            {
                ClearState();
                return;
            }

            var warnings = new List<string>();
            try
            {
                RestoreOriginalState(document, warnings);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to restore MCP clash isolation before document change: " + ex, "ClashMcp");
            }
            foreach (var warning in warnings)
                Logger.Error(warning, "ClashMcp");
            ClearState();
        }

        public void DiscardForDocumentChange()
        {
            ClearState();
        }

        public void HandleDocumentFileNameChanged(Document document)
        {
            if (!_hasActiveIsolation)
            {
                ClearState();
                return;
            }

            if (_documentIdentity != null && _documentIdentity.HasSameModelContent(document))
            {
                // Save As changes the path without replacing the active model graph.
                // File -> Open may also raise FileNameChanged before swapping the
                // models; command-time identity validation remains the backstop.
                // Keep reset available here and refresh the stored path component.
                _documentIdentity = ClashIsolationDocumentIdentity.Capture(document);
                return;
            }

            // File -> Open can reuse the same managed Document wrapper. At this
            // point restoring the previous snapshot would risk touching the new
            // model, so stale state must only be discarded.
            ClearState();
        }

        public CaptureCurrentViewResponse CaptureCurrentView(Document document, CaptureCurrentViewRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            request = request ?? new CaptureCurrentViewRequest();
            var outputPath = NormalizeRequiredScreenshotPath(request.OutputPath);
            var format = string.IsNullOrWhiteSpace(request.ScreenshotFormat)
                ? Path.GetExtension(outputPath).TrimStart('.')
                : request.ScreenshotFormat;
            string optionError;
            var options = ClashReportOptionHelper.NormalizeScreenshotOptions(
                request.ScreenshotProfile,
                format,
                request.ScreenshotMaxWidth,
                request.ScreenshotMaxHeight,
                request.ScreenshotJpegQuality,
                out optionError);
            if (options == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, optionError);
            EnsureScreenshotFormatMatchesPath(outputPath, options.Format);

            var response = new CaptureCurrentViewResponse
            {
                Apply = request.Apply == true,
                OutputPath = outputPath,
                ScreenshotProfile = options.Profile,
                ScreenshotFormat = options.Format,
                ScreenshotMaxWidth = options.MaxWidth,
                ScreenshotMaxHeight = options.MaxHeight,
                ScreenshotJpegQuality = options.JpegQuality,
            };
            if (File.Exists(outputPath) && request.Overwrite != true)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Output file already exists. Pass overwrite=true to replace it.");
            if (!response.Apply)
            {
                response.Message = "Dry-run only. Pass apply=true to capture the current Navisworks view.";
                return response;
            }

            string warning;
            if (!TryCaptureImageAtomically(document, outputPath, options, request.Overwrite == true, out warning))
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Current view screenshot failed: " + warning);

            response.Captured = true;
            response.FileSizeBytes = new FileInfo(outputPath).Length;
            response.Message = "Current Navisworks view captured.";
            return response;
        }

        private void EnsureDocumentState(Document document)
        {
            if (_preview != null &&
                _documentIdentity != null &&
                _documentIdentity.Matches(document))
                return;

            ClearState();
            _document = document;
            _documentIdentity = ClashIsolationDocumentIdentity.Capture(document);
            _preview = new ClashPreviewManager();
        }

        private bool HasActiveIsolationFor(Document document)
        {
            return _hasActiveIsolation &&
                   _documentIdentity != null &&
                   _documentIdentity.Matches(document);
        }

        private void DiscardStaleState(Document document)
        {
            if (_hasActiveIsolation && !HasActiveIsolationFor(document))
                ClearState();
        }

        private void ClearState()
        {
            _document = null;
            _documentIdentity = null;
            _preview = null;
            _originalState = null;
            _hasActiveIsolation = false;
        }

        private static ClashResult ResolveResult(ClashTest test, int requestedIndex, out string groupPath)
        {
            groupPath = string.Empty;
            if (test == null || requestedIndex < 1)
                return null;
            var currentIndex = 0;
            return ResolveResult(test.Children, requestedIndex, string.Empty, ref currentIndex, out groupPath);
        }

        private static ClashResult ResolveResult(
            IEnumerable<SavedItem> children,
            int requestedIndex,
            string parentPath,
            ref int currentIndex,
            out string groupPath)
        {
            groupPath = string.Empty;
            if (children == null)
                return null;

            foreach (var child in children)
            {
                var result = child as ClashResult;
                if (result != null)
                {
                    currentIndex++;
                    if (currentIndex == requestedIndex)
                    {
                        groupPath = parentPath;
                        return result;
                    }
                    continue;
                }

                var group = child as ClashResultGroup;
                if (group == null)
                    continue;
                var nextPath = string.IsNullOrWhiteSpace(parentPath)
                    ? group.DisplayName ?? string.Empty
                    : parentPath + "/" + (group.DisplayName ?? string.Empty);
                var nested = ResolveResult(group.Children, requestedIndex, nextPath, ref currentIndex, out groupPath);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        private static Autodesk.Navisworks.Api.Color ParseColor(
            string value,
            Autodesk.Navisworks.Api.Color defaultColor,
            string argumentName)
        {
            if (string.IsNullOrWhiteSpace(value))
                return defaultColor;
            try
            {
                var parsed = ColorParser.ParseColor(value);
                return Autodesk.Navisworks.Api.Color.FromByteRGB(parsed.R, parsed.G, parsed.B);
            }
            catch (Exception ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, argumentName + " is invalid: " + ex.Message);
            }
        }

        private static string NormalizeOptionalScreenshotPath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizeRequiredScreenshotPath(value);
        }

        private static string NormalizeRequiredScreenshotPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "outputPath is required.");
            if (!Path.IsPathRooted(value))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Screenshot output path must be absolute.");
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(value);
            }
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is PathTooLongException)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Screenshot output path is invalid: " + ex.Message);
            }
            var extension = (Path.GetExtension(fullPath) ?? string.Empty).TrimStart('.').ToLowerInvariant();
            if (extension != "png" && extension != "jpg" && extension != "jpeg" && extension != "bmp")
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Screenshot path must use .png, .jpg, .jpeg, or .bmp.");
            return fullPath;
        }

        private static ClashReportScreenshotOptions ValidateOptionalScreenshot(
            ClashIsolateResultRequest request,
            string screenshotPath)
        {
            if (string.IsNullOrWhiteSpace(screenshotPath))
                return null;
            if (File.Exists(screenshotPath) && request.OverwriteScreenshot != true)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Screenshot file already exists. Pass overwriteScreenshot=true to replace it.");
            var format = string.IsNullOrWhiteSpace(request.ScreenshotFormat)
                ? Path.GetExtension(screenshotPath).TrimStart('.')
                : request.ScreenshotFormat;
            string optionError;
            var options = ClashReportOptionHelper.NormalizeScreenshotOptions(
                request.ScreenshotProfile,
                format,
                request.ScreenshotMaxWidth,
                request.ScreenshotMaxHeight,
                request.ScreenshotJpegQuality,
                out optionError);
            if (options == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, optionError);
            EnsureScreenshotFormatMatchesPath(screenshotPath, options.Format);
            return options;
        }

        private static void CaptureScreenshot(
            ClashIsolateResultResponse response,
            ClashReportScreenshotOptions options,
            bool overwrite)
        {
            string warning;
            var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (TryCaptureImageAtomically(document, response.ScreenshotPath, options, overwrite, out warning))
                response.ScreenshotCaptured = true;
            else if (!string.IsNullOrWhiteSpace(warning))
                response.Warnings.Add("Screenshot was not captured: " + warning);
        }

        private void RestoreOriginalState(Document document, ICollection<string> warnings)
        {
            if (_preview != null)
            {
                try { _preview.ResetView(); }
                catch (Exception ex) { warnings?.Add("Could not reset clash preview overrides: " + ex.Message); }
            }

            try
            {
                var warning = ClashDocumentStateService.RestoreAll(document, _originalState, "MCP clash isolation");
                if (!string.IsNullOrWhiteSpace(warning))
                    warnings?.Add(warning);
            }
            catch (Exception ex)
            {
                warnings?.Add("Could not restore original clash isolation state: " + ex.Message);
                Logger.Error("Could not restore original clash isolation state: " + ex, "ClashMcp");
            }
            finally
            {
                _originalState = null;
            }
        }

        private static bool TryCaptureImageAtomically(
            Document document,
            string outputPath,
            ClashReportScreenshotOptions options,
            bool overwrite,
            out string warning)
        {
            warning = string.Empty;
            var directory = Path.GetDirectoryName(outputPath);
            var extension = Path.GetExtension(outputPath);
            var temporaryPath = Path.Combine(
                directory ?? string.Empty,
                "." + Path.GetFileNameWithoutExtension(outputPath) + "." + Guid.NewGuid().ToString("N") + ".tmp" + extension);
            try
            {
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                try { document?.ActiveView?.RequestDelayedRedraw(ViewRedrawRequests.All); } catch { }
                if (!ClashReportScreenshotCaptureService.TryCaptureCurrentViewImage(temporaryPath, options, out warning))
                    return false;

                if (File.Exists(outputPath))
                {
                    if (!overwrite)
                    {
                        warning = "Output file appeared during capture and overwrite is false.";
                        return false;
                    }
                    File.Replace(temporaryPath, outputPath, null);
                }
                else
                {
                    File.Move(temporaryPath, outputPath);
                }
                return true;
            }
            catch (Exception ex)
            {
                warning = ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch { }
            }
        }

        private static void EnsureScreenshotFormatMatchesPath(string path, string format)
        {
            var extension = (Path.GetExtension(path) ?? string.Empty).TrimStart('.').ToLowerInvariant();
            if (extension == "jpeg")
                extension = "jpg";
            if (!string.Equals(extension, format, StringComparison.OrdinalIgnoreCase))
            {
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    "screenshotFormat must match the output path extension.");
            }
        }

        private static void ValidateCustomCamera(ClashIsolateResultRequest request, Point3D defaultTarget)
        {
            var position = request.CameraPosition;
            var target = request.CameraTarget == null
                ? defaultTarget
                : new Point3D(request.CameraTarget.X, request.CameraTarget.Y, request.CameraTarget.Z);
            if (target == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "cameraTarget is required when the clash has no center point.");

            var viewX = position.X - target.X;
            var viewY = position.Y - target.Y;
            var viewZ = position.Z - target.Z;
            var viewLength = Math.Sqrt(viewX * viewX + viewY * viewY + viewZ * viewZ);
            if (viewLength < 1e-9)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "cameraPosition must differ from cameraTarget.");

            var up = request.CameraUp ?? new Point3Info { X = 0, Y = 0, Z = 1 };
            var upLength = Math.Sqrt(up.X * up.X + up.Y * up.Y + up.Z * up.Z);
            if (upLength < 1e-9)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "cameraUp must be a non-zero vector.");

            var crossX = viewY * up.Z - viewZ * up.Y;
            var crossY = viewZ * up.X - viewX * up.Z;
            var crossZ = viewX * up.Y - viewY * up.X;
            var crossLength = Math.Sqrt(crossX * crossX + crossY * crossY + crossZ * crossZ);
            if (crossLength / (viewLength * upLength) < 1e-6)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "cameraUp must not be parallel to the camera direction.");
        }

        private static string ResolveCameraProjection(
            string cameraMode,
            string requestedProjection,
            ViewpointProjection originalProjection)
        {
            if (requestedProjection != "current")
                return requestedProjection;
            if (cameraMode == ClashIsolationOptionHelper.CameraIso ||
                cameraMode == ClashIsolationOptionHelper.CameraIsoOpposite ||
                cameraMode == ClashIsolationOptionHelper.CameraCustom)
            {
                return originalProjection == ViewpointProjection.Orthographic
                    ? "orthographic"
                    : "perspective";
            }
            return "orthographic";
        }

        private static Point3Info ToPointInfo(Point3D point)
        {
            return point == null ? null : new Point3Info { X = point.X, Y = point.Y, Z = point.Z };
        }

        private static BoundingBoxInfo ToBoundingBoxInfo(BoundingBox3D box)
        {
            if (box == null)
                return null;
            return new BoundingBoxInfo
            {
                Min = ToPointInfo(box.Min),
                Max = ToPointInfo(box.Max),
                Center = ToPointInfo(box.Center),
                Size = new Point3Info
                {
                    X = box.Max.X - box.Min.X,
                    Y = box.Max.Y - box.Min.Y,
                    Z = box.Max.Z - box.Min.Z,
                },
            };
        }
    }

    internal static class ClashIsolationCameraService
    {
        public static void Apply(
            Document document,
            BoundingBox3D box,
            Point3D defaultTarget,
            string cameraMode,
            Point3Info customPosition,
            Point3Info customTarget,
            Point3Info customUp,
            string projection)
        {
            if (document == null || document.CurrentViewpoint == null || box == null)
                return;

            var target = customTarget == null
                ? defaultTarget ?? box.Center
                : new Point3D(customTarget.X, customTarget.Y, customTarget.Z);
            var viewpoint = document.CurrentViewpoint.CreateCopy();
            Point3D position;
            Vector3D up;

            if (cameraMode == ClashIsolationOptionHelper.CameraCustom)
            {
                position = new Point3D(customPosition.X, customPosition.Y, customPosition.Z);
                up = customUp == null
                    ? new Vector3D(0, 0, 1)
                    : new Vector3D(customUp.X, customUp.Y, customUp.Z);
                viewpoint.Position = position;
                ApplyProjection(viewpoint, projection);
                PointAndAlign(viewpoint, target, up);
                if (projection == "orthographic")
                {
                    viewpoint.ZoomBox(box);
                    viewpoint.Position = position;
                    PointAndAlign(viewpoint, target, up);
                }
                document.CurrentViewpoint.CopyFrom(viewpoint);
                return;
            }

            var size = Math.Max(
                Math.Max(Math.Abs(box.Max.X - box.Min.X), Math.Abs(box.Max.Y - box.Min.Y)),
                Math.Abs(box.Max.Z - box.Min.Z));
            var distance = Math.Max(size, 1.0) * 3.0;
            switch (cameraMode)
            {
                case ClashIsolationOptionHelper.CameraIso:
                    position = new Point3D(target.X + distance, target.Y - distance, target.Z + distance);
                    up = new Vector3D(0, 0, 1);
                    break;
                case ClashIsolationOptionHelper.CameraIsoOpposite:
                    position = new Point3D(target.X - distance, target.Y + distance, target.Z + distance);
                    up = new Vector3D(0, 0, 1);
                    break;
                case ClashIsolationOptionHelper.CameraTop:
                    position = new Point3D(target.X, target.Y, target.Z + distance);
                    up = new Vector3D(0, 1, 0);
                    break;
                case ClashIsolationOptionHelper.CameraFront:
                    position = new Point3D(target.X, target.Y - distance, target.Z);
                    up = new Vector3D(0, 0, 1);
                    break;
                case ClashIsolationOptionHelper.CameraBack:
                    position = new Point3D(target.X, target.Y + distance, target.Z);
                    up = new Vector3D(0, 0, 1);
                    break;
                case ClashIsolationOptionHelper.CameraLeft:
                    position = new Point3D(target.X - distance, target.Y, target.Z);
                    up = new Vector3D(0, 0, 1);
                    break;
                case ClashIsolationOptionHelper.CameraRight:
                    position = new Point3D(target.X + distance, target.Y, target.Z);
                    up = new Vector3D(0, 0, 1);
                    break;
                default:
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Unsupported cameraMode: " + cameraMode);
            }

            viewpoint.Position = position;
            ApplyProjection(viewpoint, projection);
            PointAndAlign(viewpoint, target, up);
            viewpoint.ZoomBox(box);
            PointAndAlign(viewpoint, target, up);
            document.CurrentViewpoint.CopyFrom(viewpoint);
        }

        public static void ApplyProjection(Document document, string projection)
        {
            if (document == null || document.CurrentViewpoint == null || projection == "current")
                return;
            var viewpoint = document.CurrentViewpoint.CreateCopy();
            ApplyProjection(viewpoint, projection);
            document.CurrentViewpoint.CopyFrom(viewpoint);
        }

        private static void ApplyProjection(Viewpoint viewpoint, string projection)
        {
            if (projection == "orthographic")
                viewpoint.Projection = ViewpointProjection.Orthographic;
            else if (projection == "perspective")
                viewpoint.Projection = ViewpointProjection.Perspective;
        }

        private static void PointAndAlign(Viewpoint viewpoint, Point3D target, Vector3D up)
        {
            viewpoint.RightOffsetAtFocalDistance = 0;
            viewpoint.UpOffsetAtFocalDistance = 0;
            viewpoint.RightOffsetFactor = 0;
            viewpoint.UpOffsetFactor = 0;
            viewpoint.PointAt(target);
            viewpoint.AlignUp(up);
            var position = viewpoint.Position;
            var dx = position.X - target.X;
            var dy = position.Y - target.Y;
            var dz = position.Z - target.Z;
            viewpoint.FocalDistance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
