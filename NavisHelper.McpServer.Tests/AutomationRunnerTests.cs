using System.Globalization;
using System.Text.Json;
using NavisHelper.AutomationRunner;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #440 slice α: Epic's Datasmith exporter takes a target path only under the Navisworks
// Automation API (Application.IsAutomated); in the GUI it opens its own Save dialog. So the
// export runs in a hidden Navisworks driven by NavisHelper.AutomationRunner.exe (net48). Its
// argument rules, the exporter parameters it builds and its one-line JSON result are pure code,
// compiled here from the runner's own sources.
public sealed class AutomationRunnerTests
{
    private const string NavisDir = @"C:\Program Files\Autodesk\Navisworks Manage 2026";
    private const string Source = @"D:\models\model.nwd";
    private const string Output = @"D:\out\model.udatasmith";

    private static string[] Valid(params string[] extra) =>
        new[] { "datasmith-export", "--navisworks-dir", NavisDir, "--source", Source, "--output", Output }
            .Concat(extra).ToArray();

    private static DatasmithExportArguments Parse(params string[] args)
    {
        var parsed = DatasmithExportArguments.Parse(args, out var error);
        Assert.Null(error);
        Assert.NotNull(parsed);
        return parsed!;
    }

    [Fact]
    public void TheMinimalCommandParses()
    {
        var parsed = Parse(Valid());

        Assert.Equal(NavisDir, parsed.NavisworksDirectory);
        Assert.Equal(Source, parsed.SourcePath);
        Assert.Equal(Output, parsed.OutputPath);
        Assert.Null(parsed.MergeLevel);
        Assert.Null(parsed.Origin);
        Assert.False(parsed.IncludeMetadata);
        Assert.False(parsed.Overwrite);
    }

    [Fact]
    public void EveryOptionParses()
    {
        var parsed = Parse(Valid("--merge", "3", "--origin", "1.5,-2,30000000", "--include-metadata", "--overwrite"));

        Assert.Equal(3, parsed.MergeLevel);
        Assert.Equal(new[] { 1.5, -2.0, 30000000.0 }, parsed.Origin);
        Assert.True(parsed.IncludeMetadata);
        Assert.True(parsed.Overwrite);
    }

