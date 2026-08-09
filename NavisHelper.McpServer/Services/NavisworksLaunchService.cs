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
    {
        _hostBridgeClient = hostBridgeClient;
        _callLogger = callLogger;
        _recentFilesService = recentFilesService;
        _startInfoFactory = new NavisworksProcessStartInfoFactory();
        _processLauncher = new SystemNavisworksProcessLauncher();
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

        startupStopwatch.Stop();
        stopwatch.Stop();
        response.StartupElapsedMs = startupStopwatch.ElapsedMilliseconds;
        response.ElapsedMs = stopwatch.ElapsedMilliseconds;
        response.ElapsedHuman = ElapsedTimeFormatter.Format(response.ElapsedMs);
        _callLogger.LogStartNavisworks(response, startInfoBuild.EnvironmentFacts);

        return response;
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
