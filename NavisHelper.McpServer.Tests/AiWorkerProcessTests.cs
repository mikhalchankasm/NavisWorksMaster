using System.Diagnostics;
using NavisHelper.AI;
using Xunit;

namespace NavisHelper.McpServer.Tests;

[Collection("Blocking infrastructure")]
public sealed class AiWorkerProcessTests
{
    private static string PowerShell => Path.Combine(Environment.SystemDirectory,
        "WindowsPowerShell", "v1.0", "powershell.exe");

    [Fact]
    public async Task Cancellation_UnblocksLargeStdinWriteAndStopsOwnedChild()
    {
        var pidPath = Path.Combine(Path.GetTempPath(), "navishelper-worker-test-" + Guid.NewGuid() + ".pid");
        using var cancellation = new CancellationTokenSource();
        Process child = null;
        Task<AiWorkerRunResult> run = null;
        try
        {
            // The owned shell records its PID before it stops consuming the pipe.
            var quotedPath = pidPath.Replace("'", "''");
            var script = "[IO.File]::WriteAllText('" + quotedPath + ".writing', [string]$PID); " +
                "[IO.File]::Move('" + quotedPath + ".writing', '" + quotedPath +
                "'); Start-Sleep -Seconds 30\r\n#" + new string('x', 2 * 1024 * 1024);
            run = new AiWorkerProcessRunner().RunAsync(PowerShell, script, "", cancellation.Token);
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(pidPath) && deadline.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(25, TestContext.Current.CancellationToken);
            Assert.True(File.Exists(pidPath), "Owned worker did not start within 10 seconds.");
            child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(pidPath, TestContext.Current.CancellationToken)));
            Assert.False(run.IsCompleted);

            cancellation.Cancel();
            var result = await run.WaitAsync(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken);

            Assert.Equal(AiWorkerRunFailureKind.Cancelled, result.FailureKind);
            await child.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.True(child.HasExited);
        }
        finally
        {
            cancellation.Cancel();
            if (child == null && File.Exists(pidPath))
            {
                try { child = Process.GetProcessById(int.Parse(File.ReadAllText(pidPath))); }
                catch (ArgumentException) { }
            }
            if (child != null)
            {
                if (!child.HasExited)
                    child.Kill();
                // Cleanup must finish even when the test runner has cancelled this test.
                await child.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                child.Dispose();
            }
            if (run != null)
                await run.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            File.Delete(pidPath);
            File.Delete(pidPath + ".writing");
        }
    }

    [Fact]
    public async Task SuccessfulWorker_StillDrainsOutput()
    {
        var result = await new AiWorkerProcessRunner().RunAsync(PowerShell,
            "[Console]::Out.WriteLine('worker-response'); exit 0\r\n", "", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Contains("worker-response", result.StandardOutput);
    }

    [Fact]
    public async Task CancelledBeforeStart_DoesNotRequireAnExecutable()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new AiWorkerProcessRunner().RunAsync("missing.exe", "", "", cancellation.Token);
        Assert.Equal(AiWorkerRunFailureKind.Cancelled, result.FailureKind);
    }
}
