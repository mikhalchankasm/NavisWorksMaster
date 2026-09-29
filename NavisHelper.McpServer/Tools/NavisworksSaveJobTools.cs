using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksSaveJobTools : NavisworksToolBase
{
    public NavisworksSaveJobTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Starts an asynchronous save of the active Navisworks document to its current path and returns immediately with an operationId. The save itself keeps running on the Navisworks UI thread; poll save_document_status, which stays answerable while the save runs. Only one save job may run at a time. For a synchronous save use save_document.")]
    [ToolCapabilities(ToolEffects.Document | ToolEffects.Files, RequiresHost = true, RequiresDocument = true)]
    public Task<StartSaveDocumentResponse> StartSaveDocument(
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.StartSaveDocumentAsync(new StartSaveDocumentRequest(), cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Returns running, completed, or failed for a save job started by start_save_document, with elapsed milliseconds and the error when it failed. Read-only, bypasses host_busy, and answered without the Navisworks UI thread, so it replies while the save is still running.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = false)]
    public Task<SaveDocumentStatusResponse> SaveDocumentStatus(
        [Description("Operation id returned by start_save_document.")] string operationId,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SaveDocumentStatusAsync(new SaveDocumentStatusRequest { OperationId = operationId }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
