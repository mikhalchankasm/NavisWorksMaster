using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NavisHelper.AutomationRunner
{
    public sealed class RunnerResult
    {
        public bool Ok { get; set; }
        public int ExitCode { get; set; }
        public int? ExporterReturnCode { get; set; }
        public string OutputPath { get; set; }
        public long FileSizeBytes { get; set; }
        public long ElapsedMs { get; set; }
        public string Error { get; set; }

        public string ToJsonLine()
        {
            return "{\"ok\":" + (Ok ? "true" : "false") +
                ",\"exitCode\":" + ExitCode.ToString(CultureInfo.InvariantCulture) +
                ",\"exporterReturnCode\":" + (ExporterReturnCode.HasValue
                    ? ExporterReturnCode.Value.ToString(CultureInfo.InvariantCulture) : "null") +
                ",\"outputPath\":" + Quote(OutputPath) +
                ",\"fileSizeBytes\":" + FileSizeBytes.ToString(CultureInfo.InvariantCulture) +
                ",\"elapsedMs\":" + ElapsedMs.ToString(CultureInfo.InvariantCulture) +
                ",\"error\":" + Quote(Error) + "}";
        }

        public static RunnerResult ParseJsonLine(string line)
        {
            if (line == null) return null;
            const string jsonString = "\"(?:[^\"\\\\\\x00-\\x1F]|\\\\(?:[\"\\\\/bfnrt]|u[0-9a-fA-F]{4}))*\"";
            const string scalar = "(?:" + jsonString + "|true|false|null|-?(?:0|[1-9][0-9]*)(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)";
            string pair = "(?<key>" + jsonString + ")\\s*:\\s*(?<value>" + scalar + ")";
            var match = Regex.Match(line, "\\A\\s*\\{\\s*" + pair + "(?:\\s*,\\s*" + pair + ")*\\s*\\}\\s*\\z",
                RegexOptions.CultureInvariant);
            if (!match.Success) return null;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < match.Groups["key"].Captures.Count; i++)
            {
                string key = Unquote(match.Groups["key"].Captures[i].Value);
                if (values.ContainsKey(key)) return null;
                values.Add(key, match.Groups["value"].Captures[i].Value);
            }
            string ok, code, error;
            int exitCode;
            if (!values.TryGetValue("ok", out ok) || (ok != "true" && ok != "false") ||
                !values.TryGetValue("exitCode", out code) ||
                !int.TryParse(code, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exitCode) ||
                !values.TryGetValue("error", out error) || (error != "null" && !error.StartsWith("\"", StringComparison.Ordinal)))
                return null;
            return new RunnerResult { Ok = ok == "true", ExitCode = exitCode, Error = error == "null" ? null : Unquote(error) };
        }

        private static string Unquote(string value)
        {
            var text = new StringBuilder();
            for (int i = 1; i < value.Length - 1; i++)
            {
                char character = value[i];
                if (character == '\\')
                {
                    character = value[++i];
                    switch (character)
                    {
                        case 'u':
                            character = (char)int.Parse(value.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            i += 4;
                            break;
                        case 'b': character = '\b'; break;
                        case 'f': character = '\f'; break;
                        case 'n': character = '\n'; break;
                        case 'r': character = '\r'; break;
                        case 't': character = '\t'; break;
                    }
                }
                text.Append(character);
            }
            return text.ToString();
        }

        private static string Quote(string value)
        {
            if (value == null)
                return "null";
            var json = new StringBuilder("\"");
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    default:
                        // ASCII only: a .NET Framework console writes in its own code page, which
                        // would turn a Cyrillic path into question marks.
                        if (character < ' ' || character > '~')
                            json.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                        else
                            json.Append(character);
                        break;
                }
            }
            return json.Append('"').ToString();
        }
    }
}
