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
    [Description("Isolates model items whose world bounding boxes intersect an explicit oriented box. Readable outside parent boxes prune their whole subtrees because Navisworks parent bounds include children. Non-geometry containers and empty leaves without valid bounds are preserved safely; genuine geometry classification errors reject apply. It does not read or change the current Section Box and does not depend on selection or match handles. Defaults to dry-run; apply is rejected if bounded traversal or visibility planning times out or is truncated. For exact replay, store the captured box, maxScannedItems, and maxDurationSeconds as literal arguments, remove get_current_section_box, and never substitute $stepResult or runtime handles.")]
    public Task<IsolateByBoxResponse> IsolateByBox(
        [Description("Required canonical box returned by get_current_section_box: formatVersion 1, document_global coordinates, document units, absolute center, positive halfExtents, and three right-handed orthonormal world axes. Exact-replay scenarios must store this object literally.")] SectionBoxGeometry box,
        [Description("False previews the visibility plan; true applies it only after a complete traversal with zero genuine geometry classification errors. Non-geometry containers and empty leaves without valid bounds remain visible without counting as errors. Default is false/dry-run.")] bool apply = false,
        [Description("Required deterministic traversal limit for Scenario Library replay. Default and maximum are 500000; values outside 1..500000 are rejected. apply=true is rejected if this limit truncates traversal.")] int maxScannedItems = SectionBoxIsolationLimits.DefaultMaxScannedItems,
        [Description("Required bounded duration for traversal, bounding-box classification, and visibility planning. Default 60 seconds, maximum 480; values outside 1..480 are rejected. Raise it explicitly on loaded workstations. Navisworks API access remains synchronous on the UI thread. Exact replay stores this value literally.")] int maxDurationSeconds = SectionBoxIsolationLimits.DefaultMaxDurationSeconds,
        [Description("Maximum visibility-change preview rows, including items that would be revealed or newly hidden. Default 10, maximum 50.")] int previewLimit = 10,
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
                MaxDurationSeconds = maxDurationSeconds,
                PreviewLimit = previewLimit,
            },
            cancellationToken,
            CreateTarget(instanceId, navisworksVersion));
    }
}
