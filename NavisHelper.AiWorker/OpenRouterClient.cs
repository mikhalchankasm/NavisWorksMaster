using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NavisHelper.AI
{
    internal sealed class OpenRouterClient : IOpenRouterTransport, IDisposable
    {
        internal const string KeyEndpoint = "https://openrouter.ai/api/v1/key";
        internal const string ModelsEndpoint =
            "https://openrouter.ai/api/v1/models/user" +
            "?output_modalities=text&input_modalities=text" +
            "&supported_parameters=structured_outputs";
        internal const string ChatEndpoint =
            "https://openrouter.ai/api/v1/chat/completions";

        private static readonly HttpClient SharedClient = CreateSharedClient();

        private readonly HttpClient _httpClient;
        private readonly bool _ownsClient;

        internal OpenRouterClient()
            : this(SharedClient, false)
        {
        }

        internal OpenRouterClient(HttpClient httpClient, bool ownsClient = false)
        {
            _httpClient = httpClient ??
                          throw new ArgumentNullException(nameof(httpClient));
            _ownsClient = ownsClient;
        }

        public async Task<OpenRouterValidationResult> ValidateKeyAsync(
            string key,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(key))
                return OpenRouterValidationResult.Failure(
                    OpenRouterFailureKind.MissingKey);

            using (var request = CreateAuthorizedRequest(
                       HttpMethod.Get,
                       KeyEndpoint,
                       key))
            {
                try
                {
                    using (var response = await _httpClient.SendAsync(
                                   request,
                                   HttpCompletionOption.ResponseHeadersRead,
                                   cancellationToken)
                               .ConfigureAwait(false))
                    {
                        if (response.IsSuccessStatusCode)
                            return OpenRouterValidationResult.Success();

                        return OpenRouterValidationResult.Failure(
                            MapFailure(response.StatusCode),
                            (int)response.StatusCode);
                    }
                }
                catch (OperationCanceledException)
                {
                    return OpenRouterValidationResult.Failure(
                        OpenRouterFailureKind.Cancelled);
                }
                catch (HttpRequestException)
                {
                    return OpenRouterValidationResult.Failure(
                        OpenRouterFailureKind.Network);
                }
            }
        }

        public async Task<OpenRouterCatalogResult> GetModelsAsync(
            string key,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(key))
                return OpenRouterCatalogResult.Unavailable(
                    OpenRouterFailureKind.MissingKey);

            using (var request = CreateAuthorizedRequest(
                       HttpMethod.Get,
                       ModelsEndpoint,
                       key))
            {
                try
                {
                    using (var response = await _httpClient.SendAsync(
                                   request,
                                   HttpCompletionOption.ResponseContentRead,
                                   cancellationToken)
                               .ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            return OpenRouterCatalogResult.Unavailable(
                                MapFailure(response.StatusCode),
                                (int)response.StatusCode);

                        var json = await response.Content.ReadAsStringAsync()
                            .ConfigureAwait(false);
                        return ParseCatalog(json);
                    }
                }
                catch (OperationCanceledException)
                {
                    return OpenRouterCatalogResult.Unavailable(
                        OpenRouterFailureKind.Cancelled);
                }
                catch (HttpRequestException)
                {
                    return OpenRouterCatalogResult.Unavailable(
                        OpenRouterFailureKind.Network);
                }
                catch (JsonException)
                {
                    return OpenRouterCatalogResult.Unavailable(
                        OpenRouterFailureKind.InvalidResponse);
                }
            }
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
                    AiColorOutcomeKind.MissingKey,
                    null);
            if (model == null || string.IsNullOrWhiteSpace(model.Id))
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.ModelNotSelected);
            if (!model.IsColoringCompatible)
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.ModelIncompatible);
            if (objectNames == null || objectNames.Count == 0)
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.InvalidRequest);
            if (objectNames.Distinct(StringComparer.Ordinal).Count() !=
                objectNames.Count)
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.InvalidRequest);
            if (objectNames.Distinct(StringComparer.Ordinal).Count() >
                OpenRouterColorRequestLimits.MaxUniqueObjectNames)
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.TooManyObjects);

            JObject payload;
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
            try
            {
                payload = OpenRouterRequestFactory.CreateColorRequest(
                    objectNames,
                    schemeName,
                    model,
                    temperature);
            }
            catch (InvalidOperationException)
            {
                return AiColorOutcome.Failure(
                    requestPolicy.FailureOutcomeKind,
                    null,
                    requestDiagnostics);
            }
            catch (ArgumentException)
            {
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.InvalidRequest);
            }

            using (var request = CreateAuthorizedRequest(
                       HttpMethod.Post,
                       ChatEndpoint,
                       key))
            {
                request.Content = new StringContent(
                    payload.ToString(Formatting.None),
                    Encoding.UTF8,
                    "application/json");
                try
                {
                    using (var response = await _httpClient.SendAsync(
                                   request,
                                   HttpCompletionOption.ResponseContentRead,
                                   cancellationToken)
                               .ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            return AiColorOutcome.Failure(
                                MapColorFailure(response.StatusCode),
                                (int)response.StatusCode);

                        var responseJson =
                            await response.Content.ReadAsStringAsync()
                                .ConfigureAwait(false);
                        var parsed = OpenRouterColorResponseParser.Parse(
                            responseJson,
                            objectNames);
                        var diagnostics = new AiColorDiagnostics(
                            parsed.FinishReason,
                            objectNames.Count,
                            requestPolicy.OutputBudget,
                            model.MaxCompletionTokens,
                            requestPolicy.ReasoningPolicy);
                        if (!parsed.IsSuccess)
                            return AiColorOutcome.Failure(
                                parsed.FailureKind,
                                null,
                                diagnostics);

                        return AiColorOutcome.Success(
                            AiColorSource.OpenRouter,
                            parsed.Colors,
                            diagnostics);
                    }
                }
                catch (OperationCanceledException)
                {
                    return AiColorOutcome.Failure(
                        AiColorOutcomeKind.Cancelled);
                }
                catch (HttpRequestException)
                {
                    return AiColorOutcome.Failure(
                        AiColorOutcomeKind.Network);
                }
            }
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

        public void Dispose()
        {
            if (_ownsClient)
                _httpClient.Dispose();
        }

        private static HttpClient CreateSharedClient()
        {
            var client = new HttpClient
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "NavisHelper/1.0");
            return client;
        }

        private static HttpRequestMessage CreateAuthorizedRequest(
            HttpMethod method,
            string endpoint,
            string key)
        {
            var request = new HttpRequestMessage(method, endpoint);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", key);
            return request;
        }

        private static OpenRouterCatalogResult ParseCatalog(string json)
        {
            var root = JObject.Parse(json);
            var models = new Dictionary<string, OpenRouterModelInfo>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var model in (root["data"] as JArray ?? new JArray())
                         .OfType<JObject>())
            {
                var id = (string)model["id"];
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                if (models.ContainsKey(id))
                    return OpenRouterCatalogResult.Unavailable(
                        OpenRouterFailureKind.InvalidResponse);

                var supported = (model["supported_parameters"] as JArray ??
                                 new JArray())
                    .Values<string>()
                    .Where(value => !string.IsNullOrWhiteSpace(value));
                var inputModalities =
                    model.SelectToken("architecture.input_modalities") as JArray;
                var outputModalities =
                    model.SelectToken("architecture.output_modalities") as JArray;
                var reasoning = model["reasoning"] as JObject;
                models[id] = new OpenRouterModelInfo(
                    id,
                    (string)model["name"],
                    supported,
                    inputModalities?.Values<string>(),
                    outputModalities?.Values<string>(),
                    (string)model.SelectToken("architecture.modality"),
                    (int?)model["context_length"],
                    (int?)model.SelectToken("top_provider.max_completion_tokens"),
                    reasoning == null
                        ? null
                        : new OpenRouterReasoningInfo(
                            (bool?)reasoning["mandatory"],
                            (bool?)reasoning["default_enabled"],
                            (reasoning["supported_efforts"] as JArray)
                                ?.Values<string>(),
                            (string)reasoning["default_effort"],
                            (bool?)reasoning["supports_max_tokens"],
                            reasoning.Property("supported_efforts") != null));
            }

            return models.Count == 0
                ? OpenRouterCatalogResult.Unavailable(
                    OpenRouterFailureKind.InvalidResponse)
                : OpenRouterCatalogResult.Available(models);
        }

        private static OpenRouterFailureKind MapFailure(
            HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            if (code == 401 || code == 403)
                return OpenRouterFailureKind.Unauthorized;
            if (code == 429)
                return OpenRouterFailureKind.RateLimited;
            return OpenRouterFailureKind.ServiceUnavailable;
        }

        private static AiColorOutcomeKind MapColorFailure(
            HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            if (code == 400 || code == 422)
                return AiColorOutcomeKind.BadRequest;
            if (code == 404)
                return AiColorOutcomeKind.ModelUnavailable;
            var failure = MapFailure(statusCode);
            switch (failure)
            {
                case OpenRouterFailureKind.Unauthorized:
                    return AiColorOutcomeKind.Unauthorized;
                case OpenRouterFailureKind.RateLimited:
                    return AiColorOutcomeKind.RateLimited;
                default:
                    return AiColorOutcomeKind.ServiceUnavailable;
            }
        }
    }
}
