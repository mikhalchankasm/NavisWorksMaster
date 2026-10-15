using System.Text.Json.Nodes;
using NavisHelper.McpConfigurator;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #290: Claude Desktop runs NavisHelper from its .mcpb extension; an old
// `mcpServers.navishelper` entry written before #280 starts a second server.
public sealed class ClaudeDesktopStaleEntryTests : IDisposable
{
    private const string Config =
        "{\"theme\":\"dark\",\"mcpServers\":{\"other\":{\"command\":\"other.exe\",\"args\":[\"--x\"]}," +
        "\"navishelper\":{\"command\":\"C:\\\\old\\\\NavisHelper.McpServer.exe\",\"args\":[]}}}";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "NavisHelperTests", Guid.NewGuid().ToString("N"));
    private readonly string _configPath;
    private readonly string _extensions;

    public ClaudeDesktopStaleEntryTests()
    {
        Directory.CreateDirectory(_root);
        _configPath = Path.Combine(_root, "claude_desktop_config.json");
        _extensions = Path.Combine(_root, "Claude Extensions");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* A leftover temp folder does not fail the test. */ }
    }

    private void InstallExtension(string name)
    {
        var server = Directory.CreateDirectory(Path.Combine(_extensions, name, "server")).FullName;
        File.WriteAllText(Path.Combine(server, "NavisHelper.McpServer.exe"), "stub");
    }

    private string[] Backups() => Directory.GetFiles(_root, "claude_desktop_config.json.bak_navishelper_*");

    [Fact]
    public void ExtensionInstalled_MatchesNavisHelperFoldersByNameOnly()
    {
        Assert.False(ClaudeDesktopStaleEntry.ExtensionInstalled(_extensions));
        InstallExtension("ant.dir.gh.vendor.othermcp");
        Assert.False(ClaudeDesktopStaleEntry.ExtensionInstalled(_extensions));
        InstallExtension("local.mcpb.navishelper-contributors.NavisHelper");
        Assert.True(ClaudeDesktopStaleEntry.ExtensionInstalled(_extensions));
    }

    [Fact]
    public void ANavisHelperFolderWithoutItsServerIsNotAnInstalledExtension()
    {
        // Removal is justified only by a working extension: an empty or broken
        // folder must not cost the user the config entry that still works.
        Directory.CreateDirectory(Path.Combine(_extensions, "navishelper-backup"));
        Directory.CreateDirectory(Path.Combine(_extensions, "local.mcpb.navishelper-contributors.navishelper", "server"));
        File.WriteAllText(_configPath, Config);

        Assert.False(ClaudeDesktopStaleEntry.ExtensionInstalled(_extensions));
        var result = ClaudeDesktopStaleEntry.RemoveIfExtensionInstalled(_configPath, _extensions, dryRun: false);
        Assert.Equal("no-extension", result.Status);
        Assert.Equal(Config, File.ReadAllText(_configPath));
    }

    [Fact]
    public void WithExtension_RemovesOnlyTheNavisHelperEntryAndBacksUpTheOriginal()
    {
        File.WriteAllText(_configPath, Config);
        InstallExtension("local.mcpb.navishelper-contributors.navishelper");

        var result = ClaudeDesktopStaleEntry.RemoveIfExtensionInstalled(_configPath, _extensions, dryRun: false);

        Assert.Equal("removed", result.Status);
        var backup = Assert.Single(Backups());
        Assert.Equal(backup, result.BackupPath);
        Assert.Equal(Config, File.ReadAllText(backup));
        var root = JsonNode.Parse(File.ReadAllText(_configPath))!.AsObject();
        Assert.Equal("dark", (string?)root["theme"]);
        var servers = root["mcpServers"]!.AsObject();
        Assert.False(servers.ContainsKey("navishelper"));
        Assert.Equal("other.exe", (string?)servers["other"]!["command"]);
        Assert.Equal("--x", (string?)servers["other"]!["args"]![0]);
    }

    [Fact]
    public void WithoutExtension_LeavesTheFileByteForByteAndWritesNoBackup()
    {
        File.WriteAllText(_configPath, Config);
        InstallExtension("ant.dir.gh.vendor.othermcp");

        var result = ClaudeDesktopStaleEntry.RemoveIfExtensionInstalled(_configPath, _extensions, dryRun: false);

        Assert.Equal("no-extension", result.Status);
        Assert.Null(result.BackupPath);
        Assert.Equal(Config, File.ReadAllText(_configPath));
        Assert.Empty(Backups());
    }

    [Fact]
    public void DryRun_ReportsTheRemovalWithoutChangingAnything()
    {
        File.WriteAllText(_configPath, Config);
        InstallExtension("local.mcpb.navishelper-contributors.navishelper");

        var result = ClaudeDesktopStaleEntry.RemoveIfExtensionInstalled(_configPath, _extensions, dryRun: true);

        Assert.Equal("dry-run", result.Status);
        Assert.Null(result.BackupPath);
        Assert.Equal(Config, File.ReadAllText(_configPath));
        Assert.Empty(Backups());
    }

    [Fact]
    public void WithExtensionButNoEntry_LeavesTheFileAndWritesNoBackup()
    {
        const string clean = "{\"mcpServers\":{\"other\":{\"command\":\"other.exe\"}}}";
        File.WriteAllText(_configPath, clean);
        InstallExtension("local.mcpb.navishelper-contributors.navishelper");

        var result = ClaudeDesktopStaleEntry.RemoveIfExtensionInstalled(_configPath, _extensions, dryRun: false);

        Assert.Equal("no-entry", result.Status);
        Assert.Equal(clean, File.ReadAllText(_configPath));
        Assert.Empty(Backups());
    }

    [Fact]
    public void WithExtensionButNoConfigFile_CreatesNothing()
    {
        InstallExtension("local.mcpb.navishelper-contributors.navishelper");

        var result = ClaudeDesktopStaleEntry.RemoveIfExtensionInstalled(_configPath, _extensions, dryRun: false);

        Assert.Equal("no-config", result.Status);
        Assert.False(File.Exists(_configPath));
    }
}
