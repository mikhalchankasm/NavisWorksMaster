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
    /// One cooperative model operation: scan (cooperative traversal), commit
    /// gate, and apply (chunked model mutation). All Autodesk document/model
    /// access stays on the Navisworks UI dispatcher; only the yields return
    /// control to input and rendering between bounded work portions.
    /// </summary>
    internal sealed class UiThreadModelOperationRequest
    {
        internal string ProgressCaption { get; set; }
        internal string ScanPhaseMessage { get; set; }
        internal string ApplyPhaseMessage { get; set; }
        internal Func<ModelItem, bool> IncludeItem { get; set; }
        internal CooperativeModelScanInvalidation ObservedChanges { get; set; }

        /// <summary>
        /// What the apply phase does with the scanned items. ReplaceSelection
        /// requires CommitGuardSelection so a failed apply can restore the
        /// original selection.
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
        internal CooperativeModelOperationTimingSnapshot Timings { get; set; } =
            new CooperativeModelOperationTimingSnapshot();
    }

    internal sealed class UiThreadModelScanCoordinator : IDisposable
    {
        // The scan sub-operation owns this share of the overall progress bar;
        // the apply sub-operation owns the rest. The bar therefore never
        // reaches completion before the apply phase has actually finished.
        private const double ScanPhaseProgressShare = 0.75;
        private const int ApplyMaxItemsPerChunk = 4096;

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
            if (request.IncludeItem == null)
                throw new ArgumentException(
                    "The request must provide an include-item predicate.",
                    nameof(request));
            if (request.ApplyKind == CooperativeModelApplyKind.ReplaceSelection &&
                request.CommitGuardSelection == null)
            {
                throw new ArgumentException(
                    "ReplaceSelection operations require a commit guard selection " +
                    "so a failed apply can restore the original selection.",
                    nameof(request));
            }

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
            if (!_operations.TryBegin(out var operation))
            {
                return new UiThreadModelOperationResult(
                    CooperativeModelScanStatus.Busy);
            }

            var context = new OperationContext
            {
                Document = document,
                Request = request,
                Operation = operation,
                Generation = _generations.Capture(observedChanges),
                DocumentIdentity = UiThreadModelScanDocumentIdentity.Capture(document),
                Result = new UiThreadModelOperationResult(
                    CooperativeModelScanStatus.Suspended)
            };
            var scanTimer = Stopwatch.StartNew();
            Progress progress = null;
            CooperativeProgressOwnership progressOwnership = null;
            try
            {
                if (!IsCurrentDocument(document) ||
                    !ReferenceEquals(_observedDocument, document))
                {
                    context.Result.Status = CooperativeModelScanStatus.DocumentChanged;
                    return context.Result;
                }

                progress = NwApplication.BeginProgress(
                    request.ProgressCaption ?? string.Empty);
                progressOwnership = new CooperativeProgressOwnership(EndProgressSafely);
                context.Progress = progress;
                context.ScanSubOperationOpen = BeginSubOperationSafely(
                    progress,
                    ScanPhaseProgressShare,
                    request.ScanPhaseMessage);
                var roots = CopyRoots(document);
                var seen = new HashSet<ModelItem>();

                for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
                {
                    int currentRootProcessed = 0;
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

                                currentRootProcessed++;
                                context.VisitedItems++;
                                if (request.IncludeItem(item))
                                    context.ScanResult.Add(item);
                                processedInChunk++;
                            }
                            while (!_chunkPolicy.ShouldYield(
                                processedInChunk,
                                chunkTimer.Elapsed));

                            if (!rootComplete)
                            {
                                context.ScanChunks++;
                                context.MaxScanChunkMs = Math.Max(
                                    context.MaxScanChunkMs,
                                    chunkTimer.ElapsedMilliseconds);
                                UpdateProgressSafely(
                                    progress,
                                    _chunkPolicy.CalculateProgress(
                                        rootIndex,
                                        roots.Count,
                                        currentRootProcessed));
                                await Dispatcher.Yield(DispatcherPriority.Background);
                            }
                        }
                    }

                    UpdateProgressSafely(
                        progress,
                        (double)(rootIndex + 1) / Math.Max(1, roots.Count));
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }

                EndSubOperationSafely(context, isApplySubOperation: false);
                scanTimer.Stop();
                context.Result.Status = CooperativeModelScanStatus.Completed;
                context.Result.ScanItemCount = context.ScanResult.Count;
                context.Result.Timings.ScanMs = scanTimer.ElapsedMilliseconds;
                context.Result.Timings.ScanChunks = context.ScanChunks;
                context.Result.Timings.MaxScanChunkMs = context.MaxScanChunkMs;
                context.Result.Timings.VisitedItems = context.VisitedItems;

                await RunCommitGateAndApplyAsync(context);
                return context.Result;
            }
            finally
            {
                totalTimer.Stop();
                EndSubOperationSafely(context, isApplySubOperation: true);
                EndSubOperationSafely(context, isApplySubOperation: false);
                progressOwnership?.Dispose();
                operation.Dispose();
                context.Result.Timings.TotalMs = totalTimer.ElapsedMilliseconds;
                LogOperationResult(request, context.Result);
            }
        }

        /// <summary>
        /// Commit gate followed by the apply phase. The gate re-validates the
        /// operation lease, the document, the generation snapshot, and (when
        /// supplied) the unchanged source selection; the apply then mutates
        /// the model in bounded chunks with UI yields.
        /// </summary>
        private async Task RunCommitGateAndApplyAsync(
            OperationContext context)
        {
            var gateTimer = Stopwatch.StartNew();
            var gateFailure = GetStopReason(context);
            if (gateFailure.HasValue)
            {
                context.Result.Status = gateFailure.Value;
                return;
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
                context.Request.CommitGuardSelection == null ||
                IsSelectionUnchanged(context.Document, context.Request.CommitGuardSelection));
            gateTimer.Stop();
            context.Result.Timings.GateMs = gateTimer.ElapsedMilliseconds;
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

            await ApplyAsync(context);
        }

        /// <summary>
        /// Applies the scan result in bounded chunks with UI yields. Cancel
        /// and invalidation are honored until the first mutation; after it the
        /// operation completes its remaining bounded chunks so a cancel can
        /// never leave the model partially changed. An exception mid-apply
        /// triggers a rollback to the captured original state.
        /// </summary>
        private async Task ApplyAsync(
            OperationContext context)
        {
            // Last cancellation point before the irreversible boundary. The
            // full stop reason (lease, generation, document, progress dialog)
            // is re-checked here, right before the first mutation.
            var stopReason = GetStopReason(context);
            if (stopReason.HasValue)
            {
                context.Result.Status = stopReason.Value;
                return;
            }

            var progress = context.Progress;
            var applyTimer = Stopwatch.StartNew();
            context.ApplySubOperationOpen = BeginSubOperationSafely(
                progress,
                1.0,
                context.Request.ApplyPhaseMessage);
            var rollback = new CooperativeHiddenStateRollback<ModelItem>();
            var request = context.Request;
            var document = context.Document;
            var scanResult = context.ScanResult;

            _suppressor.Begin();
            try
            {
                var chunks = CooperativeModelApplyChunker.Plan(
                    scanResult.Count,
                    ApplyMaxItemsPerChunk);
                if (request.ApplyKind == CooperativeModelApplyKind.ReplaceSelection)
                {
                    // Clear() is the first mutation of the irreversible phase.
                    document.CurrentSelection.Clear();
                }

                foreach (var chunk in chunks)
                {
                    var chunkTimer = Stopwatch.StartNew();
                    var slice = BuildSlice(scanResult, chunk);

                    switch (request.ApplyKind)
                    {
                        case CooperativeModelApplyKind.ReplaceSelection:
                            document.CurrentSelection.AddRange(slice);
                            break;
                        case CooperativeModelApplyKind.HideItems:
                        case CooperativeModelApplyKind.ShowItems:
                            foreach (var item in slice)
                                rollback.Record(item, item.IsHidden);
                            document.Models.SetHidden(
                                slice,
                                request.ApplyKind == CooperativeModelApplyKind.HideItems);
                            break;
                    }

                    context.Result.AppliedItemCount += slice.Count;
                    context.ApplyChunks++;
                    chunkTimer.Stop();
                    context.MaxApplyChunkMs = Math.Max(
                        context.MaxApplyChunkMs,
                        chunkTimer.ElapsedMilliseconds);
                    UpdateProgressSafely(
                        progress,
                        (double)(chunk.Start + chunk.Count) / Math.Max(1, scanResult.Count));
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }

                context.Result.ApplyStatus = CooperativeModelApplyStatus.Completed;
                context.Result.Timings.ApplyMs = applyTimer.ElapsedMilliseconds;
                context.Result.Timings.ApplyChunks = context.ApplyChunks;
                context.Result.Timings.MaxApplyChunkMs = context.MaxApplyChunkMs;
                // Only now, with every chunk applied, may the bar reach full
                // completion.
                UpdateProgressSafely(progress, 1.0);
            }
            catch (Exception applyError)
            {
                var rollbackTimer = Stopwatch.StartNew();
                Exception rollbackError = null;
                try
                {
                    RollbackApply(context, rollback);
                }
                catch (Exception restoreError)
                {
                    rollbackError = restoreError;
                }
                rollbackTimer.Stop();

                context.Result.Status = CooperativeModelScanStatus.ApplyFailed;
                context.Result.ApplyError = rollbackError == null
                    ? applyError
                    : new AggregateException(applyError, rollbackError);
                context.Result.ApplyStatus = rollbackError == null
                    ? CooperativeModelApplyStatus.FailedRolledBack
                    : CooperativeModelApplyStatus.FailedRollbackIncomplete;
                context.Result.Timings.ApplyMs = applyTimer.ElapsedMilliseconds;
                context.Result.Timings.ApplyChunks = context.ApplyChunks;
                context.Result.Timings.MaxApplyChunkMs = context.MaxApplyChunkMs;
                context.Result.Timings.RollbackMs = rollbackTimer.ElapsedMilliseconds;
                Logger.Error(
                    "Model operation apply failed and was rolled back: " + applyError,
                    "ModelScan");
            }
            finally
            {
                _suppressor.End();
                EndSubOperationSafely(context, isApplySubOperation: true);
            }
        }

        private static List<ModelItem> BuildSlice(
            ModelItemCollection source,
            CooperativeModelApplyChunk chunk)
        {
            var slice = new List<ModelItem>(chunk.Count);
            for (int i = chunk.Start; i < chunk.Start + chunk.Count; i++)
                slice.Add(source[i]);
            return slice;
        }

        private void RollbackApply(
            OperationContext context,
            CooperativeHiddenStateRollback<ModelItem> rollback)
        {
            var document = context.Document;
            var request = context.Request;
            switch (request.ApplyKind)
            {
                case CooperativeModelApplyKind.ReplaceSelection:
                    document.CurrentSelection.Clear();
                    var guard = request.CommitGuardSelection;
                    if (guard != null && guard.Count > 0)
                    {
                        foreach (var chunk in CooperativeModelApplyChunker.Plan(
                                     guard.Count,
                                     ApplyMaxItemsPerChunk))
                        {
                            document.CurrentSelection.AddRange(BuildSlice(guard, chunk));
                        }
                    }
                    break;

                case CooperativeModelApplyKind.HideItems:
                case CooperativeModelApplyKind.ShowItems:
                    foreach (var batch in rollback.BuildRestoreBatches())
                    {
                        foreach (var chunk in CooperativeModelApplyChunker.Plan(
                                     batch.Items.Count,
                                     ApplyMaxItemsPerChunk))
                        {
                            var slice = new List<ModelItem>(chunk.Count);
                            for (int i = chunk.Start; i < chunk.Start + chunk.Count; i++)
                                slice.Add(batch.Items[i]);
                            document.Models.SetHidden(slice, batch.Hidden);
                        }
                    }
                    break;
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

            // The apply phase legitimately fires selection and model
            // collection events through its own mutations; suppressing them
            // keeps the operation from invalidating itself. A document
            // replacement is never suppressed.
            if (_suppressor.IsSuppressed(change))
                return;

            _generations.Invalidate(change);
            CancelCurrent();
        }

        private void ObserveDocument(Document document)
        {
            if (ReferenceEquals(_observedDocument, document) &&
                _documentSubscriptions != null &&
                _documentSubscriptions.Count == 5 &&
                _documentSubscriptions.ActiveCount == 5)
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
            if (!_attachRequested || !_lifecycle.CanStart)
                return CooperativeModelScanStatus.Suspended;
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
            if (context.Progress != null && context.Progress.IsCanceled)
                return CooperativeModelScanStatus.Canceled;
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

        private static bool IsSelectionUnchanged(
            Document document,
            ModelItemCollection expected)
        {
            if (document == null || expected == null)
                return false;
            var current = document.CurrentSelection?.SelectedItems;
            if (current == null || current.Count != expected.Count)
                return false;

            var remaining = new HashSet<ModelItem>();
            foreach (var item in expected)
            {
                if (item != null)
                    remaining.Add(item);
            }
            foreach (var item in current)
            {
                if (item != null && !remaining.Remove(item))
                    return false;
            }
            return remaining.Count == 0;
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
            try
            {
                context.Progress?.EndSubOperation();
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    "Failed to end a progress sub-operation: " + ex,
                    "ModelScan");
            }
        }

        private static void UpdateProgressSafely(Progress progress, double fraction)
        {
            if (progress == null)
                return;
            try
            {
                // Update also reports the cancel request at the exact moment
                // of the call; GetStopReason polls IsCanceled once per chunk,
                // so both paths feed cancellation.
                progress.Update(fraction);
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    "Failed to update model-scan progress: " + ex,
                    "ModelScan");
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
            internal bool ApplySubOperationOpen;
            internal int VisitedItems;
            internal int ScanChunks;
            internal int ApplyChunks;
            internal long MaxScanChunkMs;
            internal long MaxApplyChunkMs;
        }
    }
}
