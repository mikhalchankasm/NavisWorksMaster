using System.Globalization;
using System.Text;

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
