using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace NavisHelper.AutomationRunner
{
    public sealed class NwdCopyArguments
    {
        public const string PublishPluginId = "PublishNwd.CBC";
        private const string CopyFolderPattern = @"\ANavisHelper-Datasmith-[0-9a-f]{32}\z";
        public string NavisworksDirectory { get; set; }
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public int FileVersion { get; set; }
        public string JobName { get; set; }

        public static NwdCopyArguments Parse(string[] args, out string error)
        {
            error = null;
            if (args == null || args.Length == 0 || args[0] != "nwd-copy")
                return Fail("Expected the nwd-copy command.", out error);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i++)
            {
                string option = args[i];
                if (option != "--navisworks-dir" && option != "--source" && option != "--output" &&
                    option != "--file-version" && option != "--job")
                    return Fail("Unknown option: " + option, out error);
                if (values.ContainsKey(option))
                    return Fail("Repeated option: " + option, out error);
                if (++i >= args.Length || string.IsNullOrEmpty(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                    return Fail("Missing value for " + option, out error);
                values.Add(option, args[i]);
            }
            foreach (string option in new[] { "--navisworks-dir", "--source", "--output", "--file-version" })
                if (!values.ContainsKey(option))
                    return Fail("Missing required option " + option, out error);
            foreach (string option in new[] { "--navisworks-dir", "--source", "--output" })
                if (!DatasmithExportArguments.IsAbsolute(values[option]))
                    return Fail(option + " must be an absolute drive or UNC path.", out error);
            int version;
            if (!int.TryParse(values["--file-version"], NumberStyles.Integer, CultureInfo.InvariantCulture, out version) || version < 2015)
                return Fail("--file-version must be an integer >= 2015.", out error);
            string extension = Path.GetExtension(values["--source"]);
            if (!string.Equals(extension, ".nwd", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".nwf", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".nwc", StringComparison.OrdinalIgnoreCase))
                return Fail("--source must have extension .nwd, .nwf or .nwc.", out error);
            if (!string.Equals(Path.GetExtension(values["--output"]), ".nwd", StringComparison.OrdinalIgnoreCase))
                return Fail("--output must have extension .nwd.", out error);
            string job;
            values.TryGetValue("--job", out job);
            if (job != null && !Regex.IsMatch(job, @"\ALocal\\NavisHelper-Datasmith-[0-9a-f]{32}\z", RegexOptions.CultureInvariant))
                return Fail("--job must be a server Datasmith job name.", out error);
            return new NwdCopyArguments { NavisworksDirectory = values["--navisworks-dir"], SourcePath = values["--source"],
                OutputPath = values["--output"], FileVersion = version, JobName = job };
        }

        public string[] ToPublishParameters() => new[] { OutputPath, "version=" + FileVersion.ToString(CultureInfo.InvariantCulture) };

        public string[] ToCommandLine()
        {
            var args = new List<string> { "nwd-copy", "--navisworks-dir", NavisworksDirectory,
                "--source", SourcePath, "--output", OutputPath, "--file-version", FileVersion.ToString(CultureInfo.InvariantCulture) };
            if (JobName != null) args.AddRange(new[] { "--job", JobName });
            return args.ToArray();
        }

        public static NwdCopyArguments ForFallback(DatasmithExportArguments export, string copyPath) =>
            new NwdCopyArguments { NavisworksDirectory = export.ConvertNavisworksDirectory, SourcePath = export.SourcePath,
                OutputPath = copyPath, FileVersion = export.ConvertFileVersion.Value, JobName = export.JobName };

        public static string TempCopyPath(string tempRoot, string sourcePath, Guid id) =>
            Path.Combine(tempRoot, "NavisHelper-Datasmith-" + id.ToString("N"), Path.GetFileNameWithoutExtension(sourcePath) + ".nwd");

        public static int SweepStaleCopies(string tempRoot, DateTime nowUtc)
        {
            int count = 0;
            try
            {
                if (!Directory.Exists(tempRoot)) return 0;
                foreach (string directory in Directory.GetDirectories(tempRoot, "NavisHelper-Datasmith-*"))
                {
                    try
                    {
                        if (Directory.GetLastWriteTimeUtc(directory) < nowUtc.AddHours(-24) && DeleteCopyDirectory(tempRoot, directory))
                            count++;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (SecurityException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            return count;
        }

        internal static bool DeleteCopyDirectory(string tempRoot, string directory)
        {
            string root = Path.GetFullPath(tempRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string target = Path.GetFullPath(directory);
            if (!string.Equals(Path.GetDirectoryName(target), root, StringComparison.OrdinalIgnoreCase) ||
                !Regex.IsMatch(Path.GetFileName(target), CopyFolderPattern, RegexOptions.CultureInvariant) ||
                !Directory.Exists(target) || (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                return false;
            Directory.Delete(target, true);
            return true;
        }

        internal static string JoinCommandLine(IEnumerable<string> arguments)
        {
            var quoted = new List<string>();
            foreach (string value in arguments)
            {
                var text = new StringBuilder("\"");
                int slashes = 0;
                foreach (char character in value)
                {
                    if (character == '\\') { slashes++; continue; }
                    text.Append('\\', character == '"' ? 2 * slashes + 1 : slashes).Append(character);
                    slashes = 0;
                }
                quoted.Add(text.Append('\\', 2 * slashes).Append('"').ToString());
            }
            return string.Join(" ", quoted);
        }

        private static NwdCopyArguments Fail(string message, out string error)
        {
            error = message;
            return null;
        }
    }
}
