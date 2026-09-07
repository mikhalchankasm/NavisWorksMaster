using System;

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
        Completed
    }

    internal static class CooperativeModelVisibilityPolicy
    {
        internal static bool NeedsChange(bool isHidden, CooperativeModelApplyKind kind)
        {
            return kind == CooperativeModelApplyKind.HideItems ? !isHidden :
                   kind == CooperativeModelApplyKind.ShowItems && isHidden;
        }
    }

    /// <summary>
    /// Pure decision table for document-event invalidation of the running
    /// model operation. An event only cancels the active operation when the
    /// operation opted in to observing that change kind; self-mutation
    /// suppression wins only for the kinds the apply phase actually mutates.
    /// </summary>
    internal static class CooperativeInvalidationDecision
    {
        internal static bool ShouldCancelOperation(
            CooperativeModelScanInvalidation change,
            CooperativeModelScanInvalidation activeObservedChanges,
            bool selfMutationInProgress)
        {
            if ((change & CooperativeModelScanInvalidation.Document) != 0)
                return true;

            if (selfMutationInProgress &&
                (change & CooperativeModelScanInvalidation.Document) == 0 &&
                (change & (CooperativeModelScanInvalidation.Selection |
                           CooperativeModelScanInvalidation.ModelCollection |
                           CooperativeModelScanInvalidation.ModelProperties)) != 0)
            {
                // The apply phase's own synchronous events; already covered
                // by the suppression window around the mutation call.
                return false;
            }

            return (change & activeObservedChanges) != 0;
        }
    }

    /// <summary>
    /// Manual overall-fraction mapping used when progress sub-operations are
    /// unavailable. It mirrors the sub-operation contract: the scan phase
    /// never fills the bar and full completion is reached only after apply.
    /// </summary>
    internal static class CooperativeProgressPhaseMath
    {
        internal const double ScanPhaseShare = 0.70;

        /// <summary>
        /// Overall fraction reached when the cooperative verify phase (the
        /// unchanged-source-selection check) has consumed its share.
        /// </summary>
        internal const double VerifyPhaseEndShare = 0.76;

        internal static double MapScanFraction(double fraction)
        {
            return Clamp(fraction) * ScanPhaseShare;
        }

        internal static double MapVerifyFraction(double fraction)
        {
            return ScanPhaseShare +
                   Clamp(fraction) * (VerifyPhaseEndShare - ScanPhaseShare);
        }

        internal static double MapApplyFraction(double fraction)
        {
            return VerifyPhaseEndShare + Clamp(fraction) * (1.0 - VerifyPhaseEndShare);
        }

        private static double Clamp(double fraction)
        {
            if (double.IsNaN(fraction))
                return 0.0;
            return Math.Max(0.0, Math.Min(1.0, fraction));
        }
    }

    /// <summary>
    /// Guards the apply phase against invalidation storms triggered by the
    /// operation's own document mutations (selection CopyFrom fires
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
        internal long PrepareMs { get; set; }
        internal int PrepareChunks { get; set; }
        internal long MaxPrepareChunkMs { get; set; }
        internal int VerifyChunks { get; set; }
        internal long MaxVerifyChunkMs { get; set; }
        internal long ScanMs { get; set; }
        internal long GateMs { get; set; }

        /// <summary>
        /// Duration of the indivisible apply call (CopyFrom/SetHidden over
        /// the whole scan result). Cancel is honored before it; this records
        /// the stall observed in this run, not a general duration guarantee.
        /// </summary>
        internal long ApplyMs { get; set; }
        internal long RecoveryMs { get; set; }
        internal long MaxRecoveryChunkMs { get; set; }
        internal long MaxScanChunkMs { get; set; }
        internal int ScanChunks { get; set; }
        internal int VisitedItems { get; set; }

        internal string ToLogString()
        {
            return
                "totalMs=" + TotalMs +
                " prepareMs=" + PrepareMs +
                " prepareChunks=" + PrepareChunks +
                " maxPrepareChunkMs=" + MaxPrepareChunkMs +
                " scanMs=" + ScanMs +
                " gateMs=" + GateMs +
                " verifyChunks=" + VerifyChunks +
                " maxVerifyChunkMs=" + MaxVerifyChunkMs +
                " applyMs=" + ApplyMs +
                " recoveryMs=" + RecoveryMs +
                " maxRecoveryChunkMs=" + MaxRecoveryChunkMs +
                " scanChunks=" + ScanChunks +
                " maxScanChunkMs=" + MaxScanChunkMs +
                " visitedItems=" + VisitedItems;
        }
    }
}
