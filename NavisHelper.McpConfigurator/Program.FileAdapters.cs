using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NavisHelper.McpConfigurator;

internal static partial class Program
{
    private interface IMcpClientAdapter
    {
        string Id { get; }
        DetectionResult Detect();
        ConfigureResult Configure(string mcpServerPath, bool dryRun, bool createMissing);
        ConfigureResult Remove(bool dryRun);
    }

    private sealed record DetectionResult(string Status, string Detail, string? ConfigPath = null);

    private sealed record ConfigureResult(string Status, string Detail, string? BackupPath = null);

    private abstract class FileAdapter : IMcpClientAdapter
    {
        protected FileAdapter(string id, string displayName, string configPath, string clientRootPath)
        {
            Id = id;
            DisplayName = displayName;
            ConfigPath = configPath;
            ClientRootPath = clientRootPath;
        }

        public string Id { get; }
        protected string DisplayName { get; }
        protected string ConfigPath { get; }
        protected string ClientRootPath { get; }

        public virtual DetectionResult Detect()
        {
            if (!Directory.Exists(ClientRootPath))
                return new DetectionResult("missing", DisplayName + " client directory was not found; it will be skipped.", ConfigPath);

            return File.Exists(ConfigPath)
                ? new DetectionResult("found", DisplayName + " config found.", ConfigPath)
                : new DetectionResult("missing", DisplayName + " config not found. It will be created when configuring.", ConfigPath);
        }

        public ConfigureResult Configure(string mcpServerPath, bool dryRun, bool createMissing)
        {
            if (!Directory.Exists(ClientRootPath))
            {
                if (!createMissing)
                {
                    return new ConfigureResult(
                        "skipped",
                        $"{DisplayName} client directory was not found. Re-run with --create-missing to create {Path.GetDirectoryName(ConfigPath)}.");
                }
            }

            if (dryRun)
                return new ConfigureResult("dry-run", $"Would configure {DisplayName} at {ConfigPath}.");

            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var backupPath = BackupIfExists(ConfigPath);
            ConfigureFile(mcpServerPath);
            return new ConfigureResult("configured", $"{DisplayName} now has MCP server '{ServerName}'.", backupPath);
        }

        public ConfigureResult Remove(bool dryRun)
        {
            if (!File.Exists(ConfigPath))
                return new ConfigureResult("skipped", $"{DisplayName} config was not found.");

            if (dryRun)
                return new ConfigureResult("dry-run", $"Would remove NavisHelper MCP server from {DisplayName} at {ConfigPath}.");

            var backupPath = BackupIfExists(ConfigPath);
            var removed = RemoveFileEntry();
            return removed
                ? new ConfigureResult("removed", $"{DisplayName} no longer has MCP server '{ServerName}'.", backupPath)
                : new ConfigureResult("skipped", $"{DisplayName} config did not contain MCP server '{ServerName}'.", backupPath);
        }

        protected abstract void ConfigureFile(string mcpServerPath);

        protected abstract bool RemoveFileEntry();

        protected static string? BackupIfExists(string path)
        {
            if (!File.Exists(path))
                return null;

            var backupPath = path + ".bak_navishelper_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            File.Copy(path, backupPath, overwrite: false);
            return backupPath;
        }
    }

    private sealed class GenericMcpServersJsonAdapter : FileAdapter
    {
        public GenericMcpServersJsonAdapter(string id, string displayName, string configPath, string clientRootPath)
            : base(id, displayName, configPath, clientRootPath)
        {
        }

        protected override void ConfigureFile(string mcpServerPath)
        {
            var root = ReadJsonObject(ConfigPath);
            var servers = EnsureObject(root, "mcpServers");
            var server = EnsureObject(servers, ServerName);
            server["command"] = mcpServerPath;
            server["args"] = new JsonArray();
            WriteJson(ConfigPath, root);
        }

        protected override bool RemoveFileEntry()
        {
            var root = ReadJsonObject(ConfigPath);
            var servers = root["mcpServers"] as JsonObject;
            if (servers == null || !servers.Remove(ServerName))
                return false;

            WriteJson(ConfigPath, root);
            return true;
        }
    }

    private sealed class OpenCodeJsonAdapter : FileAdapter
    {
        public OpenCodeJsonAdapter(string configPath, string clientRootPath)
            : base("opencode", "opencode", configPath, clientRootPath)
        {
        }

        protected override void ConfigureFile(string mcpServerPath)
        {
            var root = ReadJsonObject(ConfigPath);
            root["$schema"] ??= "https://opencode.ai/config.json";
            var mcp = EnsureObject(root, "mcp");
            var server = EnsureObject(mcp, ServerName);
            server["type"] = "local";
            server["command"] = new JsonArray(mcpServerPath);
            server["enabled"] = true;
            WriteJson(ConfigPath, root);
        }

        protected override bool RemoveFileEntry()
        {
            var root = ReadJsonObject(ConfigPath);
            var mcp = root["mcp"] as JsonObject;
            if (mcp == null || !mcp.Remove(ServerName))
                return false;

            WriteJson(ConfigPath, root);
            return true;
        }
    }

    private sealed class CodexTomlAdapter : FileAdapter
    {
        public CodexTomlAdapter(string configPath, string clientRootPath)
            : base("codex", "Codex", configPath, clientRootPath)
        {
        }

        protected override void ConfigureFile(string mcpServerPath)
        {
            var text = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath, Encoding.UTF8) : string.Empty;
            text = RemoveTomlTableTree(text, "mcp_servers." + ServerName);

            if (!string.IsNullOrWhiteSpace(text) && !text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                text += Environment.NewLine;

            text += Environment.NewLine;
            text += "[mcp_servers." + ServerName + "]" + Environment.NewLine;
            text += "command = " + ToTomlString(mcpServerPath) + Environment.NewLine;
            text += "args = []" + Environment.NewLine;

            WriteTextAtomic(ConfigPath, text);
        }

        protected override bool RemoveFileEntry()
        {
            var original = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath, Encoding.UTF8) : string.Empty;
            var updated = RemoveTomlTableTree(original, "mcp_servers." + ServerName);
            if (string.Equals(original, updated, StringComparison.Ordinal))
                return false;

            WriteTextAtomic(ConfigPath, updated);
            return true;
        }
    }
}
