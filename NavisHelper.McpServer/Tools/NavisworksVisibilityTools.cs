using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksVisibilityTools : NavisworksToolBase
{
    public NavisworksVisibilityTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Hides every item except the current Navisworks selection. Dry-run returns affected root/source-file scope summaries before applying.")]
    [ToolCapabilities(ToolEffects.View, RequiresHost = true, RequiresDocument = true)]
    public Task<HideUnselectedResponse> HideUnselected(
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Maximum affected item preview rows to return. Default is 10, maximum is 50.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.HideUnselectedAsync(new HideUnselectedRequest
        {
            Apply = apply,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Hides the current Navisworks selection. Dry-run returns affected root/source-file scope summaries before applying.")]
    [ToolCapabilities(ToolEffects.View, RequiresHost = true, RequiresDocument = true)]
    public Task<HideSelectedResponse> HideSelected(
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Maximum affected item preview rows to return. Default is 10, maximum is 50.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.HideSelectedAsync(new HideSelectedRequest
        {
            Apply = apply,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Shows the current Navisworks selection if it is hidden. Dry-run returns affected root/source-file scope summaries before applying.")]
    [ToolCapabilities(ToolEffects.View, RequiresHost = true, RequiresDocument = true)]
    public Task<UnhideSelectedResponse> UnhideSelected(
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Maximum affected item preview rows to return. Default is 10, maximum is 50.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.UnhideSelectedAsync(new UnhideSelectedRequest
        {
            Apply = apply,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Makes the current Navisworks selection actually visible by unhiding selected items and any hidden ancestors needed for visibility. Dry-run returns affected root/source-file scope summaries before applying.")]
    [ToolCapabilities(ToolEffects.View, RequiresHost = true, RequiresDocument = true)]
    public Task<RevealSelectedResponse> RevealSelected(
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Maximum affected item preview rows to return. Default is 10, maximum is 50.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.RevealSelectedAsync(new RevealSelectedRequest
        {
            Apply = apply,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Shows all hidden items and then hides everything except the current Navisworks selection. Dry-run returns root/source-file scope summaries for the re-hide portion; review previouslyHiddenItemCount separately before applying.")]
    [ToolCapabilities(ToolEffects.View, RequiresHost = true, RequiresDocument = true)]
    public Task<IsolateSelectedResponse> IsolateSelected(
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Maximum affected item preview rows to return. Default is 10, maximum is 50.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.IsolateSelectedAsync(new IsolateSelectedRequest
        {
            Apply = apply,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Shows all currently hidden Navisworks items. Dry-run returns affected root/source-file scope summaries before applying.")]
    [ToolCapabilities(ToolEffects.View, RequiresHost = true, RequiresDocument = true)]
    public Task<ShowAllResponse> ShowAll(
        [Description("False previews the action, true applies it. Default is false/dry-run.")] bool apply = false,
        [Description("Maximum affected item preview rows to return. Default is 10, maximum is 50.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ShowAllAsync(new ShowAllRequest
        {
            Apply = apply,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

}
