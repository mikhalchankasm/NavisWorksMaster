using System;
using System.IO;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal static class ProtectedModeGate
    {
        public static ProtectedModeGateDecision Evaluate(ProtectedModeApprovals approvals,
            string command, string documentKey, string sessionId, string clientName)
        {
            if (command != null && (command.EndsWith("_status", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("cancel_", StringComparison.OrdinalIgnoreCase)))
                return new ProtectedModeGateDecision(true, null);

            var client = string.IsNullOrWhiteSpace(clientName) ? "unknown client" : clientName;
            var decision = approvals.Check(command, documentKey, sessionId, client);
            if (decision.Allowed)
                return new ProtectedModeGateDecision(true, null);

            var fileName = Path.GetFileName(documentKey);
            return new ProtectedModeGateDecision(false, "Protected mode: " + client
                + " needs approval to change '" + (string.IsNullOrEmpty(fileName) ? documentKey : fileName)
                + "'. Ask the user to approve code " + decision.Code
                + " in the NavisHelper panel, then retry.");
        }
    }

    internal sealed class ProtectedModeGateDecision
    {
        public ProtectedModeGateDecision(bool allowed, string message)
        {
            Allowed = allowed;
            ErrorCode = allowed ? null : ErrorCodes.ApprovalRequired;
            Message = message;
        }

        public bool Allowed { get; }
        public string ErrorCode { get; }
        public string Message { get; }
    }
}
