using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace NavisHelper.AI
{
    internal sealed class AiWorkerTransport : IOpenRouterTransport
    {
        private readonly IAiWorkerProcessRunner _runner;
        private readonly Func<string> _workerPathProvider;

        internal AiWorkerTransport()
            : this(
                new AiWorkerProcessRunner(),
                ResolveWorkerPathFromPluginAssembly)
        {
        }

        internal AiWorkerTransport(
            IAiWorkerProcessRunner runner,
            Func<string> workerPathProvider)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _workerPathProvider = workerPathProvider ??
                                  throw new ArgumentNullException(
                                      nameof(workerPathProvider));
        }

        public async Task<OpenRouterValidationResult> ValidateKeyAsync(
            string key,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(key))
                return OpenRouterValidationResult.Failure(
                    OpenRouterFailureKind.MissingKey);

            var request = CreateRequest(AiWorkerOperation.ValidateKey);
            var response = await SendAsync(request, key, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsProtocolValid)
                return OpenRouterValidationResult.Failure(
                    response.FailureKind,
                    response.HttpStatus);
            if (!response.Envelope.IsSuccess)
                return OpenRouterValidationResult.Failure(
                    response.FailureKind,
                    response.HttpStatus);
            if (response.FailureKind != OpenRouterFailureKind.None)
                return OpenRouterValidationResult.Failure(
                    OpenRouterFailureKind.ProtocolMismatch);
            if (HasValues(response.Envelope.Models) ||
                HasValues(response.Envelope.Colors))
                return OpenRouterValidationResult.Failure(
                    OpenRouterFailureKind.ProtocolMismatch);
            return OpenRouterValidationResult.Success();
        }

        public async Task<OpenRouterCatalogResult> GetModelsAsync(
            string key,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(key))
                return OpenRouterCatalogResult.Unavailable(
                    OpenRouterFailureKind.MissingKey);

            var request = CreateRequest(AiWorkerOperation.GetModels);
            var response = await SendAsync(request, key, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsProtocolValid || !response.Envelope.IsSuccess)
                return OpenRouterCatalogResult.Unavailable(
                    response.FailureKind,
                    response.HttpStatus);
            if (response.FailureKind != OpenRouterFailureKind.None ||
                response.Envelope.Models == null ||
                HasValues(response.Envelope.Colors))
                return OpenRouterCatalogResult.Unavailable(
                    OpenRouterFailureKind.ProtocolMismatch);

            var models = new Dictionary<string, OpenRouterModelInfo>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var model in response.Envelope.Models)
            {
                if (model == null || string.IsNullOrWhiteSpace(model.Id) ||
                    models.ContainsKey(model.Id))
                    return OpenRouterCatalogResult.Unavailable(
                        OpenRouterFailureKind.ProtocolMismatch);
                models[model.Id] = new OpenRouterModelInfo(
                    model.Id,
                    model.Name,
                    model.SupportedParameters ?? new List<string>(),
                    model.InputModalities,
                    model.OutputModalities,
                    model.ArchitectureModality,
                    model.ContextLength,
                    model.MaxCompletionTokens,
                    model.ReasoningMandatory.HasValue ||
                    model.ReasoningDefaultEnabled.HasValue ||
                    model.ReasoningSupportedEfforts != null
                        ? new OpenRouterReasoningInfo(
                            model.ReasoningMandatory,
                            model.ReasoningDefaultEnabled,
                            model.ReasoningSupportedEfforts,
                            model.ReasoningDefaultEffort,
                            model.ReasoningSupportsMaxTokens,
                            model.ReasoningExposesEffortSelection)
                        : null);
            }
            return models.Count == 0
                ? OpenRouterCatalogResult.Unavailable(
                    OpenRouterFailureKind.InvalidResponse)
                : OpenRouterCatalogResult.Available(models);
        }

        public async Task<AiColorOutcome> GetColorsAsync(
            string key,
            IReadOnlyCollection<string> objectNames,
            string schemeName,
            OpenRouterModelInfo model,
            double temperature,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(key))
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.MissingKey);
            if (model == null || string.IsNullOrWhiteSpace(model.Id))
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.ModelNotSelected);
            if (objectNames == null || objectNames.Count == 0)
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.InvalidRequest);
            if (objectNames.Distinct(StringComparer.Ordinal).Count() >
                OpenRouterColorRequestLimits.MaxUniqueObjectNames)
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.TooManyObjects);
            if (!model.IsColoringCompatible)
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.ModelIncompatible);

            var requestPolicy = OpenRouterColorRequestPolicy.Evaluate(
                model,
                objectNames);
            var requestDiagnostics = new AiColorDiagnostics(
                string.Empty,
                objectNames.Count,
                requestPolicy.OutputBudget,
                model.MaxCompletionTokens,
                requestPolicy.ReasoningPolicy);
            if (!requestPolicy.MaySend)
                return AiColorOutcome.Failure(
                    requestPolicy.FailureOutcomeKind,
                    null,
                    requestDiagnostics);

            var expectedNames = objectNames.ToArray();
            var request = CreateRequest(
                AiWorkerOperation.GetColors,
                new AiWorkerRequestPayload
                {
                    ObjectNames = expectedNames.ToList(),
                    SchemeName = schemeName ?? string.Empty,
                    ModelId = model.Id,
                    SupportedParameters = model.SupportedParameters.ToList(),
                    InputModalities = model.InputModalities.ToList(),
                    OutputModalities = model.OutputModalities.ToList(),
                    ArchitectureModality = model.ArchitectureModality,
                    ContextLength = model.ContextLength,
                    MaxCompletionTokens = model.MaxCompletionTokens,
                    ReasoningMandatory = model.Reasoning?.Mandatory,
                    ReasoningDefaultEnabled = model.Reasoning?.DefaultEnabled,
                    ReasoningSupportedEfforts = model.Reasoning
                        ?.SupportedEfforts.ToList(),
                    ReasoningDefaultEffort = model.Reasoning?.DefaultEffort,
                    ReasoningSupportsMaxTokens = model.Reasoning
                        ?.SupportsMaxTokens,
                    ReasoningExposesEffortSelection = model.Reasoning
                        ?.ExposesEffortSelection,
                    Temperature = temperature
                });
            var response = await SendAsync(request, key, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsProtocolValid || !response.Envelope.IsSuccess)
                return AiColorOutcome.Failure(
                    MapColorFailure(response.FailureKind),
                    response.HttpStatus,
                    Diagnostics(response.Envelope) ?? requestDiagnostics);
            if (response.FailureKind != OpenRouterFailureKind.None ||
                HasValues(response.Envelope.Models) ||
                !ValidateColors(response.Envelope.Colors, expectedNames))
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.ProtocolMismatch);

            return AiColorOutcome.Success(
                AiColorSource.OpenRouter,
                response.Envelope.Colors,
                Diagnostics(response.Envelope) ?? requestDiagnostics);
        }

        internal Task<AiColorOutcome> GetColorsAsync(
            string key,
            IReadOnlyCollection<string> objectNames,
            string schemeName,
            string modelId,
            IReadOnlyCollection<string> supportedParameters,
            double temperature,
            CancellationToken cancellationToken)
        {
            var parameters = (supportedParameters ?? Array.Empty<string>())
                .Concat(new[] { "max_tokens" });
            return GetColorsAsync(
                key,
                objectNames,
                schemeName,
                new OpenRouterModelInfo(
                    modelId,
                    modelId,
                    parameters,
                    new[] { "text" },
                    new[] { "text" },
                    "text->text",
                    32000,
                    16000),
                temperature,
                cancellationToken);
        }

        private static AiColorDiagnostics Diagnostics(
            AiWorkerResponseEnvelope envelope)
        {
            return envelope == null
                ? null
                : new AiColorDiagnostics(
                    envelope.FinishReason,
                    envelope.RequestedUniqueNameCount,
                    envelope.CalculatedOutputBudget,
                    envelope.ProviderMaxCompletionTokens,
                    envelope.ReasoningPolicy);
        }

        internal static string ResolveWorkerPath(string pluginAssemblyPath)
        {
            if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
                return string.Empty;
            var versionDirectory = Path.GetDirectoryName(pluginAssemblyPath);
            var contentsDirectory = versionDirectory == null
                ? null
                : Directory.GetParent(versionDirectory)?.FullName;
            return contentsDirectory == null
                ? string.Empty
                : Path.Combine(
                    contentsDirectory,
                    AiWorkerProtocol.WorkerDirectoryName,
                    AiWorkerProtocol.WorkerExecutableName);
        }

        internal static string SerializeRequest(AiWorkerRequestEnvelope request)
        {
            return JsonConvert.SerializeObject(
                request,
                Formatting.None,
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
        }

        private static AiWorkerRequestEnvelope CreateRequest(
            AiWorkerOperation operation,
            AiWorkerRequestPayload payload = null)
        {
            return AiWorkerRequestEnvelope.Create(
                Guid.NewGuid().ToString("N"),
                operation,
                payload);
        }

        private async Task<ValidatedWorkerResponse> SendAsync(
            AiWorkerRequestEnvelope request,
            string key,
            CancellationToken cancellationToken)
        {
            var run = await _runner.RunAsync(
                    _workerPathProvider(),
                    SerializeRequest(request),
                    key,
                    cancellationToken)
                .ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                return ValidatedWorkerResponse.TransportFailure(
                    OpenRouterFailureKind.Cancelled);
            if (!run.IsSuccess)
                return ValidatedWorkerResponse.TransportFailure(
                    MapRunFailure(run.FailureKind));

            AiWorkerResponseEnvelope envelope;
            try
            {
                envelope = JsonConvert.DeserializeObject<
                    AiWorkerResponseEnvelope>(
                    run.StandardOutput,
                    new JsonSerializerSettings
                    {
                        CheckAdditionalContent = true
                    });
            }
            catch (JsonException)
            {
                return ValidatedWorkerResponse.TransportFailure(
                    OpenRouterFailureKind.ProtocolMismatch);
            }
            if (envelope == null)
                return ValidatedWorkerResponse.TransportFailure(
                    OpenRouterFailureKind.ProtocolMismatch);
            if (envelope.ProtocolVersion != AiWorkerProtocol.CurrentVersion ||
                !string.Equals(
                    envelope.RequestId,
                    request.RequestId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    envelope.Operation,
                    request.Operation,
                    StringComparison.Ordinal))
                return ValidatedWorkerResponse.TransportFailure(
                    OpenRouterFailureKind.ProtocolMismatch);

            OpenRouterFailureKind failureKind;
            if (!Enum.TryParse(
                    envelope.FailureKind,
                    false,
                    out failureKind) ||
                (envelope.IsSuccess &&
                 failureKind != OpenRouterFailureKind.None) ||
                (!envelope.IsSuccess &&
                 failureKind == OpenRouterFailureKind.None))
                return ValidatedWorkerResponse.TransportFailure(
                    OpenRouterFailureKind.InvalidResponse);

            return ValidatedWorkerResponse.Valid(
                envelope,
                failureKind);
        }

        private static string ResolveWorkerPathFromPluginAssembly()
        {
            return ResolveWorkerPath(
                typeof(AiWorkerTransport).Assembly.Location);
        }

        private static OpenRouterFailureKind MapRunFailure(
            AiWorkerRunFailureKind failureKind)
        {
            switch (failureKind)
            {
                case AiWorkerRunFailureKind.Missing:
                    return OpenRouterFailureKind.WorkerMissing;
                case AiWorkerRunFailureKind.RuntimeMissing:
                    return OpenRouterFailureKind.WorkerRuntimeMissing;
                case AiWorkerRunFailureKind.StartupFailed:
                    return OpenRouterFailureKind.WorkerStartupFailed;
                case AiWorkerRunFailureKind.NonZeroExit:
                    return OpenRouterFailureKind.WorkerFailed;
                case AiWorkerRunFailureKind.Cancelled:
                    return OpenRouterFailureKind.Cancelled;
                default:
                    return OpenRouterFailureKind.InvalidResponse;
            }
        }

        private static AiColorOutcomeKind MapColorFailure(
            OpenRouterFailureKind failureKind)
        {
            switch (failureKind)
            {
                case OpenRouterFailureKind.MissingKey:
                    return AiColorOutcomeKind.MissingKey;
                case OpenRouterFailureKind.Unauthorized:
                    return AiColorOutcomeKind.Unauthorized;
                case OpenRouterFailureKind.RateLimited:
                    return AiColorOutcomeKind.RateLimited;
                case OpenRouterFailureKind.Timeout:
                    return AiColorOutcomeKind.Timeout;
                case OpenRouterFailureKind.Cancelled:
                    return AiColorOutcomeKind.Cancelled;
                case OpenRouterFailureKind.Network:
                    return AiColorOutcomeKind.Network;
                case OpenRouterFailureKind.ServiceUnavailable:
                    return AiColorOutcomeKind.ServiceUnavailable;
                case OpenRouterFailureKind.BadRequest:
                    return AiColorOutcomeKind.BadRequest;
                case OpenRouterFailureKind.ModelUnavailable:
                    return AiColorOutcomeKind.ModelUnavailable;
                case OpenRouterFailureKind.InsufficientOutputBudget:
                    return AiColorOutcomeKind.InsufficientOutputBudget;
                case OpenRouterFailureKind.UnsupportedReasoningPolicy:
                    return AiColorOutcomeKind.UnsupportedReasoningPolicy;
                case OpenRouterFailureKind.ResponseRefused:
                    return AiColorOutcomeKind.ResponseRefused;
                case OpenRouterFailureKind.MissingAssistantContent:
                    return AiColorOutcomeKind.MissingAssistantContent;
                case OpenRouterFailureKind.TruncatedResponse:
                    return AiColorOutcomeKind.TruncatedResponse;
                case OpenRouterFailureKind.StructuredPayloadInvalid:
                    return AiColorOutcomeKind.StructuredPayloadInvalid;
                case OpenRouterFailureKind.IncompleteObjectSet:
                    return AiColorOutcomeKind.IncompleteObjectSet;
                case OpenRouterFailureKind.WorkerMissing:
                    return AiColorOutcomeKind.WorkerMissing;
                case OpenRouterFailureKind.WorkerRuntimeMissing:
                    return AiColorOutcomeKind.WorkerRuntimeMissing;
                case OpenRouterFailureKind.WorkerStartupFailed:
                    return AiColorOutcomeKind.WorkerStartupFailed;
                case OpenRouterFailureKind.WorkerFailed:
                    return AiColorOutcomeKind.WorkerFailed;
                case OpenRouterFailureKind.WorkerInternalFailure:
                    return AiColorOutcomeKind.WorkerInternalFailure;
                case OpenRouterFailureKind.ProtocolMismatch:
                    return AiColorOutcomeKind.ProtocolMismatch;
                default:
                    return AiColorOutcomeKind.InvalidResponse;
            }
        }

        private static bool ValidateColors(
            IDictionary<string, string> colors,
            IEnumerable<string> expectedNames)
        {
            if (colors == null)
                return false;
            var expected = new HashSet<string>(
                expectedNames,
                StringComparer.Ordinal);
            if (colors.Count != expected.Count ||
                colors.Keys.Any(name => !expected.Contains(name)))
                return false;
            return colors.Values.All(IsValidRgb);
        }

        private static bool HasValues<T>(ICollection<T> values)
        {
            return values != null && values.Count > 0;
        }

        private static bool IsValidRgb(string value)
        {
            var parts = (value ?? string.Empty).Split(',');
            if (parts.Length != 3)
                return false;
            int component;
            return parts.All(part =>
                int.TryParse(part.Trim(), out component) &&
                component >= 0 && component <= 255);
        }

        private sealed class ValidatedWorkerResponse
        {
            private ValidatedWorkerResponse(
                bool isProtocolValid,
                AiWorkerResponseEnvelope envelope,
                OpenRouterFailureKind failureKind,
                int? httpStatus)
            {
                IsProtocolValid = isProtocolValid;
                Envelope = envelope;
                FailureKind = failureKind;
                HttpStatus = httpStatus;
            }

            internal bool IsProtocolValid { get; }
            internal AiWorkerResponseEnvelope Envelope { get; }
            internal OpenRouterFailureKind FailureKind { get; }
            internal int? HttpStatus { get; }

            internal static ValidatedWorkerResponse Valid(
                AiWorkerResponseEnvelope envelope,
                OpenRouterFailureKind failureKind)
            {
                return new ValidatedWorkerResponse(
                    true,
                    envelope,
                    failureKind,
                    envelope.HttpStatus);
            }

            internal static ValidatedWorkerResponse TransportFailure(
                OpenRouterFailureKind failureKind)
            {
                return new ValidatedWorkerResponse(
                    false,
                    null,
                    failureKind,
                    null);
            }
        }
    }
}
