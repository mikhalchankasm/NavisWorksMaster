using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashTransferTools : NavisworksToolBase
{
    public NavisworksClashTransferTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Exports versioned portable JSON test definitions; dry-run, file writes only on apply=true. Set identity: exact full tree path; root: rootName/sourceFile. No results, viewpoints, comments or calculation history.")]
    [ToolCapabilities(ToolEffects.Files, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashTestsExportResponse> ClashTestsExport(
        [Description("Exact names; union with testHandles/namePrefix; empty scope=all tests.")] List<string> testNames = null,
        [Description("clash_list_tests handles; source-document diagnostics, stale after mutations.")] List<string> testHandles = null,
        [Description("Optional exact test-name prefix.")] string namePrefix = "",
        [Description("Absolute JSON path; dry-run returns calculatedOutputPath, never creates file.")] string outputPath = "",
        [Description("Portable format. Must be navishelper_json.")] string format = ClashTransferConstants.JsonFormat,
        [Description("Allow replacement of an existing output file. Default false.")] bool overwriteExisting = false,
        [Description("False previews only; true writes .partial, atomically completes, verifies size/readability/SHA-256.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashTestsExportAsync(new ClashTestsExportRequest
        {
            TestNames = testNames ?? new List<string>(),
            TestHandles = testHandles ?? new List<string>(),
            NamePrefix = namePrefix,
            OutputPath = outputPath,
            Format = format,
            OverwriteExisting = overwriteExisting,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Imports supported nw-exchange-12.0 batchtest XML via exact lcop_selection_set_tree/full/path to versioned transfer plan/clash_tests_from_sets. Rejects DTD/external entities/schema loading, unsupported locators, oversized input. Dry-run; never runs tests/imports old results.")]
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
