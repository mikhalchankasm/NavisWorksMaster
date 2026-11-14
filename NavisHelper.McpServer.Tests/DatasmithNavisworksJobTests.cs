using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NavisHelper.AutomationRunner;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #440 slice delta. L3 on Navisworks 2026 (comment 6880541926 on #440): the hidden Navisworks
// the runner drives is "Roamer.exe -Embedding", a COM local server started by DcomLaunch, not
// a child of the runner. An ended runner left it alive. The server now names the runner's
// kill-on-close job object, passes the name to the runner, and the runner puts the Navisworks
// COM server it caused into that job. The owner's own Navisworks must never be selected.
public sealed class DatasmithNavisworksJobTests
{
    private const string JobNamePattern = @"^Local\\NavisHelper-Datasmith-[0-9a-f]{32}$";
    private const string RoamerPath = "\"C:\\Program Files\\Autodesk\\Navisworks Manage 2026\\Roamer.exe\"";
    private const string NavisDir = @"C:\Program Files\Autodesk\Navisworks Manage 2026";

    private static NavisworksProcessInfo Process(int id, string commandLine, string image = "Roamer.exe") =>
        new NavisworksProcessInfo { ProcessId = id, ImageName = image, CommandLine = commandLine };

    private static IList<int> Select(int[] before, int[] joined, params NavisworksProcessInfo[] current) =>
        NavisworksProcessSelection.SelectNew(before, current, joined);

    [Fact]
    public void ANewNavisworksComServerIsSelected()
    {
        Assert.Equal(new[] { 48648 }, Select(new int[0], new int[0], Process(48648, RoamerPath + " -Embedding")));
    }

    [Fact]
    public void TheOwnersNavisworksIsNeverSelected()
    {
        Assert.Empty(Select(new int[0], new int[0],
            Process(100, RoamerPath),
            Process(101, RoamerPath + " \"D:\\models\\plant.nwd\""),
            Process(102, RoamerPath + " \"D:\\models\\x -Embedding.nwd\""),
            Process(103, RoamerPath + " \"D:\\models\\-Embedding.nwd\""),
            Process(104, null)));
    }

    [Fact]
    public void ComServersThatExistedBeforeOrWereJoinedAlreadyAreNotSelected()
    {
        Assert.Equal(new[] { 3 }, Select(new[] { 1 }, new[] { 2 },
            Process(1, RoamerPath + " -Embedding"),
            Process(2, RoamerPath + " -Embedding"),
            Process(3, RoamerPath + " -embedding")));
    }

    [Fact]
    public void OnlyRoamerIsSelected()
    {
        Assert.Empty(Select(new int[0], new int[0],
            Process(7, "\"C:\\Windows\\system32\\dllhost.exe\" -Embedding", "dllhost.exe"),
            Process(8, "\"C:\\tools\\Roamer.exe.bak\" -Embedding", "Roamer.exe.bak")));
        Assert.Equal(new[] { 9 }, Select(new int[0], new int[0], Process(9, RoamerPath + " -Embedding", "ROAMER.EXE")));
    }

