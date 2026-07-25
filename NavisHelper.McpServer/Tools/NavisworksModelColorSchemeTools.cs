using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksModelColorSchemeTools : NavisworksToolBase
{
    public NavisworksModelColorSchemeTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Analyzes model naming/property patterns or applies an explicit ordered color-classification scheme. Rules use first-match-wins priority. Mutations require apply=true; reset restores only overrides captured by the active runtime scheme.")]
    public Task<ModelColorSchemeResponse> ModelColorScheme(
        [Description("Operation: analyze, apply, or reset. analyze is read-only. operation=apply with apply=false returns a dry-run plan. Default is analyze.")] string operation = "analyze",
        [Description("Item scope: model (all loaded models) or selection (selected items and descendants). Default is model.")] string scope = "model",
        [Description("False keeps apply/reset read-only; true applies colors or restores the active scheme. Default is false.")] bool apply = false,
        [Description("Maximum traversed model items, from 1 to 2000000. Default is 100000. apply=true rejects truncated scopes. Prefer selection scope for very large models.")] int maxItems = 100000,
        [Description("Maximum analysis candidates returned, from 1 to 5000. Default is 100.")] int candidateLimit = 100,
        [Description("Maximum non-empty properties read per eligible item, from 1 to 1000. Default is 50.")] int maxPropertiesPerItem = 50,
        [Description("Include geometry-bearing container items as well as leaf geometry items during analyze. apply rejects true because container overrides propagate to descendants. Default is false.")] bool includeContainers = false,
        [Description("Required for apply=true when more than 25000 items match. Review the dry-run first. Default is false.")] bool confirmLargeApply = false,
        [Description("Clear the current selection after apply so Navisworks selection highlighting does not mask permanent colors. The selection is restored by reset when the user has not made another selection. Default is true.")] bool clearSelectionAfterApply = true,
        [Description("Host-side work budget in seconds, from 5 to 45. Default is 40. Large analysis/classification returns a controlled truncated result before the MCP timeout.")] int workBudgetSeconds = 40,
        [Description("Response verbosity: compact or full. compact omits long sample paths and bounds candidate text. Default is compact.")] string verbosity = "compact",
        [Description("Optional category display/internal-name contains filters for analyze property candidates. Empty scans all categories.")] List<string> analysisCategoryFilters = null,
        [Description("Optional property display/internal-name contains filters for analyze property candidates. Empty scans all properties.")] List<string> analysisPropertyFilters = null,
        [Description("Ordered classification rules. Each rule requires colorHex and either matchAll=true or at least one matcher. Use a final matchAll rule as a catch-all or to color the whole selection one fixed color. Lists within a matcher dimension are OR; populated dimensions are AND; property dimensions must match one property. First matching rule wins.")] List<ModelColorSchemeRule> rules = null,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ModelColorSchemeAsync(new ModelColorSchemeRequest
        {
            Operation = operation,
            Scope = scope,
            Apply = apply,
            MaxItems = maxItems,
            CandidateLimit = candidateLimit,
            MaxPropertiesPerItem = maxPropertiesPerItem,
            IncludeContainers = includeContainers,
            ConfirmLargeApply = confirmLargeApply,
            ClearSelectionAfterApply = clearSelectionAfterApply,
            WorkBudgetSeconds = workBudgetSeconds,
            Verbosity = verbosity,
            AnalysisCategoryFilters = analysisCategoryFilters ?? new List<string>(),
            AnalysisPropertyFilters = analysisPropertyFilters ?? new List<string>(),
            Rules = rules ?? new List<ModelColorSchemeRule>(),
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
