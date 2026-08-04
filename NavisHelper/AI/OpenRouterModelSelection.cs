using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.AI
{
    internal sealed class OpenRouterModelChoice
    {
        internal OpenRouterModelChoice(OpenRouterModelInfo model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            Model = model;
            Id = model.Id;
            DisplayName = string.IsNullOrWhiteSpace(model.Name)
                ? model.Id
                : model.Name.Trim();
            DisplayText = DisplayName + " — " + Id;
        }

        internal OpenRouterModelInfo Model { get; }
        public string Id { get; }
        public string DisplayName { get; }
        public string DisplayHeader => DisplayName + " — " + Id;
        public string DisplayText { get; private set; }
        public string CapabilityText { get; private set; } = string.Empty;

        internal void Relocalize(
            Func<string, string> getString,
            Func<string, object[], string> format)
        {
            CapabilityText = OpenRouterModelCapabilities.Format(
                Model,
                getString,
                format);
            DisplayText = DisplayName + " — " + Id +
                          (CapabilityText.Length == 0
                              ? string.Empty
                              : Environment.NewLine + CapabilityText);
        }

        public override string ToString()
        {
            return DisplayText;
        }
    }

    internal static class OpenRouterModelCapabilities
    {
        internal static string Format(
            OpenRouterModelInfo model,
            Func<string, string> getString,
            Func<string, object[], string> format)
        {
            if (model == null)
                return string.Empty;
            if (getString == null)
                throw new ArgumentNullException(nameof(getString));
            if (format == null)
                throw new ArgumentNullException(nameof(format));

            var inputs = FormatModalities(model.InputModalities, getString);
            var outputs = FormatModalities(model.OutputModalities, getString);
            var parts = new List<string>
            {
                format(
                    "Settings_Ai_Model_Capability_Flow_Format",
                    new object[]
                    {
                        string.Join(" + ", inputs),
                        string.Join(" + ", outputs)
                    }),
                getString("Settings_Ai_Model_Capability_StructuredOutput")
            };
            if (model.InputModalities.Count > 1)
                parts.Insert(
                    1,
                    getString("Settings_Ai_Model_Capability_Multimodal"));
            if (model.Reasoning?.Mandatory == true)
                parts.Add(getString(
                    "Settings_Ai_Model_Capability_ReasoningRequired"));
            else if (model.Reasoning != null)
                parts.Add(getString(
                    "Settings_Ai_Model_Capability_ReasoningOptional"));
            if (model.ContextLength.GetValueOrDefault() > 0)
                parts.Add(format(
                    "Settings_Ai_Model_Capability_Context_Format",
                    new object[] { model.ContextLength.Value }));
            return string.Join(" · ", parts);
        }

        private static IReadOnlyList<string> FormatModalities(
            IEnumerable<string> modalities,
            Func<string, string> getString)
        {
            return (modalities ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .OrderBy(ModalityRank)
                .ThenBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Select(value => LocalizeModality(value, getString))
                .ToArray();
        }

        private static string LocalizeModality(
            string modality,
            Func<string, string> getString)
        {
            switch ((modality ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "text":
                    return getString("Settings_Ai_Model_Capability_Text");
                case "image":
                    return getString("Settings_Ai_Model_Capability_Image");
                case "audio":
                    return getString("Settings_Ai_Model_Capability_Audio");
                case "file":
                case "files":
                    return getString("Settings_Ai_Model_Capability_Files");
                case "video":
                    return getString("Settings_Ai_Model_Capability_Video");
                default:
                    return modality;
            }
        }

        private static int ModalityRank(string modality)
        {
            switch ((modality ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "text": return 0;
                case "image": return 1;
                case "audio": return 2;
                case "file":
                case "files": return 3;
                case "video": return 4;
                default: return 5;
            }
        }
    }

    internal sealed class OpenRouterModelPicker
    {
        private IReadOnlyList<OpenRouterModelChoice> _all =
            Array.Empty<OpenRouterModelChoice>();

        internal string SelectedModelId { get; private set; } = string.Empty;
        internal string CurrentQuery { get; private set; } = string.Empty;

        internal void Replace(
            IEnumerable<OpenRouterModelChoice> choices,
            string selectedModelId)
        {
            _all = (choices ?? Array.Empty<OpenRouterModelChoice>()).ToArray();
            if (_all.Any(choice => string.Equals(
                    choice.Id,
                    selectedModelId,
                    StringComparison.OrdinalIgnoreCase)))
                SelectedModelId = selectedModelId ?? string.Empty;
            else if (_all.Count == 0 && string.IsNullOrWhiteSpace(selectedModelId))
                SelectedModelId = string.Empty;
        }

        internal void Select(string modelId)
        {
            if (_all.Any(choice => string.Equals(
                    choice.Id,
                    modelId,
                    StringComparison.OrdinalIgnoreCase)))
                SelectedModelId = modelId ?? string.Empty;
        }

        internal IReadOnlyList<OpenRouterModelChoice> Filter(string query)
        {
            var term = (query ?? string.Empty).Trim();
            CurrentQuery = query ?? string.Empty;
            if (term.Length == 0)
                return _all;
            return _all.Where(choice =>
                    choice.DisplayName.IndexOf(
                        term,
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    choice.Id.IndexOf(
                        term,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
        }

        internal void Relocalize(
            Func<string, string> getString,
            Func<string, object[], string> format)
        {
            foreach (var choice in _all)
                choice.Relocalize(getString, format);
        }
    }

    internal static class OpenRouterModelSelection
    {
        internal static string MigrationCandidate(string value)
        {
            var candidate = (value ?? string.Empty).Trim();
            return IsFullId(candidate) ? candidate : string.Empty;
        }

        internal static bool IsFullId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var separator = value.IndexOf('/');
            return separator > 0 && separator < value.Length - 1;
        }

        internal static IReadOnlyList<OpenRouterModelChoice> CompatibleChoices(
            OpenRouterCatalogResult catalog)
        {
            if (catalog == null || !catalog.IsAvailable)
                return Array.Empty<OpenRouterModelChoice>();
            return catalog.Models.Values
                .Where(model => model.IsColoringCompatible)
                .OrderBy(model => model.Reasoning?.Mandatory == true ? 1 : 0)
                .ThenBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
                .Select(model => new OpenRouterModelChoice(model))
                .ToArray();
        }

        internal static OpenRouterModelInfo Restore(
            OpenRouterCatalogResult catalog,
            string savedModelId)
        {
            if (catalog == null || !catalog.IsAvailable)
                return null;
            OpenRouterModelInfo model;
            return catalog.Models.TryGetValue(
                       MigrationCandidate(savedModelId),
                       out model) &&
                   model.IsColoringCompatible
                ? model
                : null;
        }
    }

    internal static class OpenRouterColorRequestLimits
    {
        internal const int MaxUniqueObjectNames = 200;

        internal const int MinimumOutputBudget = 512;

        internal static int CalculateContentBudget(
            IReadOnlyCollection<string> objectNames)
        {
            if (objectNames == null || objectNames.Count == 0)
                return 0;
            // One token per UTF-16 code unit is a deliberately conservative
            // upper bound for names, plus conservative per-item JSON overhead.
            var nameCharacters = objectNames.Aggregate(
                0L,
                (total, name) => total + JsonEscapedLength(name));
            var estimated = 192L + objectNames.Count * 48L + nameCharacters;
            return (int)Math.Min(
                int.MaxValue,
                Math.Max(MinimumOutputBudget, estimated));
        }

        private static int JsonEscapedLength(string value)
        {
            var length = 0;
            foreach (var character in value ?? string.Empty)
            {
                if (character == '"' || character == '\\')
                    length += 2;
                else if (character < 0x20)
                    length += 6;
                else
                    length++;
            }
            return length;
        }
    }

    internal enum OpenRouterColorRequestPolicyResult
    {
        Allowed = 0,
        IncompatibleModel,
        InsufficientOutputBudget,
        UnsupportedReasoningPolicy
    }

    internal sealed class OpenRouterColorRequestPolicy
    {
        private OpenRouterColorRequestPolicy(
            OpenRouterColorRequestPolicyResult result,
            int contentBudget,
            int outputBudget,
            string reasoningPolicy,
            bool? reasoningEnabled,
            string reasoningEffort)
        {
            Decision = result;
            ContentBudget = contentBudget;
            OutputBudget = outputBudget;
            ReasoningPolicy = reasoningPolicy ?? string.Empty;
            ReasoningEnabled = reasoningEnabled;
            ReasoningEffort = reasoningEffort ?? string.Empty;
        }

        internal OpenRouterColorRequestPolicyResult Decision { get; }
        internal bool MaySend => Decision == OpenRouterColorRequestPolicyResult.Allowed;
        internal int ContentBudget { get; }
        internal int OutputBudget { get; }
        internal string ReasoningPolicy { get; }
        internal bool? ReasoningEnabled { get; }
        internal string ReasoningEffort { get; }
        internal AiColorOutcomeKind FailureOutcomeKind
        {
            get
            {
                switch (Decision)
                {
                    case OpenRouterColorRequestPolicyResult.IncompatibleModel:
                        return AiColorOutcomeKind.ModelIncompatible;
                    case OpenRouterColorRequestPolicyResult.InsufficientOutputBudget:
                        return AiColorOutcomeKind.InsufficientOutputBudget;
                    case OpenRouterColorRequestPolicyResult.UnsupportedReasoningPolicy:
                        return AiColorOutcomeKind.UnsupportedReasoningPolicy;
                    default:
                        return AiColorOutcomeKind.InvalidRequest;
                }
            }
        }

        internal static OpenRouterColorRequestPolicy Evaluate(
            OpenRouterModelInfo model,
            IReadOnlyCollection<string> objectNames)
        {
            var content = OpenRouterColorRequestLimits.CalculateContentBudget(
                objectNames);
            if (model == null || !model.IsColoringCompatible)
                return new OpenRouterColorRequestPolicy(
                    OpenRouterColorRequestPolicyResult.IncompatibleModel,
                    content, 0, "incompatible", null, string.Empty);

            var output = content;
            var reasoningPolicy = "not-supported";
            bool? reasoningEnabled = null;
            var reasoningEffort = string.Empty;
            var reasoning = model.Reasoning;
            if (reasoning?.Mandatory == true)
            {
                if (!model.SupportedParameters.Contains("reasoning"))
                    return UnsupportedReasoning(content, "required-control-unavailable");
                reasoningEffort = LowestEffort(reasoning);
                if (reasoningEffort.Length > 0)
                {
                    var ratio = EffortRatio(reasoningEffort);
                    output = (int)Math.Min(
                        int.MaxValue,
                        Math.Ceiling(content / (1d - ratio)));
                    reasoningPolicy = "required:" + reasoningEffort;
                }
                else
                {
                    return UnsupportedReasoning(
                        content,
                        "required-minimum-unavailable");
                }
            }
            else if (reasoning?.Mandatory == false)
            {
                if (model.SupportedParameters.Contains("reasoning"))
                {
                    reasoningEnabled = false;
                    reasoningPolicy = "disabled";
                }
                else
                {
                    return UnsupportedReasoning(
                        content,
                        "optional-control-unavailable");
                }
            }
            else if (reasoning != null)
            {
                return UnsupportedReasoning(
                    content,
                    "mandatory-state-unknown");
            }

            var providerMaximum = model.MaxCompletionTokens.GetValueOrDefault();
            return new OpenRouterColorRequestPolicy(
                output <= providerMaximum
                    ? OpenRouterColorRequestPolicyResult.Allowed
                    : OpenRouterColorRequestPolicyResult.InsufficientOutputBudget,
                content,
                output,
                reasoningPolicy,
                reasoningEnabled,
                reasoningEffort);
        }

        private static OpenRouterColorRequestPolicy UnsupportedReasoning(
            int contentBudget,
            string diagnostic)
        {
            return new OpenRouterColorRequestPolicy(
                OpenRouterColorRequestPolicyResult.UnsupportedReasoningPolicy,
                contentBudget,
                contentBudget,
                diagnostic,
                null,
                string.Empty);
        }

        private static string LowestEffort(OpenRouterReasoningInfo reasoning)
        {
            foreach (var effort in new[] { "minimal", "low", "medium", "high", "xhigh", "max" })
                if (reasoning.SupportedEfforts.Contains(effort))
                    return effort;
            return string.Empty;
        }

        private static double EffortRatio(string effort)
        {
            switch (effort)
            {
                case "minimal": return 0.10d;
                case "low": return 0.20d;
                case "high": return 0.80d;
                case "xhigh":
                case "max": return 0.95d;
                default: return 0.50d;
            }
        }
    }
}
