using System;
using System.Threading;

namespace NavisHelper.AI
{
    internal enum AISettingsOperationStage
    {
        CaptureKeyState = 0,
        StartWorker,
        ValidateKey,
        PersistKey,
        LoadModels,
        BindModels
    }

    internal sealed class AISettingsPhaseDiagnostic
    {
        internal AISettingsPhaseDiagnostic(
            AISettingsOperationStage stage,
            OpenRouterFailureKind failureKind,
            int? httpStatus,
            long elapsedMilliseconds,
            AISettingsOperationClassification classification,
            int managedThreadId,
            bool isUiThread)
        {
            Stage = stage;
            FailureKind = failureKind;
            HttpStatus = httpStatus;
            ElapsedMilliseconds = elapsedMilliseconds;
            Classification = classification;
            ManagedThreadId = managedThreadId;
            IsUiThread = isUiThread;
        }

        internal AISettingsOperationStage Stage { get; }
        internal OpenRouterFailureKind FailureKind { get; }
        internal int? HttpStatus { get; }
        internal long ElapsedMilliseconds { get; }
        internal AISettingsOperationClassification Classification { get; }
        internal int ManagedThreadId { get; }
        internal bool IsUiThread { get; }
    }

    internal enum AISettingsOperationClassification
    {
        Completed = 0,
        Timeout,
        Cancelled,
        WorkerLifecycleFailure,
        Failure
    }

    internal static class AISettingsOperationPolicy
    {
        internal static readonly TimeSpan KeyValidationTimeout =
            TimeSpan.FromSeconds(30);

        internal static readonly TimeSpan ModelCatalogTimeout =
            TimeSpan.FromSeconds(45);

        internal static bool MayMutateKey(
            OpenRouterValidationResult validation,
            bool timedOut,
            bool cancelled)
        {
            return validation != null &&
                   validation.IsSuccess &&
                   !timedOut &&
                   !cancelled;
        }

        internal static AiConnectionDisplayState CatalogCompletionState(
            OpenRouterCatalogResult catalog,
            bool hasCompatibleSelection)
        {
            return catalog != null &&
                   catalog.IsAvailable &&
                   hasCompatibleSelection
                ? AiConnectionDisplayState.Ready
                : AiConnectionDisplayState.Connected;
        }

        internal static OpenRouterCatalogResult NormalizeCatalog(
            OpenRouterCatalogResult catalog,
            bool timedOut,
            bool cancelled)
        {
            if (catalog == null)
            {
                return OpenRouterCatalogResult.Unavailable(
                    OpenRouterFailureKind.Network);
            }
            var failure = EffectiveFailure(
                catalog.FailureKind,
                timedOut,
                cancelled);
            if (failure == OpenRouterFailureKind.None)
                return catalog;
            if (!catalog.IsAvailable && failure == catalog.FailureKind)
                return catalog;
            return OpenRouterCatalogResult.Unavailable(
                failure,
                catalog.HttpStatus);
        }

        internal static OpenRouterFailureKind EffectiveFailure(
            OpenRouterFailureKind failureKind,
            bool timedOut,
            bool cancelled)
        {
            if (cancelled)
                return OpenRouterFailureKind.Cancelled;
            if (timedOut)
                return OpenRouterFailureKind.Timeout;
            return failureKind;
        }

        internal static OpenRouterFailureKind EffectiveFailure(
            OpenRouterFailureKind failureKind,
            CancellationToken timeoutToken,
            CancellationToken lifecycleToken)
        {
            return EffectiveFailure(
                failureKind,
                timeoutToken.IsCancellationRequested,
                lifecycleToken.IsCancellationRequested);
        }
    }

    internal static class AISettingsOperationDiagnostic
    {
        internal static string FormatPhase(
            AISettingsPhaseDiagnostic diagnostic)
        {
            if (diagnostic == null)
                throw new ArgumentNullException(nameof(diagnostic));
            return Format(
                       diagnostic.Stage,
                       diagnostic.FailureKind,
                       diagnostic.HttpStatus,
                       diagnostic.ElapsedMilliseconds,
                       diagnostic.Classification) +
                   " managed_thread_id=" + diagnostic.ManagedThreadId +
                   " ui_thread=" +
                   (diagnostic.IsUiThread ? "true" : "false");
        }

        internal static string Format(
            AISettingsOperationStage stage,
            OpenRouterFailureKind failureKind,
            int? httpStatus,
            long elapsedMilliseconds,
            AISettingsOperationClassification classification)
        {
            return "stage=" + StageToken(stage) +
                   " outcome=" + OutcomeToken(failureKind) +
                   " http_status=" +
                   (httpStatus.HasValue
                       ? httpStatus.Value.ToString()
                       : "none") +
                   " elapsed_ms=" + Math.Max(0, elapsedMilliseconds) +
                   " classification=" + ClassificationToken(classification);
        }

        internal static AISettingsOperationClassification Classify(
            OpenRouterFailureKind failureKind,
            bool timedOut,
            bool cancelled)
        {
            if (cancelled)
                return AISettingsOperationClassification.Cancelled;
            if (timedOut || failureKind == OpenRouterFailureKind.Timeout)
                return AISettingsOperationClassification.Timeout;
            if (IsWorkerLifecycleFailure(failureKind))
            {
                return AISettingsOperationClassification
                    .WorkerLifecycleFailure;
            }
            return failureKind == OpenRouterFailureKind.None
                ? AISettingsOperationClassification.Completed
                : AISettingsOperationClassification.Failure;
        }

        private static bool IsWorkerLifecycleFailure(
            OpenRouterFailureKind failureKind)
        {
            switch (failureKind)
            {
                case OpenRouterFailureKind.WorkerMissing:
                case OpenRouterFailureKind.WorkerRuntimeMissing:
                case OpenRouterFailureKind.WorkerStartupFailed:
                case OpenRouterFailureKind.WorkerFailed:
                case OpenRouterFailureKind.WorkerInternalFailure:
                case OpenRouterFailureKind.ProtocolMismatch:
                    return true;
                default:
                    return false;
            }
        }

        private static string StageToken(AISettingsOperationStage stage)
        {
            switch (stage)
            {
                case AISettingsOperationStage.CaptureKeyState:
                    return "capture_key_state";
                case AISettingsOperationStage.StartWorker:
                    return "start_worker";
                case AISettingsOperationStage.ValidateKey:
                    return "validate_key";
                case AISettingsOperationStage.PersistKey:
                    return "persist_key";
                case AISettingsOperationStage.BindModels:
                    return "bind_models";
                default:
                    return "load_models";
            }
        }

        private static string OutcomeToken(
            OpenRouterFailureKind failureKind)
        {
            return failureKind == OpenRouterFailureKind.None
                ? "success"
                : failureKind.ToString().ToLowerInvariant();
        }

        private static string ClassificationToken(
            AISettingsOperationClassification classification)
        {
            switch (classification)
            {
                case AISettingsOperationClassification.Timeout:
                    return "timeout";
                case AISettingsOperationClassification.Cancelled:
                    return "cancelled";
                case AISettingsOperationClassification.WorkerLifecycleFailure:
                    return "worker_lifecycle";
                case AISettingsOperationClassification.Failure:
                    return "failure";
                default:
                    return "completed";
            }
        }
    }
}
