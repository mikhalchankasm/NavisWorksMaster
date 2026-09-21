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
        // list taken at the top of the method is as much as a probe budget old -- 60
        // seconds -- and this one decides which candidates are asked first.
        //
        // Only the order. Every candidate is confirmed by path before it is accepted, so
        // a stale snapshot, or a pid reused between the two reads, costs a round trip
        // spent in the wrong order and never a wrong host. That is also why the window
        // between this call and Start does not need closing -- which is just as well,
        // since the launch boundary is only knowable once the process exists.
        var hostsAtLaunch = _hostBridgeClient.ListNavisworksHosts().Hosts;

        var startupStopwatch = Stopwatch.StartNew();
        using var process = _processLauncher.Start(startInfoBuild.StartInfo);
        response.ProcessCreated = true;
        response.Started = true;
        response.ProcessId = process.Id;

        NavisworksStartupMonitorResult startupResult;
        if (waitForHost)
        {
            // Shared across every poll of this wait, so a candidate is not re-probed on
            // each one.
            var ledger = new HandoffProofLedger();
            startupResult = await _startupMonitor.WaitForHostAsync(
                process,
                (excludedProcessId, token) => FindHostAsync(
                    version,
                    effectiveFilePath,
                    response.ProcessId,
                    hostsAtLaunch,
                    excludedProcessId,
                    ledger,
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

    // One candidate, not the whole wait. Long enough for a busy instance to answer a
    // status call, short enough that an instance which never answers costs one poll
    // rather than every remaining one.
    private static readonly TimeSpan DefaultPerProbeTimeout = TimeSpan.FromSeconds(5);

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
        HandoffProofLedger ledger,
        CancellationToken cancellationToken)
    {
        var expectedTitle = string.IsNullOrWhiteSpace(filePath) ? string.Empty : Path.GetFileName(filePath);
        var hosts = _hostBridgeClient.ListNavisworksHosts().Hosts
            .Where(host => string.Equals(host.NavisworksVersion, navisworksVersion, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var host = SelectHost(hosts, expectedTitle, processId, excludedProcessId);
        if (host != null)
            return host;

        return await ResolveProvenHandoffAsync(
            SelectProvableCandidates(hosts, expectedTitle, hostsAtLaunch, excludedProcessId),
            filePath,
            ledger,
            (target, token) => _hostBridgeClient.HostStatusAsync(
                new HostStatusRequest(),
                token,
                new HostTargetOptions { InstanceId = target.InstanceId }),
            DateTimeOffset.UtcNow,
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
    /// <paramref name="ledger"/> keeps the cost bounded. The wait polls every 250 ms for
    /// up to five minutes, and probing every candidate on every poll would put hundreds of
    /// round trips into an instance somebody may be working in.
    /// </summary>
    internal static async Task<NavisworksHostInfo> ResolveProvenHandoffAsync(
        IReadOnlyList<NavisworksHostInfo> candidates,
        string requestedFilePath,
        HandoffProofLedger ledger,
        Func<NavisworksHostInfo, CancellationToken, Task<HostStatusResponse>> probeHostStatusAsync,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken,
        TimeSpan? perProbeTimeout = null)
    {
        if (candidates == null || candidates.Count == 0)
            return null;

        ledger ??= new HandoffProofLedger();
        var probeTimeout = perProbeTimeout ?? DefaultPerProbeTimeout;

        foreach (var candidate in candidates)
        {
            var key = candidate.InstanceId ?? string.Empty;
            if (ledger.IsProven(key))
                return candidate;

            if (!ledger.ShouldProbe(key, nowUtc))
                continue;

            HostStatusResponse status;
            // Each candidate gets its own short deadline rather than whatever is left of
            // the startup wait. Sharing one deadline lets the first candidate that blocks
            // consume the entire budget, so the candidates behind it are never asked and
            // discovery is never polled again -- and a host that does hold the requested
            // file, sitting second in the list, loses to an instance that simply stopped
            // answering.
            using var probeBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probeBudget.CancelAfter(probeTimeout);
            try
            {
                status = await probeHostStatusAsync(candidate, probeBudget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // An instance that does not answer is not disqualified: it may be mid-load
                // and answer on a later poll. Nothing is recorded, so the next poll asks
                // again immediately rather than waiting out a refusal interval.
                continue;
            }

            var proven = NavisworksAttachPolicy.DocumentPathMatches(status?.DocumentFileName, requestedFilePath);
            ledger.Record(key, proven, nowUtc);
            if (proven)
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// The host this launch produced, identified without a round trip, or null.
    ///
    /// One case only: a host registered under the pid this launch started. That is an
    /// identity, not a name, which is what makes it safe to answer with directly.
    ///
    /// An earlier version had a second tier here -- any host that registered after the
    /// launch and whose document *title* matched. That is a name, and it could be
    /// somebody else's: another instance opening `B\model.nwd` after the snapshot would
    /// win a request for `C\model.nwd` before the launched pid registered. Every other
    /// reading now goes through <see cref="SelectProvableCandidates"/> and is confirmed
    /// by path.
    /// </summary>
    internal static NavisworksHostInfo SelectHost(
        IReadOnlyList<NavisworksHostInfo> hosts,
        string expectedTitle,
        int? processId,
        int? excludedProcessId)
    {
        if (!processId.HasValue)
            return null;

        hosts ??= Array.Empty<NavisworksHostInfo>();
        var byPid = Selectable(hosts, excludedProcessId)
            .FirstOrDefault(host => host.Pid == processId.Value);

        return byPid != null && HostDocumentMatches(byPid, expectedTitle) ? byPid : null;
    }

    /// <summary>
    /// Every host that might be serving this request, in the order worth asking, for a
    /// caller that will prove each one's full path before accepting it.
    ///
    /// Two readings are covered, and neither can be told from the other by name. A host
    /// that registered after the launch may be the process Roamer started under a pid
    /// this call never saw. A host that was already running may have been handed the
    /// file, because Roamer can give it to an existing instance rather than open it
    /// itself, in which case no new host appears at all -- without which a launch would
    /// time out on a document open on screen.
    ///
    /// These are candidates, never an answer. Discovery reports a document title -- a
    /// file name -- so a same-named model in another directory is indistinguishable here.
    /// Measured live, a request for D:\nh-l3-c\6501.5.nwd returned in 140 ms naming the
    /// host that held D:\nh-l3-b\6501.5.nwd while the process it had started was still
    /// loading C. The caller confirms the full path with <c>host_status</c>, exactly as
    /// the attach path does before a launch.
    ///
    /// Nothing here narrows by name beyond the title. An earlier version offered only
    /// hosts that *acquired* the expected title since the launch, to spend fewer round
    /// trips, and that threw away the case this path exists for: when Roamer hands
    /// `D:\C\model.nwd` to an instance already showing `D:\B\model.nwd` the title never
    /// changes, so the one host that really took the file looked ineligible.
    ///
    /// <paramref name="hostsBefore"/> only orders the result. Hosts that appeared since
    /// it was taken are asked first, because they are the likelier answer and each probe
    /// costs a round trip. Being wrong about that -- a reused pid, a stale snapshot --
    /// changes who is asked first and nothing else, which is why a pid comparison is
    /// enough here where identity would not be.
    /// </summary>
    internal static IReadOnlyList<NavisworksHostInfo> SelectProvableCandidates(
        IReadOnlyList<NavisworksHostInfo> hosts,
        string expectedTitle,
        IReadOnlyList<NavisworksHostInfo> hostsBefore,
        int? excludedProcessId)
    {
        if (string.IsNullOrWhiteSpace(expectedTitle))
            return Array.Empty<NavisworksHostInfo>();

        hosts ??= Array.Empty<NavisworksHostInfo>();
        var beforePids = new HashSet<int>((hostsBefore ?? Array.Empty<NavisworksHostInfo>())
            .Select(host => host.Pid));

        return Selectable(hosts, excludedProcessId)
            .Where(host => HostDocumentMatches(host, expectedTitle))
            .OrderBy(host => beforePids.Contains(host.Pid) ? 1 : 0)
            .ThenByDescending(host => host.StartedAtUtc)
            .ToList();
    }

    private static List<NavisworksHostInfo> Selectable(
        IReadOnlyList<NavisworksHostInfo> hosts,
        int? excludedProcessId) =>
        excludedProcessId.HasValue
            ? hosts.Where(host => host.Pid != excludedProcessId.Value).ToList()
            : hosts.ToList();

    /// <summary>
    /// What one launch has already asked each instance, so the startup wait does not
    /// re-ask on every poll.
    ///
    /// A proof is final: a host confirmed to hold the requested path is not going to stop
    /// holding it in a way this launch should care about. A refusal is not, and that
    /// asymmetry is the point. Two documents can share a file name, so an instance that
    /// answers with a different path may be a stranger -- or may be part-way through being
    /// handed the requested one, because Roamer changes a host's document while that host
    /// keeps the same title. Caching a refusal for the whole wait would turn the second
    /// case into a `host_timeout` over a document that finished loading a moment later.
    /// Refusals therefore expire, and the instance is asked again.
    /// </summary>
    internal sealed class HandoffProofLedger
    {
        private static readonly TimeSpan DefaultRetryRefusalsAfter = TimeSpan.FromSeconds(2);

        private readonly TimeSpan _retryRefusalsAfter;
        private readonly Dictionary<string, DateTimeOffset> _refusedAtUtc =
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _proven = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal HandoffProofLedger(TimeSpan? retryRefusalsAfter = null)
        {
            // Two seconds against a 250 ms poll: a refused instance is asked roughly once
            // per eight polls instead of every one, which is the difference between a
            // couple of round trips during a normal startup and several hundred.
            _retryRefusalsAfter = retryRefusalsAfter ?? DefaultRetryRefusalsAfter;
        }

        internal bool IsProven(string instanceId) => _proven.Contains(instanceId ?? string.Empty);

        internal bool ShouldProbe(string instanceId, DateTimeOffset nowUtc) =>
            !_refusedAtUtc.TryGetValue(instanceId ?? string.Empty, out var refusedAt) ||
            nowUtc - refusedAt >= _retryRefusalsAfter;

        internal void Record(string instanceId, bool proven, DateTimeOffset nowUtc)
        {
            var key = instanceId ?? string.Empty;
            if (proven)
            {
                _proven.Add(key);
                _refusedAtUtc.Remove(key);
                return;
            }

            _refusedAtUtc[key] = nowUtc;
        }
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
