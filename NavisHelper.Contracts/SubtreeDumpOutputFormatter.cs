using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Shapes subtree dump rows without depending on Navisworks runtime types or file-system state.
    /// </summary>
    public static class SubtreeDumpOutputFormatter
    {
        public const string CsvFormat = "csv";
        public const string JsonlFormat = "jsonl";
        public const string InvalidFormatMessage = "format must be either 'csv' or 'jsonl'.";

        public static string NormalizeFormat(string format)
        {
            var normalized = string.IsNullOrWhiteSpace(format)
                ? CsvFormat
                : format.Trim().ToLowerInvariant();

            if (string.Equals(normalized, CsvFormat, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, JsonlFormat, StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            throw new ArgumentException(InvalidFormatMessage, nameof(format));
        }

        public static string BuildCsvHeader(bool includePath, bool includeSourceFile)
        {
            var value = "Name;DisplayName;Depth;IsHidden";
            if (includePath)
                value += ";Path";
            if (includeSourceFile)
                value += ";SourceFile";
            return value;
        }

        public static string BuildCsvRow(SubtreeDumpOutputRow row, bool includePath, bool includeSourceFile)
        {
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            var value = new StringBuilder();
            value.Append(EscapeCsv(row.Name));
            value.Append(';');
            value.Append(EscapeCsv(row.DisplayName));
            value.Append(';');
            value.Append(row.Depth.ToString(CultureInfo.InvariantCulture));
            value.Append(';');
            value.Append(row.IsHidden ? "true" : "false");
            if (includePath)
            {
                value.Append(';');
                value.Append(EscapeCsv(row.Path));
            }
            if (includeSourceFile)
            {
                value.Append(';');
                value.Append(EscapeCsv(row.SourceFile));
            }
            return value.ToString();
        }

        public static Dictionary<string, object> BuildJsonRow(SubtreeDumpOutputRow row, bool includePath, bool includeSourceFile)
        {
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            var value = new Dictionary<string, object>
            {
                { "name", row.Name ?? string.Empty },
                { "displayName", row.DisplayName ?? string.Empty },
                { "depth", row.Depth },
                { "isHidden", row.IsHidden },
            };
            if (includePath)
                value["path"] = row.Path ?? string.Empty;
            if (includeSourceFile)
                value["sourceFile"] = row.SourceFile ?? string.Empty;
            return value;
        }

        private static string EscapeCsv(string value)
        {
            value = value ?? string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }

    public sealed class SubtreeDumpOutputRow
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public int Depth { get; set; }
        public bool IsHidden { get; set; }
        public string Path { get; set; }
        public string SourceFile { get; set; }
    }
}
