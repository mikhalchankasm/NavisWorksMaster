using System.Diagnostics;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed class NavisworksLaunchService
{
    private static readonly string[] SupportedVersions = { "2027", "2026", "2025", "2024" };
    private readonly HostBridgeClient _hostBridgeClient;
    private readonly McpCallLogger _callLogger;
    private readonly NavisworksRecentFilesService _recentFilesService;

    public NavisworksLaunchService(
        HostBridgeClient hostBridgeClient,
        McpCallLogger callLogger,
        NavisworksRecentFilesService recentFilesService)
    {
        _hostBridgeClient = hostBridgeClient;
        _callLogger = callLogger;
        _recentFilesService = recentFilesService;
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

        using var process = StartRoamer(roamerPath, effectiveFilePath);
        response.Started = true;
        response.ProcessId = process == null ? null : process.Id;

        if (waitForHost)
        {
            var host = await WaitForHostAsync(
                version,
                effectiveFilePath,
                response.ProcessId,
                beforePids,
                waitTimeoutSeconds,
                cancellationToken).ConfigureAwait(false);

            if (host != null)
            {
                response.HostReady = true;
                response.Host = host;
            }
            else
            {
                response.Warnings.Add("Navisworks was started, but the MCP host did not appear before the wait timeout.");
            }
        }

        stopwatch.Stop();
        response.ElapsedMs = stopwatch.ElapsedMilliseconds;
        response.ElapsedHuman = ElapsedTimeFormatter.Format(response.ElapsedMs);
        response.Message = response.HostReady
            ? "Navisworks started and MCP host is ready."
            : "Navisworks start command was issued.";

        _callLogger.Log(new
        {
            event_name = "start_navisworks",
            timestamp_utc = DateTime.UtcNow,
            status = "ok",
            elapsed_ms = response.ElapsedMs,
            elapsed_human = response.ElapsedHuman,
            navisworks_version = response.NavisworksVersion,
            roamer_path = response.RoamerPath,
            file_path = response.FilePath,
            opened_recent_file = response.OpenedRecentFile,
            wait_for_host = waitForHost,
            host_ready = response.HostReady,
            process_id = response.ProcessId,
            instance_id = response.Host == null ? null : response.Host.InstanceId,
        });

        return response;
    }

    private static Process StartRoamer(string roamerPath, string filePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = roamerPath,
            UseShellExecute = false,
        };

        if (!string.IsNullOrWhiteSpace(filePath))
            startInfo.ArgumentList.Add(filePath);

        return Process.Start(startInfo);
    }

    private async Task<NavisworksHostInfo> WaitForHostAsync(
        string navisworksVersion,
        string filePath,
        int? processId,
        HashSet<int> beforePids,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (timeoutSeconds < 1)
            timeoutSeconds = 1;
        if (timeoutSeconds > 300)
            timeoutSeconds = 300;

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var expectedTitle = string.IsNullOrWhiteSpace(filePath) ? string.Empty : Path.GetFileName(filePath);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hosts = _hostBridgeClient.ListNavisworksHosts().Hosts
                .Where(host => string.Equals(host.NavisworksVersion, navisworksVersion, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (processId.HasValue)
            {
                var byPid = hosts.FirstOrDefault(host => host.Pid == processId.Value);
                if (byPid != null && HostDocumentMatches(byPid, expectedTitle))
                    return byPid;
            }

            var newHost = hosts
                .Where(host => !beforePids.Contains(host.Pid))
                .Where(host => HostDocumentMatches(host, expectedTitle))
                .OrderByDescending(host => host.StartedAtUtc)
                .FirstOrDefault();
            if (newHost != null)
                return newHost;

            if (!string.IsNullOrWhiteSpace(expectedTitle))
            {
                var byTitle = hosts
                    .Where(host => HostDocumentMatches(host, expectedTitle))
                    .OrderByDescending(host => host.StartedAtUtc)
                    .FirstOrDefault();
                if (byTitle != null)
                    return byTitle;
            }

            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

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
