using NavisHelper.Agent.Contracts;
using NavisHelper.AutomationRunner;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance for #483. Epic's Datasmith exporter exists only up to Navisworks 2026,
// and 2026 refuses a file saved by 2027 ("unknown version number"). When 2026 cannot open the
// source, the runner starts itself as a child ("nwd-copy") that opens the file in a hidden
// Navisworks 2027 and saves a temporary copy in the exporter's format through the plugin's
// PublishNwd add-in; the 2026 export then runs on that copy.
public sealed class DatasmithConvertFallbackTests
{
    private const string Navis2026 = @"C:\Program Files\Autodesk\Navisworks Manage 2026";
    private const string Navis2027 = @"C:\Program Files\Autodesk\Navisworks Manage 2027";
    private const string Source = @"D:\models\A B\model.nwd";
    private const string Output = @"D:\out\model.udatasmith";
    private const string Job = @"Local\NavisHelper-Datasmith-0123456789abcdef0123456789abcdef";

    private static string[] Export(params string[] extra) =>
        new[] { "datasmith-export", "--navisworks-dir", Navis2026, "--source", Source, "--output", Output }
            .Concat(extra).ToArray();

    [Fact]
    public void TheExportTakesTheConvertOptionsTogether()
    {
        var parsed = DatasmithExportArguments.Parse(
            Export("--convert-navisworks-dir", Navis2027, "--convert-file-version", "2026"), out var error);

        Assert.Null(error);
        Assert.Equal(Navis2027, parsed!.ConvertNavisworksDirectory);
        Assert.Equal(2026, parsed.ConvertFileVersion);
        Assert.Null(DatasmithExportArguments.Parse(Export(), out _)!.ConvertNavisworksDirectory);
    }

