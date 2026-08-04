using System;
using System.Collections.Generic;

namespace NavisHelper.AI
{
    internal static class AiWorkerProtocol
    {
        internal const int CurrentVersion = 3;
        internal const string KeyEnvironmentVariable = "OPEN_ROUTER_NW_KEY";
        internal const string WorkerDirectoryName = "AiWorker";
        internal const string WorkerExecutableName = "NavisHelper.AiWorker.exe";
    }

    internal enum AiWorkerOperation
    {
        ValidateKey = 0,
        GetModels,
        GetColors
    }

    internal sealed class AiWorkerRequestEnvelope
    {
        public int ProtocolVersion { get; set; }
        public string RequestId { get; set; }
        public string Operation { get; set; }
        public AiWorkerRequestPayload Payload { get; set; }

        internal static AiWorkerRequestEnvelope Create(
            string requestId,
            AiWorkerOperation operation,
            AiWorkerRequestPayload payload = null)
        {
            return new AiWorkerRequestEnvelope
            {
                ProtocolVersion = AiWorkerProtocol.CurrentVersion,
                RequestId = requestId,
                Operation = operation.ToString(),
                Payload = payload ?? new AiWorkerRequestPayload()
            };
        }
    }

    internal sealed class AiWorkerRequestPayload
    {
        public List<string> ObjectNames { get; set; }
        public string SchemeName { get; set; }
        public string ModelId { get; set; }
        public List<string> SupportedParameters { get; set; }
        public List<string> InputModalities { get; set; }
        public List<string> OutputModalities { get; set; }
        public string ArchitectureModality { get; set; }
        public int? ContextLength { get; set; }
        public int? MaxCompletionTokens { get; set; }
        public bool? ReasoningMandatory { get; set; }
        public bool? ReasoningDefaultEnabled { get; set; }
        public List<string> ReasoningSupportedEfforts { get; set; }
        public string ReasoningDefaultEffort { get; set; }
        public bool? ReasoningSupportsMaxTokens { get; set; }
        public bool? ReasoningExposesEffortSelection { get; set; }
        public double? Temperature { get; set; }
    }

    internal sealed class AiWorkerResponseEnvelope
    {
        public int ProtocolVersion { get; set; }
        public string RequestId { get; set; }
        public string Operation { get; set; }
        public bool IsSuccess { get; set; }
        public string FailureKind { get; set; }
        public int? HttpStatus { get; set; }
        public List<AiWorkerModelDto> Models { get; set; }
        public Dictionary<string, string> Colors { get; set; }
        public string FinishReason { get; set; }
        public int RequestedUniqueNameCount { get; set; }
        public int CalculatedOutputBudget { get; set; }
        public int? ProviderMaxCompletionTokens { get; set; }
        public string ReasoningPolicy { get; set; }

        internal static AiWorkerResponseEnvelope Success(
            AiWorkerRequestEnvelope request)
        {
            return FromRequest(request, true, OpenRouterFailureKind.None, null);
        }

        internal static AiWorkerResponseEnvelope Failure(
            AiWorkerRequestEnvelope request,
            OpenRouterFailureKind failureKind,
            int? httpStatus = null)
        {
            return FromRequest(request, false, failureKind, httpStatus);
        }

        private static AiWorkerResponseEnvelope FromRequest(
            AiWorkerRequestEnvelope request,
            bool isSuccess,
            OpenRouterFailureKind failureKind,
            int? httpStatus)
        {
            return new AiWorkerResponseEnvelope
            {
                ProtocolVersion = AiWorkerProtocol.CurrentVersion,
                RequestId = request?.RequestId ?? string.Empty,
                Operation = request?.Operation ?? string.Empty,
                IsSuccess = isSuccess,
                FailureKind = failureKind.ToString(),
                HttpStatus = httpStatus
            };
        }
    }

    internal sealed class AiWorkerModelDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<string> SupportedParameters { get; set; }
        public List<string> InputModalities { get; set; }
        public List<string> OutputModalities { get; set; }
        public string ArchitectureModality { get; set; }
        public int? ContextLength { get; set; }
        public int? MaxCompletionTokens { get; set; }
        public bool? ReasoningMandatory { get; set; }
        public bool? ReasoningDefaultEnabled { get; set; }
        public List<string> ReasoningSupportedEfforts { get; set; }
        public string ReasoningDefaultEffort { get; set; }
        public bool? ReasoningSupportsMaxTokens { get; set; }
        public bool? ReasoningExposesEffortSelection { get; set; }
    }
}
