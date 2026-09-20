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
        /// `last_operation_status` is recorded in the history like every other
        /// command, so a caller asking for the most recent operation without a
        /// requestId would otherwise be told about its own question. It is the one
        /// command excluded, which also makes repeated asks idempotent: the second
        /// ask still names the lost call rather than the first ask.
        ///
        /// Nothing else is excluded. A cancel is a real operation, and a status poll
        /// the caller issued deliberately is a fact about the session worth
        /// reporting; only the question about the answer is not.
        /// </summary>
        public static bool CountsAsLastOperation(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            return !string.Equals(command.Trim(), HostCommandNames.LastOperationStatus, StringComparison.OrdinalIgnoreCase);
        }
    }
}
