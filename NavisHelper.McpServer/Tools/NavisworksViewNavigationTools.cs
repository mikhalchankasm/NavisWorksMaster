using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksViewNavigationTools : NavisworksToolBase
{
    public NavisworksViewNavigationTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Zooms the current Navisworks view to the bounding box of the current selection.")]
    public Task<ZoomToSelectionResponse> ZoomToSelection(
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ZoomToSelectionAsync(new ZoomToSelectionRequest(), cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Centers the current Navisworks view on the current selection without doing a bounding-box zoom.")]
    public Task<FocusOnSelectionResponse> FocusOnSelection(
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.FocusOnSelectionAsync(new FocusOnSelectionRequest(), cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Fits the current Navisworks view to the full model.")]
    public Task<FitAllResponse> FitAll(
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.FitAllAsync(new FitAllRequest(), cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

}
