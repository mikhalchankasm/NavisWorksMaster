using System.Globalization;
using System.Reflection;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #440 slice β: start_datasmith_export runs NavisHelper.AutomationRunner.exe (slice α) as a
// job owned by the MCP server, so the live Navisworks session is never touched, and
// datasmith_export_status reports it. The plan (paths, runner arguments, the runner's result
// line) and the job state are pure code; the process itself is checked by source.
public sealed class DatasmithExportJobTests
{
    private static readonly DateTime Started = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
    private const string NavisDir = @"C:\Program Files\Autodesk\Navisworks Manage 2026";
    private const string Source = @"D:\models\plant.nwd";
    private const string Output = @"D:\out\plant.udatasmith";

    [Fact]
    public void TheRunnerGetsEveryOptionInItsOwnArgument()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var request = new StartDatasmithExportRequest
            {
                SourcePath = Source,
                OutputPath = Output,
                MergeLevel = 3,
                Origin = new[] { 1.5, -2.0, 30000000.0 },
                IncludeMetadata = true,
                Overwrite = true,
            };

            Assert.Equal(
                new[]
                {
                    "datasmith-export", "--navisworks-dir", NavisDir, "--source", Source, "--output", Output,
                    "--merge", "3", "--origin", "1.5,-2,30000000", "--include-metadata", "--overwrite",
                },
                DatasmithExportPlan.BuildRunnerArguments(request, NavisDir));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TheMinimalRequestPassesOnlyTheThreePaths()
    {
        var request = new StartDatasmithExportRequest { SourcePath = Source, OutputPath = Output };

        Assert.Equal(
            new[] { "datasmith-export", "--navisworks-dir", NavisDir, "--source", Source, "--output", Output },
            DatasmithExportPlan.BuildRunnerArguments(request, NavisDir));
    }

