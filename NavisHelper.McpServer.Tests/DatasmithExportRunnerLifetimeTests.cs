using System.Diagnostics;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #440 slice β2, from the Codex review of #445: the Automation runner must not outlive its job.
// (1) The runner sits in a Windows Job Object that ends every process in it when its handle closes,
// so descendants die even after the runner itself has exited, and when the server exits.
// (2) The server disposes the job service on shutdown. (3) Runner output is read line by line
// into bounded tails instead of being buffered whole.
public sealed class DatasmithExportRunnerLifetimeTests
{
    [Fact]
    public void TheTailKeepsTheLastNonEmptyLine()
    {
        var tail = new RunnerStreamTail(2000);
        Assert.Null(tail.LastLine);
        Assert.Equal(string.Empty, tail.Text);

        tail.Append("noise");
        tail.Append("{\"ok\":true}");
        tail.Append("");
        tail.Append("   ");
        tail.Append(null);

        Assert.Equal("{\"ok\":true}", tail.LastLine);
        Assert.Equal("noise\n{\"ok\":true}", tail.Text);
    }

    [Fact]
    public void TheTailIsBoundedWhateverTheRunnerWrites()
    {
        var tail = new RunnerStreamTail(2000);
        var line = new string('x', 99);
        for (var index = 0; index < 100_000; index++)
            tail.Append(line + (index % 10).ToString(System.Globalization.CultureInfo.InvariantCulture));
        tail.Append("last diagnostic");

        Assert.Equal("last diagnostic", tail.LastLine);
        Assert.StartsWith("...", tail.Text, StringComparison.Ordinal);
        Assert.EndsWith("\nlast diagnostic", tail.Text, StringComparison.Ordinal);
        Assert.InRange(tail.Text.Length, 2000, 2003);
    }

    [Fact]
    public void AShortTailIsNotMarkedAsCut()
    {
        var tail = new RunnerStreamTail(20);
        tail.Append("0123456789");
        tail.Append("abcdefghi");

        Assert.Equal("0123456789\nabcdefghi", tail.Text);
        Assert.DoesNotContain("...", tail.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosingTheJobObjectEndsTheProcessInIt()
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", "ping", "-n", "120", "127.0.0.1" },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        try
        {
            var job = new RunnerJobObject();
            job.Assign(process);
            Assert.False(process.HasExited);

            job.Dispose();

            Assert.True(process.WaitForExit(30_000), "the process outlived its job object");
            job.Dispose();
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void TheServiceUsesTheJobObjectAndBoundedReading()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(DatasmithExportJobService)));
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(RunnerJobObject)));

        var root = RepositoryPaths.Root;
        var job = File.ReadAllText(Path.Combine(root, "NavisHelper.McpServer", "Services", "RunnerJobObject.cs"));
        Assert.Contains("CreateJobObject", job, StringComparison.Ordinal);
        Assert.Contains("SetInformationJobObject", job, StringComparison.Ordinal);
        Assert.Contains("AssignProcessToJobObject", job, StringComparison.Ordinal);
        Assert.Contains("0x2000", job, StringComparison.Ordinal); // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE

        var service = File.ReadAllText(Path.Combine(root, "NavisHelper.McpServer", "Services", "DatasmithExportJobService.cs"));
        Assert.Contains("new RunnerJobObject()", service, StringComparison.Ordinal);
        Assert.Contains(".Assign(process)", service, StringComparison.Ordinal);
        Assert.Contains("ReadLineAsync", service, StringComparison.Ordinal);
        Assert.Contains("new RunnerStreamTail(", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadToEndAsync", service, StringComparison.Ordinal);
        Assert.Contains("public void Dispose()", service, StringComparison.Ordinal);
    }
}
