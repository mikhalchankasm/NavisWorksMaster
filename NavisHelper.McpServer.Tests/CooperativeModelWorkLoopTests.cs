using NavisHelper.Core;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class CooperativeModelWorkLoopTests
{
    [Fact]
    public async Task TimeBudgetYieldsBeforeTheItemLimitAndReportsTerminalChunk()
    {
        var elapsed = TimeSpan.Zero;
        var chunks = new List<(int Count, TimeSpan Elapsed)>();
        int yields = 0;
        var visited = new List<int>();
        await CooperativeModelWorkLoop.RunAsync(
            Enumerable.Range(0, 5),
            item => { visited.Add(item); elapsed += TimeSpan.FromMilliseconds(7); },
            () => { },
            () => { yields++; return Task.CompletedTask; },
            new CooperativeModelScanChunkPolicy(128, TimeSpan.FromMilliseconds(12)),
            (count, time) => chunks.Add((count, time)),
            () => elapsed);

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, visited);
        Assert.Equal(2, yields);
        Assert.Equal(new[] { 2, 2, 1 }, chunks.Select(chunk => chunk.Count));
        Assert.Equal(new[] { 14d, 14d, 7d }, chunks.Select(chunk => chunk.Elapsed.TotalMilliseconds));
    }

    [Theory]
    [InlineData((int)CooperativeModelScanStatus.Canceled)]
    [InlineData((int)CooperativeModelScanStatus.DocumentChanged)]
    [InlineData((int)CooperativeModelScanStatus.SourceChanged)]
    public async Task StopDuringYieldPreventsTheNextSourceReadAndPreservesReason(int stopValue)
    {
        var stop = (CooperativeModelScanStatus)stopValue;
        bool canceled = false;
        int sourceReads = 0;
        int visits = 0;
        bool disposed = false;
        IEnumerable<int> Source()
        {
            try
            {
                for (int i = 0; i < 20; i++)
                {
                    sourceReads++;
                    yield return i;
                }
            }
            finally { disposed = true; }
        }

        var error = await Assert.ThrowsAsync<CooperativeModelOperationStoppedException>(() =>
            CooperativeModelWorkLoop.RunAsync(
                Source(), _ => visits++,
                () => { if (canceled) throw new CooperativeModelOperationStoppedException(stop); },
                () => { canceled = true; return Task.CompletedTask; },
                new CooperativeModelScanChunkPolicy(2)));

        Assert.Equal(stop, error.Status);
        Assert.Equal(2, sourceReads);
        Assert.Equal(2, visits);
        Assert.True(disposed);
    }

    [Fact]
    public async Task UnloadReloadDuringPreparationCannotReviveTheOldLease()
    {
        using var gate = new CooperativeModelScanOperationGate();
        var lifecycle = new CooperativeModelScanLifecycle();
        lifecycle.Resume();
        Assert.True(gate.TryBegin(out var operation));
        int visits = 0;
        using (operation)
        {
            await Assert.ThrowsAsync<CooperativeModelOperationStoppedException>(() =>
                CooperativeModelWorkLoop.RunAsync(
                    new[] { 1, 2, 3 }, _ => visits++,
                    () =>
                    {
                        if (!operation.IsCurrent || !lifecycle.CanStart)
                            throw new CooperativeModelOperationStoppedException(CooperativeModelScanStatus.Canceled);
                    },
                    () =>
                    {
                        Assert.False(gate.TryBegin(out _));
                        lifecycle.Suspend();
                        gate.CancelCurrent();
                        lifecycle.Resume();
                        return Task.CompletedTask;
                    },
                    new CooperativeModelScanChunkPolicy(1)));
            Assert.False(gate.TryBegin(out _));
        }
        Assert.Equal(1, visits);
        Assert.True(gate.TryBegin(out var next));
        next.Dispose();
    }

    [Fact]
    public async Task VisitorFailureDisposesSourceReportsPartialChunkAndReleasesLease()
    {
        using var gate = new CooperativeModelScanOperationGate();
        Assert.True(gate.TryBegin(out var operation));
        bool disposed = false;
        int accounted = 0;
        IEnumerable<int> Source()
        {
            try { yield return 1; yield return 2; }
            finally { disposed = true; }
        }
        using (operation)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CooperativeModelWorkLoop.RunAsync(
                    Source(), item => { if (item == 2) throw new InvalidOperationException("failed read"); },
                    () => { }, () => Task.CompletedTask,
                    new CooperativeModelScanChunkPolicy(), (count, _) => accounted += count));
        }
        Assert.True(disposed);
        Assert.Equal(1, accounted);
        Assert.True(gate.TryBegin(out var next));
        next.Dispose();
    }

    [Fact]
    public async Task CanceledOperationDoesNotOpenTheSource()
    {
        int reads = 0;
        IEnumerable<int> Source() { reads++; yield return 1; }
        await Assert.ThrowsAsync<CooperativeModelOperationStoppedException>(() =>
            CooperativeModelWorkLoop.RunAsync(
                Source(), _ => { },
                () => throw new CooperativeModelOperationStoppedException(CooperativeModelScanStatus.Canceled),
                () => Task.CompletedTask, new CooperativeModelScanChunkPolicy()));
        Assert.Equal(0, reads);
    }
}
