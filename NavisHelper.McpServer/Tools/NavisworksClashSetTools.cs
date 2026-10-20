using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashSetTools : NavisworksToolBase
{
    public NavisworksClashSetTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Creates tests per Selection/Search Set or root pair; dry-run. Live SelectionSource bindings re-evaluate Search Sets. Inline: local itemId/exact path/unique name/rootName/sourceFile. planPath: navishelper.clash-test-transfer v1; itemId diagnostic-only, exact full set path portable.")]
    [ToolCapabilities(ToolEffects.Document | ToolEffects.LocalState, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashTestsFromSetsResponse> ClashTestsFromSets(
        [Description("False previews names/set paths/counts/conflicts/warnings; true creates/replaces tests.")] bool apply = false,
        [Description("a/b: Set itemId/path/name or rootName/sourceFile; pair name overrides template. Use pairs or planPath.")] List<ClashSetPair> pairs = null,
        [Description("Local JSON: pair array, {\"pairs\":[...]}, navishelper.clash-test-transfer v1. Instead of pairs for large plans/round-trip.")] string planPath = "",
        [Description("Clash type: hard, hard_conservative, clearance, or duplicate. Default hard.")] string testType = "hard",
        [Description("Tolerance in millimeters. A negative value leaves the Navisworks default unchanged.")] double toleranceMm = -1,
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
    public Task<ClashRunBatchResponse> ClashRunBatch(
        [Description("False previews the resolved run scope; true starts it.")] bool apply = false,
        [Description("Exact test names.")] List<string> testNames = null,
        [Description("clash_list_tests handles; validated only at start, stale after mutations.")] List<string> testHandles = null,
        [Description("Optional test-name prefix.")] string namePrefix = "",
        [Description("Optional maximum number of matched tests.")] int firstN = 0,
        [Description("Tests to process before pausing. Default 10.")] int batchSize = 10,
        [Description("Advisory timebox, seconds; overrun after return, no safe force-abort of synchronous test.")] int perTestTimeoutSeconds = 300,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashRunBatchAsync(new ClashRunBatchRequest
        {
            Apply = apply, TestNames = testNames ?? new List<string>(), TestHandles = testHandles ?? new List<string>(),
            NamePrefix = namePrefix, FirstN = firstN > 0 ? firstN : null, BatchSize = batchSize,
            PerTestTimeoutSeconds = perTestTimeoutSeconds,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Continues a paused asynchronous clash run for the next batch.")]
    [ToolCapabilities(ToolEffects.Document | ToolEffects.LocalState, RequiresHost = true, RequiresDocument = false)]
    public Task<ClashRunBatchResponse> ClashRunResume(
        [Description("Operation id returned by clash_run_batch or clash_tests_from_sets runAfterCreate.")] string operationId,
        [Description("Optional replacement batch size. Zero keeps the current value.")] int batchSize = 0,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashRunResumeAsync(new ClashRunResumeRequest
        {
            OperationId = operationId, BatchSize = batchSize > 0 ? batchSize : null,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Read-only async run progress/per-test outcomes; bypasses host_busy.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = false)]
    public Task<ClashRunBatchResponse> ClashRunStatus(
        [Description("Operation id returned by clash_run_batch or clash_tests_from_sets.")] string operationId,
        [Description("Maximum long-poll duration in seconds, 0-300. Default 0 returns immediately.")] int waitSeconds = 0,
        [Description("When true, poll until the run stops or waitSeconds expires. Default false.")] bool waitForCompletion = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashRunStatusAsync(new ClashRunStatusRequest { OperationId = operationId, WaitSeconds = waitSeconds, WaitForCompletion = waitForCompletion },
            cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Cooperative async-run cancellation; bypasses host_busy. Current test finishes; no later test starts.")]
    [ToolCapabilities(ToolEffects.LocalState, RequiresHost = true, RequiresDocument = false)]
    public Task<ClashRunBatchResponse> CancelClashRun(
        [Description("Operation id returned by clash_run_batch or clash_tests_from_sets.")] string operationId,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.CancelClashRunAsync(new CancelClashRunRequest { OperationId = operationId },
            cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
