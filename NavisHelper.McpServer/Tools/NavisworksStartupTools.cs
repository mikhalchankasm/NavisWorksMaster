using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksStartupTools : NavisworksToolBase
{
    public NavisworksStartupTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Lists recent Navisworks Manage model files from the current Windows user's HKCU Recent File List registry entries. Use this before opening the last/previous Navisworks file. Does not require Navisworks to be running.")]
    public NavisworksRecentFilesResponse ListRecentNavisworksFiles(
        [Description("Optional Navisworks version: 2024, 2025, 2026, or 2027. Empty means all supported versions sorted by LastOpened.")] string navisworksVersion = "",
        [Description("Maximum files to return. Default is 10, maximum is 100.")] int limit = 10,
        [Description("When true, return only recent files that still exist on disk. Default is true.")] bool existingOnly = true)
    {
        return _recentFilesService.ListRecentFiles(navisworksVersion, limit, existingOnly);
    }

    [McpServerTool]
    [Description("Starts Navisworks Manage. Pass filePath to open a specific .nwd/.nwf/.nwc, or set openLatestRecentFile=true to open the latest existing file from Navisworks Recent File List. When waiting, distinguishes host_ready, process_exited, and host_timeout; returns instanceId only when the MCP host is ready.")]
    public Task<StartNavisworksResponse> StartNavisworks(
        [Description("Optional Navisworks version: 2024, 2025, 2026, or 2027. Empty means latest installed version, or the recent file's version when openLatestRecentFile=true.")] string navisworksVersion = "",
        [Description("Optional explicit .nwd/.nwf/.nwc file path to open. Leave empty when using openLatestRecentFile=true.")] string filePath = "",
        [Description("Open the latest existing recent file from HKCU Recent File List when filePath is empty. Default is false.")] bool openLatestRecentFile = false,
        [Description("Wait until the NavisHelper in-process MCP host is discoverable after launch. Default is true.")] bool waitForHost = true,
        [Description("Seconds to wait for the NavisHelper MCP host discovery record. Default is 90, maximum is 300.")] int waitTimeoutSeconds = 90,
        CancellationToken cancellationToken = default)
    {
        return _launchService.StartAsync(
            navisworksVersion,
            filePath,
            openLatestRecentFile,
            waitForHost,
            waitTimeoutSeconds,
            cancellationToken);
    }

    [McpServerTool]
    [Description("Convenience tool for the user request: 'start Navisworks and open the last file'. It opens the latest existing file from Navisworks Recent File List and waits for the NavisHelper MCP host by default, reporting an early process exit without waiting for the full timeout.")]
    public Task<StartNavisworksResponse> OpenLatestNavisworksFile(
        [Description("Optional Navisworks version: 2024, 2025, 2026, or 2027. Empty means all supported versions and opens the globally latest recent file.")] string navisworksVersion = "",
        [Description("Wait until the NavisHelper in-process MCP host is discoverable after launch. Default is true.")] bool waitForHost = true,
        [Description("Seconds to wait for the NavisHelper MCP host discovery record. Default is 90, maximum is 300.")] int waitTimeoutSeconds = 90,
        CancellationToken cancellationToken = default)
    {
        return _launchService.StartAsync(
            navisworksVersion,
            string.Empty,
            openLatestRecentFile: true,
            waitForHost,
            waitTimeoutSeconds,
            cancellationToken);
    }

    [McpServerTool]
    [Description("Previews or closes one targeted Navisworks Manage instance. Modes: prompt requests normal exit and may show the native save dialog; save saves first and exits only after a verified save; discard permanently drops unsaved changes before exit. Defaults to preview and requires apply=true plus confirmClose=true. Target by instanceId, or by navisworksVersion only when exactly one matching host is running.")]
    public Task<CloseNavisworksResponse> CloseNavisworks(
        [Description("Close mode: prompt (native Navisworks behavior), save, or discard. Default is prompt.")] string mode = NavisworksCloseModes.Prompt,
        [Description("Optional absolute .nwd/.nwf path for mode=save. Leave empty to save to the current document path.")] string savePath = "",
        [Description("Allow replacing savePath when mode=save. Default is false.")] bool overwrite = false,
        [Description("False previews the close operation; true prepares the document and schedules exit. Default is false.")] bool apply = false,
        [Description("Required together with apply=true for every close mode, especially discard.")] bool confirmClose = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts. Recommended when several versions/instances are running.")] string instanceId = "",
        [Description("Optional Navisworks version: 2024, 2025, 2026, or 2027. Valid only when exactly one running host matches.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.CloseNavisworksAsync(
            new CloseNavisworksRequest
            {
                Mode = mode,
                SavePath = savePath,
                Overwrite = overwrite,
                Apply = apply,
                ConfirmClose = confirmClose,
            },
            cancellationToken,
            CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Starts an optional cross-tool MCP task timer for a larger user-visible workflow that spans multiple tool calls. Call this at the beginning of a user task only when an explicit end-to-end task timer is useful, then call mcp_task_timer_finish before the final answer. Individual MCP tool calls also return automatic navishelper_timing in their primary JSON result.")]
    public McpTaskTimerStartResponse McpTaskTimerStart(
        [Description("Optional short task name, for example 'clash report' or 'open latest Navisworks file'.")] string taskName = "")
    {
        return _taskTimerService.Start(taskName);
    }

    [McpServerTool]
    [Description("Finishes a cross-tool MCP task timer and returns elapsedMs, elapsedHuman, shouldReportToUser, and userMessage. If shouldReportToUser=true, include userMessage in the final answer to the user.")]
    public McpTaskTimerFinishResponse McpTaskTimerFinish(
        [Description("Timer id returned by mcp_task_timer_start.")] string timerId,
        [Description("Optional task name override for the final timer result.")] string taskName = "")
    {
        return _taskTimerService.Finish(timerId, taskName);
    }
}
