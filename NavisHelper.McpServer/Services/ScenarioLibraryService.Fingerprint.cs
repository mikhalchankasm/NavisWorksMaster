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
    private static string ComputePlanSha(string tool, IReadOnlyDictionary<string, JsonElement> arguments)
    {
        var root = new JsonObject { ["tool"] = tool };
        root["arguments"] = BuildCanonicalArguments(arguments);
        return ComputeSha256(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }

    private static string ComputeExactReplayFingerprint(ScenarioDraft scenario)
    {
        var steps = new JsonArray();
        foreach (var step in scenario.Steps)
        {
            if (string.Equals(step.Control, "foreach", StringComparison.Ordinal))
            {
                steps.Add(JsonSerializer.SerializeToNode(step, StoreJsonOptions));
                continue;
            }
            var descriptor = GetToolDescriptor(step.Tool)
                ?? throw new InvalidOperationException("Scenario tool is unavailable: " + step.Tool);
            var resolved = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var argument in step.Arguments)
            {
                if (TryGetParameterReference(argument.Value, out var parameterName))
                    resolved[argument.Key] = scenario.ExactReplay.FixedParameters[parameterName].Clone();
                else
                    resolved[argument.Key] = argument.Value.Clone();
            }
            if (descriptor.HasApply)
                resolved[descriptor.ApplyParameterName] = JsonSerializer.SerializeToElement(false);

            steps.Add(new JsonObject
            {
                ["stepId"] = step.StepId,
                ["tool"] = step.Tool,
                ["scenarioContractVersion"] = step.ScenarioContractVersion,
                ["arguments"] = BuildCanonicalArguments(resolved),
            });
        }

        var root = new JsonObject
        {
            ["schemaVersion"] = scenario.SchemaVersion,
            ["executionMode"] = scenario.ExecutionMode,
            ["context"] = JsonSerializer.SerializeToNode(scenario.Context, StoreJsonOptions),
            ["parameters"] = BuildCanonicalScenarioParameters(scenario),
            ["fixedParameters"] = BuildCanonicalArguments(scenario.ExactReplay.FixedParameters),
            ["contextPolicy"] = scenario.ExactReplay.ContextPolicy,
            ["writePolicy"] = scenario.ExactReplay.WritePolicy,
            ["stepLimits"] = BuildCanonicalSafetyLimits(scenario.ExactReplay.SafetyEnvelope.StepLimits),
            ["steps"] = steps,
        };
        return ComputeSha256(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }

    private static JsonNode BuildCanonicalScenarioParameters(ScenarioDraft scenario)
    {
        if (scenario.SchemaVersion != 1)
            return JsonSerializer.SerializeToNode(scenario.Parameters, StoreJsonOptions);

        var parameters = new JsonArray();
        foreach (var parameter in scenario.Parameters ?? new List<ScenarioParameterDefinition>())
        {
            var item = new JsonObject
            {
                ["name"] = parameter.Name,
                ["type"] = parameter.Type,
                ["title"] = parameter.Title,
                ["description"] = parameter.Description,
                ["required"] = parameter.Required,
            };
            if (parameter.Default.HasValue)
                item["default"] = JsonNode.Parse(parameter.Default.Value.GetRawText());
            parameters.Add(item);
        }
        return parameters;
    }

    private static JsonObject BuildCanonicalArguments(IReadOnlyDictionary<string, JsonElement> arguments)
    {
        var result = new JsonObject();
        foreach (var argument in arguments.OrderBy(item => item.Key, StringComparer.Ordinal))
            result[argument.Key] = JsonNode.Parse(argument.Value.GetRawText());
        return result;
    }

    private static JsonObject BuildCanonicalSafetyLimits(
        IReadOnlyDictionary<string, ScenarioStepSafetyLimit> limits)
    {
        var result = new JsonObject();
        foreach (var item in limits.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            result[item.Key] = new JsonObject
            {
                ["maxMatchedItems"] = item.Value.MaxMatchedItems,
                ["maxModelWrites"] = item.Value.MaxModelWrites,
                ["maxFileWrites"] = item.Value.MaxFileWrites,
                ["approvedScaleGates"] = new JsonArray(
                    item.Value.ApprovedScaleGates
                        .OrderBy(gate => gate, StringComparer.Ordinal)
                        .Select(gate => (JsonNode)JsonValue.Create(gate))
                        .ToArray()),
            };
            if (item.Value.MaxDurationSeconds.HasValue)
                result[item.Key]["maxDurationSeconds"] = item.Value.MaxDurationSeconds.Value;
        }
        return result;
    }

    private static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string NormalizeSha(string value)
    {
        return (value ?? string.Empty).Trim().Replace("sha256:", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExactReplay(ScenarioDraft scenario)
    {
        return string.Equals(scenario?.ExecutionMode, "exactReplay", StringComparison.Ordinal);
    }
}
