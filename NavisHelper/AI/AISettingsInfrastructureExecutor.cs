using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NavisHelper.Core;

namespace NavisHelper.AI
{
    internal interface IAISettingsDiagnosticSink
    {
        void Write(AISettingsPhaseDiagnostic diagnostic);
    }

    internal sealed class LoggerAISettingsDiagnosticSink :
        IAISettingsDiagnosticSink
    {
        public void Write(AISettingsPhaseDiagnostic diagnostic)
        {
            var message = AISettingsOperationDiagnostic.FormatPhase(diagnostic);
            if (diagnostic.FailureKind == OpenRouterFailureKind.None)
                Logger.Info(message, "OpenRouterSettings");
            else
                Logger.Warn(message, "OpenRouterSettings");
        }
    }

    internal interface IAISettingsModelConfig
    {
        string ReadSelectedModelId();
        void UpdateSelectedModelRuntime(string modelId);
        Task PersistLatestAsync();
    }

    internal sealed class AISettingsModelConfig : IAISettingsModelConfig
    {
        public string ReadSelectedModelId()
        {
            return AIConfig.Instance.CaptureSnapshot().ModelName;
        }

        public void UpdateSelectedModelRuntime(string modelId)
        {
            AIConfig.Instance.UpdateModelNameRuntime(modelId ?? string.Empty);
        }

        public Task PersistLatestAsync()
        {
            return AIConfig.Instance.PersistLatestAsync();
        }
    }

    internal sealed class AISettingsModelBinding
    {
        internal AISettingsModelBinding(
            IReadOnlyList<OpenRouterModelChoice> choices,
            OpenRouterModelChoice selectedChoice)
        {
            Choices = choices ?? Array.Empty<OpenRouterModelChoice>();
            SelectedChoice = selectedChoice;
        }

        internal IReadOnlyList<OpenRouterModelChoice> Choices { get; }
        internal OpenRouterModelChoice SelectedChoice { get; }
    }

    internal interface IAISettingsInfrastructureExecutor
    {
        Task<OpenRouterKeySnapshot> CaptureKeyStateAsync(
            CancellationToken cancellationToken);

        Task<OpenRouterValidationResult> ValidateKeyAsync(
            string key,
            CancellationToken lifecycleCancellationToken,
            CancellationToken timeoutCancellationToken);

        Task<KeyStoreMutationResult> PersistKeyAsync(
            string key,
            bool persist,
            int expectedGeneration,
            CancellationToken cancellationToken);

        Task<OpenRouterCatalogResult> LoadModelsAsync(
            string key,
            CancellationToken lifecycleCancellationToken,
            CancellationToken timeoutCancellationToken);

        Task<AISettingsModelBinding> PrepareModelBindingAsync(
            OpenRouterCatalogResult catalog,
            CancellationToken cancellationToken);

        Task<bool> IsKeyGenerationCurrentAsync(
            int keyGeneration,
            CancellationToken cancellationToken);

        void ReplaceCatalog(
            int keyGeneration,
            OpenRouterCatalogResult catalog);

        Task InvalidateCatalogAsync();
        Task<KeyStoreMutationResult> DisconnectAsync();
        void UpdateSelectedModelRuntime(string modelId);
        Task SaveSelectedModelAsync();
        void ReportPhase(
            AISettingsOperationStage stage,
            OpenRouterFailureKind failureKind,
            int? httpStatus,
            long elapsedMilliseconds,
            bool cancelled);
    }

