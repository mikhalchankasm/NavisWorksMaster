using System.Text.Json;
using System.Text.Json.Nodes;

namespace NavisHelper.McpConfigurator;

internal static class ClaudeDesktopStaleEntry
{
    internal static bool ExtensionInstalled(string extensionsRoot) =>
        Directory.Exists(extensionsRoot) && Directory.EnumerateDirectories(extensionsRoot)
            .Any(directory => Path.GetFileName(directory).Contains("navishelper", StringComparison.OrdinalIgnoreCase));

    internal static StaleEntryResult RemoveIfExtensionInstalled(string configPath, string extensionsRoot, bool dryRun)
    {
        if (!ExtensionInstalled(extensionsRoot))
            return new StaleEntryResult("no-extension", "NavisHelper .mcpb extension was not found.");
        if (!File.Exists(configPath))
            return new StaleEntryResult("no-config", "Claude Desktop configuration was not found.");

        var root = Program.ReadJsonObject(configPath);
        if (root["mcpServers"] is not JsonObject servers || !servers.ContainsKey("navishelper"))
            return new StaleEntryResult("no-entry", "Claude Desktop has no stale NavisHelper entry.");
        if (dryRun)
            return new StaleEntryResult("dry-run", "Would remove the stale NavisHelper entry; Claude Desktop uses the .mcpb extension.");

        var backupPath = configPath + ".bak_navishelper_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        File.Copy(configPath, backupPath, overwrite: false);
        servers.Remove("navishelper");
        Program.WriteTextAtomic(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        return new StaleEntryResult("removed", "Removed the stale NavisHelper entry; Claude Desktop uses the .mcpb extension.", backupPath);
    }
}

internal sealed record StaleEntryResult(string Status, string Detail, string? BackupPath = null);
