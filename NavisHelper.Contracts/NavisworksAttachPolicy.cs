using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Decides whether starting Navisworks should attach to a host that is already
    /// running, or launch another process.
    ///
    /// Observed live on 2026-09-20, Navisworks 2027: `start_navisworks` produced a
    /// ready host with no document, `open_latest_navisworks_file` then produced a
    /// **second** process, and every following call failed with
    /// `multiple_hosts_detected` until one was closed. A second session reproduced it
    /// with the requested file already open in the running host, which is the case
    /// where launching another process is provably wrong: the caller asked for that
    /// file, it is open, and a ready host is serving it.
    ///
    /// There is no host command that opens a document in a running instance. That is
    /// what bounds this policy: a request naming a file the running host does not have
    /// open cannot be satisfied by attaching, so it still launches. Fixing that case
    /// means adding the capability, not changing this decision.
    /// </summary>
    public static class NavisworksAttachPolicy
    {
        /// <summary>
        /// The host to attach to, or null to launch a process.
        ///
        /// A blank <paramref name="requestedFilePath"/> always launches. A caller
        /// asking only to start Navisworks may legitimately want another instance, and
        /// with no document named there is nothing to check a running host against --
        /// attaching on that basis would turn "start Navisworks" into "give me
        /// whatever is running", which is a different tool.
        ///
        /// A host only appears in the discovery list once it has registered, which is
        /// the same record <c>FindHost</c> treats as readiness, so a listed host of the
        /// right version is a ready host. A running-but-not-yet-registered instance is
        /// invisible here and therefore still launches, which is the case the task
        /// asked to preserve.
        /// </summary>
        public static NavisworksHostInfo SelectAttachTarget(
            IEnumerable<NavisworksHostInfo> hosts,
            string navisworksVersion,
            string requestedFilePath)
        {
            var expectedTitle = GetExpectedDocumentTitle(requestedFilePath);
            if (string.IsNullOrWhiteSpace(expectedTitle))
                return null;

            if (hosts == null)
                return null;

            return hosts
                .Where(host => host != null)
                .Where(host => string.IsNullOrWhiteSpace(navisworksVersion) ||
                               string.Equals(host.NavisworksVersion, navisworksVersion, StringComparison.OrdinalIgnoreCase))
                .Where(host => string.Equals(host.DocumentTitle, expectedTitle, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(host => host.StartedAtUtc)
                .FirstOrDefault();
        }

        /// <summary>
        /// The document title a host must report to be serving this request. Public
        /// because the launch service and the tests must agree on it exactly.
        /// </summary>
        public static string GetExpectedDocumentTitle(string requestedFilePath)
        {
            if (string.IsNullOrWhiteSpace(requestedFilePath))
                return string.Empty;

            try
            {
                return Path.GetFileName(requestedFilePath.Trim());
            }
            catch (ArgumentException)
            {
                // An unusable path cannot match a host, and deciding where to launch is
                // not the place to reject it: the caller gets the real file error later.
                return string.Empty;
            }
        }

        /// <summary>
        /// Counts the ready hosts of this version that a new process would have to
        /// share the session with. Anything above zero means the next call needs an
        /// explicit <c>instanceId</c> or it fails with `multiple_hosts_detected`.
        /// </summary>
        public static int CountReadyHostsOfVersion(
            IEnumerable<NavisworksHostInfo> hosts,
            string navisworksVersion)
        {
            if (hosts == null)
                return 0;

            return hosts
                .Where(host => host != null)
                .Count(host => string.IsNullOrWhiteSpace(navisworksVersion) ||
                               string.Equals(host.NavisworksVersion, navisworksVersion, StringComparison.OrdinalIgnoreCase));
        }

        public const string AttachedToExistingHostMessage =
            "Navisworks was already running with this document open, so no process was started and the running host is "
            + "reported instead. Pass instanceId from this response, or from list_navisworks_hosts, to target it.";

        /// <summary>
        /// Warning for a launch that leaves more than one host of the same version
        /// running. Said at the moment it becomes true, because the caller discovers it
        /// otherwise on its next call, as an error about a situation this one created.
        /// </summary>
        public static string BuildAdditionalHostWarning(int readyHostsOfVersionBefore, string navisworksVersion)
        {
            if (readyHostsOfVersionBefore <= 0)
                return null;

            var version = string.IsNullOrWhiteSpace(navisworksVersion) ? "that version" : navisworksVersion;
            return "A process was started while " + readyHostsOfVersionBefore.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                   " Navisworks " + version + " host(s) were already running, because no running host had the requested " +
                   "document open and no command opens a document in a running instance. Every tool call now needs an " +
                   "explicit instanceId, or it fails with multiple_hosts_detected. Use list_navisworks_hosts, and " +
                   "close_navisworks to retire the one you do not want.";
        }

        /// <summary>
        /// Warning for a startup whose discovered host is not the process that was
        /// launched.
        ///
        /// `SelectHost`'s last resort matches on document title without excluding hosts
        /// that were already running, so a launch can report a pre-existing host beside
        /// the pid it just created. A peer session saw exactly that: processId 42284
        /// with host.pid 57488 and the requested file as the document title, which
        /// reads as correct to precisely the caller it misleads. Keeping the fallback
        /// and saying when it fired is better than removing it -- it is the only thing
        /// that finds the host when Navisworks serves the file from another process.
        /// </summary>
        public static string BuildHostProcessMismatchWarning(int? startedProcessId, int? hostPid)
        {
            if (!startedProcessId.HasValue || !hostPid.HasValue || startedProcessId.Value == hostPid.Value)
                return null;

            return "The reported host runs in process " + hostPid.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                   ", not in the process this call started (" + startedProcessId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                   "). It was matched by document title, so it may be an instance that was already running. Check " +
                   "list_navisworks_hosts before assuming the started process is the one serving your calls.";
        }
    }
}
