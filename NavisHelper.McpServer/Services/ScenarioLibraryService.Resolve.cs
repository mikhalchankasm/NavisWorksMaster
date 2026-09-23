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
    public ScenarioResolveResponse Resolve(
        string scenarioId,
        IReadOnlyDictionary<string, JsonElement> parameterValues,
        string executionIntent,
        string navisworksVersion,
        IReadOnlyCollection<string> rootFileNames,
        string projectLabel)
    {
        var record = ReadById(scenarioId, out var errorCode, out var errorMessage);
        if (record == null)
        {
            return new ScenarioResolveResponse
            {
                Ok = false,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
            };
        }

        var scenario = record.Scenario;
        var exact = IsExactReplay(scenario);
        var exactRequested = string.Equals(executionIntent, "exact_replay", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(executionIntent, "preview", StringComparison.OrdinalIgnoreCase) && !exactRequested)
            return ResolveFail(record, "scenario_execution_intent_invalid", "execution_intent должен быть preview или exact_replay.");
        if (exactRequested && !exact)
            return ResolveFail(record, "scenario_not_exact_replay", "Этот сценарий сохранён как шаблон, а не как точное повторение.");
        if (exact && parameterValues != null && parameterValues.Count > 0)
            return ResolveFail(record, "scenario_exact_override_forbidden", "Параметры точного сценария нельзя переопределять при запуске.");

        var contextMatch = MatchContext(scenario.Context, navisworksVersion, rootFileNames, projectLabel);
        if (exactRequested && !string.Equals(contextMatch.Grade, "strong", StringComparison.Ordinal))
        {
            var failed = ResolveFail(record, "scenario_context_mismatch", "Контекст точного сценария не подтверждён. Выполнение должно быть остановлено без уточняющих вопросов.");
            failed.ContextMatch = contextMatch.Grade;
            failed.ContextReasons = contextMatch.Reasons;
            return failed;
        }

        var valueErrors = new List<string>();
        var values = exact
            ? scenario.ExactReplay.FixedParameters
            : MergeTemplateValues(scenario, parameterValues, out valueErrors);
        if (!exact && valueErrors.Count > 0)
        {
            var failed = ResolveFail(record, "scenario_parameters_required", "Не заданы или неверно заданы параметры сценария.");
            failed.Warnings.AddRange(valueErrors);
            return failed;
        }

        var response = new ScenarioResolveResponse
        {
            Ok = true,
            ScenarioId = scenario.ScenarioId,
            ScenarioName = scenario.Name,
            ExecutionMode = scenario.ExecutionMode,
            ScenarioSha256 = record.Sha256,
            ContextMatch = contextMatch.Grade,
            ContextReasons = contextMatch.Reasons,
            SafetyEnvelope = exact ? scenario.ExactReplay.SafetyEnvelope : null,
            AgentInstruction = exactRequested
                ? "Exact replay was explicitly requested. Execute each returned preview call in order, substitute allowlisted $stepResult projections from actual prior responses, enforce foreach.maxIterations, verify the plan stays inside safety_envelope, then execute the same tool with apply=true and only already-approved scale gates. Do not ask follow-up questions. Stop and report on the first mismatch or failure."
                : exact
                    ? "Preview only. Show the saved exact-replay plan and safety envelope. Do not execute or apply any step unless the user directly requests execution_intent=exact_replay."
                    : "Show this resolved plan and all writes to the user. Execute steps in order, substituting each $stepResult from the allowlisted projection of the actual prior response and enforcing foreach.maxIterations before its body. Execute preview calls first. Apply mutating steps only after the user's current confirmation.",
        };

        foreach (var step in scenario.Steps)
        {
            if (string.Equals(step.Control, "foreach", StringComparison.Ordinal))
            {
                response.Steps.Add(new ScenarioResolvedStep
                {
                    StepId = step.StepId,
                    Control = step.Control,
                    Over = step.Over?.Clone(),
                    As = step.As,
                    MaxIterations = step.MaxIterations,
                    Body = ResolveControlBody(step.Body, values, exactRequested),
                });
                response.PlannedWrites.Add(
                    step.StepId + ": bounded foreach, maximum " +
                    step.MaxIterations.GetValueOrDefault().ToString() + " iteration(s)");
                foreach (var bodyStep in step.Body ?? new List<ScenarioStepDefinition>())
                {
                    var bodyDescriptor = GetToolDescriptor(bodyStep?.Tool);
                    if (bodyDescriptor?.MutatesModel == true)
                        response.PlannedWrites.Add(step.StepId + "/" + bodyStep.StepId + ": Navisworks document changes via " + bodyStep.Tool);
                    if (bodyDescriptor?.WritesFiles == true)
                        response.PlannedWrites.Add(step.StepId + "/" + bodyStep.StepId + ": file output via " + bodyStep.Tool);
                }
                continue;
            }

            var descriptor = GetToolDescriptor(step.Tool);
            if (descriptor == null)
                return ResolveFail(record, "scenario_tool_unavailable", "Операция сценария больше не поддерживается: " + step.Tool);
            if (!descriptor.SupportedContractVersions.Contains(step.ScenarioContractVersion))
                return ResolveFail(record, "scenario_tool_contract_changed", "Контракт операции изменился: " + step.Tool);

            var resolvedArguments = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var argument in step.Arguments)
            {
                if (TryGetParameterReference(argument.Value, out var parameterName))
                {
                    if (!values.TryGetValue(parameterName, out var value))
                        return ResolveFail(record, "scenario_parameter_missing", "Не задан параметр: " + parameterName);
                    resolvedArguments[argument.Key] = value.Clone();
                }
                else
                {
                    resolvedArguments[argument.Key] = argument.Value.Clone();
                }
            }

            if (descriptor.HasApply)
                resolvedArguments[descriptor.ApplyParameterName] = JsonSerializer.SerializeToElement(false);

            if (exact && scenario.ExactReplay?.SafetyEnvelope?.StepLimits != null &&
                scenario.ExactReplay.SafetyEnvelope.StepLimits.TryGetValue(step.StepId, out var runtimeLimit) &&
                runtimeLimit != null && runtimeLimit.MaxMatchedItems > 0)
            {
                ClampIntegerArgument(
                    resolvedArguments,
                    descriptor,
                    "maxMatchedItems",
                    runtimeLimit.MaxMatchedItems);
                ClampIntegerArgument(
                    resolvedArguments,
                    descriptor,
                    "maxSelectedItems",
                    runtimeLimit.MaxMatchedItems);
            }

            var applyOverrides = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (descriptor.HasApply && exactRequested)
            {
                applyOverrides[descriptor.ApplyParameterName] = JsonSerializer.SerializeToElement(true);
                foreach (var argumentName in descriptor.AuthorizationArguments)
                    applyOverrides[argumentName] = JsonSerializer.SerializeToElement(true);

                var approvedScaleGates = scenario.ExactReplay?.SafetyEnvelope?.StepLimits != null &&
                                         scenario.ExactReplay.SafetyEnvelope.StepLimits.TryGetValue(step.StepId, out var stepLimit)
                    ? stepLimit.ApprovedScaleGates
                    : null;
                foreach (var argumentName in descriptor.ScaleAuthorizationArguments)
                {
                    if (approvedScaleGates?.Contains(argumentName, StringComparer.OrdinalIgnoreCase) == true)
                        applyOverrides[argumentName] = JsonSerializer.SerializeToElement(true);
                }
            }

            var planSha = ComputePlanSha(step.Tool, resolvedArguments);
            response.Steps.Add(new ScenarioResolvedStep
            {
                StepId = step.StepId,
                Tool = step.Tool,
                ScenarioContractVersion = step.ScenarioContractVersion,
                PreviewArguments = resolvedArguments,
                ApplyArgumentOverrides = applyOverrides,
                PlanSha256 = planSha,
                MutatesModel = descriptor.MutatesModel,
                WritesFiles = descriptor.WritesFiles,
                ChangesSelection = descriptor.ChangesSelection,
                ReviewedWrites = step.ReviewedWrites.ToList(),
            });

            if (descriptor.MutatesModel)
                response.PlannedWrites.Add(step.StepId + ": Navisworks document changes via " + step.Tool);
            if (descriptor.WritesFiles)
                response.PlannedWrites.Add(step.StepId + ": file output via " + step.Tool);
            if (descriptor.ChangesSelection)
                response.PlannedWrites.Add(step.StepId + ": current Navisworks selection changes via " + step.Tool);
            foreach (var reviewedWrite in step.ReviewedWrites)
                response.PlannedWrites.Add(step.StepId + ": reviewed write behavior '" + reviewedWrite + "' will be previewed and requires the normal apply gate");
        }

        return response;
    }

    private static void ClampIntegerArgument(
        IDictionary<string, JsonElement> arguments,
        ToolDescriptor descriptor,
        string argumentName,
        int maximum)
    {
        if (descriptor == null || maximum < 1 ||
            descriptor.Method.GetParameters().All(parameter =>
                !string.Equals(parameter.Name, argumentName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var value = maximum;
        if (arguments.TryGetValue(argumentName, out var existing) &&
            existing.ValueKind == JsonValueKind.Number &&
            existing.TryGetInt32(out var existingValue) &&
            existingValue > 0)
        {
            value = Math.Min(existingValue, maximum);
        }
        arguments[argumentName] = JsonSerializer.SerializeToElement(value);
    }

    private Dictionary<string, JsonElement> MergeTemplateValues(
        ScenarioDocument scenario,
        IReadOnlyDictionary<string, JsonElement> provided,
        out List<string> errors)
    {
        errors = new List<string>();
        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        provided ??= new Dictionary<string, JsonElement>();

        foreach (var parameter in scenario.Parameters)
        {
            if (provided.TryGetValue(parameter.Name, out var value))
            {
                if (!ValueMatchesParameterType(value, parameter.Type))
                    errors.Add("Неверный тип параметра: " + parameter.Name);
                else if (!ParameterValueMeetsConstraints(parameter, value, out var constraintError))
                    errors.Add("Параметр " + parameter.Name + ": " + constraintError);
                else
                {
                    values[parameter.Name] = value.Clone();
                    if (parameter.Type is "filePath" or "directoryPath")
                    {
                        var pathValidation = new ScenarioValidationResult();
                        ValidateFixedPath(value, parameter.Name, pathValidation);
                        errors.AddRange(pathValidation.Errors.Select(error => "Небезопасный путь параметра " + parameter.Name + ": " + error));
                    }
                }
            }
            else if (parameter.Default.HasValue)
            {
                values[parameter.Name] = parameter.Default.Value.Clone();
            }
            else if (parameter.Required)
            {
                errors.Add("Требуется параметр: " + parameter.Name);
            }
        }

        foreach (var name in provided.Keys)
        {
            if (!scenario.Parameters.Any(parameter => string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)))
                errors.Add("Неизвестный параметр: " + name);
        }

        return values;
    }

    private static ScenarioResolveResponse ResolveFail(ScenarioStoredRecord record, string code, string message)
    {
        return new ScenarioResolveResponse
        {
            Ok = false,
            ErrorCode = code,
            ErrorMessage = message,
            ScenarioId = record.Scenario.ScenarioId,
            ScenarioName = record.Scenario.Name,
            ExecutionMode = record.Scenario.ExecutionMode,
            ScenarioSha256 = record.Sha256,
        };
    }

    private static List<ScenarioResolvedStep> ResolveControlBody(
        IEnumerable<ScenarioStepDefinition> body,
        IReadOnlyDictionary<string, JsonElement> values,
        bool exactRequested)
    {
        var result = new List<ScenarioResolvedStep>();
        foreach (var source in body ?? Enumerable.Empty<ScenarioStepDefinition>())
        {
            if (source == null)
                continue;
            var descriptor = GetToolDescriptor(source.Tool)
                ?? throw new InvalidOperationException("Scenario foreach body tool is unavailable: " + source.Tool);
            var arguments = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var argument in source.Arguments ?? new Dictionary<string, JsonElement>())
            {
                if (TryGetParameterReference(argument.Value, out var parameterName) &&
                    values.TryGetValue(parameterName, out var value))
                {
                    arguments[argument.Key] = value.Clone();
                }
                else
                    arguments[argument.Key] = argument.Value.Clone();
            }
            if (descriptor.HasApply)
                arguments[descriptor.ApplyParameterName] = JsonSerializer.SerializeToElement(false);

            var applyOverrides = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (descriptor.HasApply && exactRequested)
            {
                applyOverrides[descriptor.ApplyParameterName] = JsonSerializer.SerializeToElement(true);
                foreach (var authorizationArgument in descriptor.AuthorizationArguments)
                    applyOverrides[authorizationArgument] = JsonSerializer.SerializeToElement(true);
            }

            result.Add(new ScenarioResolvedStep
            {
                StepId = source.StepId,
                Tool = source.Tool,
                ScenarioContractVersion = source.ScenarioContractVersion,
                PreviewArguments = arguments,
                ApplyArgumentOverrides = applyOverrides,
                PlanSha256 = ComputePlanSha(source.Tool, arguments),
                MutatesModel = descriptor.MutatesModel,
                WritesFiles = descriptor.WritesFiles,
                ChangesSelection = descriptor.ChangesSelection,
                ReviewedWrites = source.ReviewedWrites.ToList(),
            });
        }
        return result;
    }
}
