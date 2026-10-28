using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ViewpointAnimationExportRequest
    {
        public string Animation { get; set; }
        public string Format { get; set; }
        public string OutputDirectory { get; set; }
    }

    public sealed class ViewpointAnimationExportResponse
    {
        public string Animation { get; set; }
        public int FrameCount { get; set; }
        public int CutCount { get; set; }
        public string Format { get; set; }
        public string OutputDirectory { get; set; }
        public bool OutputDirectoryExists { get; set; }
        public string CurrentAnimation { get; set; }
        public List<ExporterPluginOptions> Plugins { get; set; } = new List<ExporterPluginOptions>();
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ExporterPluginOptions
    {
        public string Plugin { get; set; }
        public List<ExporterOption> Options { get; set; } = new List<ExporterOption>();
        public bool Truncated { get; set; }
        public string Error { get; set; }
    }

    public sealed class ExporterOption
    {
        public string Name { get; set; }
        public string UserName { get; set; }
        public string Parent { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
    }

    public static class ViewpointAnimationExportRules
    {
        public static readonly string[] Formats = { "png", "jpg", "avi" };
        public static readonly string[] ProbedPlugins = { "lcodpanim", "lcodpavi", "lcodpexpng", "lcodpexjpg", "lcodpcuranim" };
        public const int MaxOptionsPerPlugin = 200;
        public const int MaxOptionDepth = 3;

        public static string Describe(ViewpointAnimationExportRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Animation)) return "animation is required.";
            if (!Formats.Contains(NormalizeFormat(request.Format))) return "format must be png, jpg or avi.";
            if (!string.IsNullOrWhiteSpace(request.OutputDirectory) && !IsFullyQualified(request.OutputDirectory.Trim()))
                return "outputDirectory must be an absolute path.";
            return null;
        }

        private static bool IsFullyQualified(string path)
        {
            path = path.Replace('/', '\\');
            if (path.Length >= 3 && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
                && path[1] == ':' && path[2] == '\\') return true;
            if (!path.StartsWith("\\\\", StringComparison.Ordinal)) return false;
            var parts = path.Substring(2).Split('\\');
            return parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0
                && parts[0] != "." && parts[0] != ".." && parts[0] != "?" && parts[1] != "." && parts[1] != "..";
        }

        public static string NormalizeFormat(string format) =>
            string.IsNullOrWhiteSpace(format) ? "png" : format.Trim().ToLowerInvariant();

        public static string ResolveOutputDirectory(string documentPath, string outputDirectory, string animationName)
        {
            if (!string.IsNullOrWhiteSpace(outputDirectory)) return Path.GetFullPath(outputDirectory.Trim());
            if (string.IsNullOrWhiteSpace(documentPath))
                throw new ArgumentException("Specify outputDirectory when the document has no file path.");
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(documentPath)), "viewpoint_animations", SafeFileName(animationName));
        }

        public static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string((name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
            return safe.Length == 0 ? "animation" : safe;
        }

        public static string FormatOptionValue(object value, out string type)
        {
            type = value == null ? "null" : value.GetType().Name;
            if (value == null) return "";
            if (value is bool boolean) return boolean ? "true" : "false";
            return value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString();
        }
    }
}
