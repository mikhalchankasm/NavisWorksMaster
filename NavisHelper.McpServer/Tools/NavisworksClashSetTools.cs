using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashSetTools : NavisworksToolBase
{
    public NavisworksClashSetTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Creates tests per Selection/Search Set or root pair; dry-run. Search Sets stay live sources. Inline: itemId/exact path/unique name/rootName/sourceFile(+childName). planPath: navishelper.clash-test-transfer v1 (full set path portable, itemId diagnostic).")]
    [ToolCapabilities(ToolEffects.Document | ToolEffects.LocalState, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashTestsFromSetsResponse> ClashTestsFromSets(
        [Description("False previews names/set paths/counts/conflicts/warnings; true creates/replaces tests.")] bool apply = false,
        [Description("a/b: Set itemId/path/name or rootName/sourceFile; pair name overrides template. Use pairs or planPath.")] List<ClashSetPair> pairs = null,
        [Description("Local JSON: pair array, {\"pairs\":[...]}, navishelper.clash-test-transfer v1. Instead of pairs for large plans/round-trip.")] string planPath = "",
        [Description("Clash type: hard, hard_conservative, clearance, or duplicate. Default hard.")] string testType = "hard",
        [Description("Tolerance in millimeters. A negative value leaves the Navisworks default unchanged.")] double toleranceMm = -1,
        [Description("Side A primitive types: triangles, lines, points; empty keeps the Navisworks default.")] List<string> primitiveTypesA = null,
        [Description("Side B primitive types: triangles, lines, points; empty keeps the Navisworks default.")] List<string> primitiveTypesB = null,
        [Description("Name template used when pair.name is empty. Default: {index|zeroPad:3} ИЗОЛ {aName}-{bName}.")] string pairNameTemplate = "{index|zeroPad:3} ИЗОЛ {aName}-{bName}",
        [Description("Starting value for {index}. Default 1.")] int pairNameStartIndex = 1,
        [Description("Replace an existing same-name test. Default false.")] bool overwriteExisting = false,
        [Description("Start an asynchronous batch run for newly created tests. Default false.")] bool runAfterCreate = false,
        [Description("Maximum accepted pair count. Default 200, maximum 500.")] int limit = 200,
        [Description("Tests per asynchronous run batch when runAfterCreate=true.")] int runBatchSize = 10,
        [Description("Advisory timebox, seconds; cannot safely interrupt synchronous test. Overrun reported after return.")] int perTestTimeoutSeconds = 300,
        [Description("Native rules: {\"sameFile\":true} prevents intra-source-model clashes during calculation.")] ClashNativeIgnoreRules ignoreRules = null,
        [Description("Continue per-test errors; false rolls back earlier call changes when possible.")] bool continueOnError = true,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashTestsFromSetsAsync(new ClashTestsFromSetsRequest
        {
            Apply = apply,
            Pairs = pairs ?? new List<ClashSetPair>(),
            PlanPath = planPath,
            TestType = testType,
            ToleranceMm = toleranceMm,
            PrimitiveTypesA = primitiveTypesA,
            PrimitiveTypesB = primitiveTypesB,
            PairNameTemplate = pairNameTemplate,
            PairNameStartIndex = pairNameStartIndex,
            OverwriteExisting = overwriteExisting,
            RunAfterCreate = runAfterCreate,
            Limit = limit,
            RunBatchSize = runBatchSize,
            PerTestTimeoutSeconds = perTestTimeoutSeconds,
            IgnoreRules = ignoreRules,
            ContinueOnError = continueOnError,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Starts async run, returns operationId. One test/UI callback; pauses after batchSize. clash_run_status/cancel_clash_run available during calculation. Dry-run.")]
    [ToolCapabilities(ToolEffects.Document | ToolEffects.LocalState, RequiresHost = true, RequiresDocument = true)]
    public async Task<ClashRunBatchResponse> ClashRunBatch(
        [Description("False previews scope; true starts.")] bool apply = false,
        [Description("Exact test names.")] List<string> testNames = null,
        [Description("clash_list_tests handles; checked at start, stale after mutations.")] List<string> testHandles = null,
        [Description("Test-name prefix.")] string namePrefix = "",
        [Description("Matched-test cap.")] int firstN = 0,
        [Description("Tests before pause. Default 10.")] int batchSize = 10,
        [Description("Advisory seconds; overrun after return; cannot force-abort.")] int perTestTimeoutSeconds = 300,
        [Description("Per-test rows, problems first; 0 = counters only. Default is 25.")] int maxTestRows = ClashTestRowLimit.DefaultMaxRows,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        var response = await _hostBridgeClient.ClashRunBatchAsync(new ClashRunBatchRequest
        {
            Apply = apply, TestNames = testNames ?? new List<string>(), TestHandles = testHandles ?? new List<string>(),
            NamePrefix = namePrefix, FirstN = firstN > 0 ? firstN : null, BatchSize = batchSize,
            PerTestTimeoutSeconds = perTestTimeoutSeconds,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion)).ConfigureAwait(false);
        ClashTestRowLimit.Apply(response, maxTestRows);
        return response;
    }

    [McpServerTool]
    [Description("Continues a paused asynchronous clash run for the next batch.")]
    [ToolCapabilities(ToolEffects.Document | ToolEffects.LocalState, RequiresHost = true, RequiresDocument = false)]
    public async Task<ClashRunBatchResponse> ClashRunResume(
        [Description("Run id: clash_run_batch/clash_tests_from_sets runAfterCreate.")] string operationId,
        [Description("New batch size; 0 keeps current.")] int batchSize = 0,
        [Description("Per-test rows, problems first; 0 = counters only. Default is 25.")] int maxTestRows = ClashTestRowLimit.DefaultMaxRows,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        var response = await _hostBridgeClient.ClashRunResumeAsync(new ClashRunResumeRequest
        {
            OperationId = operationId, BatchSize = batchSize > 0 ? batchSize : null,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion)).ConfigureAwait(false);
        ClashTestRowLimit.Apply(response, maxTestRows);
        return response;
    }

    [McpServerTool]
    [Description("Read-only async run progress/per-test outcomes; bypasses host_busy.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = false)]
    public async Task<ClashRunBatchResponse> ClashRunStatus(
        [Description("Run id: clash_run_batch/clash_tests_from_sets.")] string operationId,
        [Description("Long-poll seconds, 0-300; 0 returns immediately.")] int waitSeconds = 0,
        [Description("Poll until stopped or waitSeconds expires. Default false.")] bool waitForCompletion = false,
        [Description("Per-test rows, problems first; 0 = counters only. Default is 25.")] int maxTestRows = ClashTestRowLimit.DefaultMaxRows,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        var response = await _hostBridgeClient.ClashRunStatusAsync(new ClashRunStatusRequest { OperationId = operationId, WaitSeconds = waitSeconds, WaitForCompletion = waitForCompletion },
            cancellationToken, CreateTarget(instanceId, navisworksVersion)).ConfigureAwait(false);
        ClashTestRowLimit.Apply(response, maxTestRows);
        return response;
    }

    [McpServerTool]
    [Description("Cooperative async-run cancellation; bypasses host_busy. Current test finishes; no later test starts.")]
    [ToolCapabilities(ToolEffects.LocalState, RequiresHost = true, RequiresDocument = false)]
    public async Task<ClashRunBatchResponse> CancelClashRun(
        [Description("Operation id returned by clash_run_batch or clash_tests_from_sets.")] string operationId,
        [Description("Per-test rows, problems first; 0 = counters only. Default is 25.")] int maxTestRows = ClashTestRowLimit.DefaultMaxRows,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        var response = await _hostBridgeClient.CancelClashRunAsync(new CancelClashRunRequest { OperationId = operationId },
            cancellationToken, CreateTarget(instanceId, navisworksVersion)).ConfigureAwait(false);
        ClashTestRowLimit.Apply(response, maxTestRows);
        return response;
    }
}
