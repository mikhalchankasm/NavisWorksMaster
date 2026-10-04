using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NavisHelper.McpConfigurator;

// Only these fields may leave the reader. Never retain configuration text or exceptions.
internal sealed record ClientBinding(string Status, string? ServerPath = null);

internal static class ClientConfigurationReader
{
    internal const int MaximumBytes = 1024 * 1024;

    internal static ClientBinding Read(string clientId, string configPath)
    {
        if (clientId is not ("claude-desktop" or "cursor" or "kimi" or "opencode" or "zcode" or "codex"))
            return new("unsupported_client");
        try
        {
            using var stream = new FileStream(configPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var bytes = new byte[MaximumBytes + 1];
            var length = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (length > MaximumBytes)
                return new("config_too_large");
            var text = new UTF8Encoding(false, true).GetString(bytes, 0, length).TrimStart('\uFEFF');
            Func<string, object?> field;
            if (clientId == "codex")
            {
                var server = CodexTomlConfig.ReadServer(text);
                if (server == null)
                    return new("not_configured");
                field = key => server.TryGetValue(key, out var value) ? value : null;
            }
            else
            {
                var root = Program.ParseJsonObject(text);
                var path = clientId switch
                {
                    "opencode" => new[] { "mcp", "navishelper" },
                    "zcode" => new[] { "mcp", "servers", "navishelper" },
                    _ => new[] { "mcpServers", "navishelper" }
                };
                JsonObject? server = root;
                foreach (var key in path)
                {
                    if (!server.TryGetPropertyValue(key, out var value))
                        return new("not_configured");
                    server = value as JsonObject ?? throw new InvalidDataException();
                }
                field = key => Normalize(server[key]);
            }
            var enabled = Flag(field("enabled"), true);
            _ = Flag(field("disabled"), false);
            // Only documented flags are interpreted; other clients may ignore them.
            if (field("disabled") != null ||
                (field("enabled") != null && clientId is not ("codex" or "opencode")) ||
                (clientId == "codex" && field("experimental_environment") is { } placement &&
                 !Equals(placement, "local")))
                return new("unsupported_setting");
            if (!enabled)
                return new("disabled");
            if (field("url") != null || field("type") is { } transport &&
                !Equals(transport, clientId == "opencode" ? "local" : "stdio"))
                return new("unsupported_command");

            string command;
            string[] args;
            if (clientId == "opencode")
            {
                var parts = Strings(field("command"));
                if (parts.Length == 0 || field("args") != null)
                    return new("unsupported_command");
                command = parts[0];
                args = parts[1..];
            }
            else
            {
                command = field("command") as string ?? throw new InvalidDataException();
                args = field("args") is { } value ? Strings(value) : [];
            }
            if (IsLocalPath(command, "NavisHelper.McpServer.exe"))
                return new("configured", command);
            if ((command.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
                 command.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
                 IsLocalPath(command, "dotnet.exe")) &&
                args.Length > 0 && IsLocalPath(args[0], "NavisHelper.McpServer.dll"))
                return new("configured", args[0]);
            return new("unsupported_command");
        }
        catch (FileNotFoundException) { return new("no_config"); }
        catch (DirectoryNotFoundException) { return new("no_config"); }
        catch (UnauthorizedAccessException) { return new("read_failed"); }
        catch (InvalidDataException) { return new("malformed_config"); }
        catch (IOException) { return new("read_failed"); }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            return new("malformed_config");
        }
    }

    private static object? Normalize(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value when value.TryGetValue<bool>(out var flag) => flag,
        JsonArray array => array.Select(Normalize).ToArray(),
        _ => throw new InvalidDataException()
    };

    private static bool Flag(object? value, bool fallback) =>
        value == null ? fallback : value is bool flag ? flag : throw new InvalidDataException();

    private static string[] Strings(object? value)
    {
        if (value is not IEnumerable items || value is string)
            throw new InvalidDataException();
        return items.Cast<object?>().Select(item => item as string ?? throw new InvalidDataException()).ToArray();
    }

    // No environment expansion, shell parsing, UNC/network access, or command execution.
    internal static bool IsLocalPath(string path, string fileName) =>
        path.Length >= 4 && char.IsAsciiLetter(path[0]) && path[1] == ':' &&
        (path[2] is '\\' or '/') && path[3..].All(c =>
            !char.IsControl(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format &&
            !"<>:\"|?*".Contains(c)) &&
        path.Replace('\\', '/').Split('/').Last().Equals(fileName, StringComparison.OrdinalIgnoreCase);
}
