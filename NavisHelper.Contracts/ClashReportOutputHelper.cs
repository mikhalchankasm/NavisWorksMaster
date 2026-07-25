using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashReportArtifactPaths
    {
        public string OutputDirectory { get; set; }
        public string ReportPath { get; set; }
        public string ManifestPath { get; set; }
        public string ClashBoxesPath { get; set; }
        public string ImagesDirectory { get; set; }
    }

    public static class ClashReportOutputHelper
    {
        public const string MarkerFileName = ".navishelper_clash_report";
        public const string ImagesDirectoryName = "images";
        public const string ReportFileName = "report.html";
        public const string ManifestFileName = "manifest.json";
        public const string ClashBoxesFileName = "clash_boxes.json";

        public static readonly string[] ProtectedFileNames =
        {
            ReportFileName,
            ManifestFileName,
            ClashBoxesFileName,
        };

        public static ClashReportArtifactPaths BuildArtifactPaths(string outputDirectory)
        {
            outputDirectory = outputDirectory ?? string.Empty;
            return new ClashReportArtifactPaths
            {
                OutputDirectory = outputDirectory,
                ReportPath = Path.Combine(outputDirectory, ReportFileName),
                ManifestPath = Path.Combine(outputDirectory, ManifestFileName),
                ClashBoxesPath = Path.Combine(outputDirectory, ClashBoxesFileName),
                ImagesDirectory = Path.Combine(outputDirectory, ImagesDirectoryName),
            };
        }

        public static string BuildScreenshotFileName(int index, string fileExtension, bool topView)
        {
            return BuildIndexedImageFileName("clash", index, fileExtension, topView);
        }

        public static string BuildClusterScreenshotFileName(int index, string fileExtension, bool topView)
        {
            return BuildIndexedImageFileName("cluster", index, fileExtension, topView);
        }

        public static string BuildScreenshotRelativePath(string fileName)
        {
            return ImagesDirectoryName + "/" + (fileName ?? string.Empty);
        }

        public static string BuildScreenshotAbsolutePath(string imagesDirectory, string fileName)
        {
            return Path.Combine(imagesDirectory ?? string.Empty, fileName ?? string.Empty);
        }

        private static string BuildIndexedImageFileName(string prefix, int index, string fileExtension, bool topView)
        {
            var suffix = topView ? "_top" : string.Empty;
            return prefix + "_" + index.ToString("000000", CultureInfo.InvariantCulture) + suffix + (fileExtension ?? string.Empty);
        }

        public static bool RequiresMarkerForOverwrite(bool imagesDirectoryExists, IEnumerable<string> existingFileNames)
        {
            if (imagesDirectoryExists)
                return true;

            return GetProtectedExistingFileNames(existingFileNames).Count > 0;
        }

        public static List<string> GetProtectedExistingFileNames(IEnumerable<string> existingFileNames)
        {
            if (existingFileNames == null)
                return new List<string>();

            return existingFileNames
                .Where(name => ProtectedFileNames.Any(protectedName => string.Equals(protectedName, name, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
