using System;
using System.Threading;

namespace NavisHelper.Core
{
    internal enum CooperativeModelScanLifecycleState
    {
        Suspended,
        Active,
        Disposed
    }

    internal sealed class CooperativeModelScanLifecycle
    {
        private readonly object _sync = new object();
        private CooperativeModelScanLifecycleState _state =
            CooperativeModelScanLifecycleState.Suspended;

        internal CooperativeModelScanLifecycleState State
        {
            get
            {
                lock (_sync)
                    return _state;
            }
        }

        internal bool CanStart
        {
            get
            {
                lock (_sync)
                    return _state == CooperativeModelScanLifecycleState.Active;
            }
        }

        internal void Resume()
        {
            lock (_sync)
            {
                if (_state == CooperativeModelScanLifecycleState.Disposed)
                    throw new ObjectDisposedException(nameof(CooperativeModelScanLifecycle));
                _state = CooperativeModelScanLifecycleState.Active;
            }
        }

        internal void Suspend()
        {
            lock (_sync)
            {
                if (_state != CooperativeModelScanLifecycleState.Disposed)
                    _state = CooperativeModelScanLifecycleState.Suspended;
            }
        }

        internal void Dispose()
        {
            lock (_sync)
                _state = CooperativeModelScanLifecycleState.Disposed;
        }
    }

    [Flags]
    internal enum CooperativeModelScanInvalidation
    {
        None = 0,
        Document = 1,
        ModelCollection = 2,
        ModelProperties = 4,
        Selection = 8
    }

    internal enum CooperativeModelScanStatus
    {
        Completed,
        Busy,
        Canceled,
        DocumentChanged,
        SourceChanged,
        Suspended,
        ApplyFailed
    }

    internal readonly struct CooperativeModelScanGenerationSnapshot
    {
        internal CooperativeModelScanGenerationSnapshot(
            CooperativeModelScanInvalidation observedChanges,
            long document,
            long modelCollection,
            long modelProperties,
            long selection)
        {
            ObservedChanges = observedChanges;
            Document = document;
            ModelCollection = modelCollection;
            ModelProperties = modelProperties;
            Selection = selection;
        }

        internal CooperativeModelScanInvalidation ObservedChanges { get; }
        internal long Document { get; }
        internal long ModelCollection { get; }
        internal long ModelProperties { get; }
        internal long Selection { get; }
    }

    internal sealed class CooperativeModelScanGenerationTracker
    {
        private long _document;
        private long _modelCollection;
        private long _modelProperties;
        private long _selection;

        internal CooperativeModelScanGenerationSnapshot Capture(
            CooperativeModelScanInvalidation observedChanges)
        {
            return new CooperativeModelScanGenerationSnapshot(
                observedChanges,
                Interlocked.Read(ref _document),
                Interlocked.Read(ref _modelCollection),
                Interlocked.Read(ref _modelProperties),
                Interlocked.Read(ref _selection));
        }

        internal void Invalidate(CooperativeModelScanInvalidation changes)
        {
            if ((changes & CooperativeModelScanInvalidation.Document) != 0)
                Interlocked.Increment(ref _document);
            if ((changes & CooperativeModelScanInvalidation.ModelCollection) != 0)
                Interlocked.Increment(ref _modelCollection);
            if ((changes & CooperativeModelScanInvalidation.ModelProperties) != 0)
                Interlocked.Increment(ref _modelProperties);
            if ((changes & CooperativeModelScanInvalidation.Selection) != 0)
                Interlocked.Increment(ref _selection);
        }

        internal bool IsCurrent(CooperativeModelScanGenerationSnapshot snapshot)
        {
            return GetChangeStatus(snapshot) == null;
        }

        internal CooperativeModelScanStatus? GetChangeStatus(
            CooperativeModelScanGenerationSnapshot snapshot)
        {
            if ((snapshot.ObservedChanges & CooperativeModelScanInvalidation.Document) != 0 &&
                snapshot.Document != Interlocked.Read(ref _document))
            {
                return CooperativeModelScanStatus.DocumentChanged;
            }

            if ((snapshot.ObservedChanges & CooperativeModelScanInvalidation.ModelCollection) != 0 &&
                snapshot.ModelCollection != Interlocked.Read(ref _modelCollection))
            {
                return CooperativeModelScanStatus.SourceChanged;
            }

            if ((snapshot.ObservedChanges & CooperativeModelScanInvalidation.ModelProperties) != 0 &&
                snapshot.ModelProperties != Interlocked.Read(ref _modelProperties))
            {
                return CooperativeModelScanStatus.SourceChanged;
            }

            if ((snapshot.ObservedChanges & CooperativeModelScanInvalidation.Selection) != 0 &&
                snapshot.Selection != Interlocked.Read(ref _selection))
            {
                return CooperativeModelScanStatus.SourceChanged;
            }

            return null;
        }
    }

