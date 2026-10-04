using System.Collections.Concurrent;
using NavisHelper.AI;
using Xunit;

namespace NavisHelper.McpServer.Tests;

[Collection("Blocking infrastructure")]
public sealed class AISettingsInfrastructureExecutorTests
{
    [Fact]
    public async Task SynchronouslySlowWorkerStartup_RunsOffCallingThread()
    {
        using var transport = new BlockingTransport();
        using var caller = new DedicatedCaller<Task<OpenRouterValidationResult>>(
            threadId => CreateExecutor(
                new RecordingEnvironment(), transport, new RecordingDiagnosticSink(), threadId)
                .ValidateKeyAsync("test-secret", CancellationToken.None, CancellationToken.None),
            () => transport.Release.Set());
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.NotEqual(caller.ThreadId, transport.ThreadId);
        var pending = await caller.Returned.Task.WaitAsync(
            TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);
        transport.Release.Set();
        Assert.True((await pending).IsSuccess);
    }

    [Fact]
    public async Task SlowEnvironmentCapture_RunsOffCallingThread()
    {
        using var environment = new BlockingEnvironment();
        using var caller = new DedicatedCaller<Task<OpenRouterKeySnapshot>>(
            threadId => CreateExecutor(
                environment, new ImmediateTransport(), new RecordingDiagnosticSink(), threadId)
                .CaptureKeyStateAsync(CancellationToken.None),
            () => environment.Release.Set());
        await environment.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.NotEqual(caller.ThreadId, environment.ThreadId);
        var pending = await caller.Returned.Task.WaitAsync(
            TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);
        environment.Release.Set();
        await pending;
    }

    [Fact]
    public async Task SlowDiagnosticSink_DoesNotDelayInfrastructureCompletion()
    {
        using var sink = new BlockingDiagnosticSink();
        var executor = CreateExecutor(
            new RecordingEnvironment(),
            new ImmediateTransport(),
            sink,
            Environment.CurrentManagedThreadId);

        try
        {
            executor.ReportPhase(
                AISettingsOperationStage.BindModels,
                OpenRouterFailureKind.None,
                null,
                1,
                false);
            // A deadlock watchdog, not an assertion about thread-pool scheduling speed.
            await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.True(sink.IsBlocked);
        }
        finally { sink.Release.Set(); }
    }

    [Fact]
    public async Task Diagnostics_AreSerializedInReportedOrder()
    {
        using var sink = new OrderedBlockingDiagnosticSink();
        var executor = CreateExecutor(
            new RecordingEnvironment(),
            new ImmediateTransport(),
            sink,
            Environment.CurrentManagedThreadId);

        executor.ReportPhase(
            AISettingsOperationStage.CaptureKeyState,
            OpenRouterFailureKind.None,
            null,
            1,
            false);
        await sink.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        executor.ReportPhase(
            AISettingsOperationStage.LoadModels,
            OpenRouterFailureKind.None,
            null,
            2,
            false);

        Assert.False(sink.SecondEntered.Task.IsCompleted);
        sink.ReleaseFirst.Set();
        var second = await sink.SecondEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Equal(AISettingsOperationStage.LoadModels, second.Stage);
    }

    [Fact]
    public async Task BackgroundValidation_ReturnsPendingTaskBeforeCompletion()
    {
        using var transport = new BlockingTransport();
        var executor = CreateExecutor(
            new RecordingEnvironment(),
            transport,
            new RecordingDiagnosticSink(),
            Environment.CurrentManagedThreadId);

        var pending = executor.ValidateKeyAsync(
            "test-secret",
            CancellationToken.None,
            CancellationToken.None);
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.False(pending.IsCompleted);
        transport.Release.Set();
        await pending;
    }

    [Fact]
    public async Task LifecycleCancellation_RemainsAvailableDuringSlowStartup()
    {
        using var lifetime = new AISettingsOperationLifetime();
        var operation = lifetime.Begin(0);
        using var transport = new BlockingTransport();
        var executor = CreateExecutor(
            new RecordingEnvironment(),
            transport,
            new RecordingDiagnosticSink(),
            Environment.CurrentManagedThreadId);
        var pending = executor.ValidateKeyAsync(
            "test-secret",
            operation.CancellationToken,
            CancellationToken.None);
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        lifetime.CancelPendingOperations();

        Assert.True(operation.CancellationToken.IsCancellationRequested);
        Assert.False(lifetime.IsCurrent(operation));
        transport.Release.Set();
        await pending;
    }

