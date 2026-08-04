using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.AI
{
    internal enum OpenRouterFailureKind
    {
        None = 0,
        MissingKey,
        Unauthorized,
        RateLimited,
        Timeout,
        Cancelled,
        Network,
        ServiceUnavailable,
        BadRequest,
        ModelUnavailable,
        ResponseRefused,
        MissingAssistantContent,
        TruncatedResponse,
        StructuredPayloadInvalid,
        IncompleteObjectSet,
        InvalidResponse,
        WorkerMissing,
        WorkerRuntimeMissing,
        WorkerStartupFailed,
        WorkerFailed,
        WorkerInternalFailure,
        ProtocolMismatch,
        StorageFailed,
        InsufficientOutputBudget,
        UnsupportedReasoningPolicy
    }

    internal sealed class OpenRouterValidationResult
    {
        private OpenRouterValidationResult(
            bool isSuccess,
            OpenRouterFailureKind failureKind,
            int? httpStatus)
        {
            IsSuccess = isSuccess;
            FailureKind = failureKind;
            HttpStatus = httpStatus;
        }

        internal bool IsSuccess { get; }
        internal OpenRouterFailureKind FailureKind { get; }
        internal int? HttpStatus { get; }
        internal string DiagnosticCode =>
            HttpStatus.HasValue
                ? FailureKind + "_http_" + HttpStatus.Value
                : FailureKind.ToString();

        internal static OpenRouterValidationResult Success()
        {
            return new OpenRouterValidationResult(
                true,
                OpenRouterFailureKind.None,
                null);
        }

        internal static OpenRouterValidationResult Failure(
            OpenRouterFailureKind failureKind,
            int? httpStatus = null)
        {
            return new OpenRouterValidationResult(
                false,
                failureKind,
                httpStatus);
        }
    }

    internal sealed class OpenRouterModelInfo
    {
        internal OpenRouterModelInfo(
            string id,
            string name,
            IEnumerable<string> supportedParameters)
            : this(
                id,
                name,
                (supportedParameters ?? Array.Empty<string>())
                    .Concat(new[] { "max_tokens" }),
                new[] { "text" },
                new[] { "text" },
                "text->text",
                32000,
                16000,
                null)
        {
        }

        internal OpenRouterModelInfo(
            string id,
            string name,
            IEnumerable<string> supportedParameters,
            bool? supportsTextOutput)
            : this(
                id,
                name,
                (supportedParameters ?? Array.Empty<string>())
                    .Concat(new[] { "max_tokens" }),
                new[] { "text" },
                supportsTextOutput == false
                    ? Array.Empty<string>()
                    : new[] { "text" },
                "text->text",
                32000,
                16000,
                null)
        {
        }

        internal OpenRouterModelInfo(
            string id,
            string name,
            IEnumerable<string> supportedParameters,
            IEnumerable<string> inputModalities = null,
            IEnumerable<string> outputModalities = null,
            string architectureModality = null,
            int? contextLength = null,
            int? maxCompletionTokens = null,
            OpenRouterReasoningInfo reasoning = null)
        {
            Id = id ?? string.Empty;
            Name = name ?? id ?? string.Empty;
            SupportedParameters = new HashSet<string>(
                supportedParameters ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            InputModalities = new HashSet<string>(
                inputModalities ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            OutputModalities = new HashSet<string>(
                outputModalities ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            ArchitectureModality = architectureModality ?? string.Empty;
            ContextLength = contextLength;
            MaxCompletionTokens = maxCompletionTokens;
            Reasoning = reasoning;
        }

        internal string Id { get; }
        internal string Name { get; }
        internal ISet<string> SupportedParameters { get; }
        internal ISet<string> InputModalities { get; }
        internal ISet<string> OutputModalities { get; }
        internal string ArchitectureModality { get; }
        internal int? ContextLength { get; }
        internal int? MaxCompletionTokens { get; }
        internal OpenRouterReasoningInfo Reasoning { get; }
        internal bool SupportsTextInput => InputModalities.Contains("text");
        internal bool SupportsTextOutput => OutputModalities.Contains("text");
        internal bool SupportsStructuredOutputs =>
            SupportedParameters.Contains("structured_outputs");
        internal bool IsColoringCompatible =>
            SupportsStructuredOutputs &&
            SupportedParameters.Contains("max_tokens") &&
            SupportsTextInput &&
            SupportsTextOutput &&
            MaxCompletionTokens.GetValueOrDefault() >=
            OpenRouterColorRequestLimits.MinimumOutputBudget;
    }

    internal sealed class OpenRouterReasoningInfo
    {
        internal OpenRouterReasoningInfo(
            bool? mandatory,
            bool? defaultEnabled,
            IEnumerable<string> supportedEfforts,
            string defaultEffort,
            bool? supportsMaxTokens,
            bool? exposesEffortSelection = null)
        {
            Mandatory = mandatory;
            DefaultEnabled = defaultEnabled;
            SupportedEfforts = new HashSet<string>(
                supportedEfforts ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            DefaultEffort = defaultEffort ?? string.Empty;
            SupportsMaxTokens = supportsMaxTokens;
            ExposesEffortSelection = exposesEffortSelection ??
                                     supportedEfforts != null;
        }

        internal bool? Mandatory { get; }
        internal bool? DefaultEnabled { get; }
        internal ISet<string> SupportedEfforts { get; }
        internal string DefaultEffort { get; }
        internal bool? SupportsMaxTokens { get; }
        internal bool ExposesEffortSelection { get; }
    }

    internal sealed class AiColorDiagnostics
    {
        internal AiColorDiagnostics(
            string finishReason,
            int requestedUniqueNameCount,
            int calculatedOutputBudget,
            int? providerMaxCompletionTokens,
            string reasoningPolicy)
        {
            FinishReason = finishReason ?? string.Empty;
            RequestedUniqueNameCount = requestedUniqueNameCount;
            CalculatedOutputBudget = calculatedOutputBudget;
            ProviderMaxCompletionTokens = providerMaxCompletionTokens;
            ReasoningPolicy = reasoningPolicy ?? string.Empty;
        }

        internal string FinishReason { get; }
        internal int RequestedUniqueNameCount { get; }
        internal int CalculatedOutputBudget { get; }
        internal int? ProviderMaxCompletionTokens { get; }
        internal string ReasoningPolicy { get; }
    }

    internal sealed class OpenRouterCatalogResult
    {
        private OpenRouterCatalogResult(
            bool isAvailable,
            IReadOnlyDictionary<string, OpenRouterModelInfo> models,
            OpenRouterFailureKind failureKind,
            int? httpStatus)
        {
            IsAvailable = isAvailable;
            Models = models ??
                     new Dictionary<string, OpenRouterModelInfo>(
                         StringComparer.OrdinalIgnoreCase);
            FailureKind = failureKind;
            HttpStatus = httpStatus;
        }

        internal bool IsAvailable { get; }
        internal IReadOnlyDictionary<string, OpenRouterModelInfo> Models { get; }
        internal OpenRouterFailureKind FailureKind { get; }
        internal int? HttpStatus { get; }

        internal static OpenRouterCatalogResult Available(
            IReadOnlyDictionary<string, OpenRouterModelInfo> models)
        {
            return new OpenRouterCatalogResult(
                true,
                models,
                OpenRouterFailureKind.None,
                null);
        }

        internal static OpenRouterCatalogResult Unavailable(
            OpenRouterFailureKind failureKind,
            int? httpStatus = null)
        {
            return new OpenRouterCatalogResult(
                false,
                null,
                failureKind,
                httpStatus);
        }
    }

    internal enum AiColorSource
    {
        None = 0,
        OpenRouter,
        LocalPalette
    }

    internal enum AiColorOutcomeKind
    {
        Success = 0,
        MissingKey,
        NoSelection,
        NoColorableObjects,
        NoObjectNames,
        InvalidModelId,
        InvalidRequest,
        CatalogUnavailable,
        ModelNotSelected,
        ModelIncompatible,
        TooManyObjects,
        InsufficientOutputBudget,
        ModelUnavailable,
        Unauthorized,
        RateLimited,
        Timeout,
        Cancelled,
        Network,
        ServiceUnavailable,
        BadRequest,
        ResponseRefused,
        MissingAssistantContent,
        TruncatedResponse,
        StructuredPayloadInvalid,
        IncompleteObjectSet,
        InvalidResponse,
        WorkerMissing,
        WorkerRuntimeMissing,
        WorkerStartupFailed,
        WorkerFailed,
        WorkerInternalFailure,
        ProtocolMismatch,
        NavisworksError,
        AlreadyRunning,
        DocumentChanged,
        UnsupportedReasoningPolicy
    }

    internal sealed class AiColorOutcome
    {
        private AiColorOutcome(
            AiColorOutcomeKind kind,
            AiColorSource source,
            IDictionary<string, string> colors,
            int? httpStatus,
            AiColorDiagnostics diagnostics)
        {
            Kind = kind;
            Source = source;
            Colors = new Dictionary<string, string>(
                colors ?? new Dictionary<string, string>(),
                StringComparer.Ordinal);
            HttpStatus = httpStatus;
            Diagnostics = diagnostics;
        }

        internal AiColorOutcomeKind Kind { get; }
        internal AiColorSource Source { get; }
        internal IReadOnlyDictionary<string, string> Colors { get; }
        internal int? HttpStatus { get; }
        internal AiColorDiagnostics Diagnostics { get; }
        internal bool IsSuccess =>
            Kind == AiColorOutcomeKind.Success &&
            Source != AiColorSource.None &&
            Colors.Count > 0;
        internal string DiagnosticCode =>
            HttpStatus.HasValue
                ? Kind + "_http_" + HttpStatus.Value
                : Kind.ToString();

        internal static AiColorOutcome Success(
            AiColorSource source,
            IDictionary<string, string> colors,
            AiColorDiagnostics diagnostics = null)
        {
            if (source == AiColorSource.None)
                throw new ArgumentException(
                    "A successful outcome requires provenance.",
                    nameof(source));
            return new AiColorOutcome(
                AiColorOutcomeKind.Success,
                source,
                colors,
                null,
                diagnostics);
        }

        internal static AiColorOutcome Failure(
            AiColorOutcomeKind kind,
            int? httpStatus = null,
            AiColorDiagnostics diagnostics = null)
        {
            if (kind == AiColorOutcomeKind.Success)
                throw new ArgumentException(
                    "Use Success for successful outcomes.",
                    nameof(kind));
            return new AiColorOutcome(
                kind,
                AiColorSource.None,
                null,
                httpStatus,
                diagnostics);
        }
    }
}
