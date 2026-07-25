using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksToolContext
{
    public NavisworksToolContext(
        HostBridgeClient hostBridgeClient,
        NavisworksRecentFilesService recentFilesService,
        NavisworksLaunchService launchService,
        McpTaskTimerService taskTimerService,
        ScenarioLibraryService scenarioLibraryService)
    {
        HostBridgeClient = hostBridgeClient;
        RecentFilesService = recentFilesService;
        LaunchService = launchService;
        TaskTimerService = taskTimerService;
        ScenarioLibraryService = scenarioLibraryService;
    }

    public HostBridgeClient HostBridgeClient { get; }
    public NavisworksRecentFilesService RecentFilesService { get; }
    public NavisworksLaunchService LaunchService { get; }
    public McpTaskTimerService TaskTimerService { get; }
    public ScenarioLibraryService ScenarioLibraryService { get; }
}

internal abstract class NavisworksToolBase
{
    protected readonly HostBridgeClient _hostBridgeClient;
    protected readonly NavisworksRecentFilesService _recentFilesService;
    protected readonly NavisworksLaunchService _launchService;
    protected readonly McpTaskTimerService _taskTimerService;
    protected readonly ScenarioLibraryService _scenarioLibraryService;

    protected NavisworksToolBase(NavisworksToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _hostBridgeClient = context.HostBridgeClient;
        _recentFilesService = context.RecentFilesService;
        _launchService = context.LaunchService;
        _taskTimerService = context.TaskTimerService;
        _scenarioLibraryService = context.ScenarioLibraryService;
    }

    protected static HostTargetOptions CreateTarget(
        string instanceId,
        string navisworksVersion)
    {
        var target = new HostTargetOptions
        {
            InstanceId = instanceId ?? string.Empty,
            NavisworksVersion = navisworksVersion ?? string.Empty,
        };

        return target.HasExplicitTarget ? target : null;
    }
}
