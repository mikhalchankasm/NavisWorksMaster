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
    /// The account of why this exists, what it deliberately does not cover, and the
    /// live measurements behind it lives in `docs/MCP_TOOL_CONTRACTS.md` under
    /// *Attaching instead of starting a second process*. Not repeated here: three
    /// copies of one dated observation drift, and the repository keeps a fact in one
    /// place.
    ///
    /// The rule this type enforces: **attach only to a host proven to hold the
    /// requested document.** Discovery exposes a document *title*, which is a file
    /// name and not an identity, so a name match is a candidate and nothing more.
    /// </summary>
    public static class NavisworksAttachPolicy
    {
        /// <summary>
        /// Every host that *may* be serving this request, newest first, or an empty
        /// list to launch a process.
        ///
        /// These are candidates, not a decision. Discovery reports a document title --
        /// a file name -- so `C:\A\model.nwd` and `D:\B\model.nwd` are
        /// indistinguishable here. Attaching on a name alone would report success
        /// without opening the requested file, and later write tools would target the
        /// wrong model. The caller must prove identity with
        /// <see cref="DocumentPathMatches"/> against each host's full document path,
        /// attach to the first that proves, and launch when none does.
        ///
        /// All of them, not only the newest, because the newest host holding a
        /// same-named file need not be the one holding *this* file. With
        /// `D:\A\model.nwd` open in an older host and `D:\B\model.nwd` in a newer
        /// one, offering only the newer one lets a request for `D:\A\model.nwd` fail
        /// its path proof and start a third process while the file it asked for is
        /// already open -- roughly 15 500 ms instead of 123 ms, and a redundant
        /// Navisworks window left behind. Newest first remains the preference among
        /// candidates that are otherwise equal.
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
        /// invisible here and therefore still launches.
        /// </summary>
        public static IReadOnlyList<NavisworksHostInfo> SelectAttachCandidates(
            IEnumerable<NavisworksHostInfo> hosts,
            string navisworksVersion,
            string requestedFilePath)
        {
            var expectedTitle = GetExpectedDocumentTitle(requestedFilePath);
            if (string.IsNullOrWhiteSpace(expectedTitle))
                return EmptyCandidates;

            if (hosts == null)
                return EmptyCandidates;

            return hosts
                .Where(host => host != null)
                .Where(host => string.IsNullOrWhiteSpace(navisworksVersion) ||
                               string.Equals(host.NavisworksVersion, navisworksVersion, StringComparison.OrdinalIgnoreCase))
                .Where(host => string.Equals(host.DocumentTitle, expectedTitle, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(host => host.StartedAtUtc)
                .ToList();
        }

        private static readonly IReadOnlyList<NavisworksHostInfo> EmptyCandidates =
            new NavisworksHostInfo[0];

        /// <summary>
        /// Whether a host's full document path is the file that was requested.
        ///
        /// This is the proof a name match cannot give. Both sides are compared as full
        /// paths, case-insensitively, because Windows paths are. A blank on either side
        /// proves nothing and therefore fails: an unproven match must launch rather
        /// than attach, because the cost of attaching to the wrong model is a write
        /// tool acting on it.
        /// </summary>
        public static bool DocumentPathMatches(string hostDocumentFileName, string requestedFilePath)
        {
            if (string.IsNullOrWhiteSpace(hostDocumentFileName) || string.IsNullOrWhiteSpace(requestedFilePath))
                return false;

            try
            {
                var hostPath = Path.GetFullPath(hostDocumentFileName.Trim());
                var requestedPath = Path.GetFullPath(requestedFilePath.Trim());
                return string.Equals(hostPath, requestedPath, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (PathTooLongException)
            {
                return false;
            }
        }

        public const string CandidateDocumentNotProvenMessage =
            "A Navisworks host of this version reports a document with the same file name, but its full path could not "
            + "be confirmed as the requested file, so a process was started instead of attaching to it. Attaching on a "
            + "matching name alone would risk later tools acting on a different model.";

        /// <summary>
        /// The unproven-candidate message for however many candidates were examined.
        /// Every candidate is checked before a process is started, so a message naming
        /// a single host would understate what was ruled out.
        /// </summary>
        public static string BuildCandidateDocumentNotProvenMessage(int candidateCount)
        {
            if (candidateCount <= 1)
                return CandidateDocumentNotProvenMessage;

            return candidateCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                   " Navisworks hosts of this version report a document with the same file name, but none of their "
                   + "full paths could be confirmed as the requested file, so a process was started instead of "
                   + "attaching to any of them. Attaching on a matching name alone would risk later tools acting on a "
                   + "different model.";
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

        /// <summary>
        /// Why a launch happened with candidates still unexamined.
        ///
        /// The probes share one deadline, so a run of unresponsive hosts cannot make
        /// start_navisworks slower than it was when only one host was ever probed. The
        /// cost is that a later candidate may have held the file; saying which ones went
        /// unexamined is what lets the caller retry with an explicit instanceId instead
        /// of wondering why a second Navisworks appeared.
        /// </summary>
        public static string BuildAttachProbeBudgetMessage(int probed, int total)
        {
            var remaining = total - probed;
            return "Checked " + probed.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                   " of " + total.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                   " running hosts with a matching document name before the shared probe budget ran out, so " +
                   remaining.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                   " were not examined and a new process was started. If one of them holds the file, close " +
                   "the new instance and pass its instanceId from list_navisworks_hosts instead.";
        }

        public const string AttachedToExistingHostMessage =
            "Navisworks was already running with this document open, so no process was started and the running host is "
            + "reported instead. Pass instanceId from this response, or from list_navisworks_hosts, to target it.";

        /// <summary>
        /// Warning for a session that now holds more than one host of the same version.
        ///
        /// Derived from the count **after** the launch, not before it. A pre-launch
        /// count of one predicts a second host, and that prediction can be wrong:
        /// Roamer can hand a file off into an instance that is already running, in
        /// which case nothing was added and the warning would have told the caller to
        /// close an instance that does not exist. Counting afterwards states what is,
        /// which is the only thing worth saying at this level of certainty.
        /// </summary>
        public static string BuildAdditionalHostWarning(int readyHostsOfVersionAfter, string navisworksVersion)
        {
            if (readyHostsOfVersionAfter <= 1)
                return null;

            var version = string.IsNullOrWhiteSpace(navisworksVersion) ? "that version" : navisworksVersion;
            return readyHostsOfVersionAfter.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                   " Navisworks " + version + " hosts are now running, because no running host held the requested " +
                   "document and no command opens a document in a running instance. Every tool call now needs an " +
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
