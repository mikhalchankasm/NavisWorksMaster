using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashSetTools : NavisworksToolBase
{
    public NavisworksClashSetTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Creates one Clash Detective test per Selection Set/Search Set or model-root pair. Set sides use native live Navisworks SelectionSource bindings, so dynamic Search Sets are re-evaluated. Dry-run by default. Inline references accept document-local itemId, exact full path, unique name, rootName, or sourceFile. planPath also accepts navishelper.clash-test-transfer v1; there itemId is diagnostic-only and exact full set path is the portable identity.")]
    public Task<ClashTestsFromSetsResponse> ClashTestsFromSets(
        [Description("False previews names, resolved set paths, current member counts, conflicts, and warnings. True creates/replaces tests.")] bool apply = false,
        [Description("Inline pairs. Each a/b reference accepts Selection Set itemId/path/name or a direct model rootName/sourceFile; optional pair name overrides the template. Use either pairs or planPath.")] List<ClashSetPair> pairs = null,
        [Description("Optional local JSON file containing a legacy pair array, {\"pairs\":[...]}, or navishelper.clash-test-transfer v1. Use instead of inline pairs for large plans or JSON round-trip transfer.")] string planPath = "",
        [Description("Clash type: hard, hard_conservative, clearance, or duplicate. Default hard.")] string testType = "hard",
        [Description("Tolerance in millimeters. A negative value leaves the Navisworks default unchanged.")] double toleranceMm = -1,
        [Description("Name template used when pair.name is empty. Default: {index|zeroPad:3} ИЗОЛ {aName}-{bName}.")] string pairNameTemplate = "{index|zeroPad:3} ИЗОЛ {aName}-{bName}",
        [Description("Starting value for {index}. Default 1.")] int pairNameStartIndex = 1,
        [Description("Replace an existing same-name test. Default false.")] bool overwriteExisting = false,
        [Description("Start an asynchronous batch run for newly created tests. Default false.")] bool runAfterCreate = false,
        [Description("Maximum accepted pair count. Default 200, maximum 500.")] int limit = 200,
        [Description("Tests per asynchronous run batch when runAfterCreate=true.")] int runBatchSize = 10,
        [Description("Advisory per-test timebox in seconds. Navisworks cannot safely interrupt a single synchronous test; overruns are reported after that test returns.")] int perTestTimeoutSeconds = 300,
        [Description("Native Clash Detective ignore rules. Set {\"sameFile\":true} to prevent clashes within the same appended source model during calculation.")] ClashNativeIgnoreRules ignoreRules = null,
        [Description("Continue after per-test errors. Default true preserves legacy behavior. False rolls back earlier changes from this call when possible.")] bool continueOnError = true,
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
    [Description("Starts an asynchronous Clash Detective run and returns immediately with operationId. Runs one Navisworks test per UI callback, pauses after batchSize, and keeps clash_run_status/cancel_clash_run available even while a test is calculating. Dry-run by default.")]
    public Task<ClashRunBatchResponse> ClashRunBatch(
        [Description("False previews the resolved run scope; true starts it.")] bool apply = false,
        [Description("Exact test names.")] List<string> testNames = null,
        [Description("Test handles from clash_list_tests. Handles are validated only when starting and can become stale after test mutations.")] List<string> testHandles = null,
        [Description("Optional test-name prefix.")] string namePrefix = "",
        [Description("Optional maximum number of matched tests.")] int firstN = 0,
        [Description("Tests to process before pausing. Default 10.")] int batchSize = 10,
        [Description("Advisory per-test timebox. An overrun is reported after Navisworks returns; a single synchronous test cannot be force-aborted safely.")] int perTestTimeoutSeconds = 300,
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
    [Description("Returns progress and per-test outcomes for an asynchronous clash run. Read-only and bypasses host_busy.")]
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
    [Description("Requests cooperative cancellation of an asynchronous clash run. It bypasses host_busy. A currently executing native Navisworks test finishes first; no later test is started.")]
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