    [Fact]
    public void TheRunnerAcceptsOnlyAServerJobName()
    {
        var name = @"Local\NavisHelper-Datasmith-" + new string('a', 32);
        var parsed = DatasmithExportArguments.Parse(Arguments("--job", name), out var error);
        Assert.Null(error);
        Assert.Equal(name, parsed.JobName);

        var without = DatasmithExportArguments.Parse(Arguments(), out error);
        Assert.Null(error);
        Assert.Null(without.JobName);

        foreach (var bad in new[]
        {
            @"Global\NavisHelper-Datasmith-" + new string('a', 32),
            @"Local\NavisHelper-Datasmith-" + new string('a', 31),
            @"Local\NavisHelper-Datasmith-" + new string('G', 32),
            @"Local\Other-" + new string('a', 32),
        })
        {
            Assert.Null(DatasmithExportArguments.Parse(Arguments("--job", bad), out error));
            Assert.Contains("--job", error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheServerPassesItsJobNameToTheRunner()
    {
        var request = new NavisHelper.Agent.Contracts.StartDatasmithExportRequest
        {
            SourcePath = @"D:\models\model.nwd",
            OutputPath = @"D:\out\model.udatasmith",
        };
        var name = @"Local\NavisHelper-Datasmith-" + new string('b', 32);

        var withJob = DatasmithExportPlan.BuildRunnerArguments(request, NavisDir, name);
        var at = withJob.ToList().IndexOf("--job");
        Assert.True(at > 0, "--job missing");
        Assert.Equal(name, withJob[at + 1]);
        Assert.DoesNotContain("--job", DatasmithExportPlan.BuildRunnerArguments(request, NavisDir));

        var service = File.ReadAllText(Path.Combine(RepositoryRoot(), "NavisHelper.McpServer", "Services", "DatasmithExportJobService.cs"));
        Assert.Contains("runnerJob.Name", service, StringComparison.Ordinal);
        var runner = File.ReadAllText(Path.Combine(RepositoryRoot(), "NavisHelper.AutomationRunner", "Program.cs"));
        Assert.Contains("NavisworksProcessSelection.SelectNew", runner + ReadRunnerSources(), StringComparison.Ordinal);
    }

    // Codex review of slice delta, both P1: kill-on-close fires on the LAST job handle, so the runner
    // may hold one only around an assignment; and NavisHelper activations are serialized, so a new
    // -Embedding Roamer seen in the window belongs to this runner, not to a concurrent export.
    [Fact]
    public void TheJoinerHoldsNoJobHandleAndActivatesOneAtATime()
    {
        var joiner = File.ReadAllText(Path.Combine(RepositoryRoot(), "NavisHelper.AutomationRunner", "NavisworksJobJoiner.cs"));

        Assert.DoesNotMatch(new Regex(@"private\s+(readonly\s+)?(NativeHandle|SafeHandle|IntPtr)\s+_job\b"), joiner);
        Assert.Matches(new Regex(@"using\s*\(\s*var\s+\w+\s*=\s*OpenJobObject\("), joiner);
        Assert.Contains(@"Local\NavisHelper-Datasmith-Activation", joiner, StringComparison.Ordinal);
        Assert.Contains("AbandonedMutexException", joiner, StringComparison.Ordinal);
        Assert.Contains("ReleaseMutex()", joiner, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNamedJobEndsAProcessAnotherProcessPutIntoIt()
    {
        using var job = new RunnerJobObject();
        Assert.Matches(new Regex(JobNamePattern), job.Name);

        using var process = System.Diagnostics.Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 120 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        });
        Assert.NotNull(process);
        try
        {
            // What the runner does: open the server's job by name and assign a process it did not start.
            var opened = OpenJobObject(JobAssignProcess | JobQuery, false, job.Name);
            Assert.True(opened != IntPtr.Zero, "OpenJobObject failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            try
            {
                Assert.True(AssignProcessToJobObject(opened, process.Handle),
                    "AssignProcessToJobObject failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
            finally
            {
                CloseHandle(opened);
            }
            Assert.False(process.WaitForExit(500), "The process ended before the server closed its job.");

            job.Dispose();

            Assert.True(process.WaitForExit(10_000), "The process outlived the server's job object.");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private static string[] Arguments(params string[] extra) =>
        new[] { "datasmith-export", "--navisworks-dir", NavisDir, "--source", @"D:\models\model.nwd", "--output", @"D:\out\model.udatasmith" }
            .Concat(extra).ToArray();

    private static string ReadRunnerSources() =>
        string.Concat(Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "NavisHelper.AutomationRunner"), "*.cs")
            .Select(File.ReadAllText));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }

    private const uint JobAssignProcess = 0x0001;
    private const uint JobQuery = 0x0004;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenJobObject(uint desiredAccess, bool inheritHandle, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