    [Fact]
    public async Task CancellationDuringSlowPersistence_RollsBackKeyMutation()
    {
        using var lifetime = new AISettingsOperationLifetime();
        var operation = lifetime.Begin(0);
        using var environment = new BlockingSetEnvironment();
        var keyStore = new OpenRouterKeyStore(environment);
        var executor = CreateExecutor(
            keyStore,
            new ImmediateTransport(),
            new RecordingDiagnosticSink(),
            Environment.CurrentManagedThreadId);
        var pending = executor.PersistKeyAsync(
            "test-secret",
            persist: true,
            expectedGeneration: 0,
            cancellationToken: operation.CancellationToken);
        await environment.SetEntered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        lifetime.CancelPendingOperations();
        environment.Release.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await pending);
        Assert.False(keyStore.HasKey);
    }

    [Fact]
    public async Task InfrastructureDiagnostics_ReportBackgroundThread()
    {
        var sink = new RecordingDiagnosticSink();
        using var caller = new DedicatedCaller<Task<OpenRouterKeySnapshot>>(
            threadId => CreateExecutor(
                new RecordingEnvironment(), new ImmediateTransport(), sink, threadId)
                .CaptureKeyStateAsync(CancellationToken.None),
            () => { });
        var pending = await caller.Returned.Task.WaitAsync(
            TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        await pending;
        var diagnostic = await sink.Next.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.Equal(AISettingsOperationStage.CaptureKeyState, diagnostic.Stage);
        Assert.False(diagnostic.IsUiThread);
        Assert.NotEqual(caller.ThreadId, diagnostic.ManagedThreadId);
        var formatted = AISettingsOperationDiagnostic.FormatPhase(diagnostic);
        Assert.Contains("managed_thread_id=", formatted);
        Assert.Contains("ui_thread=false", formatted);
    }

    [Fact]
    public async Task BindPhaseDiagnostic_IdentifiesUiBoundaryThread()
    {
        var sink = new RecordingDiagnosticSink();
        var callerThread = Environment.CurrentManagedThreadId;
        var executor = CreateExecutor(
            new RecordingEnvironment(),
            new ImmediateTransport(),
            sink,
            callerThread);

        executor.ReportPhase(
            AISettingsOperationStage.BindModels,
            OpenRouterFailureKind.None,
            null,
            2,
            false);
        var diagnostic = await sink.Next.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.Equal(AISettingsOperationStage.BindModels, diagnostic.Stage);
        Assert.True(diagnostic.IsUiThread);
        Assert.Equal(callerThread, diagnostic.ManagedThreadId);
        Assert.Contains(
            "ui_thread=true",
            AISettingsOperationDiagnostic.FormatPhase(diagnostic));
    }

    // An awaited xUnit caller returns its pool thread; Task.Run can legitimately
    // reuse that ID. A live dedicated caller models the actual UI-thread boundary.
    private sealed class DedicatedCaller<T> : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _release = new(false);
        private readonly Action _unblockOperation;
        internal TaskCompletionSource<T> Returned { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int ThreadId => _thread.ManagedThreadId;

        internal DedicatedCaller(Func<int, T> operation, Action unblockOperation)
        {
            _unblockOperation = unblockOperation;
            _thread = new Thread(() =>
            {
                try
                {
                    Returned.TrySetResult(operation(Environment.CurrentManagedThreadId));
                    _release.Wait();
                }
                catch (Exception error) { Returned.TrySetException(error); }
            }) { IsBackground = true };
            _thread.Start();
        }

        public void Dispose()
        {
            // Also releases an incorrectly inline implementation during a failed assertion.
            _unblockOperation();
            _release.Set();
            Assert.True(_thread.Join(TimeSpan.FromSeconds(15)), "Dedicated test caller did not exit.");
            _release.Dispose();
        }
    }

    private static AISettingsInfrastructureExecutor CreateExecutor(
        IEnvironmentVariableAccessor environment,
        IOpenRouterTransport transport,
        IAISettingsDiagnosticSink sink,
        int uiThreadId)
    {
        return CreateExecutor(
            new OpenRouterKeyStore(environment),
            transport,
            sink,
            uiThreadId);
    }

    private static AISettingsInfrastructureExecutor CreateExecutor(
        OpenRouterKeyStore keyStore,
        IOpenRouterTransport transport,
        IAISettingsDiagnosticSink sink,
        int uiThreadId)
    {
        return new AISettingsInfrastructureExecutor(
            keyStore,
            transport,
            new OpenRouterCatalogCache(TimeSpan.FromMinutes(1)),
            new MemoryModelConfig(),
            sink,
            uiThreadId);
    }

    private sealed class ImmediateTransport : IOpenRouterTransport
    {
        public Task<OpenRouterValidationResult> ValidateKeyAsync(
            string key,
            CancellationToken cancellationToken) =>
            Task.FromResult(OpenRouterValidationResult.Success());

        public Task<OpenRouterCatalogResult> GetModelsAsync(
            string key,
            CancellationToken cancellationToken) =>
            Task.FromResult(OpenRouterCatalogResult.Available(
                new Dictionary<string, OpenRouterModelInfo>()));

        public Task<AiColorOutcome> GetColorsAsync(
            string key,
            IReadOnlyCollection<string> objectNames,
            string schemeName,
            OpenRouterModelInfo model,
            double temperature,
            CancellationToken cancellationToken) =>
            Task.FromResult(AiColorOutcome.Failure(
                AiColorOutcomeKind.InvalidRequest));
    }

    private sealed class BlockingTransport : IOpenRouterTransport, IDisposable
    {
        public void Dispose() => Release.Set();

        internal TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new(false);
        internal int ThreadId { get; private set; }

        public Task<OpenRouterValidationResult> ValidateKeyAsync(
            string key,
            CancellationToken cancellationToken)
        {
            ThreadId = Environment.CurrentManagedThreadId;
            Entered.TrySetResult(true);
            Release.Wait();
            return Task.FromResult(OpenRouterValidationResult.Success());
        }

        public Task<OpenRouterCatalogResult> GetModelsAsync(
            string key,
            CancellationToken cancellationToken) =>
            Task.FromResult(OpenRouterCatalogResult.Unavailable(
                OpenRouterFailureKind.Network));

        public Task<AiColorOutcome> GetColorsAsync(
            string key,
            IReadOnlyCollection<string> objectNames,
            string schemeName,
            OpenRouterModelInfo model,
            double temperature,
            CancellationToken cancellationToken) =>
            Task.FromResult(AiColorOutcome.Failure(
                AiColorOutcomeKind.InvalidRequest));
    }

    private class RecordingEnvironment : IEnvironmentVariableAccessor
    {
        protected readonly ConcurrentDictionary<
            (string, EnvironmentVariableTarget), string> Values = new();

        public virtual string Get(
            string name,
            EnvironmentVariableTarget target) =>
            Values.TryGetValue((name, target), out var value) ? value : null;

        public virtual void Set(
            string name,
            string value,
            EnvironmentVariableTarget target)
        {
            if (value == null)
                Values.TryRemove((name, target), out _);
            else
                Values[(name, target)] = value;
        }
    }

    private sealed class BlockingEnvironment : RecordingEnvironment, IDisposable
    {
        public void Dispose() => Release.Set();

        internal TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new(false);
        internal int ThreadId { get; private set; }

        public override string Get(
            string name,
            EnvironmentVariableTarget target)
        {
            ThreadId = Environment.CurrentManagedThreadId;
            Entered.TrySetResult(true);
            Release.Wait();
            return base.Get(name, target);
        }
    }

    private sealed class BlockingSetEnvironment : RecordingEnvironment, IDisposable
    {
        public void Dispose() => Release.Set();

        internal TaskCompletionSource<bool> SetEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new(false);

        public override void Set(
            string name,
            string value,
            EnvironmentVariableTarget target)
        {
            SetEntered.TrySetResult(true);
            Release.Wait();
            base.Set(name, value, target);
        }
    }

    private sealed class MemoryModelConfig : IAISettingsModelConfig
    {
        private string _modelId = string.Empty;
        public string ReadSelectedModelId() => _modelId;
        public void UpdateSelectedModelRuntime(string modelId) =>
            _modelId = modelId ?? string.Empty;
        public Task PersistLatestAsync() => Task.CompletedTask;
    }

    private sealed class RecordingDiagnosticSink : IAISettingsDiagnosticSink
    {
        internal TaskCompletionSource<AISettingsPhaseDiagnostic> Next { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Write(AISettingsPhaseDiagnostic diagnostic) =>
            Next.TrySetResult(diagnostic);
    }

    private sealed class BlockingDiagnosticSink : IAISettingsDiagnosticSink, IDisposable
    {
        public void Dispose() => Release.Set();

        internal TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new(false);
        internal bool IsBlocked => !Release.IsSet;

        public void Write(AISettingsPhaseDiagnostic diagnostic)
        {
            Entered.TrySetResult(true);
            Release.Wait();
        }
    }

    private sealed class OrderedBlockingDiagnosticSink :
        IAISettingsDiagnosticSink, IDisposable
    {
        public void Dispose() => ReleaseFirst.Set();

        private int _writeCount;
        internal TaskCompletionSource<bool> FirstEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<AISettingsPhaseDiagnostic> SecondEntered
            { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim ReleaseFirst { get; } = new(false);

        public void Write(AISettingsPhaseDiagnostic diagnostic)
        {
            if (Interlocked.Increment(ref _writeCount) == 1)
            {
                FirstEntered.TrySetResult(true);
                ReleaseFirst.Wait();
                return;
            }
            SecondEntered.TrySetResult(diagnostic);
        }
    }
}
