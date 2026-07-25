using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashIsolateResultRequest
    {
        public string ResultHandle { get; set; }
        public string BoxMode { get; set; }
        public double? BoxOffsetMm { get; set; }
        public bool? UseSectionBox { get; set; }
        public bool? IsolatePair { get; set; }
        public bool? UseContextTransparency { get; set; }
        public double? ContextTransparency { get; set; }
        public string ColorAHex { get; set; }
        public string ColorBHex { get; set; }
        public string CameraMode { get; set; }
        public Point3Info CameraPosition { get; set; }
        public Point3Info CameraTarget { get; set; }
        public Point3Info CameraUp { get; set; }
        public string Projection { get; set; }
        public string ScreenshotPath { get; set; }
        public string ScreenshotProfile { get; set; }
        public string ScreenshotFormat { get; set; }
        public int? ScreenshotMaxWidth { get; set; }
        public int? ScreenshotMaxHeight { get; set; }
        public int? ScreenshotJpegQuality { get; set; }
        public bool? OverwriteScreenshot { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class ClashIsolateResultResponse
    {
        public bool Apply { get; set; }
        public bool Applied { get; set; }
        public string ResultHandle { get; set; }
        public string TestHandle { get; set; }
        public string TestName { get; set; }
        public string ResultName { get; set; }
        public string GroupPath { get; set; }
        public string BoxMode { get; set; }
        public double BoxOffsetMm { get; set; }
        public bool UseSectionBox { get; set; }
        public bool IsolatePair { get; set; }
        public string CameraMode { get; set; }
        public string Projection { get; set; }
        public Point3Info ClashPoint { get; set; }
        public BoundingBoxInfo ClashBox { get; set; }
        public string Item1Name { get; set; }
        public string Item2Name { get; set; }
        public int HiddenBranchCount { get; set; }
        public long IsolationElapsedMilliseconds { get; set; }
        public string IsolationStatus { get; set; }
        public string ScreenshotPath { get; set; }
        public bool ScreenshotCaptured { get; set; }
        public bool CanReset { get; set; }
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashResetIsolationRequest
    {
        public bool? Apply { get; set; }
    }

    public sealed class ClashResetIsolationResponse
    {
        public bool Apply { get; set; }
        public bool HadActiveIsolation { get; set; }
        public bool Reset { get; set; }
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class CaptureCurrentViewRequest
    {
        public string OutputPath { get; set; }
        public string ScreenshotProfile { get; set; }
        public string ScreenshotFormat { get; set; }
        public int? ScreenshotMaxWidth { get; set; }
        public int? ScreenshotMaxHeight { get; set; }
        public int? ScreenshotJpegQuality { get; set; }
        public bool? Overwrite { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class CaptureCurrentViewResponse
    {
        public bool Apply { get; set; }
        public string OutputPath { get; set; }
        public string ScreenshotProfile { get; set; }
        public string ScreenshotFormat { get; set; }
        public int ScreenshotMaxWidth { get; set; }
        public int ScreenshotMaxHeight { get; set; }
        public int ScreenshotJpegQuality { get; set; }
        public bool Captured { get; set; }
        public long FileSizeBytes { get; set; }
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public static class ClashIsolationOptionHelper
    {
        public const string BoxModePoint = "point";
        public const string BoxModeItems = "items";
        public const string CameraCurrent = "current";
        public const string CameraIso = "iso";
        public const string CameraIsoOpposite = "iso_opposite";
        public const string CameraTop = "top";
        public const string CameraFront = "front";
        public const string CameraBack = "back";
        public const string CameraLeft = "left";
        public const string CameraRight = "right";
        public const string CameraCustom = "custom";

        public static string NormalizeBoxMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return BoxModePoint;
            var normalized = value.Trim().ToLowerInvariant();
            return normalized == BoxModePoint || normalized == BoxModeItems ? normalized : null;
        }

        public static string NormalizeCameraMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return CameraCurrent;
            var normalized = value.Trim().ToLowerInvariant().Replace('-', '_');
            switch (normalized)
            {
                case CameraCurrent:
                case CameraIso:
                case CameraIsoOpposite:
                case CameraTop:
                case CameraFront:
                case CameraBack:
                case CameraLeft:
                case CameraRight:
                case CameraCustom:
                    return normalized;
                case "opposite":
                    return CameraIsoOpposite;
                default:
                    return null;
            }
        }

        public static string NormalizeProjection(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "current";
            var normalized = value.Trim().ToLowerInvariant();
            return normalized == "current" || normalized == "orthographic" || normalized == "perspective"
                ? normalized
                : null;
        }

        public static bool IsFinitePoint(Point3Info point)
        {
            return point != null &&
                   IsFinite(point.X) &&
                   IsFinite(point.Y) &&
                   IsFinite(point.Z);
        }

        public static bool IsValidBoxOffset(string boxMode, double value)
        {
            if (!IsFinite(value))
                return false;
            return boxMode == BoxModeItems ? value >= 0 : value > 0;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
