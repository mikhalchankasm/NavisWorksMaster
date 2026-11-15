using System.Runtime.InteropServices;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance for #478 slice alpha. The panel (net48 plugin) will start the same
// AutomationRunner the server starts, so the runner's argument list, a Windows command line for
// net48 (no ProcessStartInfo.ArgumentList there) and the kill-on-close job object move into
// NavisHelper.Contracts, and the server uses them with its behaviour unchanged.
public sealed class DatasmithRunnerCommandLineTests
{
    private const string NavisDir = @"C:\Program Files\Autodesk\Navisworks Manage 2026";

    [Fact]
    public void TheArgumentListIsTheOneTheServerBuilds()
    {
        var request = new StartDatasmithExportRequest
        {
            SourcePath = @"D:\Models\A B\model.nwd", OutputPath = @"D:\Out\model.udatasmith",
            MergeLevel = 2, Origin = new[] { 1.5, -2, 0.125 }, IncludeMetadata = true, Overwrite = true,
        };

        var shared = DatasmithRunnerCommandLine.BuildArguments(NavisDir, request.SourcePath, request.OutputPath,
            request.MergeLevel, request.Origin, request.IncludeMetadata, request.Overwrite, @"Local\NavisHelper-Datasmith-x");

        Assert.Equal(new[]
        {
            "datasmith-export", "--navisworks-dir", NavisDir, "--source", request.SourcePath, "--output", request.OutputPath,
            "--merge", "2", "--origin", "1.5,-2,0.125", "--include-metadata", "--overwrite", "--job", @"Local\NavisHelper-Datasmith-x",
        }, shared);
        Assert.Equal(shared, DatasmithExportPlan.BuildRunnerArguments(request, NavisDir, @"Local\NavisHelper-Datasmith-x"));
    }

    [Fact]
    public void BadMergeOrOriginIsRefusedWithTheServerMessages()
    {
        var merge = Assert.Throws<ArgumentException>(() =>
            DatasmithRunnerCommandLine.BuildArguments(NavisDir, "s", "o", -1, null, false, false, null));
        Assert.Contains("mergeLevel must be nonnegative", merge.Message, StringComparison.Ordinal);
        var origin = Assert.Throws<ArgumentException>(() =>
            DatasmithRunnerCommandLine.BuildArguments(NavisDir, "s", "o", null, new[] { 1.0, double.NaN, 2 }, false, false, null));
        Assert.Contains("origin requires three finite numbers", origin.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"D:\Models\A B\model.nwd")]
    [InlineData(@"D:\path with trailing slash\")]
    [InlineData(@"say ""hi"" \""there\""")]
    [InlineData(@"C:\a\b\\\")]
    [InlineData("")]
    [InlineData("plain")]
    public void TheCommandLineRoundTripsThroughWindowsParsing(string value)
    {
        var arguments = new[] { "--source", value, "--x" };

        var line = DatasmithRunnerCommandLine.Join(arguments);

        Assert.Equal(arguments, Split("runner.exe " + line));
    }

    [Fact]
    public void TheKillOnCloseJobEndsItsProcessWhenDisposed()
    {
        var job = new KillOnCloseJob(@"Local\NavisHelper-Test-");
        Assert.StartsWith(@"Local\NavisHelper-Test-", job.Name, StringComparison.Ordinal);
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
        })!;
        try
        {
            job.Assign(process);
            job.Dispose();
            Assert.True(process.WaitForExit(10000), "the process ends with the job");
        }
        finally
        {
            if (!process.HasExited) process.Kill();
        }
    }

    [Fact]
    public void TheServerJobObjectUsesTheSharedOne()
    {
        var job = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper.McpServer", "Services", "RunnerJobObject.cs"));
        Assert.Contains("KillOnCloseJob", job, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateJobObject", job, StringComparison.Ordinal);
        var plan = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper.McpServer", "Services", "DatasmithExportPlan.cs"));
        Assert.Contains("DatasmithRunnerCommandLine.BuildArguments(", plan, StringComparison.Ordinal);
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static string[] Split(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        try
        {
            return Enumerable.Range(1, count - 1)
                .Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!)
                .ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }
}
