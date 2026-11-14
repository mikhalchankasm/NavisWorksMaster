using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace NavisHelper.AutomationRunner
{
    public sealed class DatasmithExportArguments
    {
        public string NavisworksDirectory { get; set; }
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public int? MergeLevel { get; set; }
        public double[] Origin { get; set; }
        public bool IncludeMetadata { get; set; }
        public bool Overwrite { get; set; }
        public string JobName { get; set; }

        public static DatasmithExportArguments Parse(string[] args, out string error)
        {
            error = null;
            if (args == null || args.Length == 0 || args[0] != "datasmith-export")
                return Fail("Expected the datasmith-export command.", out error);

            var parsed = new DatasmithExportArguments();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i++)
            {
                string option = args[i];
                switch (option)
                {
                    case "--navisworks-dir":
                    case "--source":
                    case "--output":
                    case "--merge":
                    case "--origin":
                    case "--job":
                    case "--include-metadata":
                    case "--overwrite":
                        break;
                    default:
                        return Fail("Unknown option: " + option, out error);
                }

                if (!seen.Add(option))
                    return Fail("Repeated option: " + option, out error);
                if (option == "--include-metadata")
                {
                    parsed.IncludeMetadata = true;
                    continue;
                }
                if (option == "--overwrite")
                {
                    parsed.Overwrite = true;
                    continue;
                }
                if (i + 1 >= args.Length || string.IsNullOrEmpty(args[i + 1]) ||
                    args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    return Fail("Missing value for " + option, out error);

                string value = args[++i];
                switch (option)
                {
                    case "--navisworks-dir": parsed.NavisworksDirectory = value; break;
                    case "--source": parsed.SourcePath = value; break;
                    case "--output": parsed.OutputPath = value; break;
                    case "--job":
                        if (!Regex.IsMatch(value, @"\ALocal\\NavisHelper-Datasmith-[0-9a-f]{32}\z", RegexOptions.CultureInvariant))
                            return Fail("--job must be a server Datasmith job name.", out error);
                        parsed.JobName = value;
                        break;
                    case "--merge":
                        int merge;
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out merge) || merge < 0)
                            return Fail("--merge must be an integer >= 0.", out error);
                        parsed.MergeLevel = merge;
                        break;
                    case "--origin":
                        string[] parts = value.Split(',');
                        if (parts.Length != 3)
                            return Fail("--origin requires three finite numbers: x,y,z.", out error);
                        var origin = new double[3];
                        for (int j = 0; j < origin.Length; j++)
                        {
                            if (!double.TryParse(parts[j], NumberStyles.Float, CultureInfo.InvariantCulture, out origin[j]) ||
                                double.IsNaN(origin[j]) || double.IsInfinity(origin[j]))
                                return Fail("--origin requires three finite numbers: x,y,z.", out error);
                        }
                        parsed.Origin = origin;
                        break;
                }
            }

            foreach (string required in new[] { "--navisworks-dir", "--source", "--output" })
                if (!seen.Contains(required))
                    return Fail("Missing required option " + required, out error);

            if (!IsAbsolute(parsed.NavisworksDirectory))
                return Fail("--navisworks-dir must be an absolute drive or UNC path.", out error);
            if (!IsAbsolute(parsed.SourcePath))
                return Fail("--source must be an absolute drive or UNC path.", out error);
            if (!IsAbsolute(parsed.OutputPath))
                return Fail("--output must be an absolute drive or UNC path.", out error);

            string extension = Path.GetExtension(parsed.SourcePath);
            if (!string.Equals(extension, ".nwd", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".nwf", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".nwc", StringComparison.OrdinalIgnoreCase))
                return Fail("--source must have extension .nwd, .nwf or .nwc.", out error);
            if (!string.Equals(Path.GetExtension(parsed.OutputPath), ".udatasmith", StringComparison.OrdinalIgnoreCase))
                return Fail("--output must have extension .udatasmith.", out error);
            return parsed;
        }

        public string[] ToExporterParameters()
        {
            var parameters = new List<string> { OutputPath };
            if (MergeLevel.HasValue)
                parameters.Add("Merge=" + MergeLevel.Value.ToString(CultureInfo.InvariantCulture));
            if (Origin != null)
            {
                parameters.Add("Origin=" + Origin[0].ToString("R", CultureInfo.InvariantCulture) + ", " +
                    Origin[1].ToString("R", CultureInfo.InvariantCulture) + ", " +
                    Origin[2].ToString("R", CultureInfo.InvariantCulture));
            }
            parameters.Add("IncludeMetadata=" + (IncludeMetadata ? "true" : "false"));
            return parameters.ToArray();
        }

        private static bool IsAbsolute(string path)
        {
            try
            {
                if (!Path.IsPathRooted(path))
                    return false;
                string root = Path.GetPathRoot(path).Replace('/', '\\');
                if (root.Length == 3 && char.IsLetter(root[0]) && root[1] == ':' && root[2] == '\\')
                    return true;
                if (!root.StartsWith(@"\\", StringComparison.Ordinal))
                    return false;
                string[] segments = root.Substring(2).TrimEnd('\\').Split('\\');
                return segments.Length == 2 && segments[0].Length > 0 && segments[1].Length > 0 &&
                    segments[0] != "?" && segments[0] != ".";
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }

        private static DatasmithExportArguments Fail(string message, out string error)
        {
            error = message;
            return null;
        }
    }

    public static class DatasmithExporter
    {
        public const string PluginId = "DatasmithNavisworksExporter.EpicGames";

        /// <summary>
        /// The export counts only when the exporter returned 0 and the file is new or was
        /// rewritten during this run. With --overwrite an older file is already there, so its
        /// existence alone would report a stale export as a fresh one; comparing with the write
        /// time taken before the export holds whatever that time was, even one in the future.
        /// </summary>
        public static bool IsFreshExport(int? exporterReturnCode, bool outputExists, DateTime outputWriteUtc, DateTime? writeUtcBeforeExport)
        {
            return exporterReturnCode == 0 && outputExists &&
                (!writeUtcBeforeExport.HasValue || outputWriteUtc != writeUtcBeforeExport.Value);
        }
    }

    public static class RunnerExitCodes
    {
        public const int Ok = 0;
        public const int BadArguments = 2;
        public const int NavisworksNotFound = 3;
        public const int ExportFailed = 4;
        public const int Crashed = 5;
    }
}