    [Fact]
    public void NavisworksIsFoundByVersionAndDefaultsTo2026()
    {
        var programFiles = Path.Combine(Path.GetTempPath(), "nh-datasmith-" + Guid.NewGuid().ToString("N"));
        var dir2026 = Path.Combine(programFiles, "Autodesk", "Navisworks Manage 2026");
        Directory.CreateDirectory(dir2026);
        File.WriteAllText(Path.Combine(dir2026, "Autodesk.Navisworks.Automation.dll"), string.Empty);
        try
        {
            Assert.Equal("2026", DatasmithExportPlan.DefaultNavisworksVersion);
            Assert.Equal(dir2026, DatasmithExportPlan.ResolveNavisworksDirectory("", programFiles));
            Assert.Equal(dir2026, DatasmithExportPlan.ResolveNavisworksDirectory(" 2026 ", programFiles));

            var missing = Assert.Throws<FileNotFoundException>(
                () => DatasmithExportPlan.ResolveNavisworksDirectory("2025", programFiles));
            Assert.Contains("Navisworks Manage 2025", missing.Message, StringComparison.Ordinal);

            // Epic ships no Datasmith exporter for Navisworks 2027.
            var unsupported = Assert.Throws<ArgumentException>(
                () => DatasmithExportPlan.ResolveNavisworksDirectory("2027", programFiles));
            Assert.Contains("2024, 2025, 2026", unsupported.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(programFiles, recursive: true);
        }
    }

    [Fact]
    public void TheRunnerSitsBesideTheServerUnlessAnOverrideNamesIt()
    {
        Assert.Equal("NAVISHELPER_AUTOMATION_RUNNER", DatasmithExportPlan.RunnerPathEnvironmentVariable);
        Assert.Equal(
            @"C:\bundle\Server\AutomationRunner\NavisHelper.AutomationRunner.exe",
            DatasmithExportPlan.ResolveRunnerPath(null, @"C:\bundle\Server\"));
        Assert.Equal(
            @"C:\bundle\Server\AutomationRunner\NavisHelper.AutomationRunner.exe",
            DatasmithExportPlan.ResolveRunnerPath("  ", @"C:\bundle\Server"));
        Assert.Equal(
            @"D:\tools\runner.exe",
            DatasmithExportPlan.ResolveRunnerPath(@" D:\tools\runner.exe ", @"C:\bundle\Server"));
    }

    [Theory]
    [InlineData(0, 3600)]
    [InlineData(60, 60)]
    [InlineData(14400, 14400)]
    public void TheTimeoutDefaultsToAnHour(int requested, int expected)
    {
        Assert.Equal(expected, DatasmithExportPlan.NormalizeTimeoutSeconds(requested));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(59)]
    [InlineData(14401)]
    public void ATimeoutOutsideAMinuteToFourHoursIsRefused(int requested)
    {
        var error = Assert.Throws<ArgumentException>(() => DatasmithExportPlan.NormalizeTimeoutSeconds(requested));
        Assert.Contains("60", error.Message, StringComparison.Ordinal);
        Assert.Contains("14400", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASuccessfulResultLineIsAnExport()
    {
        var outcome = DatasmithExportPlan.ParseRunnerOutput(
            "{\"ok\":true,\"exitCode\":0,\"exporterReturnCode\":0,\"outputPath\":\"D:\\\\out\\\\plant.udatasmith\",\"fileSizeBytes\":2048,\"elapsedMs\":9000,\"error\":null}\r\n",
            0);

        Assert.True(outcome.Ok);
        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(0, outcome.ExporterReturnCode);
        Assert.Equal(2048, outcome.FileSizeBytes);
        Assert.Null(outcome.Error);
    }

    [Fact]
    public void OnlyTheLastLineIsTheResult()
    {
        var outcome = DatasmithExportPlan.ParseRunnerOutput(
            "noise from a dependency\n{\"ok\":false,\"exitCode\":4,\"exporterReturnCode\":1,\"outputPath\":null,\"fileSizeBytes\":0,\"elapsedMs\":5,\"error\":\"Datasmith exporter returned 1.\"}\n\n",
            4);

        Assert.False(outcome.Ok);
        Assert.Equal(4, outcome.ExitCode);
        Assert.Equal(1, outcome.ExporterReturnCode);
        Assert.Equal("Datasmith exporter returned 1.", outcome.Error);
    }

    [Theory]
    [InlineData("", 5)]
    [InlineData("not json", 5)]
    [InlineData("{\"ok\":true", 0)]
    public void NoResultLineIsAFailureThatNamesTheExitCode(string stdout, int exitCode)
    {
        var outcome = DatasmithExportPlan.ParseRunnerOutput(stdout, exitCode);

        Assert.False(outcome.Ok);
        Assert.Equal(exitCode, outcome.ExitCode);
        Assert.Null(outcome.ExporterReturnCode);
        Assert.Contains("without a result line", outcome.Error, StringComparison.Ordinal);
        Assert.Contains("code " + exitCode.ToString(CultureInfo.InvariantCulture), outcome.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ASuccessLineFromAProcessThatFailedIsNotAnExport()
    {
        var outcome = DatasmithExportPlan.ParseRunnerOutput(
            "{\"ok\":true,\"exitCode\":0,\"exporterReturnCode\":0,\"outputPath\":\"D:\\\\out\\\\plant.udatasmith\",\"fileSizeBytes\":2048,\"elapsedMs\":9000,\"error\":null}",
            5);

        Assert.False(outcome.Ok);
        Assert.Equal(5, outcome.ExitCode);
        Assert.Contains("code 5", outcome.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void OneJobRunsAtATime()
    {
        var running = DatasmithExportJobState.StartNew("datasmith-1", Source, Output, null, Started);

        Assert.True(running.IsRunning);
        Assert.Equal(DatasmithExportJobStates.Running, running.State);
        var error = Assert.Throws<InvalidOperationException>(
            () => DatasmithExportJobState.StartNew("datasmith-2", Source, Output, running, Started.AddSeconds(1)));
        Assert.Contains("already running", error.Message, StringComparison.Ordinal);
        Assert.Contains("datasmith-1", error.Message, StringComparison.Ordinal);

        var done = running.Complete(
            DatasmithExportPlan.ParseRunnerOutput("{\"ok\":true,\"exitCode\":0,\"exporterReturnCode\":0,\"outputPath\":null,\"fileSizeBytes\":2048,\"elapsedMs\":1,\"error\":null}", 0),
            Started.AddMinutes(2));
        var next = DatasmithExportJobState.StartNew("datasmith-2", Source, Output, done, Started.AddMinutes(3));
        Assert.True(next.IsRunning);
    }

    [Fact]
    public void TheStatusCarriesTheRunnerOutcome()
    {
        var job = DatasmithExportJobState.StartNew("datasmith-1", Source, Output, null, Started);
        var running = job.BuildStatusResponse(Started.AddSeconds(30));
        Assert.Equal("datasmith-1", running.OperationId);
        Assert.True(running.IsRunning);
        Assert.Equal(30000, running.ElapsedMs);
        Assert.Null(running.CompletedAtUtc);

        var failed = job.Complete(
            DatasmithExportPlan.ParseRunnerOutput("{\"ok\":false,\"exitCode\":4,\"exporterReturnCode\":1,\"outputPath\":null,\"fileSizeBytes\":0,\"elapsedMs\":1,\"error\":\"Datasmith exporter returned 1.\"}", 4),
            Started.AddMinutes(1));
        var status = failed.BuildStatusResponse(Started.AddMinutes(5));

        Assert.Equal(DatasmithExportJobStates.Failed, status.State);
        Assert.False(status.IsRunning);
        Assert.Equal(Source, status.SourcePath);
        Assert.Equal(Output, status.OutputPath);
        Assert.Equal(4, status.RunnerExitCode);
        Assert.Equal(1, status.ExporterReturnCode);
        Assert.Equal("Datasmith exporter returned 1.", status.ErrorMessage);
        Assert.Equal(60000, status.ElapsedMs);
        Assert.Equal(Started.AddMinutes(1), status.CompletedAtUtc);

        var completed = DatasmithExportJobState.StartNew("datasmith-2", Source, Output, null, Started).Complete(
            DatasmithExportPlan.ParseRunnerOutput("{\"ok\":true,\"exitCode\":0,\"exporterReturnCode\":0,\"outputPath\":null,\"fileSizeBytes\":2048,\"elapsedMs\":1,\"error\":null}", 0),
            Started.AddMinutes(1)).BuildStatusResponse(Started.AddMinutes(1));
        Assert.Equal(DatasmithExportJobStates.Completed, completed.State);
        Assert.Equal(2048, completed.FileSizeBytes);
        Assert.Equal(string.Empty, completed.ErrorMessage);
    }

    [Fact]
    public void ATimedOutJobFailsAndSaysSo()
    {
        var job = DatasmithExportJobState.StartNew("datasmith-1", Source, Output, null, Started)
            .TimeOut(3600, Started.AddHours(1));
        var status = job.BuildStatusResponse(Started.AddHours(2));

        Assert.Equal(DatasmithExportJobStates.Failed, status.State);
        Assert.Contains("timed out after 3600 s", status.ErrorMessage, StringComparison.Ordinal);
        Assert.Null(status.RunnerExitCode);
        Assert.Equal(3600000, status.ElapsedMs);
    }

    [Fact]
    public void TheToolsDeclareWhatTheyDoAndNeedNoHost()
    {
        var start = typeof(NavisworksDatasmithExportTools).GetMethod("StartDatasmithExport");
        var status = typeof(NavisworksDatasmithExportTools).GetMethod("DatasmithExportStatus");
        Assert.NotNull(start);
        Assert.NotNull(status);

        var startCapabilities = start!.GetCustomAttribute<ToolCapabilitiesAttribute>()!;
        Assert.Equal(ToolEffects.Files | ToolEffects.Host, startCapabilities.Effects);
        Assert.False(startCapabilities.RequiresHost);
        Assert.False(startCapabilities.RequiresDocument);

        var statusCapabilities = status!.GetCustomAttribute<ToolCapabilitiesAttribute>()!;
        Assert.Equal(ToolEffects.None, statusCapabilities.Effects);
        Assert.False(statusCapabilities.RequiresHost);
    }

    [Fact]
    public void TheServerOwnsTheJobAndEndsTheRunnersProcessTree()
    {
        var root = RepositoryPaths.Root;
        var program = File.ReadAllText(Path.Combine(root, "NavisHelper.McpServer", "Program.cs"));
        Assert.Contains("AddSingleton<DatasmithExportJobService>()", program, StringComparison.Ordinal);
        Assert.Contains("WithTools<NavisworksDatasmithExportTools>()", program, StringComparison.Ordinal);

        var service = File.ReadAllText(Path.Combine(root, "NavisHelper.McpServer", "Services", "DatasmithExportJobService.cs"));
        Assert.Contains("ArgumentList", service, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardOutput = true", service, StringComparison.Ordinal);
        Assert.Contains("Kill(entireProcessTree: true)", service, StringComparison.Ordinal);
        Assert.Contains("DatasmithExportPlan.BuildRunnerArguments(", service, StringComparison.Ordinal);
        Assert.Contains("DatasmithExportPlan.ParseRunnerOutput(", service, StringComparison.Ordinal);

        var profile = File.ReadAllText(Path.Combine(root, "NavisHelper.McpServer", "Services", "McpToolProfile.cs"));
        Assert.Contains("\"start_datasmith_export\"", profile, StringComparison.Ordinal);
        Assert.Contains("\"datasmith_export_status\"", profile, StringComparison.Ordinal);
    }
}