    internal sealed class AISettingsInfrastructureExecutor :
        IAISettingsInfrastructureExecutor
    {
        private readonly OpenRouterKeyStore _keyStore;
        private readonly IOpenRouterTransport _transport;
        private readonly OpenRouterCatalogCache _catalogCache;
        private readonly IAISettingsModelConfig _modelConfig;
        private readonly IAISettingsDiagnosticSink _diagnosticSink;
        private readonly int _uiThreadId;
        private readonly object _diagnosticSync = new object();
        private Task _diagnosticTail = Task.CompletedTask;

        internal AISettingsInfrastructureExecutor(
            OpenRouterKeyStore keyStore,
            IOpenRouterTransport transport,
            OpenRouterCatalogCache catalogCache,
            IAISettingsModelConfig modelConfig,
            IAISettingsDiagnosticSink diagnosticSink,
            int uiThreadId)
        {
            _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _catalogCache = catalogCache ??
                            throw new ArgumentNullException(nameof(catalogCache));
            _modelConfig = modelConfig ??
                           throw new ArgumentNullException(nameof(modelConfig));
            _diagnosticSink = diagnosticSink ??
                              throw new ArgumentNullException(nameof(diagnosticSink));
            _uiThreadId = uiThreadId;
        }

        internal static AISettingsInfrastructureExecutor CreateDefault(
            int uiThreadId)
        {
            return new AISettingsInfrastructureExecutor(
                OpenRouterKeyStore.Current,
                new AiWorkerTransport(),
                OpenRouterCatalogCache.Current,
                new AISettingsModelConfig(),
                new LoggerAISettingsDiagnosticSink(),
                uiThreadId);
        }

        public Task<OpenRouterKeySnapshot> CaptureKeyStateAsync(
            CancellationToken cancellationToken)
        {
            return RunBlockingAsync(
                AISettingsOperationStage.CaptureKeyState,
                cancellationToken,
                () => _keyStore.Capture(),
                snapshot => OpenRouterFailureKind.None,
                snapshot => null);
        }

        public Task<OpenRouterValidationResult> ValidateKeyAsync(
            string key,
            CancellationToken lifecycleCancellationToken,
            CancellationToken timeoutCancellationToken)
        {
            return RunTransportAsync(
                AISettingsOperationStage.ValidateKey,
                lifecycleCancellationToken,
                timeoutCancellationToken,
                cancellationToken =>
                    _transport.ValidateKeyAsync(key, cancellationToken),
                result => result.IsSuccess
                    ? OpenRouterFailureKind.None
                    : result.FailureKind,
                result => result.HttpStatus);
        }

        public Task<KeyStoreMutationResult> PersistKeyAsync(
            string key,
            bool persist,
            int expectedGeneration,
            CancellationToken cancellationToken)
        {
            return RunBlockingAsync(
                AISettingsOperationStage.PersistKey,
                cancellationToken,
                () => persist
                    ? _keyStore.TrySaveValidatedKey(
                        key,
                        expectedGeneration,
                        cancellationToken)
                    : _keyStore.TryActivateExistingKey(
                        key,
                        expectedGeneration,
                        cancellationToken),
                result => result.IsSuccess
                    ? OpenRouterFailureKind.None
                    : OpenRouterFailureKind.StorageFailed,
                result => null);
        }

        public Task<OpenRouterCatalogResult> LoadModelsAsync(
            string key,
            CancellationToken lifecycleCancellationToken,
            CancellationToken timeoutCancellationToken)
        {
            return RunTransportAsync(
                AISettingsOperationStage.LoadModels,
                lifecycleCancellationToken,
                timeoutCancellationToken,
                cancellationToken =>
                    _transport.GetModelsAsync(key, cancellationToken),
                result => result.IsAvailable
                    ? OpenRouterFailureKind.None
                    : result.FailureKind,
                result => result.HttpStatus);
        }

        public Task<AISettingsModelBinding> PrepareModelBindingAsync(
            OpenRouterCatalogResult catalog,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var saved = _modelConfig.ReadSelectedModelId();
                var choices = OpenRouterModelSelection
                    .CompatibleChoices(catalog);
                var restored = OpenRouterModelSelection.Restore(
                    catalog,
                    saved);
                var selectedChoice = restored == null
                    ? null
                    : choices.FirstOrDefault(choice => string.Equals(
                        choice.Id,
                        restored.Id,
                        StringComparison.OrdinalIgnoreCase));
                cancellationToken.ThrowIfCancellationRequested();
                return new AISettingsModelBinding(
                    choices,
                    selectedChoice);
            });
        }

        public Task<bool> IsKeyGenerationCurrentAsync(
            int keyGeneration,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _keyStore.Generation == keyGeneration;
            });
        }

        public void ReplaceCatalog(
            int keyGeneration,
            OpenRouterCatalogResult catalog)
        {
            _catalogCache.Invalidate();
            _catalogCache.Store(keyGeneration, catalog, DateTime.UtcNow);
        }

        public Task InvalidateCatalogAsync()
        {
            return Task.Run(() => _catalogCache.Invalidate());
        }

        public Task<KeyStoreMutationResult> DisconnectAsync()
        {
            return RunBlockingAsync(
                AISettingsOperationStage.PersistKey,
                CancellationToken.None,
                () => _keyStore.Disconnect(),
                result => result.IsFullyDisconnected
                    ? OpenRouterFailureKind.None
                    : OpenRouterFailureKind.StorageFailed,
                result => null);
        }

        public void UpdateSelectedModelRuntime(string modelId)
        {
            _modelConfig.UpdateSelectedModelRuntime(modelId ?? string.Empty);
        }

        public Task SaveSelectedModelAsync()
        {
            return _modelConfig.PersistLatestAsync();
        }

        public void ReportPhase(
            AISettingsOperationStage stage,
            OpenRouterFailureKind failureKind,
            int? httpStatus,
            long elapsedMilliseconds,
            bool cancelled)
        {
            QueueDiagnostic(
                stage,
                failureKind,
                httpStatus,
                elapsedMilliseconds,
                cancelled);
        }

        private Task<T> RunBlockingAsync<T>(
            AISettingsOperationStage stage,
            CancellationToken cancellationToken,
            Func<T> operation,
            Func<T, OpenRouterFailureKind> failureSelector,
            Func<T, int?> statusSelector)
        {
            return Task.Run(() =>
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = operation();
                    cancellationToken.ThrowIfCancellationRequested();
                    QueueDiagnostic(
                        stage,
                        failureSelector(result),
                        statusSelector(result),
                        watch.ElapsedMilliseconds,
                        cancellationToken.IsCancellationRequested);
                    return result;
                }
                catch (OperationCanceledException)
                {
                    QueueDiagnostic(
                        stage,
                        OpenRouterFailureKind.Cancelled,
                        null,
                        watch.ElapsedMilliseconds,
                        true);
                    throw;
                }
                catch
                {
                    QueueDiagnostic(
                        stage,
                        OpenRouterFailureKind.WorkerInternalFailure,
                        null,
                        watch.ElapsedMilliseconds,
                        false);
                    throw;
                }
            });
        }

        private Task<T> RunTransportAsync<T>(
            AISettingsOperationStage stage,
            CancellationToken lifecycleCancellationToken,
            CancellationToken timeoutCancellationToken,
            Func<CancellationToken, Task<T>> operation,
            Func<T, OpenRouterFailureKind> failureSelector,
            Func<T, int?> statusSelector)
        {
            return Task.Run(async () =>
            {
                using (var cancellation =
                       CancellationTokenSource.CreateLinkedTokenSource(
                           lifecycleCancellationToken,
                           timeoutCancellationToken))
                {
                var cancellationToken = cancellation.Token;
                var phaseWatch = Stopwatch.StartNew();
                var startupWatch = Stopwatch.StartNew();
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pending = operation(cancellationToken);
                    QueueDiagnostic(
                        AISettingsOperationStage.StartWorker,
                        OpenRouterFailureKind.None,
                        null,
                        startupWatch.ElapsedMilliseconds,
                        false);
                    var result = await pending.ConfigureAwait(false);
                    QueueDiagnostic(
                        stage,
                        AISettingsOperationPolicy.EffectiveFailure(
                            failureSelector(result),
                            timeoutCancellationToken.IsCancellationRequested,
                            lifecycleCancellationToken.IsCancellationRequested),
                        statusSelector(result),
                        phaseWatch.ElapsedMilliseconds,
                        lifecycleCancellationToken.IsCancellationRequested);
                    return result;
                }
                catch (OperationCanceledException)
                {
                    QueueDiagnostic(
                        stage,
                        AISettingsOperationPolicy.EffectiveFailure(
                            OpenRouterFailureKind.Cancelled,
                            timeoutCancellationToken.IsCancellationRequested,
                            lifecycleCancellationToken.IsCancellationRequested),
                        null,
                        phaseWatch.ElapsedMilliseconds,
                        lifecycleCancellationToken.IsCancellationRequested);
                    throw;
                }
                catch
                {
                    QueueDiagnostic(
                        stage,
                        OpenRouterFailureKind.WorkerInternalFailure,
                        null,
                        phaseWatch.ElapsedMilliseconds,
                        false);
                    throw;
                }
                }
            });
        }

        private void QueueDiagnostic(
            AISettingsOperationStage stage,
            OpenRouterFailureKind failureKind,
            int? httpStatus,
            long elapsedMilliseconds,
            bool cancelled)
        {
            var threadId = Thread.CurrentThread.ManagedThreadId;
            var diagnostic = new AISettingsPhaseDiagnostic(
                stage,
                failureKind,
                httpStatus,
                elapsedMilliseconds,
                AISettingsOperationDiagnostic.Classify(
                    failureKind,
                    failureKind == OpenRouterFailureKind.Timeout,
                    cancelled),
                threadId,
                threadId == _uiThreadId);
            lock (_diagnosticSync)
            {
                _diagnosticTail = WriteDiagnosticAfterAsync(
                    _diagnosticTail,
                    diagnostic);
            }
        }

        private async Task WriteDiagnosticAfterAsync(
            Task predecessor,
            AISettingsPhaseDiagnostic diagnostic)
        {
            try
            {
                await predecessor.ConfigureAwait(false);
            }
            catch
            {
            }

            await Task.Run(() =>
            {
                try
                {
                    _diagnosticSink.Write(diagnostic);
                }
                catch
                {
                }
            }).ConfigureAwait(false);
        }
    }
}