    [Fact]
    public void BadCommandsAreRefusedByName()
    {
        var cases = new (string Expected, string[] Args)[]
        {
            ("datasmith-export", Array.Empty<string>()),
            ("datasmith-export", new[] { "export", "--source", Source }),
            ("--source", new[] { "datasmith-export", "--navisworks-dir", NavisDir, "--output", Output }),
            ("--output", new[] { "datasmith-export", "--navisworks-dir", NavisDir, "--source", Source }),
            ("--navisworks-dir", new[] { "datasmith-export", "--source", Source, "--output", Output }),
            ("absolute", new[] { "datasmith-export", "--navisworks-dir", NavisDir, "--source", "model.nwd", "--output", Output }),
            ("absolute", new[] { "datasmith-export", "--navisworks-dir", NavisDir, "--source", Source, "--output", @"out\model.udatasmith" }),
            (".udatasmith", new[] { "datasmith-export", "--navisworks-dir", NavisDir, "--source", Source, "--output", @"D:\out\model.fbx" }),
            (".nwd", new[] { "datasmith-export", "--navisworks-dir", NavisDir, "--source", @"D:\models\model.ifc", "--output", Output }),
            ("--merge", Valid("--merge", "-1")),
            ("--merge", Valid("--merge", "x")),
            ("--merge", Valid("--merge")),
            ("--origin", Valid("--origin", "1,2")),
            ("--origin", Valid("--origin", "1,2,NaN")),
            ("--origin", Valid("--origin", "1,2,3,4")),
            ("--fast", Valid("--fast")),
            ("--source", Valid("--source", @"D:\models\other.nwd")),
        };

        foreach (var (expected, args) in cases)
        {
            var parsed = DatasmithExportArguments.Parse(args, out var error);
            Assert.Null(parsed);
            Assert.NotNull(error);
            Assert.Contains(expected, error, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(@"D:\models\model.nwf")]
    [InlineData(@"D:\models\model.nwc")]
    [InlineData(@"D:\models\MODEL.NWD")]
    public void EveryNavisworksFormatIsASource(string source)
    {
        var parsed = Parse("datasmith-export", "--navisworks-dir", NavisDir, "--source", source, "--output", Output);
        Assert.Equal(source, parsed.SourcePath);
    }

    [Fact]
    public void TheExporterGetsThePathAndAnExplicitMetadataSwitch()
    {
        Assert.Equal(new[] { Output, "IncludeMetadata=false" }, Parse(Valid()).ToExporterParameters());
    }

    [Fact]
    public void TheExporterParametersAreCultureInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // Russian Windows writes 1,5 for one and a half; the origin must not follow it,
            // on the way in or on the way out.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var parsed = Parse(Valid("--merge", "3", "--origin", "1.5,-2,30000000", "--include-metadata"));

            Assert.Equal(
                new[] { Output, "Merge=3", "Origin=1.5, -2, 30000000", "IncludeMetadata=true" },
                parsed.ToExporterParameters());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TheRunnerNamesEpicsPluginAndItsExitCodes()
    {
        Assert.Equal("DatasmithNavisworksExporter.EpicGames", DatasmithExporter.PluginId);
        Assert.Equal(0, RunnerExitCodes.Ok);
        Assert.Equal(2, RunnerExitCodes.BadArguments);
        Assert.Equal(3, RunnerExitCodes.NavisworksNotFound);
        Assert.Equal(4, RunnerExitCodes.ExportFailed);
        Assert.Equal(5, RunnerExitCodes.Crashed);
    }

    [Fact]
    public void OnlyAFileWrittenDuringThisRunIsAnExport()
    {
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        var earlier = now.AddDays(-1);
        var future = now.AddDays(3);

        // No file before: whatever the exporter left is new.
        Assert.True(DatasmithExporter.IsFreshExport(0, true, now, null));
        // --overwrite: the file was rewritten, so its write time moved.
        Assert.True(DatasmithExporter.IsFreshExport(0, true, now, earlier));
        // --overwrite and nothing written: the old file is still there, even one dated ahead.
        Assert.False(DatasmithExporter.IsFreshExport(0, true, earlier, earlier));
        Assert.False(DatasmithExporter.IsFreshExport(0, true, future, future));
        Assert.False(DatasmithExporter.IsFreshExport(0, false, DateTime.MinValue, null));
        Assert.False(DatasmithExporter.IsFreshExport(1, true, now, null));
        Assert.False(DatasmithExporter.IsFreshExport(null, true, now, null));
    }

    [Fact]
    public void TheResultLineIsAsciiWhateverTheConsoleCodePage()
    {
        var line = new RunnerResult { Ok = true, OutputPath = "D:\\Порт\\модель.udatasmith" }.ToJsonLine();

        Assert.All(line, character => Assert.InRange(character, ' ', '~'));
        using var json = JsonDocument.Parse(line);
        Assert.Equal("D:\\Порт\\модель.udatasmith", json.RootElement.GetProperty("outputPath").GetString());
    }

    [Fact]
    public void TheResultIsOneJsonLine()
    {
        var line = new RunnerResult
        {
            Ok = false,
            ExitCode = RunnerExitCodes.ExportFailed,
            ExporterReturnCode = 1,
            OutputPath = "D:\\out\\Порт \"A\".udatasmith",
            FileSizeBytes = 123,
            ElapsedMs = 45,
            Error = "line one\nline two\ttab",
        }.ToJsonLine();

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(4, root.GetProperty("exitCode").GetInt32());
        Assert.Equal(1, root.GetProperty("exporterReturnCode").GetInt32());
        Assert.Equal("D:\\out\\Порт \"A\".udatasmith", root.GetProperty("outputPath").GetString());
        Assert.Equal(123, root.GetProperty("fileSizeBytes").GetInt64());
        Assert.Equal(45, root.GetProperty("elapsedMs").GetInt64());
        Assert.Equal("line one\nline two\ttab", root.GetProperty("error").GetString());
    }

    [Fact]
    public void ASuccessfulResultHasANullErrorAndNoExporterCodeWhenItNeverRan()
    {
        using var ok = JsonDocument.Parse(new RunnerResult { Ok = true, ExitCode = 0, ExporterReturnCode = 0 }.ToJsonLine());
        Assert.Equal(JsonValueKind.Null, ok.RootElement.GetProperty("error").ValueKind);

        using var refused = JsonDocument.Parse(new RunnerResult { Ok = false, ExitCode = RunnerExitCodes.BadArguments, Error = "x" }.ToJsonLine());
        Assert.Equal(JsonValueKind.Null, refused.RootElement.GetProperty("exporterReturnCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, refused.RootElement.GetProperty("outputPath").ValueKind);
    }

    [Fact]
    public void TheRunnerBuildsWithoutTheNavisworksSdkAndCiBuildsIt()
    {
        var root = RepositoryPaths.Root;
        var project = File.ReadAllText(Path.Combine(root, "NavisHelper.AutomationRunner", "NavisHelper.AutomationRunner.csproj"));
        Assert.Contains("<TargetFramework>net48</TargetFramework>", project, StringComparison.Ordinal);
        Assert.Contains("<PlatformTarget>x64</PlatformTarget>", project, StringComparison.Ordinal);
        Assert.Contains("<OutputType>Exe</OutputType>", project, StringComparison.Ordinal);
        // The Automation API is loaded from the chosen Navisworks folder at run time.
        Assert.DoesNotContain("Autodesk", project, StringComparison.Ordinal);

        var program = File.ReadAllText(Path.Combine(root, "NavisHelper.AutomationRunner", "Program.cs"));
        Assert.Contains("Assembly.LoadFrom(", program, StringComparison.Ordinal);
        Assert.Contains("Autodesk.Navisworks.Automation.dll", program, StringComparison.Ordinal);
        Assert.Contains("ExecuteAddInPlugin", program, StringComparison.Ordinal);
        Assert.Contains("DatasmithExporter.PluginId", program, StringComparison.Ordinal);
        Assert.Contains("Dispose", program, StringComparison.Ordinal);

        var ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        Assert.Contains("NavisHelper.AutomationRunner/NavisHelper.AutomationRunner.csproj", ci, StringComparison.Ordinal);
    }
}
