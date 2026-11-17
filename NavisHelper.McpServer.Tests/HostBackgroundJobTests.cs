using System.Reflection;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #508 slice a (Port clash run, 2026-11-17): clash_tests_export on 1 334 tests took 570 s on the host and
// the MCP call timed out before the reply, so the result was lost although the host finished. With
// background=true the MCP server owns the host call, returns an operationId at once, and
// background_job_status reports the state and, when done, the tool's own response. The store is
// server code, so it is tested directly.
public sealed class HostBackgroundJobTests
{
    [Fact]
    public async Task AJobRunsAfterTheCallReturnsAndKeepsItsResult()
    {
        var store = new HostBackgroundJobStore();
        var gate = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        var started = store.Start("clash_tests_export", _ => gate.Task);
        Assert.StartsWith("job-", started.OperationId, StringComparison.Ordinal);
        Assert.Equal(HostBackgroundJobStates.Running, started.State);

        var running = store.Get(started.OperationId);
        Assert.True(running.Found);
        Assert.Equal("clash_tests_export", running.Command);
        Assert.Equal(HostBackgroundJobStates.Running, running.State);
        Assert.Null(running.Result);
        Assert.Null(running.CompletedAtUtc);

        var stillRunning = await store.WaitAsync(started.OperationId, TimeSpan.FromMilliseconds(50));
        Assert.Equal(HostBackgroundJobStates.Running, stillRunning.State);

        var result = new ClashTestsExportResponse { OutputWritten = true };
        gate.SetResult(result);
        var done = await store.WaitAsync(started.OperationId, TimeSpan.FromSeconds(10));
        Assert.Equal(HostBackgroundJobStates.Succeeded, done.State);
        Assert.Same(result, done.Result);
        Assert.NotNull(done.CompletedAtUtc);
        Assert.Null(done.ErrorCode);
    }

    [Fact]
    public async Task AFailedJobKeepsTheHostErrorCode()
    {
        var store = new HostBackgroundJobStore();
        var timedOut = store.Start("clash_tests_export", _ => Task.FromException<object>(new HostCallException(ErrorCodes.RequestTimeout, "late")));
        var crashed = store.Start("clash_tests_export", _ => Task.FromException<object>(new InvalidOperationException("boom")));

        var first = await store.WaitAsync(timedOut.OperationId, TimeSpan.FromSeconds(10));
        Assert.Equal(HostBackgroundJobStates.Failed, first.State);
        Assert.Equal(ErrorCodes.RequestTimeout, first.ErrorCode);
        Assert.Contains("late", first.ErrorMessage, StringComparison.Ordinal);
        Assert.Null(first.Result);

        var second = await store.WaitAsync(crashed.OperationId, TimeSpan.FromSeconds(10));
        Assert.Equal(HostBackgroundJobStates.Failed, second.State);
        Assert.Equal(ErrorCodes.CommandFailed, second.ErrorCode);
    }

    [Fact]
    public void UnknownAndEvictedJobsAreNotFound()
    {
        var store = new HostBackgroundJobStore();
        Assert.False(store.Get("job-missing").Found);

        var ids = Enumerable.Range(0, HostBackgroundJobStore.Capacity + 1)
            .Select(_ => store.Start("clash_tests_export", _ => Task.FromResult<object>(new object())).OperationId)
            .ToList();
        Assert.Equal(32, HostBackgroundJobStore.Capacity);
        Assert.False(store.Get(ids[0]).Found);
        Assert.True(store.Get(ids[^1]).Found);
    }

    [Fact]
    public void TheExportToolAndTheStatusToolAreWired()
    {
        var export = typeof(NavisworksClashTransferTools).GetMethod(nameof(NavisworksClashTransferTools.ClashTestsExport))!;
        var background = export.GetParameters().Single(parameter => parameter.Name == "background");
        Assert.Equal(typeof(bool), background.ParameterType);
        Assert.Equal(false, background.DefaultValue);
        Assert.NotNull(typeof(ClashTestsExportResponse).GetProperty("BackgroundOperationId"));

        var status = typeof(NavisworksClashTransferTools).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Single(method => method.Name == "BackgroundJobStatus");
        Assert.NotNull(status.GetCustomAttribute<McpServerToolAttribute>());
        Assert.Equal(new[] { "operationId", "waitSeconds" },
            status.GetParameters().Select(parameter => parameter.Name).Where(name => name is "operationId" or "waitSeconds"));
    }
}
