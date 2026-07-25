using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NavisHelper.McpConfigurator;

internal static partial class Program
{
    private static JsonObject ReadJsonObject(string path)
    {
        if (!File.Exists(path))
            return new JsonObject();

        var text = File.ReadAllText(path, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();

        var node = JsonNode.Parse(text);
        return node as JsonObject ?? new JsonObject();
    }

    private static JsonObject EnsureObject(JsonObject root, string propertyName)
    {
        if (root[propertyName] is JsonObject existing)
            return existing;

        var created = new JsonObject();
        root[propertyName] = created;
        return created;
    }

    private static void WriteJson(string path, JsonObject root)
    {
        WriteTextAtomic(path, root.ToJsonString(JsonOptions) + Environment.NewLine);
    }

    private static void WriteTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp_navishelper_" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempPath, content, new UTF8Encoding(false));
        File.Move(tempPath, path, overwrite: true);
    }

    private static string RemoveTomlTableTree(string text, string tableName)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        var result = new List<string>();
        var skipping = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var header = TryParseTomlTableHeader(trimmed);
            if (header != null && IsTomlTableInTree(header, tableName))
            {
                skipping = true;
                continue;
            }

            if (skipping && header != null)
            {
                skipping = false;
            }

            if (!skipping && IsTomlDottedKeyInTree(trimmed, tableName))
                continue;

            if (!skipping)
                result.Add(line);
        }

        return string.Join(Environment.NewLine, result).TrimEnd() + Environment.NewLine;
    }

    private static string? TryParseTomlTableHeader(string trimmedLine)
    {
        if (string.IsNullOrWhiteSpace(trimmedLine))
            return null;

        var commentIndex = trimmedLine.IndexOf('#');
        if (commentIndex >= 0)
            trimmedLine = trimmedLine.Substring(0, commentIndex).Trim();

        if (!trimmedLine.StartsWith("[", StringComparison.Ordinal) ||
            !trimmedLine.EndsWith("]", StringComparison.Ordinal) ||
            trimmedLine.StartsWith("[[", StringComparison.Ordinal))
        {
            return null;
        }

        var inner = trimmedLine.Substring(1, trimmedLine.Length - 2).Trim();
        return inner.Length == 0 ? null : inner.Replace(" ", string.Empty);
    }

    private static bool IsTomlTableInTree(string header, string tableName)
    {
        return string.Equals(header, tableName, StringComparison.OrdinalIgnoreCase) ||
               header.StartsWith(tableName + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTomlDottedKeyInTree(string trimmedLine, string tableName)
    {
        if (string.IsNullOrWhiteSpace(trimmedLine))
            return false;

        var commentIndex = trimmedLine.IndexOf('#');
        if (commentIndex >= 0)
            trimmedLine = trimmedLine.Substring(0, commentIndex).Trim();

        var equalsIndex = trimmedLine.IndexOf('=');
        if (equalsIndex <= 0)
            return false;

        var key = trimmedLine.Substring(0, equalsIndex).Replace(" ", string.Empty);
        return key.StartsWith(tableName + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToTomlString(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