    [Theory]
    [InlineData("--convert-navisworks-dir", Navis2027)]
    [InlineData("--convert-file-version", "2026")]
    public void OneConvertOptionWithoutTheOtherIsRefused(string option, string value)
    {
        Assert.Null(DatasmithExportArguments.Parse(Export(option, value), out var error));
        Assert.Contains("--convert-", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Navisworks Manage 2027", "2026")]
    [InlineData(Navis2027, "26")]
    [InlineData(Navis2027, "next")]
    public void ABadConvertOptionIsRefused(string directory, string version)
    {
        Assert.Null(DatasmithExportArguments.Parse(
            Export("--convert-navisworks-dir", directory, "--convert-file-version", version), out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TheFallbackCopyCommandRoundTrips()
    {
        var export = DatasmithExportArguments.Parse(Export("--job", Job,
            "--convert-navisworks-dir", Navis2027, "--convert-file-version", "2026"), out _)!;
        var copyPath = NwdCopyArguments.TempCopyPath(@"C:\Temp", Source, Guid.Parse("0123456789abcdef0123456789abcdef"));

        var command = NwdCopyArguments.ForFallback(export, copyPath).ToCommandLine();
        var copy = NwdCopyArguments.Parse(command, out var error);

        Assert.Equal(@"C:\Temp\NavisHelper-Datasmith-0123456789abcdef0123456789abcdef\model.nwd", copyPath);
        Assert.Equal("nwd-copy", command[0]);
        Assert.Null(error);
        Assert.Equal(Navis2027, copy!.NavisworksDirectory);
        Assert.Equal(Source, copy.SourcePath);
        Assert.Equal(copyPath, copy.OutputPath);
        Assert.Equal(2026, copy.FileVersion);
        Assert.Equal(Job, copy.JobName);
        Assert.Equal(new[] { copyPath, "version=2026" }, copy.ToPublishParameters());
        Assert.Equal("PublishNwd.CBC", NwdCopyArguments.PublishPluginId);
    }

    [Theory]
    [InlineData("--output", @"D:\out\copy.nwf")]
    [InlineData("--source", @"D:\models\model.ifc")]
    [InlineData("--file-version", "x")]
    [InlineData("--job", @"Local\Other")]
    public void TheCopyCommandRefusesBadValues(string option, string value)
    {
        var args = new Dictionary<string, string>
        {
            ["--navisworks-dir"] = Navis2027, ["--source"] = Source, ["--output"] = @"D:\out\copy.nwd",
            ["--file-version"] = "2026", ["--job"] = Job,
        };
        args[option] = value;
        var command = new[] { "nwd-copy" }.Concat(args.SelectMany(pair => new[] { pair.Key, pair.Value })).ToArray();

        Assert.Null(NwdCopyArguments.Parse(command, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void OnlyOldCopyFoldersOfTheRunnerAreSwept()
    {
        var root = Path.Combine(Path.GetTempPath(), "nh483-" + Guid.NewGuid().ToString("N"));
        var now = DateTime.UtcNow;
        string Folder(string name, double hoursOld)
        {
            var path = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
            File.WriteAllText(Path.Combine(path, "model.nwd"), "x");
            Directory.SetLastWriteTimeUtc(path, now.AddHours(-hoursOld));
            return path;
        }
        try
        {
            var stale = Folder("NavisHelper-Datasmith-" + new string('a', 32), 48);
            var fresh = Folder("NavisHelper-Datasmith-" + new string('b', 32), 1);
            var foreign = Folder("NavisHelper-Datasmith-keep", 48);

            Assert.Equal(1, NwdCopyArguments.SweepStaleCopies(root, now));

            Assert.False(Directory.Exists(stale));
            Assert.True(Directory.Exists(fresh));
            Assert.True(Directory.Exists(foreign));
            Assert.Equal(0, NwdCopyArguments.SweepStaleCopies(Path.Combine(root, "missing"), now));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void TheRunnerReadsItsChildsResultLine()
    {
        var line = new RunnerResult { Ok = false, ExitCode = 4, Error = "Файл \"a\\b\" не открыт\nвторая строка" }.ToJsonLine();

        var parsed = RunnerResult.ParseJsonLine(line);

        Assert.False(parsed!.Ok);
        Assert.Equal(4, parsed.ExitCode);
        Assert.Equal("Файл \"a\\b\" не открыт\nвторая строка", parsed.Error);
        Assert.Null(RunnerResult.ParseJsonLine("not json"));
    }

    [Fact]
    public void TheCommandLineCarriesTheConvertOptionsLast()
    {
        var plain = DatasmithRunnerCommandLine.BuildArguments(Navis2026, Source, Output, null, null, false, false, Job);
        var converted = DatasmithRunnerCommandLine.BuildArguments(Navis2026, Source, Output, null, null, false, false, Job,
            Navis2027, 2026);

        Assert.Equal(plain.Concat(new[] { "--convert-navisworks-dir", Navis2027, "--convert-file-version", "2026" }), converted);
        Assert.Throws<ArgumentException>(() =>
            DatasmithRunnerCommandLine.BuildArguments(Navis2026, Source, Output, null, null, false, false, Job, Navis2027, null));
    }

    [Fact]
    public void TheServerPassesTheExportVersionAsTheCopyFormat()
    {
        var request = new StartDatasmithExportRequest { SourcePath = Source, OutputPath = Output, NavisworksVersion = "2024" };

        var args = DatasmithExportPlan.BuildRunnerArguments(request, Navis2026, Job, Navis2027);

        Assert.Equal(new[] { "--convert-navisworks-dir", Navis2027, "--convert-file-version", "2024" }, args.TakeLast(4));
        Assert.DoesNotContain("--convert-navisworks-dir", DatasmithExportPlan.BuildRunnerArguments(request, Navis2026, Job));
    }

    [Fact]
    public void The2027DirectoryIsFoundOnlyWithItsAutomationAssembly()
    {
        var programFiles = Path.Combine(Path.GetTempPath(), "nh483-pf-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(programFiles, "Autodesk", "Navisworks Manage 2027");
        try
        {
            Directory.CreateDirectory(directory);
            Assert.Null(DatasmithRunnerCommandLine.FindConvertNavisworksDirectory(programFiles));
            File.WriteAllText(Path.Combine(directory, "Autodesk.Navisworks.Automation.dll"), "");
            Assert.Equal(directory, DatasmithRunnerCommandLine.FindConvertNavisworksDirectory(programFiles));
        }
        finally
        {
            Directory.Delete(programFiles, true);
        }
    }

    [Theory]
    [InlineData("NavisHelper.AutomationRunner/Program.cs", "NwdCopyArguments.Parse(")]
    [InlineData("NavisHelper.AutomationRunner/Program.cs", "NwdCopyArguments.ForFallback(")]
    [InlineData("NavisHelper.AutomationRunner/Program.cs", "NwdCopyArguments.PublishPluginId")]
    [InlineData("NavisHelper.AutomationRunner/Program.cs", "NwdCopyArguments.SweepStaleCopies(")]
    [InlineData("NavisHelper.McpServer/Services/DatasmithExportJobService.cs", "DatasmithRunnerCommandLine.FindConvertNavisworksDirectory(")]
    [InlineData("NavisHelper/Agent/Services/DatasmithPanelExportJob.cs", "DatasmithRunnerCommandLine.FindConvertNavisworksDirectory(")]
    public void TheCallSitesUseTheFallback(string relativePath, string needle)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, relativePath));

        Assert.Contains(needle, source, StringComparison.Ordinal);
    }
}
