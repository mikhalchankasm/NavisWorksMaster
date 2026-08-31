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
    }
}
