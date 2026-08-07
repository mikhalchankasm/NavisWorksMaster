using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksSectionBoxTools : NavisworksToolBase
{
    public NavisworksSectionBoxTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Reads the enabled Section/Clip Box from the active Navisworks view without changing clipping, viewpoint, selection, or visibility. Returns canonical typed document-global geometry for independent replay. Workflow: capture once, pass the returned box literally to isolate_by_box for preview/apply, and omit this capture step when saving exact replay.")]
    public Task<GetCurrentSectionBoxResponse> GetCurrentSectionBox(
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.GetCurrentSectionBoxAsync(
            new GetCurrentSectionBoxRequest(),
            cancellationToken,
            CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Isolates model items whose world bounding boxes intersect an explicit oriented box. It does not read or change the current Section Box and does not depend on selection or match handles. Defaults to dry-run; apply is rejected if traversal times out, is truncated, or any bounding box cannot be classified. For exact replay, store the captured box as a literal argument, remove get_current_section_box, and never substitute $stepResult or runtime handles.")]
    public Task<IsolateByBoxResponse> IsolateByBox(
        [Description("Required canonical box returned by get_current_section_box: formatVersion 1, document_global coordinates, document units, absolute center, positive halfExtents, and three right-handed orthonormal world axes. Exact-replay scenarios must store this object literally.")] SectionBoxGeometry box,
        [Description("False previews the complete visibility plan; true applies it only when classification is complete. Default is false/dry-run.")] bool apply = false,
        [Description("Maximum model items to scan. Default 100000, maximum 500000. apply=true is rejected if this limit truncates traversal.")] int maxScannedItems = 100000,
        [Description("Maximum hidden-item preview rows. Default 10, maximum 50.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.IsolateByBoxAsync(
            new IsolateByBoxRequest
            {
                Box = box,
                Apply = apply,
                MaxScannedItems = maxScannedItems,
                PreviewLimit = previewLimit,
            },
            cancellationToken,
            CreateTarget(instanceId, navisworksVersion));
    }
}
