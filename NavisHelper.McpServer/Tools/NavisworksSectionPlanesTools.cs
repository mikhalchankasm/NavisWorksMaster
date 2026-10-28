using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksSectionPlanesTools : NavisworksToolBase
{
    public NavisworksSectionPlanesTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Reads, sets or clears section planes in the active Navisworks view. The normal points at the side cut away; positions and Distance use document units, offsets and moves use mm. Set and clear default to dry-run; apply=true writes and reads back the result. Section-box tools remain separate.")]
    [ToolCapabilities(ToolEffects.View, RequiresHost = true, RequiresDocument = true)]
    public Task<SectionPlanesResponse> SectionPlanes(
        [Description("get (default) reads only; set updates indexed planes; clear disables sectioning.")] string operation = "get",
        [Description("Plane specs, index 1-6: direction (top/bottom/north/south/east/west) or normal plus exactly one point or fromSelection (max/min projection along the normal). offsetMm offsets that position. Alternatively moveByMm or enabled updates an existing plane.")] List<SectionPlaneSpec> planes = null,
        [Description("Optional linked plane state for set; omitted preserves the current state.")] bool? linked = null,
        [Description("False previews writes; true applies set/clear. get never writes.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SectionPlanesAsync(new SectionPlanesRequest
        {
            Operation = operation, Planes = planes ?? new List<SectionPlaneSpec>(), Linked = linked, Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
