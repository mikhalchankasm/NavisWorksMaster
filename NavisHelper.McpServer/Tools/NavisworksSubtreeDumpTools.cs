using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksSubtreeDumpTools : NavisworksToolBase
{
    public NavisworksSubtreeDumpTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Synchronously streams item names from one small root .rvm/.dwg subtree to a CSV or JSONL file. Hard-limited to avoid long Navisworks UI hangs; for large roots use start_subtree_names_dump plus dump_subtree_names_status.")]
    public Task<DumpSubtreeNamesResponse> DumpSubtreeNames(
        [Description("Displayed root item name or root source filename, for example example-model.rvm. Exact match only.")] string rootName,
        [Description("Output CSV/JSONL file path on this machine.")] string outputPath,
        [Description("Output format: csv or jsonl. Default is csv.")] string format = "csv",
        [Description("Optional exact Source File value or filename. Use when rootName is empty or ambiguous.")] string sourceFile = "",
        [Description("Include full Navisworks item path in each row. Default is true. For large name/position lookup dumps, pass false for much better throughput.")] bool includePath = true,
        [Description("Include inherited Source File in each row. Default is false for speed.")] bool includeSourceFile = false,
        [Description("Include hidden items. Default is true. When false, hidden item rows are skipped but descendants are still traversed.")] bool includeHidden = true,
        [Description("Overwrite outputPath if it already exists. Default is false.")] bool overwrite = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.DumpSubtreeNamesAsync(new DumpSubtreeNamesRequest
        {
            RootName = rootName,
            SourceFile = sourceFile,
            OutputPath = outputPath,
            Format = format,
            IncludePath = includePath,
            IncludeSourceFile = includeSourceFile,
            IncludeHidden = includeHidden,
            Overwrite = overwrite,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Starts a chunked CSV/JSONL dump job for all item names under one root .rvm/.dwg subtree. This returns quickly with jobId and instanceId and writes to outputPath.partial while running. Poll/cancel using the same instanceId. On success it atomically replaces outputPath; on failed/cancelled it removes the partial file.")]
    public Task<DumpSubtreeNamesJobStatusResponse> StartSubtreeNamesDump(
        [Description("Displayed root item name or root source filename, for example example-model.rvm. Exact match only.")] string rootName,
        [Description("Final output CSV/JSONL file path on this machine. The running job writes to this path plus .partial first.")] string outputPath,
        [Description("Output format: csv or jsonl. Default is csv.")] string format = "csv",
        [Description("Optional exact Source File value or filename. Use when rootName is empty or ambiguous.")] string sourceFile = "",
        [Description("Include full Navisworks item path in each row. Default is true. For large name/position lookup dumps, pass false for much better throughput.")] bool includePath = true,
        [Description("Include inherited Source File in each row. Default is false for speed.")] bool includeSourceFile = false,
        [Description("Include hidden items. Default is true. When false, hidden items are skipped but descendants are still traversed.")] bool includeHidden = true,
        [Description("Overwrite outputPath if it already exists and overwrite any stale .partial file. Default is false.")] bool overwrite = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.StartSubtreeNamesDumpAsync(new DumpSubtreeNamesRequest
        {
            RootName = rootName,
            SourceFile = sourceFile,
            OutputPath = outputPath,
            Format = format,
            IncludePath = includePath,
            IncludeSourceFile = includeSourceFile,
            IncludeHidden = includeHidden,
            Overwrite = overwrite,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Advances and returns status for a subtree name dump job. Poll this until state is done/failed/cancelled using the same instanceId returned by start_subtree_names_dump. Each poll processes a bounded chunk on the Navisworks UI thread; keep maxElapsedMs near the 500 ms default when a user is actively working.")]
    public Task<DumpSubtreeNamesJobStatusResponse> DumpSubtreeNamesStatus(
        [Description("Job id returned by start_subtree_names_dump.")] string jobId,
        [Description("Maximum items to process in this poll. Default 1000, maximum 10000.")] int maxItemsPerPoll = 1000,
        [Description("Maximum host-side processing time for this poll in milliseconds. Default 500, maximum 3000.")] int maxElapsedMs = 500,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.DumpSubtreeNamesStatusAsync(new DumpSubtreeNamesStatusRequest
        {
            JobId = jobId,
            MaxItemsPerPoll = maxItemsPerPoll,
            MaxElapsedMs = maxElapsedMs,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Cancels a running subtree name dump job on the same instanceId returned by start_subtree_names_dump, closes its writer, clears queued ModelItem references, and removes its .partial file.")]
    public Task<DumpSubtreeNamesJobStatusResponse> CancelSubtreeNamesDump(
        [Description("Job id returned by start_subtree_names_dump.")] string jobId,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.CancelSubtreeNamesDumpAsync(new CancelSubtreeNamesDumpRequest
        {
            JobId = jobId,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

}
