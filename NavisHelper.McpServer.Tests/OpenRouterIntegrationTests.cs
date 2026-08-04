using System.Net;
using System.Net.Http;
using System.Text;
using NavisHelper.AI;
using Newtonsoft.Json.Linq;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class OpenRouterIntegrationTests
{
    [Fact]
    public void CompletionDecision_BlocksSuccessfulOutcomeAfterUserCancellation()
    {
        var success = SuccessfulColorOutcome();

        var decision = AIColorCompletionDecision.Evaluate(
            success,
            documentChanged: false,
            timedOut: false,
            userCancelled: true);

        Assert.False(decision.MayApply);
        Assert.Equal(AiColorOutcomeKind.Cancelled, decision.Outcome.Kind);
        Assert.Empty(decision.Outcome.Colors);
    }

    [Fact]
    public void CompletionDecision_BlocksSuccessfulOutcomeAfterTimeout()
    {
        var decision = AIColorCompletionDecision.Evaluate(
            SuccessfulColorOutcome(),
            documentChanged: false,
            timedOut: true,
            userCancelled: true);

        Assert.False(decision.MayApply);
        Assert.Equal(AiColorOutcomeKind.Timeout, decision.Outcome.Kind);
    }

    [Fact]
    public void CompletionDecision_DocumentChangeHasHighestPriority()
    {
        var decision = AIColorCompletionDecision.Evaluate(
            SuccessfulColorOutcome(),
            documentChanged: true,
            timedOut: true,
            userCancelled: true);

        Assert.False(decision.MayApply);
        Assert.Equal(
            AiColorOutcomeKind.DocumentChanged,
            decision.Outcome.Kind);
    }

    [Fact]
    public void CompletionDecision_AllowsUncancelledSuccessfulOutcome()
    {
        var success = SuccessfulColorOutcome();

        var decision = AIColorCompletionDecision.Evaluate(
            success,
            documentChanged: false,
            timedOut: false,
            userCancelled: false);

        Assert.True(decision.MayApply);
        Assert.Same(success, decision.Outcome);
    }

    [Fact]
    public void CompletionDecision_PreservesUncancelledNetworkFailure()
    {
        var failure = AiColorOutcome.Failure(
            AiColorOutcomeKind.RateLimited,
            429);

        var decision = AIColorCompletionDecision.Evaluate(
            failure,
            documentChanged: false,
            timedOut: false,
            userCancelled: false);

        Assert.False(decision.MayApply);
        Assert.Same(failure, decision.Outcome);
        Assert.Equal(429, decision.Outcome.HttpStatus);
    }

    [Fact]
    public void CompletionDecision_NormalizesEmptySuccessfulOutcome()
    {
        var emptySuccess = AiColorOutcome.Success(
            AiColorSource.OpenRouter,
            new Dictionary<string, string>());

        var decision = AIColorCompletionDecision.Evaluate(
            emptySuccess,
            documentChanged: false,
            timedOut: false,
            userCancelled: false);

        Assert.False(decision.MayApply);
        Assert.Equal(
            AiColorOutcomeKind.InvalidResponse,
            decision.Outcome.Kind);
    }

    [Fact]
    public void CompletionDecision_CancellationOverridesNetworkFailure()
    {
        var failure = AiColorOutcome.Failure(
            AiColorOutcomeKind.Network);

        var decision = AIColorCompletionDecision.Evaluate(
            failure,
            documentChanged: false,
            timedOut: false,
            userCancelled: true);

        Assert.False(decision.MayApply);
        Assert.Equal(AiColorOutcomeKind.Cancelled, decision.Outcome.Kind);
    }

    [Fact]
    public async Task ValidateKey_UsesOfficialKeyEndpointAndBearerHeader()
    {
        HttpRequestMessage captured = null;
        var handler = new StubHandler(request =>
        {
            captured = CloneRequest(request);
            return JsonResponse(HttpStatusCode.OK, """{"data":{"label":"hidden"}}""");
        });
        using var http = new HttpClient(handler);
        using var client = new OpenRouterClient(http);

        var result = await client.ValidateKeyAsync(
            "test-secret",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OpenRouterClient.KeyEndpoint, captured.RequestUri.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization.Scheme);
        Assert.Equal("test-secret", captured.Headers.Authorization.Parameter);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "Unauthorized")]
    [InlineData((HttpStatusCode)429, "RateLimited")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ServiceUnavailable")]
    public async Task ValidateKey_MapsHttpFailures(
        HttpStatusCode status,
        string expected)
    {
        var handler = new StubHandler(_ => JsonResponse(
            status,
            """{"error":{"message":"must-not-be-exposed"}}"""));
        using var http = new HttpClient(handler);
        using var client = new OpenRouterClient(http);

        var result = await client.ValidateKeyAsync(
            "test-secret",
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.FailureKind.ToString());
        Assert.DoesNotContain("test-secret", result.DiagnosticCode);
        Assert.DoesNotContain("must-not-be-exposed", result.DiagnosticCode);
    }

    [Fact]
    public async Task ValidateKey_MapsNetworkException()
    {
        using var http = new HttpClient(new ThrowingHandler());
        using var client = new OpenRouterClient(http);

        var result = await client.ValidateKeyAsync(
            "test-secret",
            CancellationToken.None);

        Assert.Equal(OpenRouterFailureKind.Network, result.FailureKind);
    }

    [Fact]
    public async Task ValidateKey_MapsCallerTimeoutAsCancellationForCallerToClassify()
    {
        using var http = new HttpClient(new CancellingHandler())
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        using var client = new OpenRouterClient(http);
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(25));

        var result = await client.ValidateKeyAsync(
            "test-secret",
            timeout.Token);

        Assert.Equal(OpenRouterFailureKind.Cancelled, result.FailureKind);
        Assert.Equal(
            AiColorOutcomeKind.Timeout,
            AiCancellationClassifier.Classify(false, true));
        Assert.Equal(
            AiColorOutcomeKind.Cancelled,
            AiCancellationClassifier.Classify(false, false));
    }

    [Fact]
    public async Task Catalog_UsesFullIdsAndSupportedParameters()
    {
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """
            {
              "data": [
                {
                  "id": "vendor/structured-model",
                  "name": "Structured Model",
                  "supported_parameters": ["temperature", "max_tokens", "structured_outputs"],
                  "architecture": {"input_modalities":["text"],"output_modalities":["text"]},
                  "top_provider": {"max_completion_tokens":4096}
                },
                {
                  "id": "vendor/basic-model",
                  "supported_parameters": ["max_tokens"]
                }
              ]
            }
            """));
        using var http = new HttpClient(handler);
        using var client = new OpenRouterClient(http);

        var result = await client.GetModelsAsync(
            "test-secret",
            CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.True(result.Models["vendor/structured-model"].IsColoringCompatible);
        Assert.False(result.Models["vendor/basic-model"].IsColoringCompatible);
    }

    [Theory]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"data":[{"name":"missing id"}]}""")]
    [InlineData("""not-json""")]
    public async Task Catalog_RejectsEmptyOrMalformedPayload(string payload)
    {
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            payload));
        using var http = new HttpClient(handler);
        using var client = new OpenRouterClient(http);

        var result = await client.GetModelsAsync(
            "test-secret",
            CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Equal(
            OpenRouterFailureKind.InvalidResponse,
            result.FailureKind);
    }

    [Fact]
    public void RequestFactory_UsesStrictStructuredOutput()
    {
        var payload = OpenRouterRequestFactory.CreateColorRequest(
            new[] { "Pipe 1" },
            "Architectural",
            "vendor/model-id",
            ["structured_outputs", "temperature", "max_tokens"],
            0.3);

        Assert.Equal("vendor/model-id", (string)payload["model"]);
        Assert.Equal("json_schema", (string)payload["response_format"]["type"]);
        Assert.True((bool)payload["response_format"]["json_schema"]["strict"]);
        Assert.True((bool)payload["provider"]["require_parameters"]);
        Assert.False((bool)payload["stream"]);
        Assert.Equal(0.3, (double)payload["temperature"]);
        Assert.True((int)payload["max_tokens"] >= 512);
        Assert.Null(payload["reasoning"]);
    }

    [Fact]
    public void ResponseParser_RequiresEveryExactObjectAndValidRgb()
    {
        var response = new JObject
        {
            ["choices"] = new JArray
            {
                new JObject
                {
                    ["message"] = new JObject
                    {
                        ["content"] =
                            """
                            {"colors":[
                              {"object":"Pipe \"A\"","color":"10, 20,30"},
                              {"object":"Door","color":"40,50,60"}
                            ]}
                            """
                    }
                }
            }
        }.ToString();

        var parsed = OpenRouterColorResponseParser.TryParse(
            response,
            new[] { "Pipe \"A\"", "Door" },
            out var colors);

        Assert.True(parsed);
        Assert.Equal("10,20,30", colors["Pipe \"A\""]);
        Assert.Equal("40,50,60", colors["Door"]);
    }

    [Fact]
    public void ResponseParser_RejectsIncompleteResults()
    {
        var response = """
            {"choices":[{"message":{"content":"{\"colors\":[{\"object\":\"Pipe\",\"color\":\"1,2,3\"}]}"}}]}
            """;

        var parsed = OpenRouterColorResponseParser.TryParse(
            response,
            new[] { "Pipe", "Door" },
            out _);

        Assert.False(parsed);
    }

    [Theory]
    [InlineData("""{"colors":[{"object":"Pipe","color":"256,2,3"}]}""")]
    [InlineData("""{"colors":[{"object":"Pipe","color":"1,2"}]}""")]
    [InlineData("""{"colors":[{"object":"Unexpected","color":"1,2,3"}]}""")]
    [InlineData("""{"colors":[{"object":"Pipe","color":"1,2,3"},{"object":"Pipe","color":"4,5,6"}]}""")]
    public void ResponseParser_RejectsInvalidDuplicateOrUnexpectedEntries(
        string content)
    {
        var response = new JObject
        {
            ["choices"] = new JArray
            {
                new JObject
                {
                    ["message"] = new JObject
                    {
                        ["content"] = content
                    }
                }
            }
        }.ToString();

        Assert.False(OpenRouterColorResponseParser.TryParse(
            response,
            new[] { "Pipe" },
            out _));
    }

    [Fact]
    public async Task ChatSuccess_HasOpenRouterProvenance()
    {
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """
            {"choices":[{"message":{"content":"{\"colors\":[{\"object\":\"Pipe\",\"color\":\"1,2,3\"}]}"}}]}
            """));
        using var http = new HttpClient(handler);
        using var client = new OpenRouterClient(http);

        var outcome = await client.GetColorsAsync(
            "test-secret",
            new[] { "Pipe" },
            "Architectural",
            "vendor/model",
            ["structured_outputs", "temperature", "max_tokens"],
            0.3,
            CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(AiColorSource.OpenRouter, outcome.Source);
        Assert.Equal("1,2,3", outcome.Colors["Pipe"]);
    }

    [Fact]
    public async Task ChatCancellationAndNetworkFailure_AreTypedWithoutFallback()
    {
        using var cancellationHttp =
            new HttpClient(new CancellingHandler())
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        using var cancellationClient =
            new OpenRouterClient(cancellationHttp);
        using var cancellation = new CancellationTokenSource();
        var pending = cancellationClient.GetColorsAsync(
            "test-secret",
            new[] { "Pipe" },
            "Architectural",
            "vendor/model",
            ["structured_outputs"],
            0.3,
            cancellation.Token);
        cancellation.Cancel();

        var cancelled = await pending;

        using var networkHttp = new HttpClient(new ThrowingHandler());
        using var networkClient = new OpenRouterClient(networkHttp);
        var network = await networkClient.GetColorsAsync(
            "test-secret",
            new[] { "Pipe" },
            "Architectural",
            "vendor/model",
            ["structured_outputs"],
            0.3,
            CancellationToken.None);

        Assert.Equal(AiColorOutcomeKind.Cancelled, cancelled.Kind);
        Assert.Equal(AiColorOutcomeKind.Network, network.Kind);
        Assert.Equal(AiColorSource.None, cancelled.Source);
        Assert.Equal(AiColorSource.None, network.Source);
        Assert.Empty(cancelled.Colors);
        Assert.Empty(network.Colors);
    }

    [Fact]
    public async Task ColorApiError_DoesNotReturnFallbackColors()
    {
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.ServiceUnavailable,
            """{"error":{"message":"unavailable"}}"""));
        using var http = new HttpClient(handler);
        using var client = new OpenRouterClient(http);

        var outcome = await client.GetColorsAsync(
            "test-secret",
            new[] { "Pipe" },
            "Architectural",
            "vendor/model",
            ["structured_outputs"],
            0.3,
            CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(AiColorOutcomeKind.ServiceUnavailable, outcome.Kind);
        Assert.Empty(outcome.Colors);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "Unauthorized")]
    [InlineData((HttpStatusCode)429, "RateLimited")]
    [InlineData(HttpStatusCode.BadRequest, "BadRequest")]
    [InlineData(HttpStatusCode.NotFound, "ModelUnavailable")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "BadRequest")]
    public async Task ColorHttpFailures_AreMappedWithoutFallback(
        HttpStatusCode status,
        string expected)
    {
        var handler = new StubHandler(_ => JsonResponse(status, "{}"));
        using var http = new HttpClient(handler);
        using var client = new OpenRouterClient(http);

        var outcome = await client.GetColorsAsync(
            "test-secret",
            new[] { "Pipe" },
            "Architectural",
            "vendor/model",
            ["structured_outputs"],
            0.3,
            CancellationToken.None);

        Assert.Equal(expected, outcome.Kind.ToString());
        Assert.Equal((int)status, outcome.HttpStatus);
        Assert.Equal(AiColorSource.None, outcome.Source);
        Assert.Empty(outcome.Colors);
    }

    [Fact]
    public async Task Cancellation_IsObservedAndTyped()
    {
        var handler = new CancellingHandler();
        using var http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        using var client = new OpenRouterClient(http);
        using var cancellation = new CancellationTokenSource();

        var operation = client.GetModelsAsync(
            "test-secret",
            cancellation.Token);
        cancellation.Cancel();
        var result = await operation;

        Assert.False(result.IsAvailable);
        Assert.Equal(OpenRouterFailureKind.Cancelled, result.FailureKind);
    }

    [Fact]
    public void CatalogPolicy_BlocksKnownMissingModelWithoutChat()
    {
        var catalog = OpenRouterCatalogResult.Available(
            new Dictionary<string, OpenRouterModelInfo>
            {
                ["vendor/known"] = new(
                    "vendor/known",
                    "Known",
                    new[] { "structured_outputs" })
            });

        var policy = OpenRouterModelPolicy.Evaluate(
            catalog,
            "vendor/custom");

        Assert.False(policy.MaySendChat);
        Assert.Equal(
            AiColorOutcomeKind.ModelUnavailable,
            policy.Failure.Kind);
    }

    [Fact]
    public void CatalogPolicy_BlocksTemporaryCatalogFailure()
    {
        var policy = OpenRouterModelPolicy.Evaluate(
            OpenRouterCatalogResult.Unavailable(
                OpenRouterFailureKind.Network),
            "vendor/custom");

        Assert.False(policy.MaySendChat);
        Assert.Equal(AiColorOutcomeKind.CatalogUnavailable, policy.Failure.Kind);
    }

    [Fact]
    public void CatalogPolicy_AllowsOnlyStructuredOutputCompatibleModel()
    {
        var supported = OpenRouterCatalogResult.Available(
            new Dictionary<string, OpenRouterModelInfo>
            {
                ["vendor/model"] = new(
                    "vendor/model",
                    "Model",
                    new[] { "structured_outputs" })
            });
        var unsupported = OpenRouterCatalogResult.Available(
            new Dictionary<string, OpenRouterModelInfo>
            {
                ["vendor/model"] = new(
                    "vendor/model",
                    "Model",
                    Array.Empty<string>())
            });

        Assert.True(OpenRouterModelPolicy.Evaluate(
            supported,
            "vendor/model").MaySendChat);
        var blocked = OpenRouterModelPolicy.Evaluate(
            unsupported,
            "vendor/model");
        Assert.False(blocked.MaySendChat);
        Assert.Equal(AiColorOutcomeKind.ModelIncompatible, blocked.Failure.Kind);
    }

    [Fact]
    public void UnknownCustomModelId_IsPreserved()
    {
        Assert.Equal(
            "custom-provider/custom-model",
            OpenRouterModelSelection.MigrationCandidate(
                " custom-provider/custom-model "));
        Assert.Equal(string.Empty,
            OpenRouterModelSelection.MigrationCandidate("legacy-short-id"));
    }

    [Fact]
    public void CatalogCache_UsesSuccessfulCatalogForSameKeyGenerationOnly()
    {
        var cache = new OpenRouterCatalogCache(TimeSpan.FromMinutes(1));
        var now = DateTime.UtcNow;
        var catalog = OpenRouterCatalogResult.Available(
            new Dictionary<string, OpenRouterModelInfo>
            {
                ["vendor/model"] = new(
                    "vendor/model",
                    "Model",
                    Array.Empty<string>())
            });

        cache.Store(4, catalog, now);

        Assert.Same(catalog, cache.TryGet(4, now.AddSeconds(30)));
        Assert.Null(cache.TryGet(5, now.AddSeconds(30)));
        Assert.Null(cache.TryGet(4, now.AddMinutes(2)));
    }

    [Fact]
    public void ConfigMigration_IgnoresAndOmitsLegacyConnectionFields()
    {
        var defaults = new AIConfigData
        {
            ModelName = "vendor/default",
            Temperature = 0.3,
            ColorScheme = 8
        };
        var parsed = AIConfigJsonSerializer.Parse(
            """
            {
              "ModelName": "vendor/custom",
              "ApiKey": "legacy-secret",
              "ApiUrl": "https://legacy.invalid",
              "RequestTimeout": 999,
              "MaxRetries": 9,
              "EnableLogging": true
            }
            """,
            defaults);

        var serialized = AIConfigJsonSerializer.Serialize(parsed);

        Assert.Equal("vendor/custom", parsed.ModelName);
        Assert.DoesNotContain("legacy-secret", serialized);
        Assert.DoesNotContain("ApiKey", serialized);
        Assert.DoesNotContain("ApiUrl", serialized);
        Assert.DoesNotContain("RequestTimeout", serialized);
        Assert.DoesNotContain("MaxRetries", serialized);
        Assert.DoesNotContain("EnableLogging", serialized);
    }

    [Fact]
    public void PanelOutcome_PreservesTypedProvenanceAndRelocalizes()
    {
        var outcome = AiPanelOutcome.Success(
            AiColorSource.LocalPalette,
            string.Empty,
            8,
            new[] { "Pipe" },
            new Dictionary<string, string> { ["Pipe"] = "1,2,3" },
            1);
        static string En(string key) => key switch
        {
            "Panel_Colors_Ai_Source_LocalPalette" => "Local palette",
            "Panel_Colors_Ai_Model_NotApplicable" => "not applicable",
            _ => key
        };
        static string Ru(string key) => key switch
        {
            "Panel_Colors_Ai_Source_LocalPalette" => "Локальная палитра",
            "Panel_Colors_Ai_Model_NotApplicable" => "не применяется",
            _ => key
        };
        static string Format(string key, object[] args) =>
            key + ":" + string.Join("|", args);

        var english = AiPanelOutcomeFormatter.Format(
            outcome,
            En,
            Format,
            _ => "Architectural");
        var russian = AiPanelOutcomeFormatter.Format(
            outcome,
            Ru,
            Format,
            _ => "Архитектурная");

        Assert.Contains("Local palette", english);
        Assert.Contains("not applicable", english);
        Assert.Contains("Локальная палитра", russian);
        Assert.Contains("не применяется", russian);
        Assert.Equal(AiColorSource.LocalPalette, outcome.Source);
    }

    [Fact]
    public void SemanticConnectionModelAndErrorStates_RelocalizeFromResourceKeys()
    {
        Assert.Equal(
            "Settings_Ai_Status_RateLimited",
            AiConnectionStatusMapper.ResourceKey(
                AiConnectionDisplayState.RateLimited));
        var model = AiModelStatusMapper.Evaluate(
            true,
            OpenRouterCatalogResult.Available(
                new Dictionary<string, OpenRouterModelInfo>
                {
                    ["vendor/model"] = new(
                        "vendor/model",
                        "Model",
                        new[] { "structured_outputs" })
                }),
            "vendor/model");
        Assert.Equal(
            "Settings_Ai_Model_Ready",
            model.StatusResource);
        Assert.True(model.IsReady);

        var failure = AiPanelOutcome.Failure(
            AiColorOutcomeKind.ModelUnavailable,
            "vendor/model",
            new[] { "Raw object" });
        string English(string key) =>
            key == "Panel_Colors_Ai_ModelUnavailable"
                ? "Model unavailable"
                : key;
        string Russian(string key) =>
            key == "Panel_Colors_Ai_ModelUnavailable"
                ? "Модель недоступна"
                : key;
        static string Format(string key, object[] args) =>
            key + string.Join("|", args);

        Assert.Contains(
            "Model unavailable",
            AiPanelOutcomeFormatter.Format(
                failure,
                English,
                Format,
                null));
        Assert.Contains(
            "Модель недоступна",
            AiPanelOutcomeFormatter.Format(
                failure,
                Russian,
                Format,
                null));
        Assert.Equal("vendor/model", failure.ModelId);
        Assert.Equal("Raw object", failure.ObjectNames.Single());
    }

    [Fact]
    public async Task Catalog_UsesUserFilteredEndpointAndBuildsDynamicChoices()
    {
        HttpRequestMessage captured = null;
        var handler = new StubHandler(request =>
        {
            captured = CloneRequest(request);
            return JsonResponse(HttpStatusCode.OK,
                """
                {"data":[
                  {"id":"provider/compatible","name":"Compatible","context_length":32000,"supported_parameters":["structured_outputs","temperature","max_tokens"],"architecture":{"input_modalities":["text"],"output_modalities":["text"]},"top_provider":{"max_completion_tokens":4096}},
                  {"id":"provider/no-structured","name":"No schema","supported_parameters":["temperature"]},
                  {"id":"provider/no-text","name":"Image only","supported_parameters":["structured_outputs"],"architecture":{"output_modalities":["image"]}}
                ]}
                """);
        });
        using var http = new HttpClient(handler);
        using var client = new OpenRouterClient(http);

        var catalog = await client.GetModelsAsync(
            "test-secret",
            CancellationToken.None);
        var choices = OpenRouterModelSelection.CompatibleChoices(catalog);

        Assert.Equal(OpenRouterClient.ModelsEndpoint, captured.RequestUri.ToString());
        var choice = Assert.Single(choices);
        Assert.Equal("provider/compatible", choice.Id);
        Assert.StartsWith("Compatible — provider/compatible", choice.DisplayText);
    }

    [Fact]
    public void ModelSelection_RestoresOnlyExactCompatibleFullId()
    {
        var catalog = OpenRouterCatalogResult.Available(
            new Dictionary<string, OpenRouterModelInfo>
            {
                ["provider/ready"] = new(
                    "provider/ready",
                    "Ready",
                    ["structured_outputs"]),
                ["provider/incompatible"] = new(
                    "provider/incompatible",
                    "Incompatible",
                    ["temperature"])
            });

        Assert.Equal(
            "provider/ready",
            OpenRouterModelSelection.Restore(
                catalog,
                "provider/ready").Id);
        Assert.Null(OpenRouterModelSelection.Restore(
            catalog,
            "provider/incompatible"));
        Assert.Null(OpenRouterModelSelection.Restore(
            catalog,
            "provider/missing"));
    }

    [Fact]
    public async Task MissingModel_BlocksBeforeChatRequest()
    {
        var requestCount = 0;
        var handler = new StubHandler(_ =>
        {
            requestCount++;
            return JsonResponse(HttpStatusCode.OK, "{}");
        });
        using var http = new HttpClient(handler);
        using var client = new OpenRouterClient(http);

        var outcome = await client.GetColorsAsync(
            "test-secret",
            ["Pipe"],
            "Architectural",
            string.Empty,
            ["structured_outputs"],
            0.3,
            CancellationToken.None);

        Assert.Equal(AiColorOutcomeKind.ModelNotSelected, outcome.Kind);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task TooManyUniqueNames_BlocksBeforeChatRequest()
    {
        var requestCount = 0;
        var handler = new StubHandler(_ =>
        {
            requestCount++;
            return JsonResponse(HttpStatusCode.OK, "{}");
        });
        using var http = new HttpClient(handler);
        using var client = new OpenRouterClient(http);
        var names = Enumerable.Range(
                0,
                OpenRouterColorRequestLimits.MaxUniqueObjectNames + 1)
            .Select(index => "Object " + index)
            .ToArray();

        var outcome = await client.GetColorsAsync(
            "test-secret",
            names,
            "Architectural",
            "provider/model",
            ["structured_outputs"],
            0.3,
            CancellationToken.None);

        Assert.Equal(AiColorOutcomeKind.TooManyObjects, outcome.Kind);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public void StructuredRequest_UsesExactClosedSchemaAndSupportedOptionsOnly()
    {
        var payload = OpenRouterRequestFactory.CreateColorRequest(
            ["Pipe", "Door"],
            "Architectural",
            "provider/model",
            ["structured_outputs"],
            0.3);
        var schema = (JObject)payload["response_format"]["json_schema"]["schema"];
        var colors = (JObject)schema["properties"]["colors"];
        var item = (JObject)colors["items"];

        Assert.False((bool)schema["additionalProperties"]);
        Assert.Equal(["colors"], schema["required"].Values<string>());
        Assert.Equal(2, (int)colors["minItems"]);
        Assert.Equal(2, (int)colors["maxItems"]);
        Assert.False((bool)item["additionalProperties"]);
        Assert.Equal(
            ["object", "color"],
            item["required"].Values<string>());
        Assert.Equal(
            ["Pipe", "Door"],
            item["properties"]["object"]["enum"].Values<string>());
        Assert.Null(payload["temperature"]);
        Assert.True((int)payload["max_tokens"] >= 512);
        Assert.Null(payload["reasoning"]);
    }

    [Theory]
    [InlineData("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"{}\"}}]}", (int)AiColorOutcomeKind.TruncatedResponse)]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"refusal\":\"no\",\"content\":null}}]}", (int)AiColorOutcomeKind.ResponseRefused)]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":null}}]}", (int)AiColorOutcomeKind.MissingAssistantContent)]
    public void ResponseParser_ClassifiesEnvelopeFailures(
        string response,
        int expectedValue)
    {
        var parsed = OpenRouterColorResponseParser.Parse(
            response,
            ["Pipe"]);

        Assert.False(parsed.IsSuccess);
        Assert.Equal((AiColorOutcomeKind)expectedValue, parsed.FailureKind);
    }

    [Theory]
    [InlineData("{\"colors\":[{\"object\":\"Pipe\",\"color\":\"1,2,3\"}]}", (int)AiColorOutcomeKind.Success)]
    [InlineData("{\"colors\":[]}", (int)AiColorOutcomeKind.IncompleteObjectSet)]
    [InlineData("{\"colors\":[{\"object\":\"Pipe\",\"color\":\"1,2,3\"},{\"object\":\"Pipe\",\"color\":\"4,5,6\"}]}", (int)AiColorOutcomeKind.IncompleteObjectSet)]
    [InlineData("{\"colors\":[{\"object\":\"Other\",\"color\":\"1,2,3\"}]}", (int)AiColorOutcomeKind.IncompleteObjectSet)]
    public void ResponseParser_ValidatesExactObjectSet(
        string content,
        int expectedValue)
    {
        var response = new JObject
        {
            ["choices"] = new JArray(new JObject
            {
                ["finish_reason"] = "stop",
                ["message"] = new JObject { ["content"] = content }
            })
        }.ToString();

        var parsed = OpenRouterColorResponseParser.Parse(response, ["Pipe"]);

        var expected = (AiColorOutcomeKind)expectedValue;
        Assert.Equal(expected == AiColorOutcomeKind.Success, parsed.IsSuccess);
        Assert.Equal(expected, parsed.FailureKind);
    }

    [Fact]
    public async Task OneColorAction_SendsExactlyOneChatRequestWithoutRetry()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """
            {"choices":[{"finish_reason":"stop","message":{"content":"{\"colors\":[{\"object\":\"Pipe\",\"color\":\"1,2,3\"}]}"}}]}
            """);
        using var http = new HttpClient(handler);
        using var client = new OpenRouterClient(http);

        var outcome = await client.GetColorsAsync(
            "test-secret",
            ["Pipe"],
            "Architectural",
            "provider/model",
            ["structured_outputs"],
            0.3,
            CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public void KeyStore_SavesAndDisconnectsOnlyUserAndProcessTargets()
    {
        var environment = new FakeEnvironment();
        var store = new OpenRouterKeyStore(environment);

        store.SaveValidatedKey("test-secret");

        Assert.Equal(
            "test-secret",
            environment.Get(
                OpenRouterKeyStore.EnvironmentVariableName,
                EnvironmentVariableTarget.User));
        Assert.Equal(
            "test-secret",
            environment.Get(
                OpenRouterKeyStore.EnvironmentVariableName,
                EnvironmentVariableTarget.Process));
        Assert.Equal("test-secret", store.GetKey());

        store.Disconnect();

        Assert.Null(environment.Get(
            OpenRouterKeyStore.EnvironmentVariableName,
            EnvironmentVariableTarget.User));
        Assert.Null(environment.Get(
            OpenRouterKeyStore.EnvironmentVariableName,
            EnvironmentVariableTarget.Process));
        Assert.False(store.HasKey);
        Assert.DoesNotContain(
            environment.Writes,
            write => write.Target == EnvironmentVariableTarget.Machine);
    }

    [Fact]
    public void KeyStore_RollsBackWhenProcessWriteFails()
    {
        var environment = new FakeEnvironment();
        environment.FailNextSetForTarget =
            EnvironmentVariableTarget.Process;
        var store = new OpenRouterKeyStore(environment);

        var result = store.SaveValidatedKey("test-secret");

        Assert.False(result.IsSuccess);
        Assert.False(result.HasAnyValue);
        Assert.False(store.HasKey);
    }

    [Fact]
    public void LateValidation_DoesNotActivateKeyAfterDisconnect()
    {
        var environment = new FakeEnvironment();
        var store = new OpenRouterKeyStore(environment);
        var validationSnapshot = store.Capture();

        store.Disconnect();
        var staleMutation = store.TryActivateExistingKey(
            "stale-secret",
            validationSnapshot.Generation);

        Assert.False(staleMutation.GenerationMatched);
        Assert.False(staleMutation.IsSuccess);
        Assert.False(store.HasKey);
    }

    [Fact]
    public void OldValidation_DoesNotOverwriteNewerConnection()
    {
        var environment = new FakeEnvironment();
        var store = new OpenRouterKeyStore(environment);
        var oldValidationSnapshot = store.Capture();

        var newer = store.TrySaveValidatedKey(
            "new-secret",
            oldValidationSnapshot.Generation);
        var stale = store.TrySaveValidatedKey(
            "old-secret",
            oldValidationSnapshot.Generation);

        Assert.True(newer.GenerationMatched);
        Assert.True(newer.IsSuccess);
        Assert.False(stale.GenerationMatched);
        Assert.Equal("new-secret", store.GetKey());
    }

    [Fact]
    public void StaleCatalog_CannotBeReadAsNewKeyGeneration()
    {
        var cache = new OpenRouterCatalogCache(TimeSpan.FromMinutes(10));
        var catalog = OpenRouterCatalogResult.Available(
            new Dictionary<string, OpenRouterModelInfo>());
        var now = DateTime.UtcNow;

        cache.Store(4, catalog, now);

        Assert.Same(catalog, cache.TryGet(4, now));
        Assert.Null(cache.TryGet(5, now));
    }

    [Fact]
    public void SettingsOperationLifetime_CancelInvalidatesAndRemainsReusable()
    {
        using var lifetime = new AISettingsOperationLifetime();
        var first = lifetime.Begin(7);

        lifetime.CancelPendingOperations();
        lifetime.CancelPendingOperations();
        var staleActionRan = false;
        Assert.False(lifetime.TryExecuteCurrent(
            first,
            () => staleActionRan = true,
            out _));
        var second = lifetime.Begin(8);

        Assert.False(staleActionRan);
        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.False(lifetime.IsCurrent(first));
        Assert.True(lifetime.IsCurrent(second));
        Assert.Equal(8, second.KeyGeneration);
    }

    [Fact]
    public void SettingsOperationLifetime_DisposeIsIdempotentAndFinal()
    {
        var lifetime = new AISettingsOperationLifetime();
        var operation = lifetime.Begin(3);

        lifetime.Dispose();
        lifetime.Dispose();
        lifetime.CancelPendingOperations();

        Assert.True(operation.CancellationToken.IsCancellationRequested);
        Assert.False(lifetime.IsCurrent(operation));
        Assert.Null(lifetime.Begin(4));
    }

    [Fact]
    public void SettingsTimeoutPolicy_UsesIndependentAdequateBudgets()
    {
        Assert.True(
            AISettingsOperationPolicy.KeyValidationTimeout >=
            TimeSpan.FromSeconds(30));
        Assert.True(
            AISettingsOperationPolicy.ModelCatalogTimeout >=
            TimeSpan.FromSeconds(45));
        Assert.NotEqual(
            AISettingsOperationPolicy.KeyValidationTimeout,
            AISettingsOperationPolicy.ModelCatalogTimeout);
    }

    [Fact]
    public void ValidationTimeout_DoesNotPermitKeyMutation()
    {
        var environment = new FakeEnvironment();
        var store = new OpenRouterKeyStore(environment);
        var validation = OpenRouterValidationResult.Success();

        var mayMutate = AISettingsOperationPolicy.MayMutateKey(
            validation,
            timedOut: true,
            cancelled: false);

        Assert.False(mayMutate);
        Assert.False(store.HasKey);
        Assert.Empty(environment.Writes);
    }

    [Fact]
    public void CatalogTimeoutAfterValidation_KeepsConnectionConnected()
    {
        var returnedCatalog = OpenRouterCatalogResult.Available(
            new Dictionary<string, OpenRouterModelInfo>());
        var catalog = AISettingsOperationPolicy.NormalizeCatalog(
            returnedCatalog,
            timedOut: true,
            cancelled: false);

        var state = AISettingsOperationPolicy.CatalogCompletionState(
            catalog,
            hasCompatibleSelection: false);
        var model = AiModelStatusMapper.Evaluate(
            connected: true,
            catalog: catalog,
            modelId: string.Empty);

        Assert.Equal(AiConnectionDisplayState.Connected, state);
        Assert.False(catalog.IsAvailable);
        Assert.Equal(OpenRouterFailureKind.Timeout, catalog.FailureKind);
        Assert.Equal("Settings_Ai_Model_CatalogTimeout", model.StatusResource);
        Assert.NotEqual("Settings_Ai_Status_Timeout", model.StatusResource);
    }

    [Theory]
    [InlineData(
        (int)OpenRouterFailureKind.Network,
        "Settings_Ai_Model_CatalogNetworkUnavailable")]
    [InlineData(
        (int)OpenRouterFailureKind.WorkerFailed,
        "Settings_Ai_Model_CatalogWorkerFailure")]
    public void CatalogFailure_UsesModelStatusWithoutDisconnectingKey(
        int failureValue,
        string expectedResource)
    {
        var failure = (OpenRouterFailureKind)failureValue;
        var catalog = OpenRouterCatalogResult.Unavailable(failure);

        var state = AISettingsOperationPolicy.CatalogCompletionState(
            catalog,
            hasCompatibleSelection: false);
        var model = AiModelStatusMapper.Evaluate(
            connected: true,
            catalog: catalog,
            modelId: string.Empty);

        Assert.Equal(AiConnectionDisplayState.Connected, state);
        Assert.Equal(expectedResource, model.StatusResource);
    }

    [Fact]
    public void RefreshModelsTimeout_KeepsVerifiedKeyConnected()
    {
        var timeout = OpenRouterCatalogResult.Unavailable(
            OpenRouterFailureKind.Timeout);

        Assert.Equal(
            AiConnectionDisplayState.Connected,
            AISettingsOperationPolicy.CatalogCompletionState(
                timeout,
                hasCompatibleSelection: false));
    }

    [Fact]
    public void LifecycleCancellation_CancelsBothIndependentStageTokens()
    {
        using var lifetime = new AISettingsOperationLifetime();
        var operation = lifetime.Begin(1);
        using var validationTimeout = new CancellationTokenSource();
        using var catalogTimeout = new CancellationTokenSource();
        using var validation = CancellationTokenSource.CreateLinkedTokenSource(
            operation.CancellationToken,
            validationTimeout.Token);
        using var catalog = CancellationTokenSource.CreateLinkedTokenSource(
            operation.CancellationToken,
            catalogTimeout.Token);

        lifetime.CancelPendingOperations();

        Assert.True(validation.IsCancellationRequested);
        Assert.True(catalog.IsCancellationRequested);
    }

    [Fact]
    public void SettingsDiagnosticFormatter_AcceptsNoSecretsOrRawPayload()
    {
        var method = typeof(AISettingsOperationDiagnostic).GetMethod(
            "Format",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.DoesNotContain(
            method.GetParameters(),
            parameter => parameter.ParameterType == typeof(string));
        var message = AISettingsOperationDiagnostic.Format(
            AISettingsOperationStage.LoadModels,
            OpenRouterFailureKind.WorkerFailed,
            503,
            1234,
            AISettingsOperationClassification.WorkerLifecycleFailure);
        Assert.Equal(
            "stage=load_models outcome=workerfailed http_status=503 " +
            "elapsed_ms=1234 classification=worker_lifecycle",
            message);
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode status,
        string json)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static AiColorOutcome SuccessfulColorOutcome()
    {
        return AiColorOutcome.Success(
            AiColorSource.OpenRouter,
            new Dictionary<string, string>
            {
                ["Pipe"] = "1,2,3"
            });
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        clone.Headers.Authorization = request.Headers.Authorization;
        return clone;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class RecordingHandler(
        HttpStatusCode status,
        string responseJson) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }
        internal string LastRequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestJson = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return JsonResponse(status, responseJson);
        }
    }

    private sealed class CancellingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("offline");
        }
    }

    private sealed class FakeEnvironment : IEnvironmentVariableAccessor
    {
        private readonly Dictionary<(string Name, EnvironmentVariableTarget Target), string>
            _values = new();

        internal List<(string Name, string Value, EnvironmentVariableTarget Target)>
            Writes { get; } = new();
        internal EnvironmentVariableTarget? FailNextSetForTarget { get; set; }

        public string Get(string name, EnvironmentVariableTarget target)
        {
            return _values.TryGetValue((name, target), out var value)
                ? value
                : null;
        }

        public void Set(
            string name,
            string value,
            EnvironmentVariableTarget target)
        {
            if (FailNextSetForTarget == target)
            {
                FailNextSetForTarget = null;
                throw new InvalidOperationException("simulated write failure");
            }
            Writes.Add((name, value, target));
            if (value == null)
                _values.Remove((name, target));
            else
                _values[(name, target)] = value;
        }
    }
}
