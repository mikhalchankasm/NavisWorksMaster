using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class InstallerSemanticsTests
{
    [Fact]
    public void FinishAction_IsOptionalAndDoesNotCreateMissingClientRoots()
    {
        var installer = ReadInstaller();
        var runSection = GetSection(installer, "Run");
        var entry = Assert.Single(
            runSection.Split('\n'),
            line => line.StartsWith("Filename:", StringComparison.Ordinal));

        Assert.Contains("--configure --clients all", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("--create-missing", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("--create-missing", installer, StringComparison.Ordinal);
        Assert.Contains("Description: \"{cm:ConfigureMcpClients}\"", entry, StringComparison.Ordinal);
        Assert.Contains("Flags: postinstall unchecked skipifsilent", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureShortcut_DoesNotCreateMissingClientRoots()
    {
        var iconsSection = GetSection(ReadInstaller(), "Icons");
        var entry = Assert.Single(
            iconsSection.Split('\n'),
            line => line.StartsWith(
                "Name: \"{group}\\{cm:ConfigureMcpShortcut}\"",
                StringComparison.Ordinal));

        Assert.Contains("--configure --clients all", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("--create-missing", entry, StringComparison.Ordinal);

        var installDelete = GetSection(ReadInstaller(), "InstallDelete");
        Assert.Contains(
            "Name: \"{group}\\Configure MCP clients.lnk\"",
            installDelete,
            StringComparison.Ordinal);
        Assert.Contains(
            "Name: \"{group}\\Configure detected MCP clients.lnk\"",
            installDelete,
            StringComparison.Ordinal);
        Assert.Contains(
            "Name: \"{group}\\Настроить обнаруженные MCP-клиенты.lnk\"",
            installDelete,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FinishAction_ExplainsOptionalConfigurationInEnglishAndRussian()
    {
        var messages = GetSection(ReadInstaller(), "CustomMessages");

        Assert.Contains(
            "english.ConfigureMcpClients=Optional: configure detected MCP client configs",
            messages,
            StringComparison.Ordinal);
        Assert.Contains(
            "russian.ConfigureMcpClients=Необязательно: настроить конфиги обнаруженных MCP-клиентов",
            messages,
            StringComparison.Ordinal);
        Assert.Contains(
            "english.ConfigureMcpShortcut=Configure detected MCP clients",
            messages,
            StringComparison.Ordinal);
        Assert.Contains(
            "russian.ConfigureMcpShortcut=Настроить обнаруженные MCP-клиенты",
            messages,
            StringComparison.Ordinal);
        Assert.Contains(
            "english.DetectMcpShortcut=Detect MCP clients",
            messages,
            StringComparison.Ordinal);
        Assert.Contains(
            "russian.DetectMcpShortcut=Обнаружить MCP-клиенты",
            messages,
            StringComparison.Ordinal);
    }

    // Owner decision 2026-10-12: {app}\McpServer is a junction to versions\<v>-<sha8>.
    // Copying files to it would overwrite the running version through the link, so the
    // installer stages the server and hands it to McpConfigurator --install-server.
    [Fact]
    public void ServerIsStagedAndInstalledBehindTheJunction()
    {
        var installer = ReadInstaller();
        var files = GetSection(installer, "Files");
        Assert.DoesNotContain("DestDir: \"{app}\\McpServer\"", files, StringComparison.Ordinal);
        Assert.Contains(
            "Source: \"{#SourceDir}\\McpServer\\*\"; DestDir: \"{app}\\McpServerPayload\"",
            files,
            StringComparison.Ordinal);

        var code = GetSection(installer, "Code");
        var postInstall = code.IndexOf("ssPostInstall", StringComparison.Ordinal);
        Assert.True(postInstall >= 0, "No ssPostInstall step installs the staged server.");
        var step = code.Substring(postInstall);
        Assert.Contains("--install-server", step, StringComparison.Ordinal);
        Assert.Contains("McpServerPayload", step, StringComparison.Ordinal);
        Assert.Contains("ResultCode", step, StringComparison.Ordinal);
        Assert.Contains("DelTree(ExpandConstant('{app}\\McpServerPayload')", step, StringComparison.Ordinal);

        // Clients keep the stable path behind the junction.
        Assert.Contains("--mcp-server \"\"{app}\\McpServer\\NavisHelper.McpServer.exe\"\"", GetSection(installer, "Run"), StringComparison.Ordinal);
    }

    [Fact]
    public void UninstallRemovesTheJunctionBeforeTheVersions()
    {
        var installer = ReadInstaller();
        var code = GetSection(installer, "Code");
        var uninstall = code.IndexOf("usUninstall", StringComparison.Ordinal);
        Assert.True(uninstall >= 0, "No usUninstall step removes the junction.");
        // RemoveDir deletes the link itself and never follows it into a version.
        Assert.Contains("RemoveDir(ExpandConstant('{app}\\McpServer'))", code.Substring(uninstall), StringComparison.Ordinal);
        Assert.Contains("Type: filesandordirs; Name: \"{app}\\versions\"", GetSection(installer, "UninstallDelete"), StringComparison.Ordinal);
    }

    [Fact]
    public void DevInstallScriptUsesTheVersionStore()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "tools", "install_local_mcp_server.ps1"));
        Assert.Contains("--install-server", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\"NavisHelper\\McpServer-\"", script, StringComparison.Ordinal);
    }

    private static string ReadInstaller()
    {
        return File.ReadAllText(Path.Combine(
                RepositoryPaths.Root,
                "installer",
                "NavisHelper.iss"))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private static string GetSection(string installer, string name)
    {
        var marker = "[" + name + "]";
        var start = installer.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Installer section {marker} was not found.");

        var contentStart = start + marker.Length;
        var nextSection = installer.IndexOf("\n[", contentStart, StringComparison.Ordinal);
        return nextSection < 0
            ? installer.Substring(contentStart)
            : installer.Substring(contentStart, nextSection - contentStart);
    }
}
