using System;
using System.Collections.Generic;

namespace NavisHelper.Core
{
    internal enum CooperativeModelApplyKind
    {
        None,
        ReplaceSelection,
        HideItems,
        ShowItems
    }

    internal enum CooperativeModelApplyStatus
    {
        NotStarted,
        SkippedEmpty,
        Completed,
        FailedRolledBack,
        FailedRollbackIncomplete
    }

    internal readonly struct CooperativeModelApplyChunk
    {
        internal CooperativeModelApplyChunk(int start, int count)
        {
            if (start < 0)
                throw new ArgumentOutOfRangeException(nameof(start));
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            Start = start;
            Count = count;
        }

        internal int Start { get; }
        internal int Count { get; }
    }

    /// <summary>
    /// Pure chunk planner for the apply phase. The apply must be split into
    /// bounded index ranges so the Navisworks UI thread can yield between the
    /// indivisible Autodesk calls (AddRange/SetHidden) that mutate the model.
    /// </summary>
    internal static class CooperativeModelApplyChunker
    {
        internal static List<CooperativeModelApplyChunk> Plan(
            int totalItems,
            int maxPerChunk)
        {
            if (totalItems < 0)
                throw new ArgumentOutOfRangeException(nameof(totalItems));
            if (maxPerChunk <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPerChunk));

            var chunks = new List<CooperativeModelApplyChunk>();
            if (totalItems == 0)
                return chunks;

            for (int start = 0; start < totalItems; start += maxPerChunk)
            {
                int count = Math.Min(maxPerChunk, totalItems - start);
                chunks.Add(new CooperativeModelApplyChunk(start, count));
            }

            return chunks;
        }
    }

    /// <summary>
    /// Records the pre-mutation hidden state of every item an apply phase has
    /// already touched, so a failed apply can be rolled back to exactly the
    /// original visibility instead of leaving a partially changed model.
    /// </summary>
    internal sealed class CooperativeHiddenStateRollback<T>
    {
        private readonly List<T> _wasHidden = new List<T>();
        private readonly List<T> _wasVisible = new List<T>();
        private bool _sealed;

        internal int RecordedCount => _wasHidden.Count + _wasVisible.Count;

        internal void Record(T item, bool wasHidden)
        {
            if (_sealed)
                throw new InvalidOperationException(
                    "The rollback log is sealed; no further chunks may be recorded.");
            if (ReferenceEquals(item, null))
                return;

            if (wasHidden)
                _wasHidden.Add(item);
            else
                _wasVisible.Add(item);
        }

        /// <summary>
        /// Freezes the log and returns the restore batches. Recording after
        /// sealing fails loudly so an apply cannot silently grow its rollback
        /// obligations after restoration has started.
        /// </summary>
        internal IEnumerable<CooperativeHiddenRestoreBatch<T>> BuildRestoreBatches()
        {
            _sealed = true;
            if (_wasHidden.Count > 0)
                yield return new CooperativeHiddenRestoreBatch<T>(_wasHidden, true);
            if (_wasVisible.Count > 0)
                yield return new CooperativeHiddenRestoreBatch<T>(_wasVisible, false);
        }
    }

    internal readonly struct CooperativeHiddenRestoreBatch<T>
    {
        internal CooperativeHiddenRestoreBatch(IReadOnlyList<T> items, bool hidden)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
            Hidden = hidden;
        }

        internal IReadOnlyList<T> Items { get; }
        internal bool Hidden { get; }
    }

    /// <summary>
    /// Guards the apply phase against invalidation storms triggered by the
    /// operation's own document mutations (selection AddRange fires
    /// CurrentSelection.Changed, SetHidden fires model collection events).
    /// Document replacement is never suppressed: a real document switch must
    /// still invalidate the operation.
    /// </summary>
    internal sealed class CooperativeInvalidationSuppressor
    {
        private const CooperativeModelScanInvalidation SelfMutationKinds =
            CooperativeModelScanInvalidation.Selection |
            CooperativeModelScanInvalidation.ModelCollection |
            CooperativeModelScanInvalidation.ModelProperties;

        private int _depth;

        internal bool Active => _depth > 0;

        internal void Begin()
        {
            _depth++;
        }

        internal void End()
        {
            if (_depth > 0)
                _depth--;
        }

        internal bool IsSuppressed(CooperativeModelScanInvalidation change)
        {
            return _depth > 0 &&
                   (change & CooperativeModelScanInvalidation.Document) == 0 &&
                   (change & SelfMutationKinds) != 0;
        }
    }

    /// <summary>
    /// Per-phase timing evidence for one cooperative model operation. The
    /// values back the honest-progress contract: they make the cost of the
    /// scan, the commit gate, and the apply phase observable after the fact.
    /// </summary>
    internal sealed class CooperativeModelOperationTimingSnapshot
    {
        internal long TotalMs { get; set; }
        internal long ScanMs { get; set; }
        internal long GateMs { get; set; }
        internal long ApplyMs { get; set; }
        internal long RollbackMs { get; set; }
        internal long MaxScanChunkMs { get; set; }
        internal long MaxApplyChunkMs { get; set; }
        internal int ScanChunks { get; set; }
        internal int ApplyChunks { get; set; }
        internal int VisitedItems { get; set; }

        internal string ToLogString()
        {
            return
                "totalMs=" + TotalMs +
                " scanMs=" + ScanMs +
                " gateMs=" + GateMs +
                " applyMs=" + ApplyMs +
                " rollbackMs=" + RollbackMs +
                " scanChunks=" + ScanChunks +
                " applyChunks=" + ApplyChunks +
                " maxScanChunkMs=" + MaxScanChunkMs +
                " maxApplyChunkMs=" + MaxApplyChunkMs +
                " visitedItems=" + VisitedItems;
        }
    }
}
