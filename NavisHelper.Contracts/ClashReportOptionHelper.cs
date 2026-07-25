using System;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashReportScreenshotOptions
    {
        public string Profile { get; set; }
        public string Format { get; set; }
        public string FileExtension { get; set; }
        public int MaxWidth { get; set; }
        public int MaxHeight { get; set; }
        public int JpegQuality { get; set; }

        public bool RequiresPostProcess
        {
            get
            {
                return !string.Equals(Format, "bmp", StringComparison.OrdinalIgnoreCase) || MaxWidth > 0 || MaxHeight > 0;
            }
        }
    }

    public static class ClashReportOptionHelper
    {
        public const int DefaultClusterMembersInHtml = 25;
        public const int MaxClusterMembersInHtml = 200;
        public const int DefaultReportLimit = 100;
        public const int MaxReportLimit = 5000;
        public const int LargeReportConfirmationThreshold = 10000;
        public const string BoxModePoint = "point";
        public const string BoxModeItems = "items";
        public const string ArtifactGranularityResult = "result";
        public const string ArtifactGranularityCluster = "cluster";
        public const string VerbosityFull = "full";
        public const string VerbosityCompact = "compact";
        public const int DefaultCompactScreenshotMaxWidth = 1280;
        public const int DefaultCompactScreenshotMaxHeight = 720;
        public const int DefaultCompactScreenshotJpegQuality = 72;
        public const int DefaultFullHdScreenshotMaxWidth = 1920;
        public const int DefaultFullHdScreenshotMaxHeight = 1080;
        public const int DefaultFullHdScreenshotJpegQuality = 82;
        public const int DefaultLargeScreenshotMaxWidth = 2560;
        public const int DefaultLargeScreenshotMaxHeight = 1440;
        public const int DefaultLargeScreenshotJpegQuality = 88;

        public static int ClampClusterMembersInHtml(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultClusterMembersInHtml);
            if (value < 0)
                return 0;
            if (value > MaxClusterMembersInHtml)
                return MaxClusterMembersInHtml;
            return value;
        }

        public static int ClampReportLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultReportLimit);
            if (value < 1)
                return 1;
            if (value > MaxReportLimit)
                return MaxReportLimit;
            return value;
        }

        public static string NormalizeBoxMode(string boxMode)
        {
            if (string.IsNullOrWhiteSpace(boxMode))
                return BoxModePoint;

            var value = boxMode.Trim();
            if (string.Equals(value, BoxModePoint, StringComparison.OrdinalIgnoreCase))
                return BoxModePoint;
            if (string.Equals(value, BoxModeItems, StringComparison.OrdinalIgnoreCase))
                return BoxModeItems;

            return null;
        }

        public static string NormalizeArtifactGranularity(string artifactGranularity)
        {
            if (string.IsNullOrWhiteSpace(artifactGranularity))
                return ArtifactGranularityResult;

            var value = artifactGranularity.Trim();
            if (string.Equals(value, ArtifactGranularityResult, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "raw", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "clash", StringComparison.OrdinalIgnoreCase))
                return ArtifactGranularityResult;
            if (string.Equals(value, ArtifactGranularityCluster, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "group", StringComparison.OrdinalIgnoreCase))
                return ArtifactGranularityCluster;

            return null;
        }

        public static string NormalizeReportVerbosity(string verbosity)
        {
            if (string.IsNullOrWhiteSpace(verbosity))
                return VerbosityFull;

            var value = verbosity.Trim();
            if (string.Equals(value, VerbosityFull, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "standard", StringComparison.OrdinalIgnoreCase))
                return VerbosityFull;
            if (string.Equals(value, VerbosityCompact, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "short", StringComparison.OrdinalIgnoreCase))
                return VerbosityCompact;

            return null;
        }

        public static ClashReportScreenshotOptions NormalizeScreenshotOptions(
            string screenshotProfile,
            string screenshotFormat,
            int? screenshotMaxWidth,
            int? screenshotMaxHeight,
            int? screenshotJpegQuality,
            out string errorMessage)
        {
            errorMessage = string.Empty;

            var profile = screenshotProfile ?? string.Empty;
            profile = profile.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(profile))
                profile = "compact";

            var options = new ClashReportScreenshotOptions
            {
                Profile = profile,
                Format = "jpg",
                FileExtension = ".jpg",
                MaxWidth = DefaultCompactScreenshotMaxWidth,
                MaxHeight = DefaultCompactScreenshotMaxHeight,
                JpegQuality = DefaultCompactScreenshotJpegQuality,
            };

            switch (profile)
            {
                case "compact":
                case "small":
                case "minimal":
                case "min":
                    options.Profile = "compact";
                    break;
                case "standard":
                case "fullhd":
                case "full_hd":
                case "fhd":
                    options.Profile = "fullhd";
                    options.MaxWidth = DefaultFullHdScreenshotMaxWidth;
                    options.MaxHeight = DefaultFullHdScreenshotMaxHeight;
                    options.JpegQuality = DefaultFullHdScreenshotJpegQuality;
                    break;
                case "large":
                case "high":
                case "max":
                    options.Profile = "large";
                    options.MaxWidth = DefaultLargeScreenshotMaxWidth;
                    options.MaxHeight = DefaultLargeScreenshotMaxHeight;
                    options.JpegQuality = DefaultLargeScreenshotJpegQuality;
                    break;
                case "source":
                case "bmp":
                case "legacy":
                    options.Profile = "source";
                    options.Format = "bmp";
                    options.FileExtension = ".bmp";
                    options.MaxWidth = 0;
                    options.MaxHeight = 0;
                    options.JpegQuality = 100;
                    break;
                default:
                    errorMessage = "Unsupported screenshotProfile '" + profile + "'. Use compact, fullhd, large, or source.";
                    return null;
            }

            var requestedFormat = screenshotFormat ?? string.Empty;
            requestedFormat = requestedFormat.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(requestedFormat))
            {
                switch (requestedFormat)
                {
                    case "jpg":
                    case "jpeg":
                        options.Format = "jpg";
                        options.FileExtension = ".jpg";
                        break;
                    case "png":
                        options.Format = "png";
                        options.FileExtension = ".png";
                        break;
                    case "bmp":
                        options.Format = "bmp";
                        options.FileExtension = ".bmp";
                        break;
                    default:
                        errorMessage = "Unsupported screenshotFormat '" + requestedFormat + "'. Use jpg, png, or bmp.";
                        return null;
                }
            }

            if (screenshotMaxWidth.HasValue)
            {
                if (!TryNormalizeScreenshotDimension(screenshotMaxWidth.Value, "screenshotMaxWidth", out var width, out errorMessage))
                    return null;
                options.MaxWidth = width;
            }

            if (screenshotMaxHeight.HasValue)
            {
                if (!TryNormalizeScreenshotDimension(screenshotMaxHeight.Value, "screenshotMaxHeight", out var height, out errorMessage))
                    return null;
                options.MaxHeight = height;
            }

            if (screenshotJpegQuality.HasValue)
            {
                if (!TryNormalizeScreenshotJpegQuality(screenshotJpegQuality.Value, out var quality, out errorMessage))
                    return null;
                options.JpegQuality = quality;
            }

            return options;
        }

        public static bool TryNormalizeScreenshotDimension(int value, string argumentName, out int result, out string errorMessage)
        {
            result = 0;
            errorMessage = string.Empty;

            if (value < 0)
            {
                errorMessage = argumentName + " must be 0 or greater.";
                return false;
            }

            if (value == 0)
                return true;

            if (value < 320 || value > 10000)
            {
                errorMessage = argumentName + " must be between 320 and 10000 pixels, or 0 to disable that bound.";
                return false;
            }

            result = value;
            return true;
        }

        public static bool TryNormalizeScreenshotJpegQuality(int value, out int result, out string errorMessage)
        {
            result = 0;
            errorMessage = string.Empty;

            if (value < 1 || value > 100)
            {
                errorMessage = "screenshotJpegQuality must be between 1 and 100.";
                return false;
            }

            result = value;
            return true;
        }

        public static void CalculateImageTargetSize(int sourceWidth, int sourceHeight, int maxWidth, int maxHeight, out int targetWidth, out int targetHeight)
        {
            targetWidth = sourceWidth;
            targetHeight = sourceHeight;
            if (sourceWidth <= 0 || sourceHeight <= 0 || (maxWidth <= 0 && maxHeight <= 0))
                return;

            var scale = 1.0;
            if (maxWidth > 0)
                scale = Math.Min(scale, (double)maxWidth / sourceWidth);
            if (maxHeight > 0)
                scale = Math.Min(scale, (double)maxHeight / sourceHeight);
            if (scale >= 1.0)
                return;

            targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        }
    }
}
