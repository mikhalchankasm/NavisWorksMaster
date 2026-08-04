using NavisHelper.AI;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class AISettingsAsyncBoundaryTests
{
    [Theory]
    [InlineData("capture_key_state")]
    [InlineData("start_worker")]
    [InlineData("validate_key")]
    [InlineData("persist_key")]
    [InlineData("load_models")]
    [InlineData("bind_models")]
    [InlineData("disconnect")]
    [InlineData("save_selected_model")]
    public async Task InfrastructureStageFailure_IsObservedAtAsyncBoundary(
        string stage)
    {
        Exception observed = null;

        var completion = AISettingsAsyncBoundary.RunAsync(
            async () =>
            {
                await Task.Yield();
                throw new StageFailureException(stage);
            },
            ex =>
            {
                observed = ex;
                return Task.CompletedTask;
            });

        await completion;

        Assert.True(completion.IsCompletedSuccessfully);
        Assert.Equal(stage, Assert.IsType<StageFailureException>(observed).Stage);
    }

    [Fact]
    public async Task FailureReporterFailure_DoesNotFaultEventBoundary()
    {
        var completion = AISettingsAsyncBoundary.RunAsync(
            () => Task.FromException(new InvalidOperationException()),
            ex => Task.FromException(new IOException()));

        await completion;

        Assert.True(completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ExistingKeyVerificationFault_IsObserved()
    {
        var reported = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var completion = AISettingsAsyncBoundary.RunAsync(
            () => Task.FromException(new InvalidOperationException()),
            ex =>
            {
                reported.TrySetResult(true);
                return Task.CompletedTask;
            });

        await completion;

        Assert.True(await reported.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(completion.IsFaulted);
    }

    [Fact]
    public async Task BackgroundContinuation_MutatesOnlyThroughUiBoundary()
    {
        var boundary = new RecordingUiBoundary();
        var gate = new AISettingsUiMutationGate(boundary);
        var mutated = false;

        await Task.Run(async () =>
            await gate.RunAsync(() => true, () => mutated = true));

        Assert.True(mutated);
        Assert.Equal(1, boundary.CallCount);
        Assert.True(boundary.MutationObservedInsideBoundary);
    }

    [Fact]
    public async Task CancelledLifecycle_DropsQueuedUiMutation()
    {
        var boundary = new RecordingUiBoundary();
        var gate = new AISettingsUiMutationGate(boundary);
        var mutated = false;

        await Task.Run(async () =>
            await gate.RunAsync(() => false, () => mutated = true));

        Assert.False(mutated);
        Assert.Equal(1, boundary.CallCount);
    }

    [Fact]
    public async Task NewModel_IsVisibleWhilePersistenceIsBlocked()
    {
        var persistence = new BlockingConfigPersistence();
        var runtime = CreateRuntime(persistence);
        runtime.UpdateModelName("provider/old");
        var blockedWrite = runtime.PersistLatestAsync();
        await persistence.FirstWriteEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        runtime.UpdateModelName("provider/latest");

        Assert.Equal("provider/latest", runtime.Capture().ModelName);
        Assert.False(blockedWrite.IsCompleted);
        persistence.ReleaseFirstWrite.Set();
        await blockedWrite;
    }

    [Fact]
    public async Task RuntimeSnapshot_DoesNotWaitForSlowFileWrite()
    {
        var persistence = new BlockingConfigPersistence();
        var runtime = CreateRuntime(persistence);
        var blockedWrite = runtime.PersistLatestAsync();
        await persistence.FirstWriteEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        var snapshot = await Task.Run(runtime.Capture)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("provider/initial", snapshot.ModelName);
        persistence.ReleaseFirstWrite.Set();
        await blockedWrite;
    }

    [Fact]
    public async Task FastModelAThenB_PersistsAndReturnsB()
    {
        var persistence = new RecordingConfigPersistence();
        var runtime = CreateRuntime(persistence);

        runtime.UpdateModelName("provider/a");
        var first = runtime.PersistLatestAsync();
        runtime.UpdateModelName("provider/b");
        var second = runtime.PersistLatestAsync();
        await Task.WhenAll(first, second);

        Assert.Equal("provider/b", runtime.Capture().ModelName);
        Assert.Equal("provider/b", persistence.LastSnapshot.ModelName);
    }

    [Fact]
    public async Task ModelAndSchemeInterleaving_PersistsLatestCompleteState()
    {
        var persistence = new BlockingConfigPersistence();
        var runtime = CreateRuntime(persistence);
        var first = runtime.PersistLatestAsync();
        await persistence.FirstWriteEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        runtime.UpdateModelName("provider/latest");
        var modelWrite = runtime.PersistLatestAsync();
        runtime.UpdateColorScheme(11);
        var schemeWrite = runtime.PersistLatestAsync();
        persistence.ReleaseFirstWrite.Set();
        await Task.WhenAll(first, modelWrite, schemeWrite);

        var persisted = persistence.LastSnapshot;
        Assert.Equal("provider/latest", persisted.ModelName);
        Assert.Equal(0.7, persisted.Temperature);
        Assert.Equal(11, persisted.ColorScheme);
        Assert.Equal(1, persistence.MaximumParallelWrites);
    }

    [Fact]
    public void RuntimeSnapshot_IsImmutableAndInternallyConsistent()
    {
        var runtime = CreateRuntime(new RecordingConfigPersistence());
        var captured = runtime.Capture();

        runtime.UpdateModelName("provider/new");
        runtime.UpdateColorScheme(12);

        Assert.Equal("provider/initial", captured.ModelName);
        Assert.Equal(0.7, captured.Temperature);
        Assert.Equal(9, captured.ColorScheme);
        var current = runtime.Capture();
        Assert.Equal("provider/new", current.ModelName);
        Assert.Equal(0.7, current.Temperature);
        Assert.Equal(12, current.ColorScheme);
    }

    [Fact]
    public void ValidationStageTimeout_IsAlwaysTimeout()
    {
        using var timeout = new CancellationTokenSource();
        timeout.Cancel();

        var failure = AISettingsOperationPolicy.EffectiveFailure(
            OpenRouterFailureKind.Cancelled,
            timeout.Token,
            CancellationToken.None);

        Assert.Equal(OpenRouterFailureKind.Timeout, failure);
    }

    [Fact]
    public void WorkerTypedValidationTimeout_IsAlwaysTimeout()
    {
        var failure = AISettingsOperationPolicy.EffectiveFailure(
            OpenRouterFailureKind.Timeout,
            CancellationToken.None,
            CancellationToken.None);

        Assert.Equal(OpenRouterFailureKind.Timeout, failure);
    }

    [Fact]
    public void CatalogTimeout_RemainsConnectedWithTypedTimeout()
    {
        using var timeout = new CancellationTokenSource();
        timeout.Cancel();
        var failure = AISettingsOperationPolicy.EffectiveFailure(
            OpenRouterFailureKind.Cancelled,
            timeout.Token,
            CancellationToken.None);
        var catalog = OpenRouterCatalogResult.Unavailable(failure);

        Assert.Equal(OpenRouterFailureKind.Timeout, catalog.FailureKind);
        Assert.Equal(
            AiConnectionDisplayState.Connected,
            AISettingsOperationPolicy.CatalogCompletionState(
                catalog,
                hasCompatibleSelection: false));
    }

    [Fact]
    public void LifecycleCancellation_HasPriorityOverStageTimeout()
    {
        using var lifecycle = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource();
        timeout.Cancel();
        lifecycle.Cancel();

        var failure = AISettingsOperationPolicy.EffectiveFailure(
            OpenRouterFailureKind.Timeout,
            timeout.Token,
            lifecycle.Token);

        Assert.Equal(OpenRouterFailureKind.Cancelled, failure);
    }

    private sealed class RecordingUiBoundary : IAISettingsUiBoundary
    {
        private int _callCount;
        internal int CallCount => Volatile.Read(ref _callCount);
        internal bool MutationObservedInsideBoundary { get; private set; }

        public async Task RunAsync(Action action)
        {
            Interlocked.Increment(ref _callCount);
            await Task.Yield();
            await Task.Run(() =>
            {
                MutationObservedInsideBoundary = true;
                action();
            });
        }
    }

    private static AIConfigRuntime CreateRuntime(
        IAIConfigSnapshotPersistence persistence)
    {
        return new AIConfigRuntime(
            new AIConfigSnapshot("provider/initial", 0.7, 9),
            persistence);
    }

    private class RecordingConfigPersistence : IAIConfigSnapshotPersistence
    {
        internal AIConfigSnapshot LastSnapshot;

        public virtual void Save(AIConfigSnapshot snapshot)
        {
            LastSnapshot = snapshot;
        }
    }

    private sealed class BlockingConfigPersistence :
        RecordingConfigPersistence
    {
        private int _activeWrites;
        private int _maximumParallelWrites;
        internal TaskCompletionSource<bool> FirstWriteEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim ReleaseFirstWrite { get; } = new(false);
        internal int MaximumParallelWrites =>
            Volatile.Read(ref _maximumParallelWrites);

        public override void Save(AIConfigSnapshot snapshot)
        {
            var active = Interlocked.Increment(ref _activeWrites);
            UpdateMaximum(active);
            try
            {
                if (FirstWriteEntered.TrySetResult(true))
                    ReleaseFirstWrite.Wait();
                base.Save(snapshot);
            }
            finally
            {
                Interlocked.Decrement(ref _activeWrites);
            }
        }

        private void UpdateMaximum(int active)
        {
            int current;
            do
            {
                current = Volatile.Read(ref _maximumParallelWrites);
                if (current >= active)
                    return;
            }
            while (Interlocked.CompareExchange(
                       ref _maximumParallelWrites,
                       active,
                       current) != current);
        }
    }

    private sealed class StageFailureException : Exception
    {
        internal StageFailureException(string stage)
        {
            Stage = stage;
        }

        internal string Stage { get; }
    }
}
