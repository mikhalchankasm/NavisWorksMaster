using System.Collections.Concurrent;
using NavisHelper.AI;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class AISettingsInfrastructureExecutorTests
{
    [Fact]
    public async Task SynchronouslySlowWorkerStartup_RunsOffCallingThread()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var transport = new BlockingTransport();
        var executor = CreateExecutor(
            new RecordingEnvironment(),
            transport,
            new RecordingDiagnosticSink(),
            callerThread);

        var pending = executor.ValidateKeyAsync(
            "test-secret",
            CancellationToken.None,
            CancellationToken.None);
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotEqual(callerThread, transport.ThreadId);
        Assert.False(pending.IsCompleted);
        transport.Release.Set();
        Assert.True((await pending).IsSuccess);
    }

    [Fact]
    public async Task SlowEnvironmentCapture_RunsOffCallingThread()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var environment = new BlockingEnvironment();
        var executor = CreateExecutor(
            environment,
            new ImmediateTransport(),
            new RecordingDiagnosticSink(),
            callerThread);

        var pending = executor.CaptureKeyStateAsync(CancellationToken.None);
        await environment.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotEqual(callerThread, environment.ThreadId);
        Assert.False(pending.IsCompleted);
        environment.Release.Set();
        await pending;
    }

    [Fact]
    public async Task SlowDiagnosticSink_DoesNotDelayInfrastructureCompletion()
    {
        var sink = new BlockingDiagnosticSink();
        var executor = CreateExecutor(
            new RecordingEnvironment(),
            new ImmediateTransport(),
            sink,
            Environment.CurrentManagedThreadId);

        executor.ReportPhase(
            AISettingsOperationStage.BindModels,
            OpenRouterFailureKind.None,
            null,
            1,
            false);
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(sink.IsBlocked);
        sink.Release.Set();
    }

    [Fact]
    public async Task Diagnostics_AreSerializedInReportedOrder()
    {
        var sink = new OrderedBlockingDiagnosticSink();
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
        await sink.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        executor.ReportPhase(
            AISettingsOperationStage.LoadModels,
            OpenRouterFailureKind.None,
            null,
            2,
            false);

        Assert.False(sink.SecondEntered.Task.IsCompleted);
        sink.ReleaseFirst.Set();
        var second = await sink.SecondEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(2));
        Assert.Equal(AISettingsOperationStage.LoadModels, second.Stage);
    }

    [Fact]
    public async Task BackgroundValidation_ReturnsPendingTaskBeforeCompletion()
    {
        var transport = new BlockingTransport();
        var executor = CreateExecutor(
            new RecordingEnvironment(),
            transport,
            new RecordingDiagnosticSink(),
            Environment.CurrentManagedThreadId);

        var pending = executor.ValidateKeyAsync(
            "test-secret",
            CancellationToken.None,
            CancellationToken.None);
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(pending.IsCompleted);
        transport.Release.Set();
        await pending;
    }

    [Fact]
    public async Task LifecycleCancellation_RemainsAvailableDuringSlowStartup()
    {
        using var lifetime = new AISettingsOperationLifetime();
        var operation = lifetime.Begin(0);
        var transport = new BlockingTransport();
        var executor = CreateExecutor(
            new RecordingEnvironment(),
            transport,
            new RecordingDiagnosticSink(),
            Environment.CurrentManagedThreadId);
        var pending = executor.ValidateKeyAsync(
            "test-secret",
            operation.CancellationToken,
            CancellationToken.None);
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

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
        var environment = new BlockingSetEnvironment();
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
        await environment.SetEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

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
        var callerThread = Environment.CurrentManagedThreadId;
        var executor = CreateExecutor(
            new RecordingEnvironment(),
            new ImmediateTransport(),
            sink,
            callerThread);

        await executor.CaptureKeyStateAsync(CancellationToken.None);
        var diagnostic = await sink.Next.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(AISettingsOperationStage.CaptureKeyState, diagnostic.Stage);
        Assert.False(diagnostic.IsUiThread);
        Assert.NotEqual(callerThread, diagnostic.ManagedThreadId);
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
        var diagnostic = await sink.Next.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(AISettingsOperationStage.BindModels, diagnostic.Stage);
        Assert.True(diagnostic.IsUiThread);
        Assert.Equal(callerThread, diagnostic.ManagedThreadId);
        Assert.Contains(
            "ui_thread=true",
            AISettingsOperationDiagnostic.FormatPhase(diagnostic));
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

    private sealed class BlockingTransport : IOpenRouterTransport
    {
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

    private sealed class BlockingEnvironment : RecordingEnvironment
    {
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

    private sealed class BlockingSetEnvironment : RecordingEnvironment
    {
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

    private sealed class BlockingDiagnosticSink : IAISettingsDiagnosticSink
    {
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
        IAISettingsDiagnosticSink
    {
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
