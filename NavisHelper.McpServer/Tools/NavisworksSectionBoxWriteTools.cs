using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksSectionBoxWriteTools : NavisworksToolBase
{
    public NavisworksSectionBoxWriteTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Sets an explicit section box or clears sectioning in the active Navisworks view. Accepts the canonical box from get_current_section_box or axis-aligned min/max in document units. Dry-run by default; apply=true writes and reads back the result.")]
    [ToolCapabilities(ToolEffects.View, RequiresHost = true, RequiresDocument = true)]
    public Task<SectionBoxResponse> SectionBox(
        [Description("set (default) sets an explicit box; clear disables sectioning and takes no box, min or max.")] string operation = "set",
        [Description("Canonical box from get_current_section_box, with matching documentUnits. Specify box or both min and max.")] SectionBoxGeometry box = null,
        [Description("Axis-aligned minimum X/Y/Z in document units; requires max and no box.")] BoxVector3 min = null,
        [Description("Axis-aligned maximum X/Y/Z, strictly greater than min on every axis.")] BoxVector3 max = null,
        [Description("False previews the planned state; true writes and reads back.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SectionBoxAsync(new SectionBoxRequest
        {
            Operation = operation, Box = box, Min = min, Max = max, Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
