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
        public bool? Apply { get; set; }
        public bool? Overwrite { get; set; }
        public bool? ListFiles { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public int? Fps { get; set; }
        public int? AntiAliasLevel { get; set; }
        public string Exporter { get; set; } = "lcodpanim";
        public List<string> ExporterOptions { get; set; }
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
        public bool Applied { get; set; }
        public string OutputFile { get; set; }
        public string ExportStatus { get; set; }
        public List<ExportedFile> Files { get; set; } = new List<ExportedFile>();
        public int FileCount { get; set; }
        public string FirstFile { get; set; }
        public string LastFile { get; set; }
        public string FilePattern { get; set; }
        public long TotalBytes { get; set; }
        public bool FormatMatches { get; set; }
        public double ElapsedSeconds { get; set; }
        public List<string> AppliedOptions { get; set; } = new List<string>();
        public List<string> StoredOptions { get; set; } = new List<string>();
        public List<string> RestoredOptions { get; set; } = new List<string>();
        public bool OptionsRestored { get; set; }
        public bool Exported { get; set; }
        public List<ExporterOption> OptionsAfter { get; set; } = new List<ExporterOption>();
        public string CurrentAnimationAfter { get; set; }
    }

    public sealed class ExportedFile
    {
        public string Name { get; set; }
        public long Bytes { get; set; }
        public string Kind { get; set; }
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
        public static readonly string[] Exporters = { "lcodpanim", "lcodpavi", "lcodpexpng", "lcodpexjpg" };
        public static readonly string[] ProbedPlugins = { "lcodpanim", "lcodpavi", "lcodpexpng", "lcodpexjpg", "lcodpcuranim" };
        public const int MaxOptionsPerPlugin = 200;
        public const int MaxOptionDepth = 3;
        public const string DefaultRenderer = "lcodpopengl";
        public const string ProtectedFolderMessage = "outputDirectory is inside a protected folder (Windows, Program Files or the NavisHelper install).";
        public const string ExistingOutputMessage = "Output files for this animation already exist in outputDirectory; pass overwrite=true to replace them.";

        public static string Describe(ViewpointAnimationExportRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Animation)) return "animation is required.";
            if (!Formats.Contains(NormalizeFormat(request.Format))) return "format must be png, jpg or avi.";
            if (!string.IsNullOrWhiteSpace(request.OutputDirectory) && !IsFullyQualified(request.OutputDirectory.Trim()))
                return "outputDirectory must be an absolute path.";
            if (!ValidRange(request.Width, 16, 4096) || !ValidRange(request.Height, 16, 4096))
                return "width and height must be 0 or between 16 and 4096.";
            if (!ValidRange(request.Fps, 1, 60)) return "fps must be 0 or between 1 and 60.";
            if (!new[] { 0, 1, 2, 4, 8, 16, 32, 64 }.Contains(request.AntiAliasLevel ?? 0))
                return "antiAliasLevel must be 0, 1, 2, 4, 8, 16, 32 or 64.";
            if (!Exporters.Contains(NormalizeExporter(request.Exporter)))
                return "exporter must be lcodpanim, lcodpavi, lcodpexpng or lcodpexjpg.";
            if (request.ExporterOptions != null && request.ExporterOptions.Count > 20)
                return "exporterOptions accepts at most 20 entries.";
            if (request.ExporterOptions != null && request.ExporterOptions.Any(entry =>
            {
                var separator = entry == null ? -1 : entry.IndexOf('=');
                if (separator < 0) return true;
                var name = entry.Substring(0, separator).Trim();
                return name.Length == 0 || name.Any(c => !char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-');
            }))
                return "exporterOptions entries must be name=value; names use letters, digits, '.', '_' and '-'.";
            return null;
        }

        private static bool ValidRange(int? value, int min, int max) =>
            !value.HasValue || value == 0 || (value >= min && value <= max);

        public static IList<KeyValuePair<string, int>> OptionAssignments(ViewpointAnimationExportRequest request)
        {
            var options = new[]
            {
                new KeyValuePair<string, int>("export.image.width", request.Width ?? 0),
                new KeyValuePair<string, int>("export.image.height", request.Height ?? 0),
                new KeyValuePair<string, int>("export.anim.fps", request.Fps ?? 0),
                new KeyValuePair<string, int>("export.image.anti-alias.level", request.AntiAliasLevel ?? 0),
            };
            return options.Where(pair => pair.Value != 0).ToList();
        }

        public static string NormalizeExporter(string exporter) =>
            string.IsNullOrWhiteSpace(exporter) ? "lcodpanim" : exporter.Trim().ToLowerInvariant();

        public static IList<KeyValuePair<string, object>> ParseExporterOptions(IList<string> entries)
        {
            var result = new List<KeyValuePair<string, object>>();
            foreach (var entry in entries ?? Array.Empty<string>())
            {
                var separator = entry.IndexOf('=');
                var name = entry.Substring(0, separator).Trim();
                var text = entry.Substring(separator + 1).Trim();
                object value = text;
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) value = number;
                else if (bool.TryParse(text, out var boolean)) value = boolean;
                result.Add(new KeyValuePair<string, object>(name, value));
            }
            return result;
        }

        public static IList<KeyValuePair<string, object>> MergedAssignments(ViewpointAnimationExportRequest request)
        {
            var result = OptionAssignments(request).Select(pair => new KeyValuePair<string, object>(pair.Key, pair.Value)).ToList();
            if (NormalizeExporter(request.Exporter) == "lcodpanim")
            {
                result.Add(new KeyValuePair<string, object>("export.image.format", AnimationFormatValue(request.Format)));
                result.Add(new KeyValuePair<string, object>("export.image.renderer", DefaultRenderer));
            }
            foreach (var pair in ParseExporterOptions(request.ExporterOptions))
            {
                var index = result.FindIndex(existing => existing.Key == pair.Key);
                if (index < 0) result.Add(pair);
                else result[index] = pair;
            }
            return result;
        }

        public static string AnimationFormatValue(string format)
        {
            switch (NormalizeFormat(format))
            {
                case "png": return "image_anim_adapter_lcodpexpng";
                case "jpg": return "image_anim_adapter_lcodpexjpg";
                case "avi": return "lcodpavi";
                default: throw new ArgumentException("format must be png, jpg or avi.", nameof(format));
            }
        }

        public static IList<string> ChangedOptions(IDictionary<string, object> before, IDictionary<string, object> after) =>
            (before ?? new Dictionary<string, object>()).Where(pair =>
                after == null || !after.TryGetValue(pair.Key, out var value)
                || FormatOptionValue(pair.Value, out _) != FormatOptionValue(value, out _))
            .Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal).ToList();

        public static string OptionsRestoreWarning(IEnumerable<string> keys, IDictionary<string, object> after)
        {
            var values = (keys ?? Enumerable.Empty<string>()).Select(key => key + "="
                + (after != null && after.TryGetValue(key, out var value) ? FormatOptionValue(value, out _) : "")).ToList();
            return values.Count == 0 ? null
                : "The Export Animation dialog keeps this call's values: " + string.Join(", ", values) + ".";
        }

        public static string FailedExportMessage(string status) =>
            "The animation exporter returned " + status + "; nothing was exported.";

        public static string RestoreWarning(string name) =>
            "The previous current animation could not be restored; Navisworks keeps '" + name + "' selected.";

        public static string RestoreWarningFor(string previous, string after) =>
            string.IsNullOrEmpty(previous) || (after ?? "") == previous ? null : RestoreWarning(after);

        public static string OutputFile(string folder, string animationName, string format) =>
            Path.Combine(folder, SafeFileName(animationName) + (NormalizeFormat(format) == "avi" ? "." : "_.") + NormalizeFormat(format));

        public static string PaddedFrameName(string name, string format, int frame, int count) =>
            SafeFileName(name) + "_" + frame.ToString("D" + FrameDigits(count), CultureInfo.InvariantCulture) + "." + NormalizeFormat(format);

        public static int? FrameNumber(string fileName, string name, string format)
        {
            var file = Path.GetFileName(fileName);
            var prefix = SafeFileName(name) + "_";
            var extension = "." + NormalizeFormat(format);
            if (file == null || !file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                || file.Length <= prefix.Length + extension.Length) return null;
            var digits = file.Substring(prefix.Length, file.Length - prefix.Length - extension.Length);
            return digits.All(c => c >= '0' && c <= '9')
                && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var frame) ? (int?)frame : null;
        }

        public static string FilePattern(string name, string format, int count) =>
            SafeFileName(name) + (NormalizeFormat(format) == "avi" ? "" : "_" + new string('#', FrameDigits(count)))
                + "." + NormalizeFormat(format);

        private static int FrameDigits(int count) => Math.Max(4, count.ToString(CultureInfo.InvariantCulture).Length);

        public static string DetectKind(byte[] head)
        {
            if (head == null) return "unknown";
            if (head.Length >= 8 && head.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "png";
            if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return "jpg";
            if (head.Length >= 12 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F'
                && head[8] == 'A' && head[9] == 'V' && head[10] == 'I' && head[11] == ' ') return "avi";
            if (head.Length >= 2 && head[0] == 'B' && head[1] == 'M') return "bmp";
            return "unknown";
        }

        public static bool IsForbiddenOutputRoot(string path, IEnumerable<string> roots)
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return (roots ?? Enumerable.Empty<string>()).Where(root => !string.IsNullOrWhiteSpace(root)).Any(root =>
            {
                var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
                    || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            });
        }

        public static bool HasExistingOutput(IEnumerable<string> fileNames, string animationName, string format) =>
            fileNames != null && fileNames.Any(name => Path.GetFileName(name).StartsWith(SafeFileName(animationName), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(name), "." + NormalizeFormat(format), StringComparison.OrdinalIgnoreCase));

        internal static bool IsFullyQualified(string path)
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
