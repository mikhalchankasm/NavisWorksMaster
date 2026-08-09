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

    private static string ReadInstaller()
    {
        return File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate NavisHelper.sln.");
    }
}
