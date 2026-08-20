using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashTransferTools : NavisworksToolBase
{
    public NavisworksClashTransferTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Exports portable Clash Detective test definitions to a versioned NavisHelper JSON transfer plan. Selection Set/Search Set sides use exact full tree paths; model-root sides use rootName/sourceFile. Results, viewpoints, comments, and calculation history are never exported. Dry-run by default and never writes a file unless apply=true.")]
    public Task<ClashTestsExportResponse> ClashTestsExport(
        [Description("Exact test names to export. Combined as a union with testHandles and namePrefix. Empty scope exports all tests.")] List<string> testNames = null,
        [Description("Test handles from clash_list_tests. Handles are source-document diagnostics and can become stale after test mutations.")] List<string> testHandles = null,
        [Description("Optional exact test-name prefix.")] string namePrefix = "",
        [Description("Exact absolute JSON output path. Dry-run returns it as calculatedOutputPath but does not create it.")] string outputPath = "",
        [Description("Portable format. Must be navishelper_json.")] string format = ClashTransferConstants.JsonFormat,
        [Description("Allow replacement of an existing output file. Default false.")] bool overwriteExisting = false,
        [Description("False previews the transfer plan without writing. True writes through a .partial file, atomically completes it, and verifies size/readability/SHA-256.")] bool apply = false,
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
    [Description("Imports the supported subset of Autodesk Navisworks nw-exchange-12.0 <batchtest> XML by adapting exact lcop_selection_set_tree/full/path locators into the common versioned transfer plan and existing clash_tests_from_sets mutation path. DTD, external entities, external schema loading, unsupported locators, and oversized input are rejected. Dry-run by default; created tests are never run and old results are never imported.")]
    public Task<ClashBatchtestImportResponse> ClashBatchtestImport(
        [Description("Exact absolute path to a Navisworks nw-exchange-12.0 batchtest XML file.")] string inputPath,
        [Description("False parses and resolves both sides without document mutation. True creates/replaces supported tests.")] bool apply = false,
        [Description("Replace existing same-name Clash Tests. Default false.")] bool overwriteExisting = false,
        [Description("Maximum accepted test count. Default 200, maximum 500.")] int limit = ClashTransferConstants.DefaultTestLimit,
        [Description("Continue after a per-test resolution/conflict/mutation failure. Default false; false rolls back earlier changes from this call when possible.")] bool continueOnError = false,
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
