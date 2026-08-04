using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Autodesk.Navisworks.Api;
using NavisHelper.Core;
using NavisHelper.WPF;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisHelper.AI
{
    internal sealed class AIColorOperationCoordinator
    {
        private sealed class ActiveOperation : IDisposable
        {
            internal ActiveOperation(
                long id,
                AIColorOperationContext context,
                Dispatcher dispatcher,
                CancellationTokenSource userCancellation,
                CancellationTokenSource documentCancellation,
                CancellationTokenSource timeoutCancellation,
                CancellationTokenSource linkedCancellation)
            {
                Id = id;
                Context = context;
                Dispatcher = dispatcher;
                UserCancellation = userCancellation;
                DocumentCancellation = documentCancellation;
                TimeoutCancellation = timeoutCancellation;
                LinkedCancellation = linkedCancellation;
                StartedUtc = DateTime.UtcNow;
            }

            internal long Id { get; }
            internal AIColorOperationContext Context { get; }
            internal Dispatcher Dispatcher { get; }
            internal CancellationTokenSource UserCancellation { get; }
            internal CancellationTokenSource DocumentCancellation { get; }
            internal CancellationTokenSource TimeoutCancellation { get; }
            internal CancellationTokenSource LinkedCancellation { get; }
            internal DateTime StartedUtc { get; }

            public void Dispose()
            {
                LinkedCancellation.Dispose();
                TimeoutCancellation.Dispose();
                DocumentCancellation.Dispose();
                UserCancellation.Dispose();
            }
        }

        private static readonly Lazy<AIColorOperationCoordinator> LazyCurrent =
            new Lazy<AIColorOperationCoordinator>(
                () => new AIColorOperationCoordinator(
                    new AIColorWorkflow(),
                    TimeSpan.FromSeconds(90)));

        private readonly object _sync = new object();
        private readonly AIColorWorkflow _workflow;
        private readonly TimeSpan _operationTimeout;
        private ActiveOperation _active;
        private long _nextOperationId;

        internal AIColorOperationCoordinator(
            AIColorWorkflow workflow,
            TimeSpan operationTimeout)
        {
            _workflow = workflow ??
                        throw new ArgumentNullException(nameof(workflow));
            if (operationTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(operationTimeout));
            _operationTimeout = operationTimeout;
        }

        internal static AIColorOperationCoordinator Current =>
            LazyCurrent.Value;

        internal bool TryStartOpenRouter()
        {
            var panel = NavisHelperPanel.Current;
            if (panel == null)
            {
                Logger.Error(
                    "AI coloring requires the active NavisHelper panel.",
                    "AIColorOperationCoordinator");
                return false;
            }
            lock (_sync)
            {
                if (_active != null)
                {
                    panel?.SetAIOutcome(AiPanelOutcome.Failure(
                        AiColorOutcomeKind.AlreadyRunning));
                    return false;
                }
            }

            AIColorOperationContext context;
            AiColorOutcome failure;
            if (!_workflow.TryPrepareOnUiThread(
                    true,
                    out context,
                    out failure))
            {
                panel?.SetAIOutcome(AiPanelOutcome.Failure(failure.Kind));
                return false;
            }

            var dispatcher = panel.Dispatcher;
            var userCancellation = new CancellationTokenSource();
            var documentCancellation = new CancellationTokenSource();
            var timeoutCancellation =
                new CancellationTokenSource(_operationTimeout);
            var linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    userCancellation.Token,
                    documentCancellation.Token,
                    timeoutCancellation.Token);
            var operation = new ActiveOperation(
                Interlocked.Increment(ref _nextOperationId),
                context,
                dispatcher,
                userCancellation,
                documentCancellation,
                timeoutCancellation,
                linkedCancellation);

            lock (_sync)
            {
                if (_active != null)
                {
                    operation.Dispose();
                    panel?.SetAIOutcome(AiPanelOutcome.Failure(
                        AiColorOutcomeKind.AlreadyRunning));
                    return false;
                }
                _active = operation;
                NwApplication.ActiveDocumentChanging +=
                    OnActiveDocumentChanging;
            }

            panel?.SetAIOperationBusy(true);
            panel?.SetAIOutcome(AiPanelOutcome.Starting(
                context.ModelId,
                (int)context.Scheme,
                context.ObjectNames));

            var networkTask = Task.Run(
                () => _workflow.ExecuteNetworkAsync(
                    context,
                    linkedCancellation.Token),
                CancellationToken.None);
            ObserveNetworkAsync(operation, networkTask);
            return true;
        }

        internal bool TryApplyLocalPalette()
        {
            var panel = NavisHelperPanel.Current;
            lock (_sync)
            {
                if (_active != null)
                {
                    panel?.SetAIOutcome(AiPanelOutcome.Failure(
                        AiColorOutcomeKind.AlreadyRunning));
                    return false;
                }
            }

            AIColorOperationContext context;
            AiColorOutcome failure;
            if (!_workflow.TryPrepareOnUiThread(
                    false,
                    out context,
                    out failure))
            {
                panel?.SetAIOutcome(AiPanelOutcome.Failure(failure.Kind));
                return false;
            }

            var outcome = _workflow.CreateLocalPaletteOutcome(context);
            if (!outcome.IsSuccess)
            {
                panel?.SetAIOutcome(AiPanelOutcome.Failure(outcome.Kind));
                return false;
            }
            if (context.DocumentIdentity == null ||
                !context.DocumentIdentity.Matches(
                    NwApplication.ActiveDocument))
            {
                panel?.SetAIOutcome(AiPanelOutcome.Failure(
                    AiColorOutcomeKind.DocumentChanged));
                return false;
            }

            var appliedCount = _workflow.ApplyOnUiThread(context, outcome);
            if (appliedCount == 0)
            {
                panel?.SetAIOutcome(AiPanelOutcome.Failure(
                    AiColorOutcomeKind.NavisworksError));
                return false;
            }
            panel?.SetAIOutcome(AiPanelOutcome.Success(
                AiColorSource.LocalPalette,
                string.Empty,
                (int)context.Scheme,
                context.ObjectNames,
                outcome.Colors.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value),
                appliedCount));
            panel?.AddColorHistory(
                new System.Collections.Generic.List<string>(
                    context.ObjectNames),
                outcome.Colors.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value),
                context.Selection);
            Logger.Info(
                "Local palette applied to " + appliedCount + " objects.",
                "AIColorOperationCoordinator");
            return appliedCount > 0;
        }

        internal void CancelCurrent()
        {
            ActiveOperation operation;
            lock (_sync)
                operation = _active;
            TryCancel(operation?.UserCancellation);
        }

        private async void ObserveNetworkAsync(
            ActiveOperation operation,
            Task<AiColorOutcome> networkTask)
        {
            AiColorOutcome outcome;
            try
            {
                outcome = await networkTask.ConfigureAwait(false);
                if (outcome.Kind == AiColorOutcomeKind.Cancelled)
                    outcome = CancellationOutcome(
                        operation,
                        outcome.Diagnostics);
            }
            catch (OperationCanceledException)
            {
                outcome = CancellationOutcome(operation, null);
            }
            catch (Exception ex)
            {
                Logger.Error(
                    "OpenRouter network exception: " +
                    ex.GetType().Name,
                    "AIColorOperationCoordinator.Network");
                outcome = AiColorOutcome.Failure(
                    AiColorOutcomeKind.ServiceUnavailable);
            }

            try
            {
                _ = operation.Dispatcher.BeginInvoke(
                    new Action(() => CompleteSafelyOnUiThread(
                        operation,
                        outcome)),
                    DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                Logger.Error(
                    "OpenRouter dispatch exception: " +
                    ex.GetType().Name,
                    "AIColorOperationCoordinator.Dispatch");
                Release(operation);
            }
        }

        private void CompleteSafelyOnUiThread(
            ActiveOperation operation,
            AiColorOutcome outcome)
        {
            try
            {
                CompleteOnUiThread(operation, outcome);
            }
            catch (Exception ex)
            {
                Logger.Error(
                    "OpenRouter completion exception: " +
                    ex.GetType().Name,
                    "AIColorOperationCoordinator.Complete");
                if (Release(operation))
                {
                    try
                    {
                        NavisHelperPanel.Current?.SetAIOperationBusy(false);
                        NavisHelperPanel.Current?.SetAIOutcome(
                            AiPanelOutcome.Failure(
                                AiColorOutcomeKind.NavisworksError));
                    }
                    catch (Exception panelException)
                    {
                        Logger.Error(
                            "OpenRouter panel exception: " +
                            panelException.GetType().Name,
                            "AIColorOperationCoordinator.Panel");
                    }
                }
            }
        }

        private void CompleteOnUiThread(
            ActiveOperation operation,
            AiColorOutcome outcome)
        {
            var documentChanged =
                operation.DocumentCancellation.IsCancellationRequested ||
                operation.Context.DocumentIdentity == null ||
                !operation.Context.DocumentIdentity.Matches(
                    NwApplication.ActiveDocument);
            var decision = AIColorCompletionDecision.Evaluate(
                outcome,
                documentChanged,
                operation.TimeoutCancellation.IsCancellationRequested,
                operation.UserCancellation.IsCancellationRequested);
            if (!Release(operation))
                return;

            var panel = NavisHelperPanel.Current;
            panel?.SetAIOperationBusy(false);
            if (!decision.MayApply)
            {
                panel?.SetAIOutcome(AiPanelOutcome.Failure(
                    decision.Outcome.Kind,
                    operation.Context.ModelId,
                    operation.Context.ObjectNames));
                Logger.Error(
                    FormatDiagnostic(
                        "complete",
                        decision.Outcome,
                        operation,
                        0),
                    "AIColorOperationCoordinator");
                return;
            }

            var appliedCount = _workflow.ApplyOnUiThread(
                operation.Context,
                decision.Outcome);
            if (appliedCount == 0)
            {
                panel?.SetAIOutcome(AiPanelOutcome.Failure(
                    AiColorOutcomeKind.NavisworksError,
                    operation.Context.ModelId,
                    operation.Context.ObjectNames));
                return;
            }
            panel?.SetAIOutcome(AiPanelOutcome.Success(
                AiColorSource.OpenRouter,
                operation.Context.ModelId,
                (int)operation.Context.Scheme,
                operation.Context.ObjectNames,
                decision.Outcome.Colors.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value),
                appliedCount));
            panel?.AddColorHistory(
                new System.Collections.Generic.List<string>(
                    operation.Context.ObjectNames),
                decision.Outcome.Colors.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value),
                operation.Context.Selection);
            Logger.Info(
                FormatDiagnostic(
                    "complete",
                    decision.Outcome,
                    operation,
                    appliedCount),
                "AIColorOperationCoordinator");
        }

        private static string FormatDiagnostic(
            string stage,
            AiColorOutcome outcome,
            ActiveOperation operation,
            int appliedCount)
        {
            var diagnostics = outcome?.Diagnostics;
            var elapsed = operation == null
                ? 0L
                : (long)(DateTime.UtcNow - operation.StartedUtc)
                    .TotalMilliseconds;
            return "stage=" + (stage ?? string.Empty) +
                   "; outcome=" + (outcome?.DiagnosticCode ?? "Unknown") +
                   "; model=" + (operation?.Context.ModelId ?? string.Empty) +
                   "; elapsed_ms=" + elapsed +
                   "; finish_reason=" + (diagnostics?.FinishReason ?? string.Empty) +
                   "; unique_names=" +
                   (diagnostics?.RequestedUniqueNameCount ??
                    operation?.Context.ObjectNames.Count ?? 0) +
                   "; output_budget=" +
                   (diagnostics?.CalculatedOutputBudget ?? 0) +
                   "; provider_max=" +
                   (diagnostics?.ProviderMaxCompletionTokens?.ToString() ?? "unknown") +
                   "; reasoning=" +
                   (diagnostics?.ReasoningPolicy ?? "unknown") +
                   "; applied=" + appliedCount;
        }

        private void OnActiveDocumentChanging(object sender, EventArgs args)
        {
            ActiveOperation operation;
            lock (_sync)
                operation = _active;
            TryCancel(operation?.DocumentCancellation);
        }

        private static void TryCancel(
            CancellationTokenSource cancellation)
        {
            if (cancellation == null)
                return;
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Release won the race; the operation is already inactive.
            }
        }

        private static AiColorOutcome CancellationOutcome(
            ActiveOperation operation,
            AiColorDiagnostics diagnostics)
        {
            return AiColorOutcome.Failure(
                AiCancellationClassifier.Classify(
                    operation.DocumentCancellation.IsCancellationRequested,
                    operation.TimeoutCancellation.IsCancellationRequested),
                null,
                diagnostics);
        }

        private bool Release(ActiveOperation operation)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(_active, operation))
                    return false;
                NwApplication.ActiveDocumentChanging -=
                    OnActiveDocumentChanging;
                _active = null;
            }
            operation.Dispose();
            return true;
        }
    }
}
