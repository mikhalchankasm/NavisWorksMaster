using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NavisHelper.AI
{
    internal sealed class AiWorkerRequestHandler
    {
        private readonly IOpenRouterTransport _openRouter;

        internal AiWorkerRequestHandler(IOpenRouterTransport openRouter)
        {
            _openRouter = openRouter ??
                          throw new ArgumentNullException(nameof(openRouter));
        }

        internal async Task<AiWorkerResponseEnvelope> HandleAsync(
            AiWorkerRequestEnvelope request,
            string key,
            CancellationToken cancellationToken)
        {
            AiWorkerOperation operation;
            if (!ValidateEnvelope(request, out operation))
                return AiWorkerResponseEnvelope.Failure(
                    request,
                    OpenRouterFailureKind.ProtocolMismatch);

            switch (operation)
            {
                case AiWorkerOperation.ValidateKey:
                    return await ValidateKeyAsync(
                            request,
                            key,
                            cancellationToken)
                        .ConfigureAwait(false);
                case AiWorkerOperation.GetModels:
                    return await GetModelsAsync(
                            request,
                            key,
                            cancellationToken)
                        .ConfigureAwait(false);
                case AiWorkerOperation.GetColors:
                    return await GetColorsAsync(
                            request,
                            key,
                            cancellationToken)
                        .ConfigureAwait(false);
                default:
                    return AiWorkerResponseEnvelope.Failure(
                        request,
                        OpenRouterFailureKind.ProtocolMismatch);
            }
        }

        private async Task<AiWorkerResponseEnvelope> ValidateKeyAsync(
            AiWorkerRequestEnvelope request,
            string key,
            CancellationToken cancellationToken)
        {
            var result = await _openRouter.ValidateKeyAsync(
                    key,
                    cancellationToken)
                .ConfigureAwait(false);
            return result.IsSuccess
                ? AiWorkerResponseEnvelope.Success(request)
                : AiWorkerResponseEnvelope.Failure(
                    request,
                    result.FailureKind,
                    result.HttpStatus);
        }

        private async Task<AiWorkerResponseEnvelope> GetModelsAsync(
            AiWorkerRequestEnvelope request,
            string key,
            CancellationToken cancellationToken)
        {
            var result = await _openRouter.GetModelsAsync(
                    key,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsAvailable)
                return AiWorkerResponseEnvelope.Failure(
                    request,
                    result.FailureKind,
                    result.HttpStatus);

            var response = AiWorkerResponseEnvelope.Success(request);
            response.Models = result.Models.Values
                .OrderBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
                .Select(model => new AiWorkerModelDto
                {
                    Id = model.Id,
                    Name = model.Name,
                    SupportedParameters = model.SupportedParameters
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
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
                        ?.ExposesEffortSelection
                })
                .ToList();
            return response;
        }

        private async Task<AiWorkerResponseEnvelope> GetColorsAsync(
            AiWorkerRequestEnvelope request,
            string key,
            CancellationToken cancellationToken)
        {
            var payload = request.Payload;
            if (payload == null || payload.ObjectNames == null ||
                payload.ObjectNames.Count == 0 ||
                payload.ObjectNames.Any(string.IsNullOrWhiteSpace) ||
                payload.ObjectNames.Distinct(StringComparer.Ordinal).Count() !=
                payload.ObjectNames.Count ||
                string.IsNullOrWhiteSpace(payload.ModelId) ||
                payload.SupportedParameters == null ||
                !payload.SupportedParameters.Contains(
                    "structured_outputs",
                    StringComparer.OrdinalIgnoreCase))
                return AiWorkerResponseEnvelope.Failure(
                    request,
                    OpenRouterFailureKind.InvalidResponse);

            var model = new OpenRouterModelInfo(
                payload.ModelId,
                payload.ModelId,
                payload.SupportedParameters,
                payload.InputModalities,
                payload.OutputModalities,
                payload.ArchitectureModality,
                payload.ContextLength,
                payload.MaxCompletionTokens,
                payload.ReasoningMandatory.HasValue ||
                payload.ReasoningDefaultEnabled.HasValue ||
                payload.ReasoningSupportedEfforts != null
                    ? new OpenRouterReasoningInfo(
                        payload.ReasoningMandatory,
                        payload.ReasoningDefaultEnabled,
                        payload.ReasoningSupportedEfforts,
                        payload.ReasoningDefaultEffort,
                        payload.ReasoningSupportsMaxTokens,
                        payload.ReasoningExposesEffortSelection)
                    : null);
            var outcome = await _openRouter.GetColorsAsync(
                    key,
                    payload.ObjectNames,
                    payload.SchemeName,
                    model,
                    payload.Temperature ?? 0.3,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!outcome.IsSuccess)
            {
                var failure = AiWorkerResponseEnvelope.Failure(
                    request,
                    MapFailure(outcome.Kind),
                    outcome.HttpStatus);
                CopyDiagnostics(failure, outcome.Diagnostics);
                return failure;
            }

            var response = AiWorkerResponseEnvelope.Success(request);
            response.Colors = new Dictionary<string, string>(
                outcome.Colors,
                StringComparer.Ordinal);
            CopyDiagnostics(response, outcome.Diagnostics);
            return response;
        }

        private static void CopyDiagnostics(
            AiWorkerResponseEnvelope response,
            AiColorDiagnostics diagnostics)
        {
            if (response == null || diagnostics == null)
                return;
            response.FinishReason = diagnostics.FinishReason;
            response.RequestedUniqueNameCount =
                diagnostics.RequestedUniqueNameCount;
            response.CalculatedOutputBudget =
                diagnostics.CalculatedOutputBudget;
            response.ProviderMaxCompletionTokens =
                diagnostics.ProviderMaxCompletionTokens;
            response.ReasoningPolicy = diagnostics.ReasoningPolicy;
        }

        private static bool ValidateEnvelope(
            AiWorkerRequestEnvelope request,
            out AiWorkerOperation operation)
        {
            operation = default(AiWorkerOperation);
            Guid requestId;
            return request != null &&
                   request.ProtocolVersion == AiWorkerProtocol.CurrentVersion &&
                   Guid.TryParseExact(request.RequestId, "N", out requestId) &&
                   Enum.TryParse(request.Operation, false, out operation) &&
                   Enum.IsDefined(typeof(AiWorkerOperation), operation);
        }

        private static OpenRouterFailureKind MapFailure(
            AiColorOutcomeKind kind)
        {
            switch (kind)
            {
                case AiColorOutcomeKind.MissingKey:
                    return OpenRouterFailureKind.MissingKey;
                case AiColorOutcomeKind.Unauthorized:
                    return OpenRouterFailureKind.Unauthorized;
                case AiColorOutcomeKind.RateLimited:
                    return OpenRouterFailureKind.RateLimited;
                case AiColorOutcomeKind.Timeout:
                    return OpenRouterFailureKind.Timeout;
                case AiColorOutcomeKind.Cancelled:
                    return OpenRouterFailureKind.Cancelled;
                case AiColorOutcomeKind.Network:
                    return OpenRouterFailureKind.Network;
                case AiColorOutcomeKind.ServiceUnavailable:
                    return OpenRouterFailureKind.ServiceUnavailable;
                case AiColorOutcomeKind.BadRequest:
                    return OpenRouterFailureKind.BadRequest;
                case AiColorOutcomeKind.ModelUnavailable:
                    return OpenRouterFailureKind.ModelUnavailable;
                case AiColorOutcomeKind.InsufficientOutputBudget:
                    return OpenRouterFailureKind.InsufficientOutputBudget;
                case AiColorOutcomeKind.UnsupportedReasoningPolicy:
                    return OpenRouterFailureKind.UnsupportedReasoningPolicy;
                case AiColorOutcomeKind.ResponseRefused:
                    return OpenRouterFailureKind.ResponseRefused;
                case AiColorOutcomeKind.MissingAssistantContent:
                    return OpenRouterFailureKind.MissingAssistantContent;
                case AiColorOutcomeKind.TruncatedResponse:
                    return OpenRouterFailureKind.TruncatedResponse;
                case AiColorOutcomeKind.StructuredPayloadInvalid:
                    return OpenRouterFailureKind.StructuredPayloadInvalid;
                case AiColorOutcomeKind.IncompleteObjectSet:
                    return OpenRouterFailureKind.IncompleteObjectSet;
                case AiColorOutcomeKind.InvalidResponse:
                case AiColorOutcomeKind.InvalidRequest:
                    return OpenRouterFailureKind.InvalidResponse;
                case AiColorOutcomeKind.WorkerInternalFailure:
                    return OpenRouterFailureKind.WorkerInternalFailure;
                default:
                    return OpenRouterFailureKind.WorkerFailed;
            }
        }
    }
}
