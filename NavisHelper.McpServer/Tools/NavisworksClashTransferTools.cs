using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashTransferTools : NavisworksToolBase
{
    private const int BackgroundHostTimeoutMs = 1800000;
    private readonly HostBackgroundJobStore _jobs;

    public NavisworksClashTransferTools(NavisworksToolContext context, HostBackgroundJobStore jobs) : base(context) => _jobs = jobs;

    [McpServerTool]
    [Description("Exports versioned portable JSON test definitions; dry-run, writes only on apply=true. Sets by exact full tree path; roots by rootName/sourceFile(+childName). No results, viewpoints, comments or history.")]
    [ToolCapabilities(ToolEffects.Files, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashTestsExportResponse> ClashTestsExport(
        [Description("Exact names; union with testHandles/namePrefix; empty scope=all tests.")] List<string> testNames = null,
        [Description("clash_list_tests handles; source-document diagnostics, stale after mutations.")] List<string> testHandles = null,
        [Description("Optional exact test-name prefix.")] string namePrefix = "",
        [Description("Absolute JSON path; dry-run returns calculatedOutputPath, never creates file.")] string outputPath = "",
        [Description("Portable format. Must be navishelper_json.")] string format = ClashTransferConstants.JsonFormat,
        [Description("Allow replacement of an existing output file. Default false.")] bool overwriteExisting = false,
        [Description("False previews only; true writes .partial, atomically completes, verifies size/readability/SHA-256.")] bool apply = false,
        [Description("Run in background; poll background_job_status.")] bool background = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        var request = new ClashTestsExportRequest
        {
            TestNames = testNames ?? new List<string>(),
            TestHandles = testHandles ?? new List<string>(),
            NamePrefix = namePrefix,
            OutputPath = outputPath,
            Format = format,
            OverwriteExisting = overwriteExisting,
            Apply = apply,
        };
        var target = CreateTarget(instanceId, navisworksVersion);
        if (background)
        {
            var started = _jobs.Start("clash_tests_export", async token =>
                (object)await _hostBridgeClient.ClashTestsExportAsync(request, token, target, BackgroundHostTimeoutMs));
            return Task.FromResult(new ClashTestsExportResponse { BackgroundOperationId = started.OperationId, Applied = false });
        }
        return _hostBridgeClient.ClashTestsExportAsync(request, cancellationToken, target);
    }

    [McpServerTool]
    [Description("Imports nw-exchange-12.0 batchtest XML (exact lcop_selection_set_tree/full/path locators) through a transfer plan into clash_tests_from_sets. Rejects DTD/external entities, unsupported locators, oversized input. Dry-run; never runs tests or imports results.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashBatchtestImportResponse> ClashBatchtestImport(
        [Description("Exact absolute path to a Navisworks nw-exchange-12.0 batchtest XML file.")] string inputPath,
        [Description("False parses/resolves both sides; true creates/replaces supported tests.")] bool apply = false,
        [Description("Replace existing same-name Clash Tests. Default false.")] bool overwriteExisting = false,
        [Description("Maximum accepted test count. Default 200, maximum 500.")] int limit = ClashTransferConstants.DefaultTestLimit,
        [Description("Continue on resolution/conflict/mutation errors; false rolls back earlier call changes when possible.")] bool continueOnError = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashBatchtestImportAsync(new ClashBatchtestImportRequest
        {
            InputPath = inputPath,
            Apply = apply,
            OverwriteExisting = overwriteExisting,
            Limit = limit,
            ContinueOnError = continueOnError,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
