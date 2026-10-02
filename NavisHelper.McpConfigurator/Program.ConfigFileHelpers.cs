using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NavisHelper.McpConfigurator;

internal static partial class Program
{
    internal static JsonObject ReadJsonObject(string path)
    {
        if (!File.Exists(path))
            return new JsonObject();

        var text = File.ReadAllText(path, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();

        var node = JsonNode.Parse(text);
        return node as JsonObject ?? throw new InvalidDataException("Client configuration root must be a JSON object.");
    }

    internal static JsonObject EnsureObject(JsonObject root, string propertyName)
    {
        if (root[propertyName] is JsonObject existing)
            return existing;

        if (root[propertyName] != null)
            throw new InvalidDataException("Client configuration property '" + propertyName + "' must be a JSON object.");

        var created = new JsonObject();
        root[propertyName] = created;
        return created;
    }

    private static void WriteJson(string path, JsonObject root)
    {
        WriteTextAtomic(path, root.ToJsonString(JsonOptions) + Environment.NewLine);
    }

    internal static void WriteTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp_navishelper_" + Guid.NewGuid().ToString("N");
        var ownsTemporary = false;
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(content);
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(tempPath, path, overwrite: true);
            ownsTemporary = false;
        }
        finally
        {
            if (ownsTemporary)
            {
                try { File.Delete(tempPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

}
