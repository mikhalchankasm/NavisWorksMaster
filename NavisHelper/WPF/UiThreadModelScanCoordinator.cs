using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;
using Autodesk.Navisworks.Api;
using NavisHelper.Core;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisHelper.WPF
{
    /// <summary>
    /// One cooperative model operation: preparation, traversal, commit
    /// gate, and one synchronous apply call. All Autodesk document/model
    /// access stays on the Navisworks UI dispatcher; only the yields return
    /// control to input and rendering between bounded work portions.
    /// </summary>
    internal sealed class UiThreadModelOperationRequest
    {
        internal string ProgressCaption { get; set; }
        internal string PreparePhaseMessage { get; set; }
        internal Func<UiThreadModelPreparationContext, Task> PrepareAsync { get; set; }
        internal Action<int> ReportScanProgress { get; set; }
        internal string ScanPhaseMessage { get; set; }
        internal string VerifyPhaseMessage { get; set; }
        internal string ApplyPhaseMessage { get; set; }
        internal string RecoveryPhaseMessage { get; set; }
        internal string SnapshotUnavailableMessage { get; set; }
        internal Func<ModelItem, bool> IncludeItem { get; set; }
        internal CooperativeModelScanInvalidation ObservedChanges { get; set; }

        /// <summary>
        /// What the apply phase does with the scanned items. ReplaceSelection
        /// requires CommitGuardSelection to verify the source before applying.
        /// </summary>
        internal CooperativeModelApplyKind ApplyKind { get; set; }
        internal ModelItemCollection CommitGuardSelection { get; set; }

        /// <summary>
        /// When true, an empty scan result skips the apply phase entirely so
        /// callers can report "nothing matched" without clearing anything.
        /// </summary>
        internal bool ApplyOnlyIfNonEmptyResult { get; set; }
    }

    /// <summary>
    /// Final state of one cooperative model operation. Plain data: the
    /// coordinator keeps the progress ownership and the operation lease for
    /// the whole scan+apply lifetime, so every path (success, cancel,
    /// invalidation, apply failure) closes the progress dialog and releases
    /// the busy gate deterministically.
    /// </summary>
    internal sealed class UiThreadModelOperationResult
    {
        internal UiThreadModelOperationResult(
            CooperativeModelScanStatus status)
        {
            Status = status;
        }

        internal CooperativeModelScanStatus Status { get; set; }
        internal int ScanItemCount { get; set; }
        internal int AppliedItemCount { get; set; }
        internal CooperativeModelApplyStatus ApplyStatus { get; set; } =
            CooperativeModelApplyStatus.NotStarted;
        internal Exception ApplyError { get; set; }
        internal CooperativeModelRecoveryStatus RecoveryStatus { get; set; }
        internal Exception RecoveryError { get; set; }
        internal CooperativeModelOperationTimingSnapshot Timings { get; set; } =
            new CooperativeModelOperationTimingSnapshot();
    }

    internal sealed class UiThreadModelScanCoordinator : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly CooperativeModelScanOperationGate _operations =
            new CooperativeModelScanOperationGate();
        private readonly CooperativeModelScanGenerationTracker _generations =
            new CooperativeModelScanGenerationTracker();
        private readonly CooperativeModelScanLifecycle _lifecycle =
            new CooperativeModelScanLifecycle();
        private readonly CooperativeModelScanChunkPolicy _chunkPolicy;
        private readonly CooperativeInvalidationSuppressor _suppressor =
            new CooperativeInvalidationSuppressor();
        private CooperativeSubscriptionSet _applicationSubscriptions;
        private CooperativeSubscriptionSet _documentSubscriptions;
        private Document _observedDocument;
        private CooperativeModelScanInvalidation _activeObservedChanges;
        private bool _eventsAttached;
        private bool _attachRequested;
        private bool _disposed;

        internal UiThreadModelScanCoordinator(
            Dispatcher dispatcher,
            CooperativeModelScanChunkPolicy chunkPolicy = null)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _chunkPolicy = chunkPolicy ?? new CooperativeModelScanChunkPolicy();
        }

        internal void Attach()
        {
            VerifyDispatcherAccess();
            if (_disposed)
                throw new ObjectDisposedException(nameof(UiThreadModelScanCoordinator));
            if (_lifecycle.CanStart)
                return;

            _attachRequested = true;
            try
            {
                var staleFailures = StopObservingDocumentBestEffort();
                staleFailures.AddRange(StopApplicationEventsBestEffort());
                ThrowSubscriptionFailures(staleFailures);

                _applicationSubscriptions = new CooperativeSubscriptionSet();
                Subscribe(
                    () => NwApplication.ActiveDocumentChanging += OnActiveDocumentChanging,
                    () => NwApplication.ActiveDocumentChanging -= OnActiveDocumentChanging,
                    _applicationSubscriptions);
                Subscribe(
                    () => NwApplication.ActiveDocumentChanged += OnActiveDocumentChanged,
                    () => NwApplication.ActiveDocumentChanged -= OnActiveDocumentChanged,
                    _applicationSubscriptions);
                _eventsAttached = true;
                ObserveDocument(NwApplication.ActiveDocument);
                _lifecycle.Resume();
            }
            catch (Exception attachError)
            {
                _attachRequested = false;
                _lifecycle.Suspend();
                CancelCurrent();
                var failures = new List<Exception> { attachError };
                failures.AddRange(StopObservingDocumentBestEffort());
                failures.AddRange(StopApplicationEventsBestEffort());
                throw new AggregateException(
                    "Failed to attach the model-scan lifecycle.",
                    failures);
            }
        }

        internal void Detach()
        {
            VerifyDispatcherAccess();
            _attachRequested = false;
            _lifecycle.Suspend();
            CancelCurrent();
            var failures = StopObservingDocumentBestEffort();
            failures.AddRange(StopApplicationEventsBestEffort());
            ThrowSubscriptionFailures(failures);
        }

        internal void CancelCurrent()
        {
            _operations.CancelCurrent();
        }

        internal bool IsAvailable =>
            !_disposed && _attachRequested && _lifecycle.CanStart;

        internal async Task<UiThreadModelOperationResult> RunOperationAsync(
            Document document,
            UiThreadModelOperationRequest request)
        {
            VerifyDispatcherAccess();
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var totalTimer = Stopwatch.StartNew();
            if (_disposed ||
                !_attachRequested ||
                !_lifecycle.CanStart ||
                !_eventsAttached ||
                !ReferenceEquals(_observedDocument, document))
            {
                return new UiThreadModelOperationResult(
                    CooperativeModelScanStatus.Suspended);
            }

            var observedChanges = request.ObservedChanges |
                                  CooperativeModelScanInvalidation.Document |
                                  CooperativeModelScanInvalidation.ModelCollection;
            bool changesVisibility = request.ApplyKind == CooperativeModelApplyKind.HideItems ||
                                     request.ApplyKind == CooperativeModelApplyKind.ShowItems;
            if (changesVisibility)
                observedChanges |= CooperativeModelScanInvalidation.ModelProperties;
            if (!_operations.TryBegin(out var operation))
            {
                return new UiThreadModelOperationResult(
                    CooperativeModelScanStatus.Busy);
            }

            _activeObservedChanges = observedChanges;
            var context = new OperationContext
            {
                Document = document,
                Request = request,
                Operation = operation,
                Generation = _generations.Capture(observedChanges),
                Result = new UiThreadModelOperationResult(
                    CooperativeModelScanStatus.Suspended)
            };
            var scanTimer = new Stopwatch();
            Progress progress = null;
            CooperativeProgressOwnership progressOwnership = null;
            try
            {
                context.DocumentIdentity = UiThreadModelScanDocumentIdentity.Capture(document);
                if (changesVisibility)
                    context.VisibilitySnapshot = new UiThreadModelVisibilitySnapshot();
                if (!IsCurrentDocument(document) ||
                    !ReferenceEquals(_observedDocument, document))
                {
                    context.Result.Status = CooperativeModelScanStatus.DocumentChanged;
                    return context.Result;
                }

                progress = NwApplication.BeginProgress(
                    request.ProgressCaption ?? string.Empty);
                // A null progress (degraded dialog) must not close a foreign
                // progress scope when the ownership is disposed.
                if (progress != null)
                    progressOwnership = new CooperativeProgressOwnership(EndProgressSafely);
                context.Progress = progress;
                ThrowIfStopped(context);
                if (request.PrepareAsync != null)
                {
                    var prepareTimer = Stopwatch.StartNew();
                    // Preparation has unknown total work (ancestor depth may
                    // vary). Name it honestly without assigning a percentage.
                    bool preparing = BeginSubOperationSafely(progress, 0.0, request.PreparePhaseMessage);
                    bool prepared = false;
                    try
                    {
                        await Dispatcher.Yield(DispatcherPriority.Background);
                        ThrowIfStopped(context);
                        var preparation = new UiThreadModelPreparationContext(
                            document,
                            () => ThrowIfStopped(context),
                            (source, visit) => CooperativeModelWorkLoop.RunAsync(
                                source, visit, () => ThrowIfStopped(context),
                                YieldToDispatcherAsync, _chunkPolicy,
                                (count, elapsed) =>
                                {
                                    context.Result.Timings.PrepareChunks++;
                                    context.Result.Timings.MaxPrepareChunkMs = Math.Max(
                                        context.Result.Timings.MaxPrepareChunkMs, (long)elapsed.TotalMilliseconds);
                                    if (!UpdateProgressSafely(progress, 0.0)) context.ProgressCanceled = true;
                                }));
                        await request.PrepareAsync(preparation);
                        ThrowIfStopped(context);
                        prepared = true;
                    }
                    finally
                    {
                        context.Result.Timings.PrepareMs = prepareTimer.ElapsedMilliseconds;
                        if (preparing) EndSubOperationSafely(progress, prepared);
                    }
                }
                if (request.IncludeItem == null)
                    throw new ArgumentException("The request must provide an include-item predicate.", nameof(request));
                if (request.ApplyKind == CooperativeModelApplyKind.ReplaceSelection && request.CommitGuardSelection == null)
                    throw new ArgumentException("ReplaceSelection requires a source selection guard.", nameof(request));

                scanTimer.Start();
                context.ScanSubOperationOpen = BeginSubOperationSafely(
                    progress,
                    CooperativeProgressPhaseMath.ScanPhaseShare,
                    request.ScanPhaseMessage);
                var roots = CopyRoots(document);
                var seen = new HashSet<ModelItem>();

                for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
                {
                    using (var traversal = new CooperativeDepthFirstTraversal<ModelItem>(
                        roots[rootIndex],
                        item => item.Children,
                        seen))
                    {
                        bool rootComplete = false;
                        while (!rootComplete)
                        {
                            var stopReason = GetStopReason(context);
                            if (stopReason.HasValue)
                            {
                                context.Result.Status = stopReason.Value;
                                return context.Result;
                            }

                            var chunkTimer = Stopwatch.StartNew();
                            int processedInChunk = 0;
                            do
                            {
                                if (!traversal.TryTakeNext(out var item))
                                {
                                    rootComplete = true;
                                    break;
                                }

                                context.VisitedItems++;
                                if (context.VisibilitySnapshot != null &&
                                    !context.VisibilitySnapshot.TryRecord(item))
                                    throw new InvalidOperationException(request.SnapshotUnavailableMessage);
                                if (request.IncludeItem(item))
                                    context.ScanResult.Add(item);
                                processedInChunk++;
                            }
                            while (!_chunkPolicy.ShouldYield(
                                processedInChunk,
                                chunkTimer.Elapsed));

                            if (processedInChunk > 0)
                            {
                                context.ScanChunks++;
                                context.MaxScanChunkMs = Math.Max(
                                    context.MaxScanChunkMs,
                                    chunkTimer.ElapsedMilliseconds);
                                PublishScanTimingSnapshot(context, scanTimer);
                                request.ReportScanProgress?.Invoke(context.VisitedItems);
                                UpdateScanProgress(context, 0.0);
                            }
                            if (!rootComplete)
                            {
                                await Dispatcher.Yield(DispatcherPriority.Background);
                            }
                        }
                    }

                    await Dispatcher.Yield(DispatcherPriority.Background);
                }

                ThrowIfStopped(context);
                UpdateScanProgress(context, 1.0);
                context.ScanCompleted = true;
                EndSubOperationSafely(context, isApplySubOperation: false);
                scanTimer.Stop();
                context.Result.Status = CooperativeModelScanStatus.Completed;
                context.Result.ScanItemCount = context.ScanResult.Count;
                PublishScanTimingSnapshot(context, scanTimer);

                await RunCommitGateAndApplyAsync(context);
                return context.Result;
            }
            catch (CooperativeModelOperationStoppedException stopped)
            {
                context.Result.Status = stopped.Status;
                return context.Result;
            }
            finally
            {
                totalTimer.Stop();
                if (scanTimer.IsRunning)
                    scanTimer.Stop();
                PublishScanTimingSnapshot(context, scanTimer);
                EndSubOperationSafely(context, isApplySubOperation: true);
                EndSubOperationSafely(context, isApplySubOperation: false);
                progressOwnership?.Dispose();
                operation.Dispose();
                _activeObservedChanges = CooperativeModelScanInvalidation.None;
                context.Result.Timings.TotalMs = totalTimer.ElapsedMilliseconds;
                LogOperationResult(request, context.Result);
            }
        }

        /// <summary>
        /// Commit gate followed by the apply phase. The gate re-validates the
        /// operation lease, the document, the generation snapshot, and (when
        /// supplied) the unchanged source selection before the apply call.
        /// </summary>
        private async Task RunCommitGateAndApplyAsync(
            OperationContext context)
        {
            var gateTimer = Stopwatch.StartNew();
            try
            {
                var gateFailure = GetStopReason(context);
                if (gateFailure.HasValue)
                {
                    context.Result.Status = gateFailure.Value;
                    return;
                }

                // The unchanged-selection verification is the one gate step that
                // scales with the size of the operation's source selection
                // (measured: ~9s for a 41k-item selection), so it runs as its own
                // cooperative phase with chunked yields, progress, and stop
                // checks instead of one synchronous stall.
                bool selectionUnchanged = true;
                if (context.Request.CommitGuardSelection != null)
                {
                    selectionUnchanged = await VerifySelectionUnchangedAsync(context);
                    if (!selectionUnchanged)
                    {
                        context.Result.Status = CooperativeModelScanStatus.SourceChanged;
                        return;
                    }
                }

                var canCommit = CooperativeModelScanCommitPolicy.CanCommit(
                    CooperativeModelScanStatus.Completed,
                    context.Operation.IsCurrent,
                    _attachRequested &&
                    _lifecycle.CanStart &&
                    IsCurrentDocument(context.Document) &&
                    ReferenceEquals(_observedDocument, context.Document) &&
                    context.DocumentIdentity != null &&
                    context.DocumentIdentity.Matches(context.Document),
                    _generations.IsCurrent(context.Generation),
                    selectionUnchanged);
                if (!canCommit)
                {
                    var resolved = GetStopReason(context);
                    context.Result.Status = resolved.HasValue
                        ? resolved.Value
                        : CooperativeModelScanStatus.SourceChanged;
                    return;
                }

                if (context.Request.ApplyKind == CooperativeModelApplyKind.None)
                {
                    return;
                }

                if (context.ScanResult.Count == 0 &&
                    context.Request.ApplyOnlyIfNonEmptyResult)
                {
                    context.Result.ApplyStatus = CooperativeModelApplyStatus.SkippedEmpty;
                    return;
                }

            }
            finally
            {
                context.Result.Timings.GateMs = gateTimer.ElapsedMilliseconds;
            }
            await ApplyAsync(context);
        }

        /// <summary>
        /// Cooperative replacement for the old synchronous
        /// IsSelectionUnchanged: builds the expected-selection set and drains
        /// the current selection in bounded chunks with UI yields so a huge
        /// source selection cannot stall the dispatcher at the commit gate.
        /// </summary>
        private async Task<bool> VerifySelectionUnchangedAsync(
            OperationContext context)
        {
            ThrowIfStopped(context);
            var document = context.Document;
            var expected = context.Request.CommitGuardSelection;
            var current = document.CurrentSelection?.SelectedItems;
            if (current == null || current.Count != expected.Count)
                return false;

            bool verifySubOperationOpen = false;
            bool verified = false;
            try
            {
                verifySubOperationOpen = BeginSubOperationSafely(
                    context.Progress,
                    0.2,
                    context.Request.VerifyPhaseMessage);
                var remaining = new HashSet<ModelItem>();
                int processed = 0;
                long total = (long)expected.Count * 2;
                Action<int, TimeSpan> reportChunk = (count, elapsed) =>
                {
                    context.Result.Timings.VerifyChunks++;
                    context.Result.Timings.MaxVerifyChunkMs = Math.Max(
                        context.Result.Timings.MaxVerifyChunkMs, (long)elapsed.TotalMilliseconds);
                    UpdateVerifyProgress(context, verifySubOperationOpen, total == 0 ? 1.0 : (double)processed / total);
                };
                await CooperativeModelWorkLoop.RunAsync(
                    expected,
                    item =>
                    {
                        if (item != null) remaining.Add(item);
                        processed++;
                    },
                    () => ThrowIfStopped(context), YieldToDispatcherAsync, _chunkPolicy, reportChunk);
                bool matches = true;
                await CooperativeModelWorkLoop.RunAsync(
                    current,
                    item =>
                    {
                        if (item == null || !remaining.Remove(item)) matches = false;
                        processed++;
                    },
                    () => ThrowIfStopped(context), YieldToDispatcherAsync, _chunkPolicy, reportChunk);
                ThrowIfStopped(context);
                UpdateVerifyProgress(context, verifySubOperationOpen, 1.0);
                verified = true;
                return matches && remaining.Count == 0;
            }
            finally
            {
                if (verifySubOperationOpen)
                {
                    verifySubOperationOpen = false;
                    EndSubOperationSafely(context.Progress, verified);
                }
            }
        }

        private static void UpdateVerifyProgress(
            OperationContext context,
            bool verifySubOperationOpen,
            double fraction)
        {
            if (context.Progress == null)
                return;
            if (!UpdateProgressSafely(
                context.Progress,
                verifySubOperationOpen
                    ? fraction
                    : CooperativeProgressPhaseMath.MapVerifyFraction(fraction)))
                context.ProgressCanceled = true;
        }

        /// <summary>
        /// Applies the scan result with one indivisible Autodesk call per
        /// operation kind (CurrentSelection.CopyFrom or Models.SetHidden over
        /// the whole result). Repeated mutations would also repeat native
        /// selection/visibility updates. The apply remains synchronous:
        /// cancel and invalidation are honored up to the last
        /// stop check. Its measured duration is logged as applyMs; this is
        /// evidence for that run, not an upper bound for other models.
        /// Autodesk provides no cancellation inside the call or guaranteed
        /// rollback on exceptions; failure uses verified compensation.
        /// </summary>
        private async Task ApplyAsync(
            OperationContext context)
        {
            // Last cancellation point before the irreversible boundary. The
            // full stop reason (lease, generation, document, progress dialog)
            // is re-checked here, right before the mutation.
            var stopReason = GetStopReason(context);
            if (stopReason.HasValue)
            {
                context.Result.Status = stopReason.Value;
                return;
            }

            var progress = context.Progress;
            var applyTimer = new Stopwatch();
            context.ApplySubOperationOpen = BeginSubOperationSafely(
                progress,
                1.0,
                context.Request.ApplyPhaseMessage);
            // Allow the phase label and pending Cancel input to be processed
            // before entering the synchronous Autodesk call.
            await Dispatcher.Yield(DispatcherPriority.Background);
            ThrowIfStopped(context);
            if (context.DocumentIdentity == null || !context.DocumentIdentity.Matches(context.Document))
                throw new CooperativeModelOperationStoppedException(CooperativeModelScanStatus.DocumentChanged);
            var request = context.Request;
            var document = context.Document;
            var scanResult = context.ScanResult;

            applyTimer.Start();
            try
            {
                switch (request.ApplyKind)
                {
                    case CooperativeModelApplyKind.ReplaceSelection:
                        // CopyFrom replaces the selection in one call;
                        // its own synchronous Changed event is suppressed so
                        // the operation cannot invalidate itself.
                        SuppressSelfMutations(
                            () => document.CurrentSelection.CopyFrom(scanResult));
                        break;
                    case CooperativeModelApplyKind.HideItems:
                    case CooperativeModelApplyKind.ShowItems:
                        SuppressSelfMutations(
                            () => document.Models.SetHidden(
                                scanResult,
                                request.ApplyKind == CooperativeModelApplyKind.HideItems));
                        break;
                }

                context.Result.AppliedItemCount = scanResult.Count;
                context.Result.ApplyStatus = CooperativeModelApplyStatus.Completed;
                context.Result.Timings.ApplyMs = applyTimer.ElapsedMilliseconds;
                PublishApplyTimingSnapshot(context, applyTimer);
                // Only now, with the result applied, may the bar reach full
                // completion.
                UpdateProgressSafely(progress, 1.0);
            }
            catch (Exception applyError)
            {
                context.Result.Status = CooperativeModelScanStatus.ApplyFailed;
                context.Result.ApplyError = applyError;
                context.Result.Timings.ApplyMs = applyTimer.ElapsedMilliseconds;
                PublishApplyTimingSnapshot(context, applyTimer);
                Logger.Error(
                    "Model operation apply failed: " + applyError,
                    "ModelScan");
                EndSubOperationSafely(context, isApplySubOperation: true);
                await RecoverApplyAsync(context);
            }
            finally
            {
                EndSubOperationSafely(context, isApplySubOperation: true);
            }

        }

        private async Task RecoverApplyAsync(OperationContext context)
        {
            var timer = Stopwatch.StartNew();
            bool recovering = BeginSubOperationSafely(
                context.Progress, 0.0, context.Request.RecoveryPhaseMessage);
            context.Result.RecoveryStatus = CooperativeModelRecoveryStatus.Unknown;
            try
            {
                // Do not yield before the compensating calls: foreign input
                // must not become part of the state that we are restoring.
                // Ignore user Cancel after mutation, but never cross a
                // document/model change or an external source invalidation.
                Action check = () => CheckRecoveryCurrent(context, false);
                Action checkIdentity = () => CheckRecoveryCurrent(context, true);
                Func<Task> yield = async () =>
                {
                    await YieldToDispatcherAsync();
                    checkIdentity();
                };
                Action<int, TimeSpan> report = (count, elapsed) =>
                {
                    context.Result.Timings.MaxRecoveryChunkMs = Math.Max(
                        context.Result.Timings.MaxRecoveryChunkMs, (long)elapsed.TotalMilliseconds);
                    UpdateProgressSafely(context.Progress, 0.0);
                };
                var restores = new List<Action>();
                Func<Task<bool>> verify;
                if (context.Request.ApplyKind == CooperativeModelApplyKind.ReplaceSelection)
                {
                    var expected = context.Request.CommitGuardSelection;
                    restores.Add(() => SuppressSelfMutations(
                        () => context.Document.CurrentSelection.CopyFrom(expected)));
                    verify = async () =>
                    {
                        check();
                        var current = context.Document.CurrentSelection.SelectedItems;
                        if (current.Count != expected.Count) return false;
                        var remaining = new HashSet<ModelItem>();
                        await CooperativeModelWorkLoop.RunAsync(expected, item => remaining.Add(item),
                            check, yield, _chunkPolicy, report);
                        bool matches = true;
                        await CooperativeModelWorkLoop.RunAsync(current, item =>
                        {
                            if (!remaining.Remove(item)) matches = false;
                        }, check, yield, _chunkPolicy, report);
                        return matches && remaining.Count == 0;
                    };
                }
                else
                {
                    var snapshot = context.VisibilitySnapshot.State;
                    if (snapshot.Visible.Count > 0)
                        restores.Add(() => SuppressSelfMutations(
                            () => context.Document.Models.SetHidden(snapshot.Visible, false)));
                    if (snapshot.Hidden.Count > 0)
                        restores.Add(() => SuppressSelfMutations(
                            () => context.Document.Models.SetHidden(snapshot.Hidden, true)));
                    verify = async () =>
                    {
                        bool matches = true;
                        await CooperativeModelWorkLoop.RunAsync(snapshot.Entries, entry =>
                        {
                            if (entry.Key.IsHidden != entry.Value) matches = false;
                        }, check, yield, _chunkPolicy, report);
                        return matches;
                    };
                }
                var recovered = await CooperativeModelApplyRecovery.RecoverAsync(
                    restores, checkIdentity, verify);
                context.Result.RecoveryStatus = recovered.Status;
                if (recovered.Errors.Count > 0)
                    context.Result.RecoveryError = new AggregateException(recovered.Errors);
            }
            catch (Exception error)
            {
                context.Result.RecoveryError = error;
            }
            finally
            {
                context.Result.Timings.RecoveryMs = timer.ElapsedMilliseconds;
                if (recovering) EndSubOperationSafely(context.Progress, false);
            }
        }

        private void CheckRecoveryCurrent(OperationContext context, bool checkIdentity)
        {
            if (_disposed || !_attachRequested || !_lifecycle.CanStart ||
                !IsCurrentDocument(context.Document) ||
                !ReferenceEquals(_observedDocument, context.Document) ||
                (checkIdentity && (context.DocumentIdentity == null ||
                                  !context.DocumentIdentity.Matches(context.Document))))
                throw new CooperativeModelOperationStoppedException(CooperativeModelScanStatus.DocumentChanged);
            var sourceChange = _generations.GetChangeStatus(context.Generation);
            if (sourceChange.HasValue)
                throw new CooperativeModelOperationStoppedException(sourceChange.Value);
        }

        private void ThrowIfStopped(OperationContext context)
        {
            var status = GetStopReason(context);
            if (status.HasValue)
                throw new CooperativeModelOperationStoppedException(status.Value);
        }

        private static async Task YieldToDispatcherAsync()
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
        }

        private void SuppressSelfMutations(Action mutate)
        {
            _suppressor.Begin();
            try
            {
                mutate();
            }
            finally
            {
                _suppressor.End();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            if (!_dispatcher.CheckAccess())
            {
                if (_dispatcher.HasShutdownStarted ||
                    _dispatcher.HasShutdownFinished)
                {
                    // Dispatcher shutdown cannot schedule the Dispose
                    // continuation of an in-flight operation; its progress
                    // dialog and lease are torn down with the process. This
                    // is accepted at shutdown and documented here.
                    _attachRequested = false;
                    _disposed = true;
                    _lifecycle.Dispose();
                    _operations.Dispose();
                    var failures = StopObservingDocumentBestEffort();
                    failures.AddRange(StopApplicationEventsBestEffort());
                    LogSubscriptionFailures(failures);
                    return;
                }

                _dispatcher.Invoke(new Action(Dispose));
                return;
            }

            Exception detachError = null;
            try
            {
                Detach();
            }
            catch (Exception ex)
            {
                detachError = ex;
            }
            finally
            {
                _lifecycle.Dispose();
                _operations.Dispose();
                _disposed = true;
            }

            if (detachError != null)
                throw detachError;
        }

        private void OnActiveDocumentChanging(object sender, EventArgs e)
        {
            if (_disposed)
                return;

            _lifecycle.Suspend();
            _generations.Invalidate(CooperativeModelScanInvalidation.Document);
            CancelCurrent();
            LogSubscriptionFailures(StopObservingDocumentBestEffort());
        }

        private void OnActiveDocumentChanged(object sender, EventArgs e)
        {
            if (_disposed)
                return;

            _generations.Invalidate(CooperativeModelScanInvalidation.Document);
            CancelCurrent();
            if (!_attachRequested || !_eventsAttached)
                return;

            try
            {
                ObserveDocument(NwApplication.ActiveDocument);
                _lifecycle.Resume();
            }
            catch (Exception ex)
            {
                _lifecycle.Suspend();
                CancelCurrent();
                Logger.Error(
                    "Failed to observe the active document for model scans: " + ex,
                    "ModelScan");
            }
        }

        private void OnModelCollectionChanging(object sender, EventArgs e)
        {
            InvalidateCurrent(CooperativeModelScanInvalidation.ModelCollection);
        }

        private void OnModelCollectionChanged(object sender, EventArgs e)
        {
            InvalidateCurrent(CooperativeModelScanInvalidation.ModelCollection);
        }

        private void OnModelItemPropertiesChanged(object sender, EventArgs e)
        {
            InvalidateCurrent(CooperativeModelScanInvalidation.ModelProperties);
        }

        private void OnSelectionChanging(object sender, EventArgs e)
        {
            InvalidateCurrent(CooperativeModelScanInvalidation.Selection);
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            InvalidateCurrent(CooperativeModelScanInvalidation.Selection);
        }

        private void InvalidateCurrent(CooperativeModelScanInvalidation change)
        {
            if (_disposed)
                return;

            // The decision gates both the generation bump and the
            // cancellation: an event the active operation did not opt in to
            // observe must neither abort it nor poison its generation
            // snapshot, and the apply phase's own synchronous mutation
            // events must not invalidate the operation mid-apply. Without
            // the shared gate an observed self-event would cancel the very
            // operation that produced it on the next chunk check.
            if (!CooperativeInvalidationDecision.ShouldCancelOperation(
                    change,
                    _activeObservedChanges,
                    _suppressor.Active))
            {
                return;
            }

            _generations.Invalidate(change);
            CancelCurrent();
        }

        private void ObserveDocument(Document document)
        {
            if (ReferenceEquals(_observedDocument, document) &&
                _documentSubscriptions != null &&
                _documentSubscriptions.Count == DocumentSubscriptionCount &&
                _documentSubscriptions.ActiveCount == DocumentSubscriptionCount)
            {
                return;
            }

            var staleFailures = StopObservingDocumentBestEffort();
            ThrowSubscriptionFailures(staleFailures);
            if (document == null)
                return;

            _observedDocument = document;
            _documentSubscriptions = new CooperativeSubscriptionSet();
            try
            {
                Subscribe(
                    () => document.Models.CollectionChanging += OnModelCollectionChanging,
                    () => document.Models.CollectionChanging -= OnModelCollectionChanging,
                    _documentSubscriptions);
                Subscribe(
                    () => document.Models.CollectionChanged += OnModelCollectionChanged,
                    () => document.Models.CollectionChanged -= OnModelCollectionChanged,
                    _documentSubscriptions);
                Subscribe(
                    () => document.Models.ModelItemPropertiesChanged += OnModelItemPropertiesChanged,
                    () => document.Models.ModelItemPropertiesChanged -= OnModelItemPropertiesChanged,
                    _documentSubscriptions);
                Subscribe(
                    () => document.CurrentSelection.Changing += OnSelectionChanging,
                    () => document.CurrentSelection.Changing -= OnSelectionChanging,
                    _documentSubscriptions);
                Subscribe(
                    () => document.CurrentSelection.Changed += OnSelectionChanged,
                    () => document.CurrentSelection.Changed -= OnSelectionChanged,
                    _documentSubscriptions);
            }
            catch (Exception subscribeError)
            {
                var failures = new List<Exception> { subscribeError };
                failures.AddRange(StopObservingDocumentBestEffort());
                throw new AggregateException(
                    "Failed to subscribe to active-document model events.",
                    failures);
            }
        }

        private List<Exception> StopObservingDocumentBestEffort()
        {
            var failures = new List<Exception>();
            var subscriptions = _documentSubscriptions;
            if (subscriptions == null)
            {
                _observedDocument = null;
                return failures;
            }

            var result = subscriptions.DetachAll();
            failures.AddRange(result.Failures);
            if (!subscriptions.HasActive)
            {
                _documentSubscriptions = null;
                _observedDocument = null;
            }
            return failures;
        }

        private List<Exception> StopApplicationEventsBestEffort()
        {
            var failures = new List<Exception>();
            var subscriptions = _applicationSubscriptions;
            if (subscriptions == null)
            {
                _eventsAttached = false;
                return failures;
            }

            var result = subscriptions.DetachAll();
            failures.AddRange(result.Failures);
            _eventsAttached = subscriptions.HasActive;
            if (!_eventsAttached)
                _applicationSubscriptions = null;
            return failures;
        }

        private static void Subscribe(
            Action subscribe,
            Action unsubscribe,
            CooperativeSubscriptionSet subscriptions)
        {
            subscribe();
            subscriptions.Add(unsubscribe);
        }

        private static void ThrowSubscriptionFailures(
            IReadOnlyCollection<Exception> failures)
        {
            if (failures != null && failures.Count > 0)
            {
                throw new AggregateException(
                    "One or more model-scan event handlers could not be detached.",
                    failures);
            }
        }

        private static void LogSubscriptionFailures(
            IReadOnlyCollection<Exception> failures)
        {
            if (failures == null || failures.Count == 0)
                return;
            Logger.Error(
                new AggregateException(
                    "One or more model-scan event handlers could not be detached.",
                    failures).ToString(),
                "ModelScan");
        }

        private CooperativeModelScanStatus? GetStopReason(OperationContext context)
        {
            if (!IsCurrentDocument(context.Document) ||
                !ReferenceEquals(_observedDocument, context.Document))
            {
                return CooperativeModelScanStatus.DocumentChanged;
            }

            var changeStatus = _generations.GetChangeStatus(context.Generation);
            if (changeStatus.HasValue)
                return changeStatus;
            if (!context.Operation.IsCurrent ||
                context.Operation.Token.IsCancellationRequested)
            {
                return CooperativeModelScanStatus.Canceled;
            }
            if (context.ProgressCanceled || (context.Progress != null && context.Progress.IsCanceled))
                return CooperativeModelScanStatus.Canceled;
            if (!_attachRequested || !_lifecycle.CanStart)
                return CooperativeModelScanStatus.Suspended;
            return null;
        }

        private static List<ModelItem> CopyRoots(Document document)
        {
            var roots = new List<ModelItem>();
            var rootItems = document.Models.CreateCollectionFromRootItems();
            if (rootItems == null)
                return roots;
            foreach (var root in rootItems)
            {
                if (root != null)
                    roots.Add(root);
            }
            return roots;
        }

        private static bool IsCurrentDocument(Document document)
        {
            return document != null &&
                   ReferenceEquals(NwApplication.ActiveDocument, document);
        }

        private static bool BeginSubOperationSafely(
            Progress progress,
            double fractionOfRemainingTime,
            string message)
        {
            if (progress == null)
                return false;
            try
            {
                progress.BeginSubOperation(
                    fractionOfRemainingTime,
                    message ?? string.Empty);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    "Failed to begin a progress sub-operation: " + ex,
                    "ModelScan");
                return false;
            }
        }

        private static void EndSubOperationSafely(
            OperationContext context,
            bool isApplySubOperation)
        {
            var open = isApplySubOperation
                ? context.ApplySubOperationOpen
                : context.ScanSubOperationOpen;
            if (!open)
                return;
            if (isApplySubOperation)
                context.ApplySubOperationOpen = false;
            else
                context.ScanSubOperationOpen = false;
            EndSubOperationSafely(context.Progress, isApplySubOperation
                ? context.Result.ApplyStatus == CooperativeModelApplyStatus.Completed
                : context.ScanCompleted);
        }

        private static void EndSubOperationSafely(Progress progress, bool completed)
        {
            try
            {
                progress?.EndSubOperation(completed);
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    "Failed to end a progress sub-operation: " + ex,
                    "ModelScan");
            }
        }

        /// <summary>
        /// Updates progress inside the scan phase. When the scan
        /// sub-operation is unavailable, the share mapping is applied
        /// manually so the bar still never claims completion before the
        /// apply phase has run.
        /// </summary>
        private static void UpdateScanProgress(
            OperationContext context,
            double scanFraction)
        {
            var overall = context.ScanSubOperationOpen
                ? scanFraction
                : CooperativeProgressPhaseMath.MapScanFraction(scanFraction);
            if (!UpdateProgressSafely(context.Progress, overall)) context.ProgressCanceled = true;
        }

        private static bool UpdateProgressSafely(Progress progress, double fraction)
        {
            if (progress == null)
                return true;
            try
            {
                return progress.Update(fraction);
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    "Failed to update model-scan progress: " + ex,
                    "ModelScan");
                return true;
            }
        }

        private static void EndProgressSafely()
        {
            try
            {
                NwApplication.EndProgress();
            }
            catch (Exception ex)
            {
                Logger.Error(
                    "Failed to close model-scan progress: " + ex,
                    "ModelScan");
            }
        }

        private static void PublishScanTimingSnapshot(
            OperationContext context,
            Stopwatch scanTimer)
        {
            context.Result.Timings.ScanMs = scanTimer.ElapsedMilliseconds;
            context.Result.Timings.ScanChunks = context.ScanChunks;
            context.Result.Timings.MaxScanChunkMs = context.MaxScanChunkMs;
            context.Result.Timings.VisitedItems = context.VisitedItems;
        }

        private static void PublishApplyTimingSnapshot(
            OperationContext context,
            Stopwatch applyTimer)
        {
            context.Result.Timings.ApplyMs = applyTimer.ElapsedMilliseconds;
        }

        private static void LogOperationResult(
            UiThreadModelOperationRequest request,
            UiThreadModelOperationResult result)
        {
            try
            {
                Logger.Info(
                    "model operation status=" + result.Status +
                    " apply=" + result.ApplyStatus +
                    " scanItems=" + result.ScanItemCount +
                    " appliedItems=" + result.AppliedItemCount +
                    " recovery=" + result.RecoveryStatus +
                    " " + result.Timings.ToLogString() +
                    " caption=" + (request.ProgressCaption ?? string.Empty),
                    "ModelScan");
            }
            catch
            {
                // Timing logs must never break the operation result path.
            }
        }

        private void VerifyDispatcherAccess()
        {
            if (!_dispatcher.CheckAccess())
            {
                throw new InvalidOperationException(
                    "Model scanning must run on the Navisworks UI dispatcher.");
            }
        }

        private const int DocumentSubscriptionCount = 5;

        private sealed class OperationContext
        {
            internal Document Document;
            internal UiThreadModelOperationRequest Request;
            internal CooperativeModelScanLease Operation;
            internal CooperativeModelScanGenerationSnapshot Generation;
            internal UiThreadModelScanDocumentIdentity DocumentIdentity;
            internal Progress Progress;
            internal ModelItemCollection ScanResult { get; } =
                new ModelItemCollection();
            internal UiThreadModelOperationResult Result;
            internal bool ScanSubOperationOpen;
            internal bool ScanCompleted;
            internal bool ApplySubOperationOpen;
            internal UiThreadModelVisibilitySnapshot VisibilitySnapshot;
            internal bool ProgressCanceled;
            internal int VisitedItems;
            internal int ScanChunks;
            internal long MaxScanChunkMs;
        }
    }
}
