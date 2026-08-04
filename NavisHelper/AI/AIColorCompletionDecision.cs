using System;

namespace NavisHelper.AI
{
    internal sealed class AIColorCompletionDecision
    {
        private AIColorCompletionDecision(
            bool mayApply,
            AiColorOutcome outcome)
        {
            MayApply = mayApply;
            Outcome = outcome ??
                      throw new ArgumentNullException(nameof(outcome));
        }

        internal bool MayApply { get; }
        internal AiColorOutcome Outcome { get; }

        internal static AIColorCompletionDecision Evaluate(
            AiColorOutcome networkOutcome,
            bool documentChanged,
            bool timedOut,
            bool userCancelled)
        {
            if (documentChanged)
                return Block(
                    AiColorOutcomeKind.DocumentChanged,
                    networkOutcome?.Diagnostics);
            if (timedOut)
                return Block(
                    AiColorOutcomeKind.Timeout,
                    networkOutcome?.Diagnostics);
            if (userCancelled)
                return Block(
                    AiColorOutcomeKind.Cancelled,
                    networkOutcome?.Diagnostics);

            var outcome = networkOutcome ??
                          AiColorOutcome.Failure(
                              AiColorOutcomeKind.InvalidRequest);
            if (outcome.Kind == AiColorOutcomeKind.Success &&
                !outcome.IsSuccess)
            {
                outcome = AiColorOutcome.Failure(
                    AiColorOutcomeKind.InvalidResponse);
            }
            return new AIColorCompletionDecision(
                outcome.IsSuccess,
                outcome);
        }

        private static AIColorCompletionDecision Block(
            AiColorOutcomeKind kind,
            AiColorDiagnostics diagnostics)
        {
            return new AIColorCompletionDecision(
                false,
                AiColorOutcome.Failure(kind, null, diagnostics));
        }
    }
}