    internal sealed class CooperativeModelScanLease : IDisposable
    {
        private readonly CooperativeModelScanOperationGate _owner;
        private int _disposed;

        internal CooperativeModelScanLease(
            CooperativeModelScanOperationGate owner,
            long generation,
            CancellationTokenSource cancellation)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Generation = generation;
            Cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
        }

        internal long Generation { get; }

        internal CancellationTokenSource Cancellation { get; }

        internal CancellationToken Token => Cancellation.Token;

        internal bool IsCurrent => _owner.IsCurrent(this);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _owner.Complete(this);
        }
    }

    internal sealed class CooperativeModelScanOperationGate : IDisposable
    {
        private readonly object _sync = new object();
        private CooperativeModelScanLease _active;
        private long _generation;
        private bool _disposed;

        internal bool TryBegin(out CooperativeModelScanLease lease)
        {
            lock (_sync)
            {
                if (_disposed || _active != null)
                {
                    lease = null;
                    return false;
                }

                var cancellation = new CancellationTokenSource();
                lease = new CooperativeModelScanLease(
                    this,
                    ++_generation,
                    cancellation);
                _active = lease;
                return true;
            }
        }

        internal bool IsCurrent(CooperativeModelScanLease lease)
        {
            if (lease == null)
                return false;
            lock (_sync)
            {
                return !_disposed &&
                       ReferenceEquals(_active, lease) &&
                       lease.Generation == _generation &&
                       !lease.Cancellation.IsCancellationRequested;
            }
        }

        internal void CancelCurrent()
        {
            CooperativeModelScanLease active;
            lock (_sync)
            {
                active = _active;
                _generation++;
            }
            TryCancel(active);
        }

        internal void Complete(CooperativeModelScanLease lease)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_active, lease))
                    _active = null;
            }
            lease?.Cancellation.Dispose();
        }

        public void Dispose()
        {
            CooperativeModelScanLease active;
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                active = _active;
                _active = null;
                _generation++;
            }
            TryCancel(active);
        }

        private static void TryCancel(CooperativeModelScanLease lease)
        {
            if (lease == null)
                return;
            try
            {
                lease.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The completed lease already owns cleanup; cancellation is satisfied.
            }
        }
    }

    internal sealed class CooperativeModelScanChunkPolicy
    {
        internal const int DefaultMaxItemsPerChunk = 128;
        internal static readonly TimeSpan DefaultMaxChunkDuration =
            TimeSpan.FromMilliseconds(12);

        private readonly int _maxItemsPerChunk;
        private readonly TimeSpan _maxChunkDuration;

        internal CooperativeModelScanChunkPolicy(
            int maxItemsPerChunk = DefaultMaxItemsPerChunk,
            TimeSpan? maxChunkDuration = null)
        {
            if (maxItemsPerChunk <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxItemsPerChunk));
            var duration = maxChunkDuration ?? DefaultMaxChunkDuration;
            if (duration <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(maxChunkDuration));
            _maxItemsPerChunk = maxItemsPerChunk;
            _maxChunkDuration = duration;
        }

        internal bool ShouldYield(int processedInChunk, TimeSpan elapsed)
        {
            return processedInChunk >= _maxItemsPerChunk ||
                   elapsed >= _maxChunkDuration;
        }

    }

    internal static class CooperativeModelScanCommitPolicy
    {
        internal static bool CanCommit(
            CooperativeModelScanStatus status,
            bool operationIsCurrent,
            bool documentIsCurrent,
            bool generationIsCurrent,
            bool sourceStateIsCurrent)
        {
            return status == CooperativeModelScanStatus.Completed &&
                   operationIsCurrent &&
                   documentIsCurrent &&
                   generationIsCurrent &&
                   sourceStateIsCurrent;
        }
    }
}
