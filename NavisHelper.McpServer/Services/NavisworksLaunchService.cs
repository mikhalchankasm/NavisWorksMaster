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
        var beforePids = new HashSet<int>(hostsBefore.Select(host => host.Pid));

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
        var startupStopwatch = Stopwatch.StartNew();
        using var process = _processLauncher.Start(startInfoBuild.StartInfo);
        response.ProcessCreated = true;
        response.Started = true;
        response.ProcessId = process.Id;

        NavisworksStartupMonitorResult startupResult;
        if (waitForHost)
        {
            startupResult = await _startupMonitor.WaitForHostAsync(
                process,
                excludedProcessId => FindHost(
                    version,
                    effectiveFilePath,
                    response.ProcessId,
                    beforePids,
                    excludedProcessId),
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

        // SelectHost's last resort matches on document title without excluding hosts
        // that were already running, so a launch can report a pre-existing host beside
        // the pid it just created. That fallback stays -- it is the only thing that
        // finds the host when Navisworks serves the file from another process -- but
        // the caller is told when it fired rather than left to notice the mismatch.
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
    internal static async Task<NavisworksHostInfo> ResolveProvenAttachTargetAsync(
        IReadOnlyList<NavisworksHostInfo> candidates,
        string requestedFilePath,
        StartNavisworksResponse response,
        Func<NavisworksHostInfo, CancellationToken, Task<HostStatusResponse>> probeHostStatusAsync,
        CancellationToken cancellationToken)
    {
        if (candidates == null || candidates.Count == 0)
            return null;

        foreach (var candidate in candidates)
        {
            HostStatusResponse status = null;
            try
            {
                status = await probeHostStatusAsync(candidate, cancellationToken).ConfigureAwait(false);
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

    private NavisworksHostInfo FindHost(
        string navisworksVersion,
        string filePath,
        int? processId,
        HashSet<int> beforePids,
        int? excludedProcessId)
    {
        var expectedTitle = string.IsNullOrWhiteSpace(filePath) ? string.Empty : Path.GetFileName(filePath);
        var hosts = _hostBridgeClient.ListNavisworksHosts().Hosts
            .Where(host => string.Equals(host.NavisworksVersion, navisworksVersion, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return SelectHost(hosts, expectedTitle, processId, beforePids, excludedProcessId);
    }

    internal static NavisworksHostInfo SelectHost(
        IReadOnlyList<NavisworksHostInfo> hosts,
        string expectedTitle,
        int? processId,
        HashSet<int> beforePids,
        int? excludedProcessId)
    {
        hosts ??= Array.Empty<NavisworksHostInfo>();
        beforePids ??= new HashSet<int>();
        var candidates = excludedProcessId.HasValue
            ? hosts.Where(host => host.Pid != excludedProcessId.Value).ToList()
            : hosts.ToList();

        if (processId.HasValue)
        {
            var byPid = candidates.FirstOrDefault(host => host.Pid == processId.Value);
            if (byPid != null && HostDocumentMatches(byPid, expectedTitle))
                return byPid;
        }

        var newHost = candidates
            .Where(host => !beforePids.Contains(host.Pid))
            .Where(host => HostDocumentMatches(host, expectedTitle))
            .OrderByDescending(host => host.StartedAtUtc)
            .FirstOrDefault();
        if (newHost != null)
            return newHost;

        if (string.IsNullOrWhiteSpace(expectedTitle))
            return null;

        return candidates
            .Where(host => HostDocumentMatches(host, expectedTitle))
            .OrderByDescending(host => host.StartedAtUtc)
            .FirstOrDefault();
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
