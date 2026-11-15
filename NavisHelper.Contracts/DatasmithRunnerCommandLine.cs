using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    public static class DatasmithRunnerCommandLine
    {
        public static IReadOnlyList<string> BuildArguments(string navisworksDirectory, string sourcePath,
            string outputPath, int? mergeLevel, double[] origin, bool includeMetadata, bool overwrite, string jobName)
        {
            if (mergeLevel < 0)
                throw new ArgumentException("mergeLevel must be nonnegative when supplied.");
            if (origin != null && (origin.Length != 3 || origin.Any(value => double.IsNaN(value) || double.IsInfinity(value))))
                throw new ArgumentException("origin requires three finite numbers: x,y,z.");
            var arguments = new List<string>
            {
                "datasmith-export", "--navisworks-dir", navisworksDirectory,
                "--source", sourcePath, "--output", outputPath,
            };
            if (mergeLevel.HasValue)
                arguments.AddRange(new[] { "--merge", mergeLevel.Value.ToString(CultureInfo.InvariantCulture) });
            if (origin != null)
                arguments.AddRange(new[] { "--origin", string.Join(",", origin.Select(value => value.ToString("R", CultureInfo.InvariantCulture))) });
            if (includeMetadata)
                arguments.Add("--include-metadata");
            if (overwrite)
                arguments.Add("--overwrite");
            if (jobName != null)
                arguments.AddRange(new[] { "--job", jobName });
            return arguments;
        }

        public static string Join(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(Quote));

        private static string Quote(string value)
        {
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
                return value;
            var quoted = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                // A quote needs twice the preceding slashes plus its own escape.
                quoted.Append('\\', character == '"' ? 2 * backslashes + 1 : backslashes);
                quoted.Append(character);
                backslashes = 0;
            }
            return quoted.Append('\\', 2 * backslashes).Append('"').ToString();
        }
    }
}
