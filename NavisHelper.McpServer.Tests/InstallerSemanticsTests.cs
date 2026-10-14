using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class InstallerSemanticsTests
{
    // Owner decision 2026-10-14 (#277): client setup is on by default and names every
    // configurator client except Claude Desktop, which installs the bundled .mcpb instead.
    // The user can still clear the box, so the action stays optional.
    [Fact]
    public void FinishAction_IsOptionalAndDoesNotCreateMissingClientRoots()
    {
        var installer = ReadInstaller();
        var runSection = GetSection(installer, "Run");
        var entry = Assert.Single(
            runSection.Split('\n'),
            line => line.StartsWith("Filename:", StringComparison.Ordinal));

        AssertConfiguresEveryClientExceptClaudeDesktop(entry);
        Assert.DoesNotContain("--create-missing", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("--create-missing", installer, StringComparison.Ordinal);
        Assert.Contains("Description: \"{cm:ConfigureMcpClients}\"", entry, StringComparison.Ordinal);
        Assert.Contains("Flags: postinstall skipifsilent", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("unchecked", entry, StringComparison.Ordinal);
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

        AssertConfiguresEveryClientExceptClaudeDesktop(entry);
        Assert.DoesNotContain("--create-missing", entry, StringComparison.Ordinal);

        var installDelete = GetSection(ReadInstaller(), "InstallDelete");
        foreach (var oldShortcut in new[]
        {
            "Configure MCP clients.lnk",
            "Configure detected MCP clients.lnk",
            "Настроить обнаруженные MCP-клиенты.lnk",
            "Detect MCP clients.lnk",
            "Обнаружить MCP-клиенты.lnk",
        })
        {
            Assert.Contains("Name: \"{group}\\" + oldShortcut + "\"", installDelete, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FinishAction_ExplainsOptionalConfigurationInEnglishAndRussian()
    {
        var messages = GetSection(ReadInstaller(), "CustomMessages");

        foreach (var expected in new[]
        {
            "english.ConfigureMcpClients=Connect NavisHelper to the AI apps on this computer (all except Claude Desktop)",
            "russian.ConfigureMcpClients=Подключить NavisHelper к AI-программам на этом компьютере (кроме Claude Desktop)",
            "english.ConfigureMcpShortcut=Connect NavisHelper to AI apps",
            "russian.ConfigureMcpShortcut=Подключить NavisHelper к AI-программам",
            "english.DetectMcpShortcut=Show which AI apps were found",
            "russian.DetectMcpShortcut=Показать найденные AI-программы",
            "english.ClaudeDesktopShortcut=NavisHelper extension for Claude Desktop",
            "russian.ClaudeDesktopShortcut=Расширение NavisHelper для Claude Desktop",
        })
        {
            Assert.Contains(expected, messages, StringComparison.Ordinal);
        }
    }

    // #277: messages are for ordinary users, not developers.
    [Fact]
    public void RussianMessages_AvoidDeveloperJargon()
    {
        var installer = ReadInstaller();
        var russian = (GetSection(installer, "CustomMessages") + "\n" + GetSection(installer, "Messages"))
            .Split('\n')
            .Where(line => line.StartsWith("russian.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(russian);
        foreach (var line in russian)
        {
            foreach (var jargon in new[] { "installer", "elevated", "конфиг" })
            {
                Assert.DoesNotContain(jargon, line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // #277: the finish page names three next steps in both languages.
    [Fact]
    public void FinishPage_ListsThreeNextStepsInEnglishAndRussian()
    {
        var messages = GetSection(ReadInstaller(), "Messages");
        foreach (var language in new[] { "english", "russian" })
        {
            var label = Assert.Single(
                messages.Split('\n'),
                line => line.StartsWith(language + ".FinishedLabel=", StringComparison.Ordinal));

            Assert.Contains("%n1. ", label, StringComparison.Ordinal);
            Assert.Contains("%n2. ", label, StringComparison.Ordinal);
            Assert.Contains("%n3. ", label, StringComparison.Ordinal);
            Assert.DoesNotContain("%n4. ", label, StringComparison.Ordinal);
            // Step 1: the plugin adds a NavisHelper ribbon tab whose NavisHelper button opens the panel.
            Assert.Contains("NavisHelper", label, StringComparison.Ordinal);
            // Step 3: Claude Desktop installs the bundled extension from the shortcut's folder.
            Assert.Contains("Settings → Extensions", label, StringComparison.Ordinal);
            Assert.Contains("NavisHelper.mcpb", label, StringComparison.Ordinal);
        }
    }

    // #277: the EXE carries the Claude Desktop extension, a Start-menu shortcut opens its
    // folder, and uninstall removes that folder.
    [Fact]
    public void ClaudeDesktopExtension_IsShippedWithAFolderShortcutAndRemovedOnUninstall()
    {
        var installer = ReadInstaller();
        Assert.Contains(
            "Source: \"{#SourceDir}\\ClaudeDesktop\\NavisHelper.mcpb\"; DestDir: \"{app}\\ClaudeDesktop\"",
            GetSection(installer, "Files"),
            StringComparison.Ordinal);
        Assert.Contains(
            "Name: \"{group}\\{cm:ClaudeDesktopShortcut}\"; Filename: \"{app}\\ClaudeDesktop\"",
            GetSection(installer, "Icons"),
            StringComparison.Ordinal);
        Assert.Contains(
            "Type: filesandordirs; Name: \"{app}\\ClaudeDesktop\"",
            GetSection(installer, "UninstallDelete"),
            StringComparison.Ordinal);

        var build = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "tools", "build_installer.ps1"));
        Assert.Contains("build_mcpb.py", build, StringComparison.Ordinal);
        Assert.Contains("ClaudeDesktop", build, StringComparison.Ordinal);

        // The CI smoke installs and uninstalls with the .mcpb present.
        var smoke = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "scripts", "test_installer_upgrade.ps1"));
        Assert.Contains("ClaudeDesktop\\NavisHelper.mcpb", smoke, StringComparison.Ordinal);
    }

    // #277: the README install section starts with the EXE; the checksum script is a subsection.
    [Theory]
    [InlineData("README.md", "## Install", "### Verify the download")]
    [InlineData("README.ru.md", "## Установка", "### Проверка файла")]
    public void Readme_InstallSectionStartsWithTheExeAndKeepsTheChecksumInASubsection(
        string file,
        string installHeading,
        string checksumHeading)
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryPaths.Root, file)).Replace("\r\n", "\n", StringComparison.Ordinal);
        var install = readme.IndexOf("\n" + installHeading + "\n", StringComparison.Ordinal);
        Assert.True(install >= 0, $"{file} has no '{installHeading}' section.");
        var checksum = readme.IndexOf("\n" + checksumHeading + "\n", install, StringComparison.Ordinal);
        Assert.True(checksum > install, $"{file} has no '{checksumHeading}' subsection after '{installHeading}'.");

        var firstParagraph = readme.Substring(install + installHeading.Length + 2).TrimStart('\n').Split("\n\n")[0];
        Assert.Contains("NavisHelperSetup-", firstParagraph, StringComparison.Ordinal);
        Assert.Contains("releases/latest", firstParagraph, StringComparison.Ordinal);

        var beforeChecksum = readme.Substring(install, checksum - install);
        Assert.DoesNotContain("Get-FileHash", beforeChecksum, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", readme.Substring(checksum), StringComparison.Ordinal);
    }

    // #277: release notes tell users which asset to download.
    [Fact]
    public void ReleaseNotesTemplate_SaysWhichFileToDownload()
    {
        var path = Path.Combine(RepositoryPaths.Root, "docs", "releases", "TEMPLATE.md");
        Assert.True(File.Exists(path), "docs/releases/TEMPLATE.md was not found.");
        var template = File.ReadAllText(path);
        Assert.Contains("## Какой файл скачать", template, StringComparison.Ordinal);
        foreach (var asset in new[] { "NavisHelperSetup-", ".mcpb", ".zip", "release-receipt.json", "SHA256SUMS" })
        {
            Assert.Contains(asset, template, StringComparison.Ordinal);
        }
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

    // The explicit list is every configurator client except Claude Desktop; never "all".
    private static void AssertConfiguresEveryClientExceptClaudeDesktop(string entry)
    {
        Assert.DoesNotContain("claude-desktop", entry, StringComparison.Ordinal);
        var match = System.Text.RegularExpressions.Regex.Match(entry, @"--configure --clients ([^\s""]+)");
        Assert.True(match.Success, $"No --configure --clients list in: {entry}");
        var listed = match.Groups[1].Value.Split(',');
        Assert.DoesNotContain("all", listed);

        var expected = ConfiguratorClientIds()
            .Where(id => id != "claude-desktop")
            .OrderBy(id => id, StringComparer.Ordinal);
        Assert.Equal(expected, listed.OrderBy(id => id, StringComparer.Ordinal));
    }

    // The client ids come from the configurator itself, so a new client fails this test
    // until the installer lists it.
    private static IReadOnlyList<string> ConfiguratorClientIds()
    {
        var build = typeof(NavisHelper.McpConfigurator.Program).GetMethod(
            "BuildAdapters",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(build);
        var adapters = (System.Collections.IEnumerable)build.Invoke(null, null)!;
        var ids = adapters.Cast<object>()
            .Select(adapter => (string)adapter.GetType().GetInterfaces()
                .Single(type => type.Name == "IMcpClientAdapter")
                .GetProperty("Id")!
                .GetValue(adapter)!)
            .ToList();
        Assert.Contains("claude-desktop", ids);
        return ids;
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
