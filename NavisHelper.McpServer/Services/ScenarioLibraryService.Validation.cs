using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;

namespace NavisHelper.McpServer.Services;

internal sealed partial class ScenarioLibraryService
{
    internal ScenarioValidationResult ValidateDraft(ScenarioDraft draft, bool verifyExactFingerprint = true)
    {
        var result = new ScenarioValidationResult();
        if (draft == null)
        {
            result.Errors.Add("scenario is required");
            return result;
        }

        try
        {
            NormalizeDraftCollections(draft);
        }
        catch (ArgumentException)
        {
            result.Errors.Add("dictionary keys must be unique ignoring case");
            return result;
        }

        if (draft.SchemaVersion != 1 && draft.SchemaVersion != CurrentSchemaVersion)
            result.Errors.Add("schemaVersion must be 1 or 2");
        if (!string.Equals(draft.ExecutionMode, "template", StringComparison.Ordinal) &&
            !string.Equals(draft.ExecutionMode, "exactReplay", StringComparison.Ordinal))
            result.Errors.Add("executionMode must be template or exactReplay");
        ValidateDisplayText(draft.Name, "name", 1, 120, result);
        ValidateDisplayText(draft.Description, "description", 0, 1000, result);
        ValidateContext(draft.Context, result);

        draft.Parameters ??= new List<ScenarioParameterDefinition>();
        draft.Steps ??= new List<ScenarioStepDefinition>();
        if (draft.Parameters.Count > 32)
            result.Errors.Add("parameters cannot contain more than 32 items");
        if (draft.Steps.Count < 1 || draft.Steps.Count > 32)
            result.Errors.Add("steps must contain 1 to 32 items");

        var parameters = new Dictionary<string, ScenarioParameterDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in draft.Parameters)
        {
            if (parameter == null || !NameRegex.IsMatch(parameter.Name ?? string.Empty))
            {
                result.Errors.Add("parameter name is invalid");
                continue;
            }
            if (!parameters.TryAdd(parameter.Name, parameter))
                result.Errors.Add("parameter names must be unique ignoring case: " + parameter.Name);
            if (!ParameterTypes.Contains(parameter.Type ?? string.Empty))
                result.Errors.Add("unsupported parameter type for " + parameter.Name);
            ValidateDisplayText(parameter.Title, "parameter title", 0, 200, result);
            ValidateDisplayText(parameter.Description, "parameter description", 0, 500, result);
            if (parameter.Default.HasValue)
            {
                if (parameter.Type is "filePath" or "directoryPath")
                    result.Errors.Add("path parameter defaults are forbidden: " + parameter.Name);
                else if (!ValueMatchesParameterType(parameter.Default.Value, parameter.Type))
                    result.Errors.Add("default value type does not match parameter " + parameter.Name);
            }
            parameter.Enum ??= new List<JsonElement>();
            if (parameter.Enum.Count > 0 || string.Equals(parameter.Type, "enum", StringComparison.Ordinal))
            {
                if (draft.SchemaVersion < 2)
                    result.Errors.Add("parameter enum is supported only in schema version 2: " + parameter.Name);
                if (parameter.Enum.Count < 1 || parameter.Enum.Count > 100)
                    result.Errors.Add("parameter enum must contain 1 to 100 values: " + parameter.Name);
                if (parameter.Enum.Any(value => value.ValueKind != JsonValueKind.String))
                    result.Errors.Add("parameter enum values must be strings: " + parameter.Name);
                if (parameter.Default.HasValue && !ParameterValueMeetsConstraints(parameter, parameter.Default.Value, out _))
                    result.Errors.Add("default value is outside enum/pattern constraints: " + parameter.Name);
            }
            if (!string.IsNullOrWhiteSpace(parameter.Pattern))
            {
                if (draft.SchemaVersion < 2)
                    result.Errors.Add("parameter pattern is supported only in schema version 2: " + parameter.Name);
                if (parameter.Type is not ("string" or "enum"))
                    result.Errors.Add("parameter pattern is valid only for string or enum: " + parameter.Name);
                if (parameter.Pattern.Length > 500)
                    result.Errors.Add("parameter pattern cannot exceed 500 characters: " + parameter.Name);
                else
                {
                    try { _ = new Regex(parameter.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)); }
                    catch (ArgumentException) { result.Errors.Add("parameter pattern is invalid: " + parameter.Name); }
                }
            }
        }

        var exact = IsExactReplay(draft);
        if (exact)
            ValidateExactReplay(draft, parameters, result);
        else if (draft.ExactReplay != null)
            result.Errors.Add("exactReplay object is allowed only in exactReplay mode");

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var priorSteps = new Dictionary<string, ToolDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in draft.Steps)
        {
            ValidateStep(step, draft.SchemaVersion, exact, parameters, referenced, stepIds, priorSteps, result, allowForeach: true);
            if (step != null && !string.IsNullOrWhiteSpace(step.StepId) && GetToolDescriptor(step.Tool) is { } stepDescriptor)
                priorSteps[step.StepId] = stepDescriptor;
        }

        if (exact && draft.ExactReplay?.SafetyEnvelope != null)
        {
            var limits = draft.ExactReplay.SafetyEnvelope.StepLimits ?? new Dictionary<string, ScenarioStepSafetyLimit>();
            foreach (var step in draft.Steps)
            {
                if (step == null || string.IsNullOrWhiteSpace(step.StepId))
                    continue;
                if (!limits.ContainsKey(step.StepId))
                    result.Errors.Add("exactReplay safety limit is missing for step: " + step.StepId);
            }
            foreach (var limitName in limits.Keys)
            {
                if (!stepIds.Contains(limitName))
                    result.Errors.Add("exactReplay safety limit references an unknown step: " + limitName);
            }
            foreach (var step in draft.Steps.Where(item => item != null && !string.IsNullOrWhiteSpace(item.StepId)))
            {
                if (!limits.TryGetValue(step.StepId, out var limit) || limit == null)
                    continue;
                var descriptor = GetToolDescriptor(step.Tool);
                if (descriptor == null)
                    continue;
                foreach (var gate in limit.ApprovedScaleGates)
                {
                    if (!descriptor.ScaleAuthorizationArguments.Contains(gate, StringComparer.OrdinalIgnoreCase))
                        result.Errors.Add("unknown scale authorization gate for " + step.StepId + ": " + gate);
                }
                if (string.Equals(step.Tool, "isolate_by_box", StringComparison.Ordinal))
                    ValidateSectionBoxDurationSafetyEnvelope(step, limit, result);
            }

            if (verifyExactFingerprint && result.Errors.Count == 0)
            {
                var expectedFingerprint = "sha256:" + ComputeExactReplayFingerprint(draft);
                if (!string.Equals(
                        expectedFingerprint,
                        draft.ExactReplay.SafetyEnvelope.PreviewFingerprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add("exactReplay safety previewFingerprint does not match the canonical resolved plan");
                }
            }
        }

        foreach (var parameter in parameters.Values.Where(item => item.Required))
        {
            if (!referenced.Contains(parameter.Name))
                result.Errors.Add("required parameter is not referenced by any step: " + parameter.Name);
        }

        return result;
    }

    private void ValidateExactReplay(
        ScenarioDraft draft,
        IReadOnlyDictionary<string, ScenarioParameterDefinition> parameters,
        ScenarioValidationResult result)
    {
        if (draft.ExactReplay == null)
        {
            result.Errors.Add("exactReplay object is required");
            return;
        }

        if (!string.Equals(draft.ExactReplay.ContextPolicy, "strict", StringComparison.Ordinal))
            result.Errors.Add("exactReplay.contextPolicy must be strict");
        if (!string.Equals(draft.ExactReplay.WritePolicy, "repeatReviewedWrites", StringComparison.Ordinal))
            result.Errors.Add("exactReplay.writePolicy must be repeatReviewedWrites");
        if (draft.Context == null ||
            ((draft.Context.NavisworksVersions?.Count ?? 0) == 0 &&
             (draft.Context.RootFilePatterns?.Count ?? 0) == 0 &&
             string.IsNullOrWhiteSpace(draft.Context.ProjectLabel)))
        {
            result.Errors.Add("exactReplay requires at least one strict context hint");
        }

        draft.ExactReplay.FixedParameters ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in parameters.Values)
        {
            if (!draft.ExactReplay.FixedParameters.TryGetValue(parameter.Name, out var fixedValue))
            {
                result.Errors.Add("exactReplay fixed value is missing: " + parameter.Name);
                continue;
            }
            if (!ValueMatchesParameterType(fixedValue, parameter.Type))
                result.Errors.Add("exactReplay fixed value type is invalid: " + parameter.Name);
            if (parameter.Type is "filePath" or "directoryPath")
                ValidateFixedPath(fixedValue, parameter.Name, result);
        }

        foreach (var fixedName in draft.ExactReplay.FixedParameters.Keys)
        {
            if (!parameters.ContainsKey(fixedName))
                result.Errors.Add("exactReplay has an undeclared fixed parameter: " + fixedName);
        }

        if (draft.ExactReplay.SafetyEnvelope == null)
        {
            result.Errors.Add("exactReplay.safetyEnvelope is required");
        }
        else
        {
            var envelope = draft.ExactReplay.SafetyEnvelope;
            if (!Regex.IsMatch(envelope.PreviewFingerprint ?? string.Empty, "^sha256:[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
                result.Errors.Add("exactReplay safety previewFingerprint must be sha256:<64 hex characters>");
            envelope.StepLimits ??= new Dictionary<string, ScenarioStepSafetyLimit>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in envelope.StepLimits)
            {
                if (item.Value == null || item.Value.MaxMatchedItems < 0 || item.Value.MaxModelWrites < 0 ||
                    item.Value.MaxFileWrites < 0 || item.Value.MaxDurationSeconds < 0)
                    result.Errors.Add("exactReplay safety limits must be non-negative: " + item.Key);
                if (item.Value != null)
                {
                    item.Value.ApprovedScaleGates ??= new List<string>();
                    foreach (var gate in item.Value.ApprovedScaleGates)
                        ValidateDisplayText(gate, "approved scale gate", 1, 100, result);
                }
            }
        }
    }

    private void ValidateStep(
        ScenarioStepDefinition step,
        int schemaVersion,
        bool exact,
        IReadOnlyDictionary<string, ScenarioParameterDefinition> parameters,
        ISet<string> referenced,
        ISet<string> stepIds,
        IReadOnlyDictionary<string, ToolDescriptor> priorSteps,
        ScenarioValidationResult result,
        bool allowForeach)
    {
        if (step == null)
        {
            result.Errors.Add("step cannot be null");
            return;
        }
        if (!NameRegex.IsMatch(step.StepId ?? string.Empty))
            result.Errors.Add("stepId has invalid format (expected ^[A-Za-z][A-Za-z0-9_]{0,63}$): " + step.StepId);
        else if (!stepIds.Add(step.StepId))
            result.Errors.Add("stepId is duplicated ignoring case: " + step.StepId);

        if (string.Equals(step.Control, "foreach", StringComparison.Ordinal))
        {
            if (schemaVersion < 2)
                result.Errors.Add("foreach is supported only in schema version 2: " + step.StepId);
            if (!allowForeach)
                result.Errors.Add("nested foreach is forbidden: " + step.StepId);
            if (!step.Over.HasValue || !TryGetStepResultReference(step.Over.Value, out var overReference))
                result.Errors.Add("foreach over must be a $stepResult reference: " + step.StepId);
            else
                ValidateStepResultReference(overReference, priorSteps, null, result);
            if (!NameRegex.IsMatch(step.As ?? string.Empty))
                result.Errors.Add("foreach as is invalid: " + step.StepId);
            if (!step.MaxIterations.HasValue || step.MaxIterations.Value < 1 || step.MaxIterations.Value > 1000)
                result.Errors.Add("foreach maxIterations must be between 1 and 1000: " + step.StepId);
            if (step.Body == null || step.Body.Count < 1 || step.Body.Count > 16)
                result.Errors.Add("foreach body must contain 1 to 16 steps: " + step.StepId);
            else
            {
                foreach (var bodyStep in step.Body)
                    ValidateStep(bodyStep, schemaVersion, exact, parameters, referenced, stepIds, priorSteps, result, allowForeach: false);
            }
            if (!string.IsNullOrWhiteSpace(step.Tool) || (step.Arguments?.Count ?? 0) > 0)
                result.Errors.Add("foreach control step cannot also define tool/arguments: " + step.StepId);
            return;
        }
        if (!string.IsNullOrWhiteSpace(step.Control))
        {
            result.Errors.Add("unsupported control value: " + step.Control);
            return;
        }

        var descriptor = GetToolDescriptor(step.Tool);
        if (descriptor == null)
        {
            result.Errors.Add(
                "tool is not scenario-allowlisted: " + step.Tool +
                "; allowed: [" +
                string.Join(", ", ToolDescriptors.Keys.OrderBy(name => name, StringComparer.Ordinal)) +
                "]");
            return;
        }
        if (!descriptor.SupportedContractVersions.Contains(step.ScenarioContractVersion))
            result.Errors.Add("scenarioContractVersion is stale for " + step.Tool);

        step.Arguments ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var methodParameters = descriptor.Method.GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
            .ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var argument in step.Arguments)
        {
            if (IsAuthorizationArgument(argument.Key))
            {
                result.Errors.Add("authorization argument cannot be stored: " + argument.Key);
                continue;
            }
            if (RuntimeIdentityArguments.Contains(argument.Key) || IsCredentialName(argument.Key))
            {
                result.Errors.Add("runtime identity or credential argument cannot be stored: " + argument.Key);
                continue;
            }
            if (!methodParameters.TryGetValue(argument.Key, out var methodParameter))
            {
                result.Errors.Add("unknown tool argument " + argument.Key + " for " + step.Tool);
                continue;
            }
            if (ReviewedWriteBehaviorArguments.Contains(argument.Key) && !exact &&
                !(schemaVersion >= 2 && step.ReviewedWrites.Contains(argument.Key, StringComparer.OrdinalIgnoreCase)))
            {
                result.Errors.Add("reviewed write behavior requires reviewedWrites declaration in schema version 2 template: " + argument.Key);
                continue;
            }

            if (string.Equals(step.Tool, "isolate_by_box", StringComparison.Ordinal) &&
                string.Equals(argument.Key, "box", StringComparison.OrdinalIgnoreCase))
            {
                ValidateSectionBoxLiteral(argument.Value, result);
                continue;
            }
            if (string.Equals(step.Tool, "isolate_by_box", StringComparison.Ordinal) &&
                string.Equals(argument.Key, "maxScannedItems", StringComparison.OrdinalIgnoreCase))
            {
                ValidateSectionBoxTraversalLimit(argument.Value, result);
                continue;
            }
            if (string.Equals(step.Tool, "isolate_by_box", StringComparison.Ordinal) &&
                string.Equals(argument.Key, "maxDurationSeconds", StringComparison.OrdinalIgnoreCase))
            {
                ValidateSectionBoxDurationLimit(argument.Value, result);
                continue;
            }

            if (TryGetParameterReference(argument.Value, out var parameterName))
            {
                if (!parameters.TryGetValue(parameterName, out var definition) ||
                    !string.Equals(definition.Name, parameterName, StringComparison.Ordinal))
                {
                    result.Errors.Add("parameter reference is undeclared or has wrong case: " + parameterName);
                    continue;
                }
                if (!ParameterTypeMatchesToolType(definition.Type, methodParameter.ParameterType))
                    result.Errors.Add("parameter type does not match tool argument " + argument.Key);
                referenced.Add(definition.Name);
                continue;
            }

            if (TryGetStepResultReference(argument.Value, out var stepResultReference))
            {
                if (schemaVersion < 2)
                    result.Errors.Add("$stepResult is supported only in schema version 2: " + argument.Key);
                else
                    ValidateStepResultReference(stepResultReference, priorSteps, methodParameter.ParameterType, result);
                continue;
            }
            if (TryGetLoopReference(argument.Value, out _))
            {
                if (allowForeach)
                    result.Errors.Add("$loop is allowed only inside foreach body: " + argument.Key);
                continue;
            }

            if (ContainsNestedParameterReference(argument.Value))
            {
                result.Errors.Add("nested parameter references are not supported in schema version 1: " + argument.Key);
                continue;
            }
            if (ContainsForbiddenPropertyName(argument.Value, out var forbiddenProperty))
            {
                result.Errors.Add("runtime identity or credential property cannot be stored: " + forbiddenProperty);
                continue;
            }
            if (IsFileSystemPathArgument(argument.Key) && argument.Value.ValueKind == JsonValueKind.String)
            {
                result.Errors.Add("path arguments must reference a declared path parameter: " + argument.Key);
                continue;
            }
            if (!CanDeserialize(argument.Value, methodParameter.ParameterType))
                result.Errors.Add("literal value does not match tool argument type: " + argument.Key);
            ValidateJsonStrings(argument.Value, "argument " + argument.Key, result);
        }

        foreach (var requiredArgument in descriptor.RequiredArguments)
        {
            if (!step.Arguments.Keys.Contains(requiredArgument, StringComparer.OrdinalIgnoreCase))
                result.Errors.Add("required tool argument is missing: " + requiredArgument);
        }

        step.ReviewedWrites ??= new List<string>();
        foreach (var reviewedWrite in step.ReviewedWrites)
        {
            if (!ReviewedWriteBehaviorArguments.Contains(reviewedWrite))
                result.Errors.Add("unknown reviewedWrites entry for " + step.StepId + ": " + reviewedWrite);
            else if (!methodParameters.ContainsKey(reviewedWrite))
                result.Errors.Add("reviewedWrites entry is not an argument of " + step.Tool + ": " + reviewedWrite);
            else if (!step.Arguments.ContainsKey(reviewedWrite))
                result.Errors.Add("reviewedWrites entry has no stored argument in " + step.Tool + ": " + reviewedWrite);
        }
    }
}
