using System.Diagnostics;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed class NavisworksLaunchService
{
    private static readonly string[] SupportedVersions = { "2027", "2026", "2025", "2024" };
    private readonly HostBridgeClient _hostBridgeClient;
    private readonly McpCallLogger _callLogger;
    private readonly NavisworksRecentFilesService _recentFilesService;
    private readonly NavisworksProcessStartInfoFactory _startInfoFactory;
    private readonly INavisworksProcessLauncher _processLauncher;
    private readonly NavisworksStartupMonitor _startupMonitor;

    public NavisworksLaunchService(
        HostBridgeClient hostBridgeClient,
        McpCallLogger callLogger,
        NavisworksRecentFilesService recentFilesService)
        : this(hostBridgeClient, callLogger, recentFilesService, new SystemNavisworksProcessLauncher())
    {
    }

    internal NavisworksLaunchService(
        HostBridgeClient hostBridgeClient,
        McpCallLogger callLogger,
        NavisworksRecentFilesService recentFilesService,
        INavisworksProcessLauncher processLauncher)
    {
        _hostBridgeClient = hostBridgeClient;
        _callLogger = callLogger;
        _recentFilesService = recentFilesService;
        _startInfoFactory = new NavisworksProcessStartInfoFactory();
        _processLauncher = processLauncher ?? new SystemNavisworksProcessLauncher();
        _startupMonitor = new NavisworksStartupMonitor();
    }

    public async Task<StartNavisworksResponse> StartAsync(
        string navisworksVersion,
        string filePath,
        bool openLatestRecentFile,
        bool waitForHost,
        int waitTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        // Starting Navisworks costs roughly fifteen seconds and leaves a window open.
        // A request the caller has already abandoned buys neither, so nothing below
        // runs for one.
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var response = new StartNavisworksResponse
        {
            WaitedForHost = waitForHost,
        };

        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Navisworks launch is available only on Windows.");

        var hostsBefore = _hostBridgeClient.ListNavisworksHosts().Hosts;

        var effectiveFilePath = (filePath ?? string.Empty).Trim();
        NavisworksRecentFileInfo recentFile = null;
        if (openLatestRecentFile && string.IsNullOrWhiteSpace(effectiveFilePath))
        {
            recentFile = _recentFilesService.GetLatestRecentFile(navisworksVersion, existingOnly: true, out var warnings);
            response.Warnings.AddRange(warnings);
            if (recentFile == null)
                throw new InvalidOperationException("No existing recent Navisworks file was found for the requested version scope.");

            effectiveFilePath = recentFile.Path;
            response.OpenedRecentFile = true;
            response.RecentFile = recentFile;
            if (string.IsNullOrWhiteSpace(navisworksVersion))
                navisworksVersion = recentFile.NavisworksVersion;
        }

        if (!string.IsNullOrWhiteSpace(effectiveFilePath))
        {
            effectiveFilePath = Path.GetFullPath(effectiveFilePath);
            if (!File.Exists(effectiveFilePath))
                throw new FileNotFoundException("Navisworks model file was not found.", effectiveFilePath);
        }

        var version = ResolveNavisworksVersion(navisworksVersion);
        var roamerPath = ResolveRoamerPath(version);

        response.NavisworksVersion = version;
        response.RoamerPath = roamerPath;
        response.FilePath = effectiveFilePath;

        // Attach before launching, but only to a host proven to hold the requested
        // file. Discovery reports a document title, which is a file name: two models of
        // the same name in different directories are indistinguishable there, and
        // attaching on the name alone would report success without opening what was
        // asked for, leaving later write tools pointed at the wrong model. Every
        // name-matching host is offered, not only the newest, because the newest one
        // holding a same-named file need not be the one holding this file.
        var attachTarget = await ResolveProvenAttachTargetAsync(
            NavisworksAttachPolicy.SelectAttachCandidates(hostsBefore, version, effectiveFilePath),
            effectiveFilePath,
            response,
            (target, token) => _hostBridgeClient.HostStatusAsync(
                new HostStatusRequest(),
                token,
                new HostTargetOptions { InstanceId = target.InstanceId }),
            cancellationToken).ConfigureAwait(false);
        if (attachTarget != null)
        {
            response.Started = true;
            response.ProcessCreated = false;
            response.ProcessId = attachTarget.Pid;
            response.HostReady = true;
            response.Host = attachTarget;
            response.Outcome = StartNavisworksOutcomes.AttachedToExistingHost;
            response.Message = NavisworksAttachPolicy.AttachedToExistingHostMessage;

            stopwatch.Stop();
            response.StartupElapsedMs = 0;
            response.ElapsedMs = stopwatch.ElapsedMilliseconds;
            response.ElapsedHuman = ElapsedTimeFormatter.Format(response.ElapsedMs);
            // No process was launched, so there are no launch-environment facts to log.
            _callLogger.LogStartNavisworks(response, null);
            return response;
        }

        // Last check before the only irreversible step in this method. Resolving the
        // version, listing hosts and probing a candidate all take time the caller may
        // have spent cancelling; creating the process anyway would leave a Navisworks
        // window open for a request whose answer nobody reads.
        cancellationToken.ThrowIfCancellationRequested();

        var startInfoBuild = _startInfoFactory.Create(roamerPath, effectiveFilePath);

        // Snapshot discovery again, as close to the launch as this method can get. The
        // list taken at the top is as much as a probe budget old -- 60 seconds -- and
        // SelectHandoffCandidates asks which hosts acquired the requested title since
        // this launch. Read from the older list, every document somebody opened by hand
        // while the probes ran looks like this launch's hand-off.
        //
        // What that costs is a wasted round trip, not a wrong answer: a candidate is only
        // ever accepted after its full path is proven, so this snapshot decides how many
        // instances get probed, not which host is reported. That is why the residual
        // window between this call and Start is affordable -- it cannot be closed from
        // here anyway, since the launch boundary is only knowable once the process exists.
        var hostsAtLaunch = _hostBridgeClient.ListNavisworksHosts().Hosts;

        var startupStopwatch = Stopwatch.StartNew();
        using var process = _processLauncher.Start(startInfoBuild.StartInfo);
        response.ProcessCreated = true;
        response.Started = true;
        response.ProcessId = process.Id;

        NavisworksStartupMonitorResult startupResult;
        if (waitForHost)
        {
            // One verdict per instance for the whole wait, not per poll.
            var pathProofByInstance = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            startupResult = await _startupMonitor.WaitForHostAsync(
                process,
                (excludedProcessId, token) => FindHostAsync(
                    version,
                    effectiveFilePath,
                    response.ProcessId,
                    hostsAtLaunch,
                    excludedProcessId,
                    pathProofByInstance,
                    token),
                TimeSpan.FromSeconds(ClampWaitTimeoutSeconds(waitTimeoutSeconds)),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            startupResult = _startupMonitor.ObserveWithoutWait(process);
        }

        ApplyStartupResult(response, startupResult, waitForHost);

        // Count hosts again rather than predicting from the pre-launch count. Roamer can
        // hand a file off into an instance that is already running, in which case the
        // launch adds no host and a warning derived before the launch would have been
        // false -- telling the caller to close an instance that does not exist.
        var hostsOfVersionAfter = NavisworksAttachPolicy.CountReadyHostsOfVersion(
            _hostBridgeClient.ListNavisworksHosts().Hosts,
            version);
        var additionalHostWarning = NavisworksAttachPolicy.BuildAdditionalHostWarning(hostsOfVersionAfter, version);
        if (!string.IsNullOrEmpty(additionalHostWarning))
            response.Warnings.Add(additionalHostWarning);

        // A launch can still legitimately report a host other than the pid it created:
        // Roamer hands the file to an existing instance, that instance picks up the
        // requested document, and SelectHost's last resort returns it. The caller is
        // told when the reported host is not the process that was started rather than
        // left to notice the mismatch, because every later tool addresses the host.
        var mismatchWarning = NavisworksAttachPolicy.BuildHostProcessMismatchWarning(
            response.ProcessId,
            response.Host == null ? (int?)null : response.Host.Pid);
        if (!string.IsNullOrEmpty(mismatchWarning))
            response.Warnings.Add(mismatchWarning);

        startupStopwatch.Stop();
        stopwatch.Stop();
        response.StartupElapsedMs = startupStopwatch.ElapsedMilliseconds;
        response.ElapsedMs = stopwatch.ElapsedMilliseconds;
        response.ElapsedHuman = ElapsedTimeFormatter.Format(response.ElapsedMs);
        _callLogger.LogStartNavisworks(response, startInfoBuild.EnvironmentFacts);

        return response;
    }

    /// <summary>
    /// Turns name-matched candidates into the host proven to hold the requested file,
    /// or null so the caller launches.
    ///
    /// Discovery carries a document title, not a path, so the proof needs one extra
    /// call per candidate: <c>host_status</c> on that instance reports
    /// <c>DocumentFileName</c>. Candidates arrive newest first and every one is tried,
    /// because the newest host with a same-named document need not be the one holding
    /// the requested path -- stopping at the first would launch a redundant process
    /// past a host that already has the file open. Only when none proves does the
    /// caller launch, and the response says why: a wrong attach is worse than a
    /// redundant process, because the next write tool would act on the wrong model.
    /// </summary>
    /// <remarks>
    /// The probes share one deadline. Each <c>host_status</c> call otherwise carries the
    /// transport's own 60-second timeout, so three unresponsive candidates would hold
    /// start_navisworks for three minutes where one candidate held it for one -- and a
    /// client written against the old envelope would give up before the candidate that
    /// would have matched was ever reached. Trying more hosts must not cost more time
    /// than trying one did.
    ///
    /// When the budget runs out the caller launches, which is the same safe outcome as
    /// no candidate proving its path. Note that budget expiry is *not* caller
    /// cancellation: the linked token trips, <c>cancellationToken</c> does not, so the
    /// rethrow below does not fire and the loop ends by the check at the top instead.
    /// </remarks>
    internal static async Task<NavisworksHostInfo> ResolveProvenAttachTargetAsync(
        IReadOnlyList<NavisworksHostInfo> candidates,
        string requestedFilePath,
        StartNavisworksResponse response,
        Func<NavisworksHostInfo, CancellationToken, Task<HostStatusResponse>> probeHostStatusAsync,
        CancellationToken cancellationToken,
        TimeSpan? probeBudget = null)
    {
        if (candidates == null || candidates.Count == 0)
            return null;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(probeBudget ?? DefaultAttachProbeBudget);

        var probedCount = 0;
        foreach (var candidate in candidates)
        {
            if (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                response.Warnings.Add(NavisworksAttachPolicy.BuildAttachProbeBudgetMessage(
                    probedCount, candidates.Count));
                return null;
            }

            probedCount++;
            HostStatusResponse status = null;
            try
            {
                status = await probeHostStatusAsync(candidate, budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller cancelled, which is not evidence about the host, and not a
                // reason to move to the next candidate either. Swallowing it here would
                // send the request on to launch a Navisworks nobody asked for. The
                // transport turns its own timeout into HostCallException, so an
                // unresponsive host still arrives at the catch below.
                throw;
            }
            catch (Exception)
            {
                // The host answered discovery and then did not answer this, so it is not
                // a host to hand the caller. The next candidate may still be, and if none
                // is, launching is the safe outcome rather than an error.
                status = null;
            }

            if (NavisworksAttachPolicy.DocumentPathMatches(
                    status == null ? null : status.DocumentFileName,
                    requestedFilePath))
            {
                return candidate;
            }
        }

        response.Warnings.Add(
            NavisworksAttachPolicy.BuildCandidateDocumentNotProvenMessage(candidates.Count));
        return null;
    }

    /// The whole attach resolution gets the budget one host_status call used to
    /// have, so more candidates never cost more wall clock than one candidate did.
    private static readonly TimeSpan DefaultAttachProbeBudget = TimeSpan.FromSeconds(60);

    internal static void ApplyStartupResult(
        StartNavisworksResponse response,
        NavisworksStartupMonitorResult startupResult,
        bool waitedForHost)
    {
        response.Outcome = startupResult.Outcome;
        response.ProcessExited = startupResult.ProcessExited;
        response.ExitCode = startupResult.ExitCode;
        response.Host = startupResult.Host;
        response.HostReady = startupResult.Host != null;

        if (response.HostReady)
        {
            response.Started = true;
            response.Message = startupResult.ProcessExited
                ? "Navisworks launcher handed off to a ready MCP host."
                : "Navisworks started and MCP host is ready.";
            return;
        }

        if (startupResult.ProcessExited)
        {
            response.Started = false;
            response.FailureReason = startupResult.Outcome == StartNavisworksOutcomes.HostTimeout
                ? "Navisworks launcher exited cleanly, but no handed-off MCP host appeared before the wait timeout."
                : "Navisworks exited during startup before the MCP host became ready.";
            response.Warnings.Add(response.FailureReason);
            response.Message = startupResult.Outcome == StartNavisworksOutcomes.HostTimeout
                ? "Navisworks launcher handoff timed out."
                : "Navisworks process exited during startup.";
            return;
        }

        if (waitedForHost)
        {
            response.FailureReason = "Navisworks remained running, but the MCP host did not appear before the wait timeout.";
            response.Warnings.Add(response.FailureReason);
            response.Message = "Navisworks is running, but MCP host startup timed out.";
            return;
        }

        response.Message = "Navisworks process was created; MCP host readiness was not requested.";
    }

    private async Task<NavisworksHostInfo> FindHostAsync(
        string navisworksVersion,
        string filePath,
        int? processId,
        IReadOnlyList<NavisworksHostInfo> hostsAtLaunch,
        int? excludedProcessId,
        IDictionary<string, bool> pathProofByInstance,
        CancellationToken cancellationToken)
    {
        var expectedTitle = string.IsNullOrWhiteSpace(filePath) ? string.Empty : Path.GetFileName(filePath);
        var hosts = _hostBridgeClient.ListNavisworksHosts().Hosts
            .Where(host => string.Equals(host.NavisworksVersion, navisworksVersion, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var host = SelectHost(hosts, expectedTitle, processId, hostsAtLaunch, excludedProcessId);
        if (host != null)
            return host;

        return await ResolveProvenHandoffAsync(
            SelectHandoffCandidates(hosts, expectedTitle, hostsAtLaunch, excludedProcessId),
            filePath,
            pathProofByInstance,
            (target, token) => _hostBridgeClient.HostStatusAsync(
                new HostStatusRequest(),
                token,
                new HostTargetOptions { InstanceId = target.InstanceId }),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The hand-off candidate proven to hold the requested file, or null to keep polling.
    ///
    /// A hand-off registers no new host, so the only reading left once both tiers of
    /// <see cref="SelectHost"/> come up empty is that an instance already running now
    /// holds the file. That is a claim about a path; discovery carries only a name, so it
    /// is confirmed the way the attach path confirms it -- <c>host_status</c> on that
    /// instance, comparing the full <c>documentFileName</c>. Without this a launch reports
    /// a host it never opened whenever a same-named model is opened elsewhere during
    /// startup.
    ///
    /// <paramref name="pathProofByInstance"/> carries one verdict per instance for the
    /// whole wait. The poll runs every 250 ms for up to five minutes, so re-asking each
    /// time would put hundreds of round trips into an instance somebody may be working
    /// in, to repeat a question whose answer changes only if that instance loads another
    /// document -- and then its title moves and the pre-filter re-evaluates it anyway.
    /// </summary>
    internal static async Task<NavisworksHostInfo> ResolveProvenHandoffAsync(
        IReadOnlyList<NavisworksHostInfo> candidates,
        string requestedFilePath,
        IDictionary<string, bool> pathProofByInstance,
        Func<NavisworksHostInfo, CancellationToken, Task<HostStatusResponse>> probeHostStatusAsync,
        CancellationToken cancellationToken)
    {
        if (candidates == null || candidates.Count == 0)
            return null;

        foreach (var candidate in candidates)
        {
            var key = candidate.InstanceId ?? string.Empty;
            if (pathProofByInstance != null && pathProofByInstance.TryGetValue(key, out var alreadyProven))
            {
                if (alreadyProven)
                    return candidate;

                continue;
            }

            HostStatusResponse status;
            try
            {
                status = await probeHostStatusAsync(candidate, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // An instance that does not answer is not disqualified for the rest of the
                // wait: it may be mid-load and answer on a later poll. No verdict is
                // recorded, so the next poll asks again.
                continue;
            }

            var proven = NavisworksAttachPolicy.DocumentPathMatches(status?.DocumentFileName, requestedFilePath);
            if (pathProofByInstance != null)
                pathProofByInstance[key] = proven;

            if (proven)
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// The host this launch produced, identified without a round trip, or null.
    ///
    /// Two tiers, both of which identify the host by something other than a file name:
    /// the pid that was started, and a host that registered after the launch. Null does
    /// not mean "no host" -- a hand-off registers no new host, and
    /// <see cref="SelectHandoffCandidates"/> covers that case at the cost of a probe.
    /// </summary>
    internal static NavisworksHostInfo SelectHost(
        IReadOnlyList<NavisworksHostInfo> hosts,
        string expectedTitle,
        int? processId,
        IReadOnlyList<NavisworksHostInfo> hostsBefore,
        int? excludedProcessId)
    {
        hosts ??= Array.Empty<NavisworksHostInfo>();
        hostsBefore ??= Array.Empty<NavisworksHostInfo>();
        var candidates = Selectable(hosts, excludedProcessId);

        if (processId.HasValue)
        {
            var byPid = candidates.FirstOrDefault(host => host.Pid == processId.Value);
            if (byPid != null && HostDocumentMatches(byPid, expectedTitle))
                return byPid;
        }

        var beforePids = new HashSet<int>(hostsBefore.Select(host => host.Pid));
        return candidates
            .Where(host => !beforePids.Contains(host.Pid))
            .Where(host => HostDocumentMatches(host, expectedTitle))
            .OrderByDescending(host => host.StartedAtUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Hosts that were already running and may have been handed the file, newest first,
    /// for a caller that will prove each one's full path before accepting it.
    ///
    /// Roamer can hand the file to an existing instance rather than open it itself, and
    /// then no new host appears. Without this a launch would time out on a document open
    /// on screen, which is why the case is covered at all.
    ///
    /// These are candidates, never an answer. Discovery reports a document title -- a
    /// file name -- so a same-named model in another directory is indistinguishable here.
    /// Measured live, a request for D:\nh-l3-c\6501.5.nwd returned in 140 ms naming the
    /// host that held D:\nh-l3-b\6501.5.nwd while the process it had started was still
    /// loading C. The caller must confirm the full path with <c>host_status</c>, exactly
    /// as the attach path does before a launch.
    ///
    /// The title filter is a *pre-filter*, not the proof: only a host that acquired the
    /// expected title since <paramref name="hostsBefore"/> was taken is offered, which
    /// keeps the common case free of round trips -- nothing acquired the title, nothing
    /// is probed. Correctness rests on the caller's path proof, so a stale
    /// <paramref name="hostsBefore"/> costs a wasted probe rather than a wrong host.
    /// </summary>
    internal static IReadOnlyList<NavisworksHostInfo> SelectHandoffCandidates(
        IReadOnlyList<NavisworksHostInfo> hosts,
        string expectedTitle,
        IReadOnlyList<NavisworksHostInfo> hostsBefore,
        int? excludedProcessId)
    {
        if (string.IsNullOrWhiteSpace(expectedTitle))
            return Array.Empty<NavisworksHostInfo>();

        hosts ??= Array.Empty<NavisworksHostInfo>();
        hostsBefore ??= Array.Empty<NavisworksHostInfo>();

        return Selectable(hosts, excludedProcessId)
            .Where(host => HostDocumentMatches(host, expectedTitle))
            .Where(host => !HeldExpectedTitleBeforeLaunch(hostsBefore, host, expectedTitle))
            .OrderByDescending(host => host.StartedAtUtc)
            .ToList();
    }

    private static List<NavisworksHostInfo> Selectable(
        IReadOnlyList<NavisworksHostInfo> hosts,
        int? excludedProcessId) =>
        excludedProcessId.HasValue
            ? hosts.Where(host => host.Pid != excludedProcessId.Value).ToList()
            : hosts.ToList();

    private static bool HeldExpectedTitleBeforeLaunch(
        IReadOnlyList<NavisworksHostInfo> hostsBefore,
        NavisworksHostInfo host,
        string expectedTitle)
    {
        var before = hostsBefore.FirstOrDefault(candidate => IsSameHostRecord(candidate, host));
        return before != null && HostDocumentMatches(before, expectedTitle);
    }

    // Instance ids are preferred over pids because a pid can be reused by a later
    // process, which would make a brand-new host look like one of the hosts observed
    // before the launch. Pids are the fallback for a record that carries no instance id.
    //
    // That fallback can misread a pid-reusing host as its predecessor and rule out a
    // genuine new host, and it is kept because that is the safe direction: ruling one
    // out ends in a truthful host_timeout, while failing to would let a launch claim a
    // host it never opened. A host with no instance id could not be addressed by the
    // tools that follow anyway -- they target hosts by instance id.
    private static bool IsSameHostRecord(NavisworksHostInfo left, NavisworksHostInfo right)
    {
        if (!string.IsNullOrWhiteSpace(left.InstanceId) && !string.IsNullOrWhiteSpace(right.InstanceId))
            return string.Equals(left.InstanceId, right.InstanceId, StringComparison.OrdinalIgnoreCase);

        return left.Pid == right.Pid;
    }

    private static int ClampWaitTimeoutSeconds(int timeoutSeconds) => Math.Clamp(timeoutSeconds, 1, 300);

    private static bool HostDocumentMatches(NavisworksHostInfo host, string expectedTitle)
    {
        if (string.IsNullOrWhiteSpace(expectedTitle))
            return true;

        return string.Equals(host.DocumentTitle, expectedTitle, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveNavisworksVersion(string requestedVersion)
    {
        if (!string.IsNullOrWhiteSpace(requestedVersion))
        {
            var version = requestedVersion.Trim();
            if (!SupportedVersions.Contains(version, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsupported Navisworks version '" + requestedVersion + "'. Expected one of 2024, 2025, 2026, 2027.");

            if (!File.Exists(GetRoamerPath(version)))
                throw new FileNotFoundException("Navisworks Manage " + version + " was not found.", GetRoamerPath(version));

            return version;
        }

        foreach (var version in SupportedVersions)
        {
            if (File.Exists(GetRoamerPath(version)))
                return version;
        }

        throw new FileNotFoundException("No supported Navisworks Manage installation was found.");
    }

    private static string ResolveRoamerPath(string version)
    {
        var roamerPath = GetRoamerPath(version);
        if (!File.Exists(roamerPath))
            throw new FileNotFoundException("Navisworks Manage " + version + " was not found.", roamerPath);

        return roamerPath;
    }

    private static string GetRoamerPath(string version)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Autodesk",
            "Navisworks Manage " + version,
            "Roamer.exe");
    }
}
