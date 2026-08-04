using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NavisHelper.AI
{
    internal enum AiPanelOutcomeKind
    {
        None = 0,
        Starting,
        Success,
        Failure
    }

    internal sealed class AiPanelOutcome
    {
        private static readonly AiPanelOutcome Empty =
            new AiPanelOutcome(
                AiPanelOutcomeKind.None,
                AiColorSource.None,
                null,
                0,
                null,
                null,
                0);

        private AiPanelOutcome(
            AiPanelOutcomeKind kind,
            AiColorSource source,
            string modelId,
            int colorScheme,
            IEnumerable<string> objectNames,
            IDictionary<string, string> colors,
            int appliedCount,
            AiColorOutcomeKind failureKind = AiColorOutcomeKind.InvalidRequest)
        {
            Kind = kind;
            Source = source;
            ModelId = modelId ?? string.Empty;
            ColorScheme = colorScheme;
            ObjectNames = (objectNames ?? Array.Empty<string>()).ToArray();
            Colors = new Dictionary<string, string>(
                colors ?? new Dictionary<string, string>(),
                StringComparer.Ordinal);
            AppliedCount = appliedCount;
            FailureKind = failureKind;
        }

        internal AiPanelOutcomeKind Kind { get; }
        internal AiColorSource Source { get; }
        internal string ModelId { get; }
        internal int ColorScheme { get; }
        internal IReadOnlyList<string> ObjectNames { get; }
        internal IReadOnlyDictionary<string, string> Colors { get; }
        internal int AppliedCount { get; }
        internal AiColorOutcomeKind FailureKind { get; }

        internal static AiPanelOutcome None => Empty;

        internal static AiPanelOutcome Starting(
            string modelId,
            int colorScheme,
            IEnumerable<string> objectNames)
        {
            return new AiPanelOutcome(
                AiPanelOutcomeKind.Starting,
                AiColorSource.OpenRouter,
                modelId,
                colorScheme,
                objectNames,
                null,
                0);
        }

        internal static AiPanelOutcome Success(
            AiColorSource source,
            string modelId,
            int colorScheme,
            IEnumerable<string> objectNames,
            IDictionary<string, string> colors,
            int appliedCount)
        {
            return new AiPanelOutcome(
                AiPanelOutcomeKind.Success,
                source,
                modelId,
                colorScheme,
                objectNames,
                colors,
                appliedCount);
        }

        internal static AiPanelOutcome Failure(
            AiColorOutcomeKind failureKind,
            string modelId = null,
            IEnumerable<string> objectNames = null)
        {
            return new AiPanelOutcome(
                AiPanelOutcomeKind.Failure,
                AiColorSource.None,
                modelId,
                0,
                objectNames,
                null,
                0,
                failureKind);
        }
    }

    internal static class AiPanelOutcomeFormatter
    {
        internal static string Format(
            AiPanelOutcome outcome,
            Func<string, string> getString,
            Func<string, object[], string> format,
            Func<int, string> getSchemeName)
        {
            if (getString == null)
                throw new ArgumentNullException(nameof(getString));
            if (format == null)
                throw new ArgumentNullException(nameof(format));
            outcome = outcome ?? AiPanelOutcome.None;

            switch (outcome.Kind)
            {
                case AiPanelOutcomeKind.Starting:
                    return format(
                        "Panel_Colors_Ai_Starting_Semantic_Format",
                        new object[]
                        {
                            outcome.ModelId,
                            getSchemeName == null
                                ? outcome.ColorScheme.ToString()
                                : getSchemeName(outcome.ColorScheme),
                            outcome.ObjectNames.Count
                        });
                case AiPanelOutcomeKind.Success:
                    return FormatSuccess(outcome, getString, format);
                case AiPanelOutcomeKind.Failure:
                    return FormatFailure(outcome, getString, format);
                default:
                    return string.Empty;
            }
        }

        private static string FormatFailure(
            AiPanelOutcome outcome,
            Func<string, string> getString,
            Func<string, object[], string> format)
        {
            var builder = new StringBuilder();
            builder.AppendLine(format(
                "Panel_Colors_Ai_Model_Format",
                new object[]
                {
                    string.IsNullOrWhiteSpace(outcome.ModelId)
                        ? getString("Panel_Colors_Ai_Model_NotApplicable")
                        : outcome.ModelId
                }));
            builder.AppendLine(getString(FailureResource(outcome.FailureKind)));
            builder.AppendLine(getString("Panel_Colors_Ai_ZeroApplied"));
            builder.Append(getString(FailureSuggestionResource(
                outcome.FailureKind)));
            return builder.ToString();
        }

        private static string FormatSuccess(
            AiPanelOutcome outcome,
            Func<string, string> getString,
            Func<string, object[], string> format)
        {
            var groups = outcome.Colors
                .GroupBy(pair => pair.Value, StringComparer.Ordinal)
                .ToList();
            var sourceResource =
                outcome.Source == AiColorSource.LocalPalette
                    ? "Panel_Colors_Ai_Source_LocalPalette"
                    : "Panel_Colors_Ai_Source_OpenRouter";
            var builder = new StringBuilder();
            builder.AppendLine(format(
                "Panel_Colors_Ai_Source_Format",
                new object[] { getString(sourceResource) }));
            builder.AppendLine(format(
                "Panel_Colors_Ai_Model_Format",
                new object[]
                {
                    string.IsNullOrWhiteSpace(outcome.ModelId)
                        ? getString("Panel_Colors_Ai_Model_NotApplicable")
                        : outcome.ModelId
                }));
            builder.AppendLine(format(
                "Panel_Colors_Ai_ResultSummary_Format",
                new object[] { outcome.AppliedCount, groups.Count }));
            builder.AppendLine(new string('=', 50));

            var groupIndex = 1;
            foreach (var group in groups)
            {
                builder.AppendLine(format(
                    "Panel_Colors_Ai_GroupDetail_Format",
                    new object[] { groupIndex, group.Key }));
                foreach (var pair in group)
                    builder.AppendLine("  " + pair.Key);
                groupIndex++;
            }
            return builder.ToString();
        }

        internal static string FailureResource(AiColorOutcomeKind kind)
        {
            switch (kind)
            {
                case AiColorOutcomeKind.MissingKey:
                    return "Panel_Colors_Ai_KeyRequired";
                case AiColorOutcomeKind.NoSelection:
                    return "Panel_Colors_Ai_NoSelection";
                case AiColorOutcomeKind.NoColorableObjects:
                    return "Panel_Colors_Ai_NoColorableObjects";
                case AiColorOutcomeKind.NoObjectNames:
                    return "Panel_Colors_Ai_NoObjectNames";
                case AiColorOutcomeKind.InvalidModelId:
                    return "Panel_Colors_Ai_InvalidModelId";
                case AiColorOutcomeKind.CatalogUnavailable:
                    return "Panel_Colors_Ai_CatalogUnavailable";
                case AiColorOutcomeKind.ModelNotSelected:
                    return "Panel_Colors_Ai_ModelNotSelected";
                case AiColorOutcomeKind.ModelIncompatible:
                    return "Panel_Colors_Ai_ModelIncompatible";
                case AiColorOutcomeKind.TooManyObjects:
                    return "Panel_Colors_Ai_TooManyObjects";
                case AiColorOutcomeKind.InsufficientOutputBudget:
                    return "Panel_Colors_Ai_InsufficientOutputBudget";
                case AiColorOutcomeKind.UnsupportedReasoningPolicy:
                    return "Panel_Colors_Ai_UnsupportedReasoningPolicy";
                case AiColorOutcomeKind.ModelUnavailable:
                    return "Panel_Colors_Ai_ModelUnavailable";
                case AiColorOutcomeKind.Unauthorized:
                    return "Panel_Colors_Ai_Unauthorized";
                case AiColorOutcomeKind.RateLimited:
                    return "Panel_Colors_Ai_RateLimited";
                case AiColorOutcomeKind.Timeout:
                    return "Panel_Colors_Ai_Timeout";
                case AiColorOutcomeKind.Cancelled:
                    return "Panel_Colors_Ai_Cancelled";
                case AiColorOutcomeKind.Network:
                    return "Panel_Colors_Ai_NetworkError";
                case AiColorOutcomeKind.ServiceUnavailable:
                    return "Panel_Colors_Ai_ServiceUnavailable";
                case AiColorOutcomeKind.BadRequest:
                    return "Panel_Colors_Ai_BadRequest";
                case AiColorOutcomeKind.ResponseRefused:
                    return "Panel_Colors_Ai_ResponseRefused";
                case AiColorOutcomeKind.MissingAssistantContent:
                    return "Panel_Colors_Ai_MissingAssistantContent";
                case AiColorOutcomeKind.TruncatedResponse:
                    return "Panel_Colors_Ai_TruncatedResponse";
                case AiColorOutcomeKind.StructuredPayloadInvalid:
                    return "Panel_Colors_Ai_StructuredPayloadInvalid";
                case AiColorOutcomeKind.IncompleteObjectSet:
                    return "Panel_Colors_Ai_IncompleteObjectSet";
                case AiColorOutcomeKind.InvalidResponse:
                    return "Panel_Colors_Ai_InvalidResponse";
                case AiColorOutcomeKind.WorkerMissing:
                    return "Panel_Colors_Ai_WorkerMissing";
                case AiColorOutcomeKind.WorkerRuntimeMissing:
                    return "Panel_Colors_Ai_WorkerRuntimeMissing";
                case AiColorOutcomeKind.WorkerStartupFailed:
                    return "Panel_Colors_Ai_WorkerStartupFailed";
                case AiColorOutcomeKind.WorkerFailed:
                    return "Panel_Colors_Ai_WorkerFailed";
                case AiColorOutcomeKind.WorkerInternalFailure:
                    return "Panel_Colors_Ai_WorkerInternalFailure";
                case AiColorOutcomeKind.ProtocolMismatch:
                    return "Panel_Colors_Ai_ProtocolMismatch";
                case AiColorOutcomeKind.NavisworksError:
                    return "Panel_Colors_Ai_NavisworksError";
                case AiColorOutcomeKind.AlreadyRunning:
                    return "Panel_Colors_Ai_AlreadyRunning";
                case AiColorOutcomeKind.DocumentChanged:
                    return "Panel_Colors_Ai_DocumentChanged";
                default:
                    return "Panel_Colors_Ai_RequestFailed";
            }
        }

        internal static string FailureSuggestionResource(
            AiColorOutcomeKind kind)
        {
            switch (kind)
            {
                case AiColorOutcomeKind.InsufficientOutputBudget:
                    return "Panel_Colors_Ai_InsufficientOutputBudgetSuggestion";
                case AiColorOutcomeKind.UnsupportedReasoningPolicy:
                    return "Panel_Colors_Ai_UnsupportedReasoningPolicySuggestion";
                default:
                    return "Panel_Colors_Ai_FailureSuggestion";
            }
        }
    }

    internal enum AiConnectionDisplayState
    {
        Disconnected = 0,
        Checking,
        Connected,
        Ready,
        MissingKey,
        Unauthorized,
        RateLimited,
        Timeout,
        Cancelled,
        NetworkUnavailable,
        WorkerMissing,
        WorkerRuntimeMissing,
        WorkerStartupFailed,
        WorkerFailed,
        WorkerInternalFailure,
        ProtocolMismatch,
        StorageFailed
    }

    internal static class AiConnectionStatusMapper
    {
        internal static string ResourceKey(AiConnectionDisplayState state)
        {
            switch (state)
            {
                case AiConnectionDisplayState.Checking:
                    return "Settings_Ai_Status_Checking";
                case AiConnectionDisplayState.Connected:
                    return "Settings_Ai_Status_Connected";
                case AiConnectionDisplayState.Ready:
                    return "Settings_Ai_Status_Ready";
                case AiConnectionDisplayState.MissingKey:
                    return "Settings_Ai_Status_KeyRequired";
                case AiConnectionDisplayState.Unauthorized:
                    return "Settings_Ai_Status_InvalidKey";
                case AiConnectionDisplayState.RateLimited:
                    return "Settings_Ai_Status_RateLimited";
                case AiConnectionDisplayState.Timeout:
                    return "Settings_Ai_Status_Timeout";
                case AiConnectionDisplayState.Cancelled:
                    return "Settings_Ai_Status_Cancelled";
                case AiConnectionDisplayState.NetworkUnavailable:
                    return "Settings_Ai_Status_NetworkUnavailable";
                case AiConnectionDisplayState.WorkerMissing:
                    return "Settings_Ai_Status_WorkerMissing";
                case AiConnectionDisplayState.WorkerRuntimeMissing:
                    return "Settings_Ai_Status_WorkerRuntimeMissing";
                case AiConnectionDisplayState.WorkerStartupFailed:
                    return "Settings_Ai_Status_WorkerStartupFailed";
                case AiConnectionDisplayState.WorkerFailed:
                    return "Settings_Ai_Status_WorkerFailed";
                case AiConnectionDisplayState.WorkerInternalFailure:
                    return "Settings_Ai_Status_WorkerInternalFailure";
                case AiConnectionDisplayState.ProtocolMismatch:
                    return "Settings_Ai_Status_ProtocolMismatch";
                case AiConnectionDisplayState.StorageFailed:
                    return "Settings_Ai_Status_StorageFailed";
                default:
                    return "Settings_Ai_Status_Disconnected";
            }
        }
    }

    internal sealed class AiModelDisplay
    {
        internal AiModelDisplay(
            string statusResource,
            bool isReady)
        {
            StatusResource = statusResource;
            IsReady = isReady;
        }

        internal string StatusResource { get; }
        internal bool IsReady { get; }
    }

    internal static class AiModelStatusMapper
    {
        internal static AiModelDisplay Evaluate(
            bool connected,
            OpenRouterCatalogResult catalog,
            string modelId)
        {
            if (!connected)
                return new AiModelDisplay(
                    "Settings_Ai_Model_ConnectFirst",
                    false);
            if (catalog == null)
                return new AiModelDisplay(
                    "Settings_Ai_Model_Checking",
                    false);
            if (!catalog.IsAvailable)
                return CatalogFailure(catalog.FailureKind);
            if (string.IsNullOrWhiteSpace(modelId))
                return new AiModelDisplay(
                    "Settings_Ai_Model_Choose",
                    false);

            OpenRouterModelInfo model;
            if (!catalog.Models.TryGetValue(modelId ?? string.Empty, out model))
                return new AiModelDisplay(
                    "Settings_Ai_Model_NotFound",
                    false);
            if (!model.IsColoringCompatible)
                return new AiModelDisplay(
                    "Settings_Ai_Model_Incompatible",
                    false);
            return new AiModelDisplay(
                "Settings_Ai_Model_Ready",
                true);
        }

        private static AiModelDisplay CatalogFailure(
            OpenRouterFailureKind failureKind)
        {
            switch (failureKind)
            {
                case OpenRouterFailureKind.Timeout:
                    return new AiModelDisplay(
                        "Settings_Ai_Model_CatalogTimeout",
                        false);
                case OpenRouterFailureKind.Network:
                case OpenRouterFailureKind.ServiceUnavailable:
                    return new AiModelDisplay(
                        "Settings_Ai_Model_CatalogNetworkUnavailable",
                        false);
                case OpenRouterFailureKind.WorkerMissing:
                case OpenRouterFailureKind.WorkerRuntimeMissing:
                case OpenRouterFailureKind.WorkerStartupFailed:
                case OpenRouterFailureKind.WorkerFailed:
                case OpenRouterFailureKind.WorkerInternalFailure:
                case OpenRouterFailureKind.ProtocolMismatch:
                    return new AiModelDisplay(
                        "Settings_Ai_Model_CatalogWorkerFailure",
                        false);
                default:
                    return new AiModelDisplay(
                        "Settings_Ai_Model_CatalogUnavailable",
                        false);
            }
        }
    }

    internal static class AiCancellationClassifier
    {
        internal static AiColorOutcomeKind Classify(
            bool documentChanged,
            bool timedOut)
        {
            if (documentChanged)
                return AiColorOutcomeKind.DocumentChanged;
            return timedOut
                ? AiColorOutcomeKind.Timeout
                : AiColorOutcomeKind.Cancelled;
        }
    }
}
