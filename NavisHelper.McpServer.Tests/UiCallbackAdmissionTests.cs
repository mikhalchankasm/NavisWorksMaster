using System.Threading;
using System.Threading.Tasks;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance for #457. A request whose UI callback has not started when the host
// times out must never run: InvokeOnUiThread posts the callback, and on timeout it used to only
// defer the gate release, so the callback still ran later against whatever document was active
// then, and a client retry ran the command twice. REQUEST_TIMEOUT for a queued request now means
// "not executed"; a callback that already started keeps today's deferred-completion behaviour.
public sealed class UiCallbackAdmissionTests
{
    [Fact]
    public void ANewAdmissionIsQueued()
    {
        Assert.Equal(UiCallbackAdmissionState.Queued, new UiCallbackAdmission().State);
    }

    [Fact]
    public void AbandonedBeforeStartTheCallbackBodyDoesNotRun()
    {
        var admission = new UiCallbackAdmission();
        var runs = 0;

        Assert.True(admission.TryAbandon());
        if (admission.TryStart()) runs++;

        Assert.Equal(0, runs);
        Assert.Equal(UiCallbackAdmissionState.Abandoned, admission.State);
    }

    [Fact]
    public void StartedBeforeAbandonItRunsOnceAndAbandonFails()
    {
        var admission = new UiCallbackAdmission();
        var runs = 0;

        if (admission.TryStart()) runs++;
        Assert.False(admission.TryAbandon());
        if (admission.TryStart()) runs++;

        Assert.Equal(1, runs);
        Assert.Equal(UiCallbackAdmissionState.Running, admission.State);
    }

    [Fact]
    public void AbandonTwiceSucceedsOnlyOnce()
    {
        var admission = new UiCallbackAdmission();

        Assert.True(admission.TryAbandon());
        Assert.False(admission.TryAbandon());
    }

    // The UI thread and the waiting pipe thread race; exactly one of them may win every time.
    [Fact]
    public void ARaceBetweenStartAndAbandonHasExactlyOneWinner()
    {
        for (var round = 0; round < 2000; round++)
        {
            var admission = new UiCallbackAdmission();
            using var barrier = new Barrier(2);
            bool started = false, abandoned = false;
            var ui = Task.Run(() => { barrier.SignalAndWait(); started = admission.TryStart(); });
            var pipe = Task.Run(() => { barrier.SignalAndWait(); abandoned = admission.TryAbandon(); });
            Task.WaitAll(ui, pipe);

            Assert.True(started ^ abandoned, "round " + round);
            Assert.Equal(started ? UiCallbackAdmissionState.Running : UiCallbackAdmissionState.Abandoned, admission.State);
        }
    }

    // The plugin cannot compile in L0, so the wiring in InvokeOnUiThread is pinned by source:
    // both posted callbacks (SynchronizationContext.Post and Control.BeginInvoke) ask for admission
    // before running, and both timeout branches try to abandon before deferring the completion.
    [Fact]
    public void BothDispatchPathsAdmitBeforeRunningAndAbandonBeforeDeferring()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "NavisHelper", "Agent", "Host", "AgentHostService.Dispatch.cs"));
        int start = source.IndexOf("private T InvokeOnUiThread<T>(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = source.IndexOf("\n        }", start, StringComparison.Ordinal);
        var body = source.Substring(start, end - start);

        Assert.Contains("new UiCallbackAdmission()", body, StringComparison.Ordinal);
        foreach (var run in new[] { "resultFromControl = callback();", "result = callback();" })
        {
            int at = body.IndexOf(run, StringComparison.Ordinal);
            Assert.True(at > 0, run + " is present");
            var before = body.Substring(Math.Max(0, at - 400), Math.Min(400, at));
            Assert.Contains(".TryStart()", before, StringComparison.Ordinal);
        }

        var abandons = Positions(body, ".TryAbandon()");
        var defers = Positions(body, "RecordDeferredOperationCompletion(");
        Assert.Equal(2, defers.Length);
        Assert.Equal(2, abandons.Length);
        Assert.True(abandons[0] < defers[0] && abandons[1] < defers[1] && abandons[1] > defers[0],
            "each timeout branch tries to abandon before it defers");
        Assert.Contains("was not executed", body, StringComparison.Ordinal);
    }

    private static int[] Positions(string text, string value)
    {
        var positions = new List<int>();
        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
            positions.Add(at);
        return positions.ToArray();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
