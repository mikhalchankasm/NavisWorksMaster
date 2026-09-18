using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksGeometryTools : NavisworksToolBase
{
    public NavisworksGeometryTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Exports native tessellated triangles with exact node paths, fragment identities and transforms. No smoothing or decimation. Read-only model access; apply=false counts and validates without writing. Limits fail rather than publishing partial meshes.")]
    public Task<ExportSelectionGeometryResponse> ExportSelectionGeometry(
        [Description("Absolute output path on the Navisworks host, with extension matching format.")] string outputPath,
        [Description("obj, jsonl, ply or stl. OBJ/JSONL/PLY retain node and fragment metadata reliably.")] string format = "obj",
        [Description("current_selection or match_handle. Includes descendant geometry exactly once.")] string scope = "current_selection",
        [Description("Required with match_handle scope; use the issuing instanceId.")] List<string> matchHandles = null,
        [Description("world (default, document units), or item_local (first owned fragment frame per geometry item; OutputToWorld included).")] string coordinateSpace = "world",
        [Description("OBJ group granularity: item or fragment. Fragment metadata is always retained.")] string groupBy = "fragment",
        [Description("Maximum input and geometry-owner items, 1..10000.")] int itemLimit = 1000,
        [Description("Maximum triangles, 1..5000000. Default 1000000. Exceeding limits fails atomically.")] int maxTriangles = 1000000,
        [Description("Default false performs a dry-run without files. True writes atomically.")] bool apply = false,
        [Description("Allow atomic replacement of an existing output file.")] bool overwrite = false,
        [Description("Explicit host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional version selector when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ExportSelectionGeometryAsync(new ExportSelectionGeometryRequest
        {
            OutputPath=outputPath, Format=format, Scope=scope, MatchHandles=matchHandles ?? new List<string>(),
            CoordinateSpace=coordinateSpace, GroupBy=groupBy, ItemLimit=itemLimit, MaxTriangles=maxTriangles,
            Apply=apply, Overwrite=overwrite,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
