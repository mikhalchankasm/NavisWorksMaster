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
    internal sealed class UiThreadModelScanResult : IDisposable
    {
        private CooperativeModelScanLease _lease;
        private CooperativeProgressOwnership _progressOwnership;

        internal UiThreadModelScanResult(
            CooperativeModelScanStatus status,
            ModelItemCollection items,
            CooperativeModelScanLease lease = null,
            CooperativeProgressOwnership progressOwnership = null,
            CooperativeModelScanGenerationSnapshot generation = default(CooperativeModelScanGenerationSnapshot),
            UiThreadModelScanDocumentIdentity documentIdentity = null)
        {
            Status = status;
            Items = items ?? new ModelItemCollection();
            _lease = lease;
            _progressOwnership = progressOwnership;
            Generation = generation;
            DocumentIdentity = documentIdentity;
        }

        internal CooperativeModelScanStatus Status { get; }
        internal ModelItemCollection Items { get; }
        internal CooperativeModelScanGenerationSnapshot Generation { get; }
        internal UiThreadModelScanDocumentIdentity DocumentIdentity { get; }
        internal bool OperationIsCurrent => _lease != null && _lease.IsCurrent;

        public void Dispose()
        {
            var progressOwnership = _progressOwnership;
            _progressOwnership = null;
            progressOwnership?.Dispose();

            var lease = _lease;
            _lease = null;
            lease?.Dispose();
        }
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

        internal async Task<UiThreadModelScanResult> ScanAsync(
            Document document,
            string progressCaption,
            Func<ModelItem, bool> includeItem,
            CooperativeModelScanInvalidation observedChanges)
        {
            VerifyDispatcherAccess();
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (includeItem == null)
                throw new ArgumentNullException(nameof(includeItem));

            if (_disposed ||
                !_attachRequested ||
                !_lifecycle.CanStart ||
                !_eventsAttached ||
                !ReferenceEquals(_observedDocument, document))
            {
                return new UiThreadModelScanResult(
                    CooperativeModelScanStatus.Suspended,
                    null);
            }

            observedChanges |= CooperativeModelScanInvalidation.Document |
                               CooperativeModelScanInvalidation.ModelCollection;
            if (!_operations.TryBegin(out var operation))
            {
                return new UiThreadModelScanResult(
                    CooperativeModelScanStatus.Busy,
                    null);
            }

            CooperativeModelScanLease operationToDispose = operation;
            CooperativeProgressOwnership progressOwnership = null;
            var result = new ModelItemCollection();
            var generation = _generations.Capture(observedChanges);
            var documentIdentity = UiThreadModelScanDocumentIdentity.Capture(document);
            Progress progress = null;
            try
            {
                if (!IsCurrentDocument(document) ||
                    !ReferenceEquals(_observedDocument, document))
                {
                    return new UiThreadModelScanResult(
                        CooperativeModelScanStatus.DocumentChanged,
                        null);
                }

                progress = NwApplication.BeginProgress(progressCaption ?? string.Empty);
                progressOwnership = new CooperativeProgressOwnership(EndProgressSafely);
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
                            var stopReason = GetStopReason(
                                document,
                                generation,
                                operation,
                                progress);
                            if (stopReason.HasValue)
                            {
                                return new UiThreadModelScanResult(
                                    stopReason.Value,
                                    null);
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
                                if (includeItem(item))
                                    result.Add(item);
                                processedInChunk++;
                            }
                            while (!_chunkPolicy.ShouldYield(
                                processedInChunk,
                                chunkTimer.Elapsed));

                            if (!rootComplete)
                            {
                                progress.Update(_chunkPolicy.CalculateProgress(
                                    rootIndex,
                                    roots.Count,
                                    currentRootProcessed));
                                await Dispatcher.Yield(DispatcherPriority.Background);
                            }
                        }
                    }

                    progress.Update((double)(rootIndex + 1) / Math.Max(1, roots.Count));
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }

                var finalReason = GetStopReason(document, generation, operation, progress);
                if (finalReason.HasValue)
                {
                    return new UiThreadModelScanResult(finalReason.Value, null);
                }

                if (documentIdentity == null || !documentIdentity.Matches(document))
                {
                    return new UiThreadModelScanResult(
                        CooperativeModelScanStatus.SourceChanged,
                        null);
                }

                progress.Update(1.0);
                var completed = new UiThreadModelScanResult(
                    CooperativeModelScanStatus.Completed,
                    result,
                    operation,
                    progressOwnership,
                    generation,
                    documentIdentity);
                operationToDispose = null;
                progressOwnership = null;
                return completed;
            }
            finally
            {
                progressOwnership?.Dispose();
                operationToDispose?.Dispose();
            }
        }

        internal bool CanCommit(
            UiThreadModelScanResult result,
            Document document,
            ModelItemCollection sourceSelection = null)
        {
            VerifyDispatcherAccess();
            if (result == null)
                return false;

            return CooperativeModelScanCommitPolicy.CanCommit(
                result.Status,
                result.OperationIsCurrent,
                _attachRequested &&
                _lifecycle.CanStart &&
                IsCurrentDocument(document) &&
                ReferenceEquals(_observedDocument, document) &&
                result.DocumentIdentity != null &&
                result.DocumentIdentity.Matches(document),
                _generations.IsCurrent(result.Generation),
                sourceSelection == null ||
                IsSelectionUnchanged(document, sourceSelection));
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
                    _lifecycle.Dispose();
                    _operations.Dispose();
                    _disposed = true;
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
            _lifecycle.Suspend();
            _generations.Invalidate(CooperativeModelScanInvalidation.Document);
            CancelCurrent();
            LogSubscriptionFailures(StopObservingDocumentBestEffort());
        }

        private void OnActiveDocumentChanged(object sender, EventArgs e)
        {
            _generations.Invalidate(CooperativeModelScanInvalidation.Document);
            CancelCurrent();
            if (_disposed || !_attachRequested || !_eventsAttached)
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

        private CooperativeModelScanStatus? GetStopReason(
            Document document,
            CooperativeModelScanGenerationSnapshot generation,
            CooperativeModelScanLease operation,
            Progress progress)
        {
            if (!_attachRequested || !_lifecycle.CanStart)
                return CooperativeModelScanStatus.Suspended;
            if (!IsCurrentDocument(document) ||
                !ReferenceEquals(_observedDocument, document))
            {
                return CooperativeModelScanStatus.DocumentChanged;
            }

            var changeStatus = _generations.GetChangeStatus(generation);
            if (changeStatus.HasValue)
                return changeStatus;
            if (!operation.IsCurrent || operation.Token.IsCancellationRequested)
                return CooperativeModelScanStatus.Canceled;
            if (progress != null && progress.IsCanceled)
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

        private void VerifyDispatcherAccess()
        {
            if (!_dispatcher.CheckAccess())
            {
                throw new InvalidOperationException(
                    "Model scanning must run on the Navisworks UI dispatcher.");
            }
        }
    }
}
