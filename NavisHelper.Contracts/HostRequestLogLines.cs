using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Every line the host writes for one request: arrival, the pre-state captured on the
    /// UI thread, and completion. A gated request used to write eight INFO lines, seven of
    /// which only named the stage the request had reached while repeating `request_id`,
    /// `command` and `elapsed_ms`. Each line costs a named mutex and a file open, which is
    /// measurable on tools whose whole host time is around 20 ms; see the read-only table
    /// in `docs/MCP_TOOL_BASELINE.md`.
    ///
    /// The completion line is load-bearing: that table compares host-side `elapsed_ms` read
    /// from it, so its shape is pinned by `HostRequestLogLinesTests`.
    /// </summary>
    public static class HostRequestLogLines
    {
        public static string Arrival(string requestId, string command)
        {
            return Identity(requestId, command) + " received";
        }

        /// <summary>
        /// The request as the UI thread saw it before the command ran. `elapsedMs` is read
        /// on entry to the callback, so it is the wait for the UI thread and not the cost of
        /// resolving the document; that wait is what separates a starved dispatcher from a
        /// slow command.
        /// </summary>
        public static string OperationStart(
            string requestId,
            string command,
            int timeoutMs,
            string dispatcher,
            string documentTitle,
            int selectedItemCount,
            string payloadSummary,
            long elapsedMs)
        {
            return Identity(requestId, command) +
                " operation_start timeout_ms=" + Number(timeoutMs) +
                " dispatcher=" + dispatcher +
                " document=\"" + documentTitle + "\"" +
                " selected_item_count=" + Number(selectedItemCount) +
                " parameters=" + payloadSummary +
                " elapsed_ms=" + Number(elapsedMs);
        }

        public static string Completed(string requestId, string command, long elapsedMs)
        {
            return Identity(requestId, command) + " elapsed_ms=" + Number(elapsedMs);
        }

        public static string BypassCompleted(string requestId, string command, long elapsedMs)
        {
            return Identity(requestId, command) + " bypass elapsed_ms=" + Number(elapsedMs);
        }

        private static string Identity(string requestId, string command)
        {
            return "request_id=" + Value(requestId) + " command=" + Value(command);
        }

        private static string Value(string value)
        {
            return value ?? "<null>";
        }

        private static string Number(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
} 
