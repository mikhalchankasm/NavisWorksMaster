using System.Text.Json;

namespace NavisHelper.McpServer.Services;

internal class ScenarioDraft
{
    public int SchemaVersion { get; set; } = 1;
    public string ExecutionMode { get; set; } = "template";
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ScenarioContext Context { get; set; }
    public List<ScenarioParameterDefinition> Parameters { get; set; } = new();
    public List<ScenarioStepDefinition> Steps { get; set; } = new();
    public ScenarioExactReplayDefinition ExactReplay { get; set; }
}

internal sealed class ScenarioDocument : ScenarioDraft
{
    public string ScenarioId { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

internal sealed class ScenarioContext
{
    public List<string> NavisworksVersions { get; set; } = new();
    public List<string> RootFilePatterns { get; set; } = new();
    public string ProjectLabel { get; set; } = string.Empty;
}

internal sealed class ScenarioParameterDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Required { get; set; }
    public JsonElement? Default { get; set; }
    public List<JsonElement> Enum { get; set; } = new();
    public string Pattern { get; set; } = string.Empty;
}

internal sealed class ScenarioStepDefinition
{
    public string StepId { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public int ScenarioContractVersion { get; set; } = 1;
    public Dictionary<string, JsonElement> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ReviewedWrites { get; set; } = new();
    public string Control { get; set; } = string.Empty;
    public JsonElement? Over { get; set; }
    public string As { get; set; } = string.Empty;
    public int? MaxIterations { get; set; }
    public List<ScenarioStepDefinition> Body { get; set; } = new();
}

internal sealed class ScenarioExactReplayDefinition
{
    public Dictionary<string, JsonElement> FixedParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ContextPolicy { get; set; } = "strict";
    public string WritePolicy { get; set; } = "repeatReviewedWrites";
    public ScenarioSafetyEnvelope SafetyEnvelope { get; set; }
}

internal sealed class ScenarioSafetyEnvelope
{
    public string PreviewFingerprint { get; set; } = string.Empty;
    public Dictionary<string, ScenarioStepSafetyLimit> StepLimits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class ScenarioStepSafetyLimit
{
    public int MaxMatchedItems { get; set; }
    public int MaxModelWrites { get; set; }
    public int MaxFileWrites { get; set; }
    public List<string> ApprovedScaleGates { get; set; } = new();
}

internal sealed class ScenarioValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
}

internal sealed class ScenarioStoredRecord
{
    public ScenarioDocument Scenario { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}

internal sealed class ScenarioListResponse
{
    public bool Ok { get; set; } = true;
    public string ScenarioDirectory { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<ScenarioListItem> Scenarios { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> AllowedTools { get; set; } = new();
}

internal sealed class ScenarioListItem
{
    public string ScenarioId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExecutionMode { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string MatchGrade { get; set; } = "weak";
    public List<string> MatchReasons { get; set; } = new();
}

internal sealed class ScenarioGetResponse
{
    public bool Ok { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public ScenarioDocument Scenario { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}

internal sealed class ScenarioMutationResponse
{
    public bool Ok { get; set; }
    public bool Applied { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string ScenarioId { get; set; } = string.Empty;
    public string ScenarioName { get; set; } = string.Empty;
    public string ExecutionMode { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime? UpdatedUtc { get; set; }
    public List<string> PlannedWrites { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> AllowedTools { get; set; } = new();
}

internal sealed class ScenarioCapabilitiesResponse
{
    public int CurrentSchemaVersion { get; set; } = 2;
    public List<int> SupportedSchemaVersions { get; set; } = new() { 1, 2 };
    public string ParameterReferenceSyntax { get; set; } = "{\"$parameter\":\"parameterName\"}";
    public string StepResultReferenceSyntax { get; set; } = "{\"$stepResult\":\"previousStepId.output.path\"}";
    public string StepIdPattern { get; set; } = "^[A-Za-z][A-Za-z0-9_]{0,63}$";
    public List<string> ParameterTypes { get; set; } = new();
    public List<string> PairNameTokens { get; set; } = new();
    public List<string> PairNameTransforms { get; set; } = new();
    public string ForeachRules { get; set; } = string.Empty;
    public List<ScenarioToolCapability> AllowedTools { get; set; } = new();
    public ScenarioDraft Example { get; set; }
}

internal sealed class ScenarioToolCapability
{
    public string Tool { get; set; } = string.Empty;
    public int ScenarioContractVersion { get; set; }
    public bool HasApply { get; set; }
    public bool MutatesModel { get; set; }
    public bool WritesFiles { get; set; }
    public bool ChangesSelection { get; set; }
    public List<string> ReviewedWrites { get; set; } = new();
    public List<string> OutputProjections { get; set; } = new();
}

internal sealed class ScenarioResolveResponse
{
    public bool Ok { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string ScenarioId { get; set; } = string.Empty;
    public string ScenarioName { get; set; } = string.Empty;
    public string ExecutionMode { get; set; } = string.Empty;
    public string ScenarioSha256 { get; set; } = string.Empty;
    public string ContextMatch { get; set; } = string.Empty;
    public List<string> ContextReasons { get; set; } = new();
    public List<ScenarioResolvedStep> Steps { get; set; } = new();
    public ScenarioSafetyEnvelope SafetyEnvelope { get; set; }
    public List<string> PlannedWrites { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string AgentInstruction { get; set; } = string.Empty;
}

internal sealed class ScenarioResolvedStep
{
    public string StepId { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public int ScenarioContractVersion { get; set; }
    public Dictionary<string, JsonElement> PreviewArguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonElement> ApplyArgumentOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string PlanSha256 { get; set; } = string.Empty;
    public bool MutatesModel { get; set; }
    public bool WritesFiles { get; set; }
    public bool ChangesSelection { get; set; }
    public List<string> ReviewedWrites { get; set; } = new();
    public string Control { get; set; } = string.Empty;
    public JsonElement? Over { get; set; }
    public string As { get; set; } = string.Empty;
    public int? MaxIterations { get; set; }
    public List<ScenarioResolvedStep> Body { get; set; } = new();
}
