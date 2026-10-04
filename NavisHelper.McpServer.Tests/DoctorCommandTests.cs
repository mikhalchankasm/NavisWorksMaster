using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using NavisHelper.McpConfigurator;
using Xunit;
using Configurator = NavisHelper.McpConfigurator.Program;

namespace NavisHelper.McpServer.Tests;

[Collection("Blocking infrastructure")]
public sealed class DoctorCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NavisHelperTests", Guid.NewGuid().ToString("N"));
    private static readonly AssemblyEvidence Current = new("readable", "2.10.0.0", new string('A', 64));

    public DoctorCommandTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void MetadataReadsActualAssemblySnapshotWithoutLoadingIt()
    {
        var assembly = typeof(Configurator).Assembly;
        var copy = Path.Combine(_directory, "sample.dll");
        File.Copy(assembly.Location, copy);
        var bytes = File.ReadAllBytes(copy);
        var result = AssemblyMetadata.Read(copy, assembly.GetName().Name);
        Assert.Equal("readable", result.Status);
        Assert.Equal(assembly.GetName().Version.ToString(), result.Version);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), result.Sha256);
        Assert.Equal(bytes, File.ReadAllBytes(copy));
        Assert.Single(Directory.GetFiles(_directory));
        Assert.Equal(new AssemblyEvidence("invalid_assembly"), AssemblyMetadata.Read(copy, "Other"));
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(),
            loaded => !loaded.IsDynamic && loaded.Location == copy);
    }

    [Fact]
    public void MissingCorruptAndOversizedAssembliesHaveSanitizedStatuses()
    {
        var file = Path.Combine(_directory, "sample.dll");
        Assert.Equal(new AssemblyEvidence("missing"), AssemblyMetadata.Read(file, "NavisHelper"));
        File.WriteAllText(file, "SENTINEL_SECRET");
        Assert.Equal(new AssemblyEvidence("invalid_assembly"), AssemblyMetadata.Read(file, "NavisHelper"));
        using (var stream = File.Open(file, FileMode.Create))
            stream.SetLength(AssemblyMetadata.MaximumBytes + 1);
        Assert.Equal(new AssemblyEvidence("file_too_large"), AssemblyMetadata.Read(file, "NavisHelper"));
    }

    [Fact]
    public void EqualVersionsRemainOfflineEvidence()
    {
        var report = Report((_, _) => Current);
        Assert.Equal("consistent", report.VersionAgreement);
        Assert.Equal("unverified", report.Readiness);
        Assert.Equal("user_file_only", report.ConfigurationScope);
        Assert.Equal(0, DoctorCommand.ExitCode(report));
        Assert.Equal(4, report.Plugins.Count);
        Assert.Contains("VerifyBuild", report.NextActions);
        Assert.Contains("ReloadClient", report.NextActions);
        Assert.Contains("HealthCheck", report.NextActions);
    }

    [Theory]
    [InlineData("missing", null)]
    [InlineData("invalid_assembly", null)]
    [InlineData("read_failed", null)]
    [InlineData("readable", "2.9.0.0")]
    public void PluginFailureOrMismatchRequiresRepair(string status, string version)
    {
        var report = Report((_, name) => name == "NavisHelper" ? new(status, version) : Current);
        Assert.Equal(status == "readable" ? "mismatch" : "unverified", report.VersionAgreement);
        Assert.Equal(1, DoctorCommand.ExitCode(report));
        Assert.Contains("RepairInstallation", report.NextActions);
        Assert.Equal("unverified", report.Readiness);
    }

    [Fact]
    public void OneMismatchedSdkIsReportedEvenWhenAnotherIsMissing()
    {
        var report = Report((path, name) => name != "NavisHelper" ? Current :
            path.Contains("2024") ? new("missing") : new("readable", "2.9.0.0"));
        Assert.Equal("mismatch", report.VersionAgreement);
        Assert.Equal("missing", report.Plugins["2024"].Status);
    }

    [Fact]
    public void PluginVersionDriftIsVisibleWhenServerIsMissing()
    {
        var report = Report((path, name) => name == "NavisHelper.McpServer" ? new("missing") :
            path.Contains("2024") ? new("readable", "2.9.0.0") : Current);
        Assert.Equal("mismatch", report.VersionAgreement);
        Assert.Contains("RepairInstallation", report.NextActions);
    }

    [Theory]
    [InlineData("claude-desktop", "claude_desktop_config.json")]
    [InlineData("cursor", "mcp.json")]
    [InlineData("kimi", "mcp.json")]
    [InlineData("opencode", "opencode.json")]
    [InlineData("zcode", "config.json")]
    [InlineData("codex", "config.toml")]
    public void AllFileClientsResolveThroughExistingAdapterAuthority(string client, string file)
    {
        var target = Configurator.ResolveDoctorTarget(client);
        Assert.NotNull(target);
        Assert.Equal(client, target.Value.Id);
        Assert.Equal(file, Path.GetFileName(target.Value.ConfigPath));
        Assert.Null(Configurator.ResolveDoctorTarget("all"));
        Assert.Null(Configurator.ResolveDoctorTarget("cursor,codex"));
        Assert.Null(Configurator.ResolveDoctorTarget("claude-code").Value.ConfigPath);
    }

    [Fact]
    public void MissingLauncherCannotBeHiddenByPresentDll()
    {
        var report = Report((_, _) => Current, exists: false);
        Assert.Equal("missing", report.LauncherStatus);
        Assert.Equal(1, DoctorCommand.ExitCode(report));
        Assert.Contains("RepairInstallation", report.NextActions);
    }

    [Fact]
    public void DotnetLauncherIsExplicitlyUnverified()
    {
        var report = DoctorCommand.Inspect("codex", new("configured", @"C:\bin\NavisHelper.McpServer.dll"),
            _directory, (_, _) => Current, _ => true);
        Assert.Equal("unverified", report.LauncherStatus);
        Assert.Equal(1, DoctorCommand.ExitCode(report));
        Assert.Contains("VerifyLauncher", report.NextActions);
    }

    [Fact]
    public void MissingConfigCanBeDiagnosedWithoutAnyInstallationOrSideEffects()
    {
        using var output = new StringWriter();
        var code = DoctorCommand.Run("cursor", Path.Combine(_directory, "absent.json"),
            Path.Combine(_directory, "bundle"), true, output);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(1, code);
        Assert.Equal("no_config", json.RootElement.GetProperty("configurationStatus").GetString());
        Assert.Equal("unverified", json.RootElement.GetProperty("readiness").GetString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Fact]
    public void MalformedConfigNeverEscapesIntoTextOrJson()
    {
        var path = Path.Combine(_directory, "config.json");
        const string content = "{\"mcpServers\":SENTINEL_SECRET}";
        File.WriteAllText(path, content);
        foreach (var json in new[] { false, true })
        {
            using var output = new StringWriter();
            Assert.Equal(1, DoctorCommand.Run("cursor", path, _directory, json, output));
            Assert.DoesNotContain("SENTINEL_SECRET", output.ToString());
        }
        Assert.Equal(content, File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("en", "Unverified")]
    [InlineData("ru", "Не проверено")]
    public void TextIsLocalizedWhileJsonCodesStayStable(string language, string expected)
    {
        var oldCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var report = Report((_, _) => Current);
            using var output = new StringWriter();
            DoctorCommand.Write(report, false, output);
            Assert.Contains(expected, output.ToString());
            using var jsonOutput = new StringWriter();
            DoctorCommand.Write(report, true, jsonOutput);
            using var json = JsonDocument.Parse(jsonOutput.ToString());
            Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("unverified", json.RootElement.GetProperty("readiness").GetString());
            foreach (var key in report.NextActions.Append("Usage").Append("Help").Append("unsupported_setting"))
                Assert.NotEqual(key, DoctorCommand.Text(key));
        }
        finally { CultureInfo.CurrentUICulture = oldCulture; }
    }

    [Theory]
    [InlineData("--configure")]
    [InlineData("--remove")]
    [InlineData("--detect")]
    [InlineData("--dry-run")]
    [InlineData("--create-missing")]
    [InlineData("--mcp-server missing.exe")]
    public void ParsedConflictingOperationsCannotEnterDoctor(string extra)
    {
        var options = Configurator.Options.Parse(("--doctor --clients cursor " + extra).Split(' '));
        Assert.False(options.IsDoctorOnly);
        Assert.True(Configurator.Options.Parse(["--doctor", "--clients", "cursor", "--json"]).IsDoctorOnly);
    }

    [Theory]
    [InlineData("--doctor")]
    [InlineData("--doctor --clients all")]
    [InlineData("--doctor --clients cursor,codex")]
    [InlineData("--doctor --clients SENTINEL_SECRET --configure")]
    [InlineData("--doctor --clients SENTINEL_SECRET --remove")]
    [InlineData("--doctor --clients SENTINEL_SECRET --detect")]
    [InlineData("--doctor --clients SENTINEL_SECRET --mcp-server SENTINEL_SECRET")]
    [InlineData("--doctor --clients SENTINEL_SECRET --dry-run")]
    [InlineData("--doctor --clients SENTINEL_SECRET --create-missing")]
    [InlineData("--doctor --clients SENTINEL_SECRET --unknown")]
    [InlineData("--doctor --clients")]
    [InlineData("--doctor --clients cursor --help")]
    [InlineData("--json")]
    public void InvalidCliUsageIsSanitizedAndNeverReachesInstallResolution(string arguments)
    {
        var oldOutput = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(2, Configurator.Main((arguments + " --json").Split(' ')));
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal("usage_error", json.RootElement.GetProperty("status").GetString());
            Assert.DoesNotContain("SENTINEL_SECRET", output.ToString());
        }
        finally { Console.SetOut(oldOutput); }
    }

    private DoctorReport Report(Func<string, string, AssemblyEvidence> read, bool exists = true) =>
        DoctorCommand.Inspect("cursor", new("configured", @"C:\bin\NavisHelper.McpServer.exe"),
            _directory, read, _ => exists);

    public void Dispose() => Directory.Delete(_directory, true);
}
