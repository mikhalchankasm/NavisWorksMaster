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
    private static ScenarioDocument CopyDraft(ScenarioDraft draft)
    {
        var json = JsonSerializer.Serialize(draft, StoreJsonOptions);
        var document = JsonSerializer.Deserialize<ScenarioDocument>(json, StoreJsonOptions)
            ?? throw new InvalidOperationException("Could not clone scenario draft.");
        NormalizeDocumentCollections(document);
        return document;
    }

    private static void NormalizeDocumentCollections(ScenarioDocument scenario)
    {
        NormalizeDraftCollections(scenario);
    }

    private static void NormalizeDraftCollections(ScenarioDraft scenario)
    {
        scenario.Parameters ??= new List<ScenarioParameterDefinition>();
        scenario.Steps ??= new List<ScenarioStepDefinition>();
        foreach (var step in scenario.Steps)
        {
            if (step == null)
                continue;
            step.Arguments = new Dictionary<string, JsonElement>(
                step.Arguments ?? new Dictionary<string, JsonElement>(),
                StringComparer.OrdinalIgnoreCase);
            step.ReviewedWrites ??= new List<string>();
            step.Body ??= new List<ScenarioStepDefinition>();
            foreach (var bodyStep in step.Body)
            {
                if (bodyStep == null)
                    continue;
                bodyStep.Arguments = new Dictionary<string, JsonElement>(
                    bodyStep.Arguments ?? new Dictionary<string, JsonElement>(),
                    StringComparer.OrdinalIgnoreCase);
                bodyStep.ReviewedWrites ??= new List<string>();
                bodyStep.Body ??= new List<ScenarioStepDefinition>();
            }
        }

        if (scenario.ExactReplay == null)
            return;
        scenario.ExactReplay.FixedParameters = new Dictionary<string, JsonElement>(
            scenario.ExactReplay.FixedParameters ?? new Dictionary<string, JsonElement>(),
            StringComparer.OrdinalIgnoreCase);
        if (scenario.ExactReplay.SafetyEnvelope == null)
            return;
        scenario.ExactReplay.SafetyEnvelope.StepLimits = new Dictionary<string, ScenarioStepSafetyLimit>(
            scenario.ExactReplay.SafetyEnvelope.StepLimits ?? new Dictionary<string, ScenarioStepSafetyLimit>(),
            StringComparer.OrdinalIgnoreCase);
    }
}
