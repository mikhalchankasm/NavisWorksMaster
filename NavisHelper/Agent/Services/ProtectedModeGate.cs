using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal static class ProtectedModeGate
    {
        public static IReadOnlyCollection<string> ExemptCommands { get; } = Array.AsReadOnly(new[]
        {
            HostCommandNames.CancelClashReport, HostCommandNames.CancelClashRun, HostCommandNames.CancelSubtreeNamesDump,
            HostCommandNames.ClashReportStatus, HostCommandNames.ClashRunStatus, HostCommandNames.DumpSubtreeNamesStatus,
            HostCommandNames.LastOperationStatus, HostCommandNames.SaveDocumentStatus,
        });

        internal static bool RequiresApproval(string command)
        {
            return !ExemptCommands.Contains(command, StringComparer.Ordinal) && HostCommandEffects.RequiresApproval(command);
        }

        public static ProtectedModeGateDecision Evaluate(ProtectedModeApprovals approvals,
            string command, string documentKey, string sessionId, string clientName)
        {
            var decision = approvals.Check(command, documentKey, sessionId, clientName);
            if (decision.Allowed)
                return new ProtectedModeGateDecision(true, null, decision.Ticket);

            if (decision.ErrorCode == ErrorCodes.SessionRequired)
                return new ProtectedModeGateDecision(false,
                    "This MCP server did not identify its session; update NavisHelper's MCP server.",
                    errorCode: ErrorCodes.SessionRequired);

            var client = ProtectedModeLabels.ClientLabel(clientName, sessionId, "unknown client");
            var fileName = Path.GetFileName(documentKey);
            return new ProtectedModeGateDecision(false, "Protected mode: " + client
                + " needs approval to change '" + (string.IsNullOrEmpty(fileName) ? documentKey : fileName)
                + "'. Ask the user to approve code " + decision.Code
                + " in the NavisHelper panel, then retry.");
        }
    }

    internal sealed class ProtectedModeGateDecision
    {
        public ProtectedModeGateDecision(bool allowed, string message, ApprovalTicket ticket = null,
            string errorCode = ErrorCodes.ApprovalRequired)
        {
            Allowed = allowed;
            ErrorCode = allowed ? null : errorCode;
            Message = message;
            Ticket = ticket;
        }

        public bool Allowed { get; }
        public string ErrorCode { get; }
        public string Message { get; }
        public ApprovalTicket Ticket { get; }
    }
}
