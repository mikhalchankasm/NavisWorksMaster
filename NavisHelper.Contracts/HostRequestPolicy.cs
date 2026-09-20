using System;

namespace NavisHelper.Agent.Contracts
{
    public enum HostRequestGateBypassKind
    {
        None = 0,
        ClashReportStatus = 1,
        LastOperationStatus = 2,
        CancelClashReport = 3,
        CancelSubtreeNamesDump = 4,
        ClashRunStatus = 5,
        CancelClashRun = 6,
    }

    public static class HostRequestPolicy
    {
        public static HostRequestGateBypassKind GetRequestGateBypassKind(string command)
        {
            if (string.Equals(command, HostCommandNames.ClashReportStatus, StringComparison.OrdinalIgnoreCase))
                return HostRequestGateBypassKind.ClashReportStatus;
            if (string.Equals(command, HostCommandNames.LastOperationStatus, StringComparison.OrdinalIgnoreCase))
                return HostRequestGateBypassKind.LastOperationStatus;
            if (string.Equals(command, HostCommandNames.CancelClashReport, StringComparison.OrdinalIgnoreCase))
                return HostRequestGateBypassKind.CancelClashReport;
            if (string.Equals(command, HostCommandNames.CancelSubtreeNamesDump, StringComparison.OrdinalIgnoreCase))
                return HostRequestGateBypassKind.CancelSubtreeNamesDump;
            if (string.Equals(command, HostCommandNames.ClashRunStatus, StringComparison.OrdinalIgnoreCase))
                return HostRequestGateBypassKind.ClashRunStatus;
            if (string.Equals(command, HostCommandNames.CancelClashRun, StringComparison.OrdinalIgnoreCase))
                return HostRequestGateBypassKind.CancelClashRun;

            return HostRequestGateBypassKind.None;
        }

        public static bool IsRequestGateBypassCommand(string command)
        {
            return GetRequestGateBypassKind(command) != HostRequestGateBypassKind.None;
        }

        public static bool IsOperationStatusPollCommand(string command)
        {
            var kind = GetRequestGateBypassKind(command);
            return kind == HostRequestGateBypassKind.ClashReportStatus ||
                   kind == HostRequestGateBypassKind.ClashRunStatus ||
                   kind == HostRequestGateBypassKind.LastOperationStatus;
        }
    }

    public static class OperationHistoryPolicy
    {
        public static bool IsAuthoritativeSuccessfulCompletion(string state, bool? ok)
        {
            return ok == true && string.Equals(state, "completed", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether an operation may be the answer to "what did I just lose?".
        ///
        /// This is <see cref="HostRequestPolicy.IsOperationStatusPollCommand"/>
        /// inverted, and deliberately not its own list. Status polls -- the two
        /// clash polls and `last_operation_status` itself -- are never written to
        /// the history at all: `RecordOperationStarted`, `RecordOperationCompleted`
        /// and `RecordOperationFailed` each return early for them, so that polling
        /// cannot evict the real operation from a bounded history. This predicate
        /// therefore states what the history can contain rather than filtering it a
        /// second time.
        ///
        /// An earlier version of this method excluded only `last_operation_status`,
        /// on the stated grounds that it "is recorded in the history like every
        /// other command" and that a deliberate status poll is "worth reporting".
        /// Both halves were wrong: none of the three is recorded. An external review
        /// caught it.
        ///
        /// The consequence is a real limitation, not a detail: **if a status poll is
        /// the call whose reply was lost, an empty `last_operation_status` answers
        /// about an older, unrelated operation.** That is why the response carries
        /// the command name and `resolvedFromMostRecent`, and why the contract tells
        /// the caller to check the command is the one it lost. Recording polls
        /// instead would trade this for a history that a polling loop can flush.
        ///
        /// A cancel is not a poll: it changes something, it is recorded, and it can
        /// be the lost call.
        /// </summary>
        public static bool CountsAsLastOperation(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            return !HostRequestPolicy.IsOperationStatusPollCommand(command);
        }
    }
}
