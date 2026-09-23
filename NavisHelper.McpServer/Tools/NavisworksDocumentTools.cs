using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksDocumentTools : NavisworksToolBase
{
    public NavisworksDocumentTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Saves the active Navisworks document to its current path. This writes model changes immediately and runs on the Navisworks UI thread.")]
    public Task<SaveDocumentResponse> SaveDocument(
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SaveDocumentAsync(new SaveDocumentRequest(), cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Saves the active Navisworks document to a specified .nwd or .nwf path. Existing files are protected unless overwrite=true. Use .nwd to produce a self-contained deliverable with geometry.")]
    public Task<SaveDocumentResponse> SaveDocumentAs(
        [Description("Absolute target path with a .nwd or .nwf extension.")] string path,
        [Description("Allow replacing an existing target file. Default is false.")] bool overwrite = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SaveDocumentAsAsync(new SaveDocumentAsRequest
        {
            Path = path,
            Overwrite = overwrite,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

}
