using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal static class SelectionMarkupViewpointService
    {
        private const double DefaultRed = 1.0;
        private const double DefaultGreen = 0.0;
        private const double DefaultBlue = 0.0;
        private const int DefaultThickness = 3;
        private const double DefaultPaddingFactor = 0.20;
        private const double DefaultMinMarkSizeMm = 500;
        private const double DefaultHatchSpacingMm = 500;
        private const double DefaultMarkSoloMinSizeMm = 1500;
        private const double DefaultMarkMergeGapMm = 1000;

        public static SelectionMarkupStyle CreateStyle(
            IList<double> color,
            int? thickness,
            double? paddingFactor,
            int? minRadiusPixels,
            double? minMarkSizeMm,
            double? markSoloMinSizeMm,
            double? markMergeGapMm,
            string markStyle,
            bool? arrowCallout,
            double? arrowLengthMm,
            bool? targetCrosshair,
            double? hatchAngleDeg,
            int? hatchSpacingPx,
            double? hatchSpacingMm,
            int? hatchThickness)
        {
            var rgb = color == null || color.Count == 0
                ? new[] { DefaultRed, DefaultGreen, DefaultBlue }
                : color.ToArray();
            if (rgb.Length != 3 || rgb.Any(value => double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "ellipse_color must contain exactly three finite RGB values from 0 to 1.");

            var resolvedThickness = thickness.GetValueOrDefault(DefaultThickness);
            if (resolvedThickness < 1 || resolvedThickness > 20)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "thickness must be from 1 to 20.");

            var resolvedPaddingFactor = paddingFactor.GetValueOrDefault(DefaultPaddingFactor);
            if (double.IsNaN(resolvedPaddingFactor) || double.IsInfinity(resolvedPaddingFactor) || resolvedPaddingFactor < 0 || resolvedPaddingFactor > 5)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "padding_factor must be from 0 to 5.");

            var resolvedMinMarkSizeMm = minMarkSizeMm ?? PixelsToLegacyDocumentSizeMm(minRadiusPixels, DefaultMinMarkSizeMm, 1);
            if (!IsFinite(resolvedMinMarkSizeMm) || resolvedMinMarkSizeMm < 0 || resolvedMinMarkSizeMm > 1000000)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "minMarkSizeMm must be a finite value from 0 to 1000000.");

            var resolvedMarkSoloMinSizeMm = markSoloMinSizeMm.GetValueOrDefault(DefaultMarkSoloMinSizeMm);
            if (!IsFinite(resolvedMarkSoloMinSizeMm) || resolvedMarkSoloMinSizeMm < 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "mark_solo_min_size_mm must be a finite value greater than or equal to 0.");

            var resolvedMarkMergeGapMm = markMergeGapMm.GetValueOrDefault(DefaultMarkMergeGapMm);
            if (!IsFinite(resolvedMarkMergeGapMm) || resolvedMarkMergeGapMm < 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "mark_merge_gap_mm must be a finite value greater than or equal to 0.");

            string resolvedMarkStyle;
            try
            {
                resolvedMarkStyle = MarkupRedlineJsonBuilder.NormalizeStyle(markStyle);
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }

            var resolvedArrowLengthMm = arrowLengthMm.GetValueOrDefault(0);
            if (!IsFinite(resolvedArrowLengthMm) || resolvedArrowLengthMm < 0 || resolvedArrowLengthMm > 1000000)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "arrowLengthMm must be a finite value from 0 to 1000000. Use 0 for the automatic 8 percent camera-height length.");

            var resolvedHatchAngleDeg = hatchAngleDeg.GetValueOrDefault(45);
            if (!IsFinite(resolvedHatchAngleDeg) || resolvedHatchAngleDeg < -360 || resolvedHatchAngleDeg > 360)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "hatchAngleDeg must be a finite value from -360 to 360.");
            var resolvedHatchSpacingMm = hatchSpacingMm ?? PixelsToLegacyDocumentSizeMm(hatchSpacingPx, DefaultHatchSpacingMm, 2);
            if (!IsFinite(resolvedHatchSpacingMm) || resolvedHatchSpacingMm <= 0 || resolvedHatchSpacingMm > 1000000)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "hatchSpacingMm must be a finite value greater than 0 and no more than 1000000.");
            var resolvedHatchThickness = hatchThickness.GetValueOrDefault(resolvedThickness);
            if (resolvedHatchThickness < 1 || resolvedHatchThickness > 20)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "hatchThickness must be from 1 to 20.");
            return new SelectionMarkupStyle
            {
                Red = rgb[0],
                Green = rgb[1],
                Blue = rgb[2],
                Thickness = resolvedThickness,
                PaddingFactor = resolvedPaddingFactor,
                MinMarkSizeMm = resolvedMinMarkSizeMm,
                MarkSoloMinSizeMm = resolvedMarkSoloMinSizeMm,
                MarkMergeGapMm = resolvedMarkMergeGapMm,
                MarkStyle = resolvedMarkStyle,
                ArrowCallout = arrowCallout.GetValueOrDefault(false),
                ArrowLengthMm = resolvedArrowLengthMm,
                TargetCrosshair = targetCrosshair.GetValueOrDefault(false),
                HatchAngleDeg = resolvedHatchAngleDeg,
                HatchSpacingMm = resolvedHatchSpacingMm,
                HatchThickness = resolvedHatchThickness,
            };
        }

        public static SelectionMarkupGeometry BuildGeometry(IEnumerable<ModelItem> items, Viewpoint viewpoint, Autodesk.Navisworks.Api.View fallbackView, SelectionMarkupStyle style)
        {
            if (viewpoint == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "There is no viewpoint for markup projection.");
            if (style == null)
                throw new ArgumentNullException(nameof(style));

            var markRects = new List<double[]>();
            var skippedItemCount = 0;
            var minimumRadii = GetMinimumRadii(viewpoint, fallbackView, style);
            var boxes = new List<BoundingBox3D>();
            foreach (var item in items ?? Enumerable.Empty<ModelItem>())
            {
                BoundingBox3D box = null;
                try
                {
                    box = item == null ? null : item.BoundingBox();
                }
                catch
                {
                }

                if (box == null)
                {
                    skippedItemCount++;
                    continue;
                }

                boxes.Add(box);
            }

            var soloSizeUnits = SectionBoxHelper.MmToDocUnits(style.MarkSoloMinSizeMm);
            var mergeGapUnits = SectionBoxHelper.MmToDocUnits(style.MarkMergeGapMm);
            var markGroups = MarkupFrameGroupingHelper.Group(boxes.Select(ToFrameBounds).ToList(), soloSizeUnits, mergeGapUnits);
            var soloMarkCount = 0;
            var mergedMarkCount = 0;
            foreach (var group in markGroups)
            {
                var box = ToBoundingBox(group.Bounds);
                var rect = ProjectBounds(box, viewpoint, fallbackView, style.PaddingFactor, minimumRadii);
                if (rect == null)
                {
                    skippedItemCount += group.SourceIndices.Count;
                    continue;
                }

                markRects.Add(rect);
                if (group.IsSolo)
                    soloMarkCount++;
                else
                    mergedMarkCount++;
            }

            int arrowCount;
            var cameraFrame = ResolveCameraFrame(viewpoint, fallbackView);
            var cameraHeight = cameraFrame[1] * 2.0;
            var redlinesJson = MarkupRedlineJsonBuilder.Build(
                style.MarkStyle,
                markRects.Select(rect => new MarkupRedlineRect
                {
                    Left = rect[0],
                    Top = rect[1],
                    Right = rect[2],
                    Bottom = rect[3],
                }),
                style.Red,
                style.Green,
                style.Blue,
                style.Thickness,
                minimumRadii[0] * 5,
                minimumRadii[1] * 5,
                new MarkupRedlineBuildOptions
                {
                    HatchAngleDeg = style.HatchAngleDeg,
                    HatchSpacing = ResolveHatchSpacing(viewpoint, fallbackView, style, minimumRadii),
                    HatchThickness = style.HatchThickness,
                    CameraHalfHeight = cameraFrame[1],
                    CameraHalfWidth = cameraFrame[0],
                    ArrowCallout = style.ArrowCallout,
                    ArrowLength = style.ArrowCallout || string.Equals(style.MarkStyle, MarkupRedlineJsonBuilder.ArrowStyle, StringComparison.Ordinal)
                        ? MarkupRedlineJsonBuilder.ResolveArrowLength(
                            cameraHeight,
                            viewpoint.Projection == ViewpointProjection.Perspective || style.ArrowLengthMm <= 1e-9
                                ? 0
                                : SectionBoxHelper.MmToDocUnits(style.ArrowLengthMm))
                        : 0,
                    TargetCrosshair = style.TargetCrosshair,
                },
                out arrowCount);

            return new SelectionMarkupGeometry
            {
                MarkCount = markRects.Count,
                EllipseCount = string.Equals(style.MarkStyle, MarkupRedlineJsonBuilder.TargetStyle, StringComparison.Ordinal) ? markRects.Count : 0,
                ArrowCount = arrowCount,
                SoloMarkCount = soloMarkCount,
                MergedMarkCount = mergedMarkCount,
                SkippedItemCount = skippedItemCount,
                RedlinesJson = redlinesJson,
            };
        }

        public static void ValidateGroupingSafety(IEnumerable<ModelItem> items, SelectionMarkupStyle style)
        {
            if (style == null)
                throw new ArgumentNullException(nameof(style));
            var bounds = new List<MarkupFrameBounds>();
            foreach (var item in items ?? Enumerable.Empty<ModelItem>())
            {
                try
                {
                    var box = item == null ? null : item.BoundingBox();
                    if (box != null)
                        bounds.Add(ToFrameBounds(box));
                }
                catch
                {
                }
            }
            MarkupFrameGroupingHelper.Group(
                bounds,
                SectionBoxHelper.MmToDocUnits(style.MarkSoloMinSizeMm),
                SectionBoxHelper.MmToDocUnits(style.MarkMergeGapMm));
        }

        private static double[] ProjectBounds(
            BoundingBox3D box,
            Viewpoint viewpoint,
            Autodesk.Navisworks.Api.View fallbackView,
            double paddingFactor,
            double[] minimumRadii)
        {
            if (box == null)
                return null;

            var center = new Point3D(
                (box.Min.X + box.Max.X) / 2,
                (box.Min.Y + box.Max.Y) / 2,
                (box.Min.Z + box.Max.Z) / 2);
            var projectedCenter = WorldToRedline(center, viewpoint, fallbackView);
            if (projectedCenter == null)
                return null;

            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;
            var validCorners = 0;
            foreach (var x in new[] { box.Min.X, box.Max.X })
            foreach (var y in new[] { box.Min.Y, box.Max.Y })
            foreach (var z in new[] { box.Min.Z, box.Max.Z })
            {
                var projectedCorner = WorldToRedline(new Point3D(x, y, z), viewpoint, fallbackView);
                if (projectedCorner == null)
                    continue;

                minX = Math.Min(minX, projectedCorner[0]);
                maxX = Math.Max(maxX, projectedCorner[0]);
                minY = Math.Min(minY, projectedCorner[1]);
                maxY = Math.Max(maxY, projectedCorner[1]);
                validCorners++;
            }

            var radiusX = validCorners >= 2
                ? Math.Max((maxX - minX) / 2 * (1 + paddingFactor), minimumRadii[0])
                : minimumRadii[0];
            var radiusY = validCorners >= 2
                ? Math.Max((maxY - minY) / 2 * (1 + paddingFactor), minimumRadii[1])
                : minimumRadii[1];
            var rect = new[]
            {
                projectedCenter[0] - radiusX,
                projectedCenter[1] + radiusY,
                projectedCenter[0] + radiusX,
                projectedCenter[1] - radiusY,
            };
            return rect.Any(value => !IsFinite(value)) ? null : rect;
        }

        private static MarkupFrameBounds ToFrameBounds(BoundingBox3D bounds)
        {
            return new MarkupFrameBounds
            {
                MinX = bounds.Min.X,
                MinY = bounds.Min.Y,
                MinZ = bounds.Min.Z,
                MaxX = bounds.Max.X,
                MaxY = bounds.Max.Y,
                MaxZ = bounds.Max.Z,
            };
        }

        private static BoundingBox3D ToBoundingBox(MarkupFrameBounds bounds)
        {
            return new BoundingBox3D(
                new Point3D(bounds.MinX, bounds.MinY, bounds.MinZ),
                new Point3D(bounds.MaxX, bounds.MaxY, bounds.MaxZ));
        }

        private static double[] WorldToRedline(Point3D point, Viewpoint viewpoint, Autodesk.Navisworks.Api.View fallbackView)
        {
            if (point == null || viewpoint == null)
                return null;

            if (viewpoint.Projection == ViewpointProjection.Orthographic)
            {
                double orthographicX;
                double orthographicY;
                if (OrthographicRedlineProjectionHelper.TryProject(
                        point.X,
                        point.Y,
                        point.Z,
                        viewpoint.Position.X,
                        viewpoint.Position.Y,
                        viewpoint.Position.Z,
                        viewpoint.Rotation.A,
                        viewpoint.Rotation.B,
                        viewpoint.Rotation.C,
                        viewpoint.Rotation.D,
                        viewpoint.HeightField,
                        viewpoint.AspectRatio,
                        out orthographicX,
                        out orthographicY))
                {
                    return new[] { orthographicX, orthographicY };
                }
            }

            if (fallbackView != null)
            {
                try
                {
                    var projected = fallbackView.ProjectPoint(point, false, false);
                    if (projected != null)
                    {
                        var camera = LcOpRedline.ScreenToCameraSpace(
                            fallbackView.Viewer,
                            projected.X,
                            projected.Y);
                        if (IsFinite(camera.X) && IsFinite(camera.Y))
                            return new[] { camera.X, camera.Y };
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static double[] GetMinimumRadii(
            Viewpoint viewpoint,
            Autodesk.Navisworks.Api.View fallbackView,
            SelectionMarkupStyle style)
        {
            if (viewpoint.Projection == ViewpointProjection.Perspective && fallbackView != null)
            {
                var pixelUnits = GetCameraUnitsPerPixel(fallbackView);
                const double minimumRadiusPixels = 8;
                return new[]
                {
                    pixelUnits[0] * minimumRadiusPixels,
                    pixelUnits[1] * minimumRadiusPixels,
                };
            }

            var minimumRadius = OrthographicRedlineProjectionHelper.GetMinimumRadiusFromSize(
                SectionBoxHelper.MmToDocUnits(style.MinMarkSizeMm));
            return new[] { minimumRadius, minimumRadius };
        }

        private static double ResolveHatchSpacing(
            Viewpoint viewpoint,
            Autodesk.Navisworks.Api.View fallbackView,
            SelectionMarkupStyle style,
            double[] minimumRadii)
        {
            if (viewpoint.Projection == ViewpointProjection.Perspective && fallbackView != null)
                return Math.Max((minimumRadii[0] + minimumRadii[1]) * 0.625, 1e-9);

            return SectionBoxHelper.MmToDocUnits(style.HatchSpacingMm);
        }

        private static double[] GetCameraUnitsPerPixel(Autodesk.Navisworks.Api.View view)
        {
            try
            {
                var centerX = Math.Max(view.Width / 2, 1);
                var centerY = Math.Max(view.Height / 2, 1);
                var rightX = Math.Min(centerX + 1, Math.Max(view.Width - 1, 1));
                var downY = Math.Min(centerY + 1, Math.Max(view.Height - 1, 1));
                var center = LcOpRedline.ScreenToCameraSpace(view.Viewer, centerX, centerY);
                var right = LcOpRedline.ScreenToCameraSpace(view.Viewer, rightX, centerY);
                var down = LcOpRedline.ScreenToCameraSpace(view.Viewer, centerX, downY);
                var x = Math.Abs(right.X - center.X);
                var y = Math.Abs(down.Y - center.Y);
                if (IsFinite(x) && IsFinite(y) && x > 1e-9 && y > 1e-9)
                    return new[] { x, y };
            }
            catch
            {
            }

            return new[] { 0.002, 0.002 };
        }

        private static double[] ResolveCameraFrame(
            Viewpoint viewpoint,
            Autodesk.Navisworks.Api.View view)
        {
            if (viewpoint.Projection == ViewpointProjection.Orthographic || view == null)
            {
                return new[]
                {
                    viewpoint.HeightField * viewpoint.AspectRatio / 2.0,
                    viewpoint.HeightField / 2.0,
                };
            }

            try
            {
                var first = LcOpRedline.ScreenToCameraSpace(view.Viewer, 0, 0);
                var opposite = LcOpRedline.ScreenToCameraSpace(
                    view.Viewer,
                    Math.Max(view.Width - 1, 1),
                    Math.Max(view.Height - 1, 1));
                var halfWidth = Math.Max(Math.Abs(first.X), Math.Abs(opposite.X));
                var halfHeight = Math.Max(Math.Abs(first.Y), Math.Abs(opposite.Y));
                if (IsFinite(halfWidth) &&
                    IsFinite(halfHeight) &&
                    halfWidth > 1e-9 &&
                    halfHeight > 1e-9)
                {
                    return new[] { halfWidth, halfHeight };
                }
            }
            catch
            {
            }

            var pixelUnits = GetCameraUnitsPerPixel(view);
            return new[]
            {
                pixelUnits[0] * Math.Max(view.Width, 1) / 2.0,
                pixelUnits[1] * Math.Max(view.Height, 1) / 2.0,
            };
        }

        private static double PixelsToLegacyDocumentSizeMm(int? pixels, double fallbackMm, int minimumPixels)
        {
            if (!pixels.HasValue)
                return fallbackMm;
            if (pixels.Value < minimumPixels || pixels.Value > 200)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Deprecated pixel markup size parameters must be from " + minimumPixels.ToString() + " to 200.");
            return pixels.Value * 50.0;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

    }

    internal sealed class SelectionMarkupStyle
    {
        public double Red { get; set; }
        public double Green { get; set; }
        public double Blue { get; set; }
        public int Thickness { get; set; }
        public double PaddingFactor { get; set; }
        public double MinMarkSizeMm { get; set; }
        public double MarkSoloMinSizeMm { get; set; }
        public double MarkMergeGapMm { get; set; }
        public string MarkStyle { get; set; }
        public bool ArrowCallout { get; set; }
        public double ArrowLengthMm { get; set; }
        public bool TargetCrosshair { get; set; }
        public double HatchAngleDeg { get; set; }
        public double HatchSpacingMm { get; set; }
        public int HatchThickness { get; set; }
    }

    internal sealed class SelectionMarkupGeometry
    {
        public string RedlinesJson { get; set; }
        public int MarkCount { get; set; }
        public int SoloMarkCount { get; set; }
        public int MergedMarkCount { get; set; }
        public int EllipseCount { get; set; }
        public int ArrowCount { get; set; }
        public int SkippedItemCount { get; set; }
    }
}
