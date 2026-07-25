using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksScenarioTools : NavisworksToolBase
{
    public NavisworksScenarioTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Returns supported scenario schema versions, complete tool allowlist, contract versions, reviewed writes, safe output projections, parameter/$stepResult syntax, foreach rules, pair-name grammar, and a complete valid example. Read-only.")]
    public ScenarioCapabilitiesResponse ScenarioCapabilities()
    {
        return _scenarioLibraryService.GetCapabilities();
    }

    [McpServerTool]
    [Description("Lists user-approved NavisHelper scenarios from the current Windows profile. Returns metadata and bounded context-match suggestions only; it never executes scenario steps.")]
    public ScenarioListResponse ListScenarios(
        [Description("Optional case-insensitive substring in scenario name or description.")] string query = "",
        [Description("Optional current Navisworks version hint: 2024, 2025, 2026, or 2027.")] string navisworksVersion = "",
        [Description("Optional current root/source filenames used only for advisory scenario matching.")] List<string> rootFileNames = null,
        [Description("Optional exact user-provided project label used only for advisory matching.")] string projectLabel = "",
        [Description("Maximum results. Default is 3, range 1-20.")] int limit = 3)
    {
        return _scenarioLibraryService.List(query, navisworksVersion, rootFileNames, projectLabel, limit);
    }

    [McpServerTool]
    [Description("Reads one saved NavisHelper scenario by scenario_id. This is read-only and does not resolve parameters or execute any step.")]
    public ScenarioGetResponse GetScenario(
        [Description("Stable scenario_id returned by list_scenarios or save_scenario.")] string scenarioId)
    {
        return _scenarioLibraryService.Get(scenarioId);
    }

    [McpServerTool]
    [Description("Validates and saves a user-approved NavisHelper schema v1/v2 scenario under %APPDATA%\\NavisHelper\\Scenarios. Defaults to preview. Use {\"$parameter\":\"name\"}; schema v2 also supports allowlisted {\"$stepResult\":\"step.output\"}, bounded foreach, typed parameters, and per-tool reviewedWrites. stepId must match ^[A-Za-z][A-Za-z0-9_]{0,63}$. Call scenario_capabilities for the full grammar and example. Never store apply/confirm flags, handles, instance/document IDs, credentials, or raw transcripts.")]
    public ScenarioMutationResponse SaveScenario(
        [Description("Scenario schema version 1 or 2 draft. Store-owned scenarioId/createdUtc/updatedUtc are omitted.")] ScenarioDraft scenario,
        [Description("Existing scenario_id for update; omit to create a new scenario.")] string scenarioId = "",
        [Description("Required current SHA-256 from get_scenario/list_scenarios when updating.")] string expectedSha256 = "",
        [Description("False validates and previews the file write; true writes atomically. Default is false.")] bool apply = false,
        [Description("Required true with apply=true after the user reviews the save preview.")] bool confirmSave = false,
        [Description("Additionally required true when saving exactReplay. Confirms that later direct replay requests should not ask follow-up questions when strict preflight succeeds.")] bool confirmExactReplay = false)
    {
        return _scenarioLibraryService.Save(
            scenario,
            scenarioId,
            expectedSha256,
            apply,
            confirmSave,
            confirmExactReplay);
    }

    [McpServerTool]
    [Description("Deletes exactly one saved NavisHelper scenario. Defaults to preview and uses SHA-256 optimistic concurrency. It never recursively deletes the scenario directory.")]
    public ScenarioMutationResponse DeleteScenario(
        [Description("Stable scenario_id to delete.")] string scenarioId,
        [Description("Current SHA-256 returned by get_scenario/list_scenarios.")] string expectedSha256 = "",
        [Description("False previews deletion; true deletes the file. Default is false.")] bool apply = false,
        [Description("Required true with apply=true after reviewing the deletion preview.")] bool confirmDelete = false)
    {
        return _scenarioLibraryService.Delete(scenarioId, expectedSha256, apply, confirmDelete);
    }

    [McpServerTool]
    [Description("Resolves a saved scenario into ordered existing MCP tool calls without executing them. For template mode, show the plan and obtain normal apply confirmation. For exactReplay, a direct user replay request is current authorization: follow agent_instruction, run each preview, enforce the saved safety envelope, then apply without follow-up questions; stop on the first mismatch.")]
    public ScenarioResolveResponse ResolveScenario(
        [Description("Stable scenario_id to resolve.")] string scenarioId,
        [Description("Template parameter values keyed by declared parameter name. Must be empty for exactReplay.")] Dictionary<string, JsonElement> parameterValues = null,
        [Description("preview or exact_replay. exact_replay is valid only after a direct current user command to replay that exact scenario.")] string executionIntent = "preview",
        [Description("Optional current Navisworks version hint used for strict exactReplay context validation.")] string navisworksVersion = "",
        [Description("Optional current root/source filenames used for strict exactReplay context validation.")] List<string> rootFileNames = null,
        [Description("Optional current exact project label used for strict exactReplay context validation.")] string projectLabel = "")
    {
        return _scenarioLibraryService.Resolve(
            scenarioId,
            parameterValues,
            executionIntent,
            navisworksVersion,
            rootFileNames,
            projectLabel);
    }
}
