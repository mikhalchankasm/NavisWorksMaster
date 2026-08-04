using System.Diagnostics;
using NavisHelper.AI;
using Newtonsoft.Json;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class AiWorkerTransportTests
{
    [Theory]
    [InlineData((int)AiWorkerOperation.ValidateKey)]
    [InlineData((int)AiWorkerOperation.GetModels)]
    [InlineData((int)AiWorkerOperation.GetColors)]
    public void RequestEnvelope_IsVersionedTypedAndContainsNoKey(
        int operationValue)
    {
        var operation = (AiWorkerOperation)operationValue;
        const string secret = "secret-never-in-json";
        var request = AiWorkerRequestEnvelope.Create(
            Guid.NewGuid().ToString("N"),
            operation,
            operation == AiWorkerOperation.GetColors
                ? new AiWorkerRequestPayload
                {
                    ObjectNames = ["Pipe"],
                    ModelId = "vendor/model",
                    SchemeName = "Architectural",
                    SupportedParameters = ["structured_outputs"]
                }
                : null);

        var json = AiWorkerTransport.SerializeRequest(request);

        Assert.Equal(AiWorkerProtocol.CurrentVersion, request.ProtocolVersion);
        Assert.Equal(operation.ToString(), request.Operation);
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OPEN_ROUTER_NW_KEY", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessStartInfo_PassesKeyOnlyThroughChildEnvironment()
    {
        const string secret = "child-only-secret";

        ProcessStartInfo startInfo = AiWorkerProcessRunner.CreateStartInfo(
            @"C:\bundle\AiWorker\NavisHelper.AiWorker.exe",
            secret);

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal(string.Empty, startInfo.Arguments);
        Assert.DoesNotContain(secret, startInfo.FileName, StringComparison.Ordinal);
        Assert.Equal(
            secret,
            startInfo.EnvironmentVariables[
                AiWorkerProtocol.KeyEnvironmentVariable]);
    }

    [Fact]
    public void WorkerPath_UsesOneStableSiblingDirectory()
    {
        var resolved = AiWorkerTransport.ResolveWorkerPath(
            @"C:\bundle\Contents\2026\NavisHelper.dll");

        Assert.Equal(
            @"C:\bundle\Contents\AiWorker\NavisHelper.AiWorker.exe",
            resolved);
    }

    [Fact]
    public async Task ValidateKey_AcceptsMatchingEnvelope()
    {
        var runner = new EnvelopeRunner(request =>
            AiWorkerResponseEnvelope.Success(request));
        var transport = CreateTransport(runner);

        var result = await transport.ValidateKeyAsync(
            "secret",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("secret", runner.ReceivedKey);
        Assert.Equal(AiWorkerOperation.ValidateKey.ToString(), runner.Request.Operation);
    }

    [Fact]
    public async Task GetModels_ValidatesAndMapsTypedModels()
    {
        var runner = new EnvelopeRunner(request =>
        {
            var response = AiWorkerResponseEnvelope.Success(request);
            response.Models =
            [
                new AiWorkerModelDto
                {
                    Id = "vendor/model",
                    Name = "Model",
                    SupportedParameters = ["structured_outputs"]
                }
            ];
            return response;
        });
        var transport = CreateTransport(runner);

        var result = await transport.GetModelsAsync(
            "secret",
            CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.True(result.Models["vendor/model"].SupportsStructuredOutputs);
    }

    [Fact]
    public async Task GetColors_PreservesOpenRouterProvenanceWithoutFallback()
    {
        var runner = new EnvelopeRunner(request =>
        {
            var response = AiWorkerResponseEnvelope.Success(request);
            response.Colors = new Dictionary<string, string>
            {
                ["Pipe"] = "1,2,3"
            };
            return response;
        });
        var transport = CreateTransport(runner);

        var outcome = await transport.GetColorsAsync(
            "secret",
            ["Pipe"],
            "Architectural",
            "vendor/model",
            ["structured_outputs"],
            0.3,
            CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(AiColorSource.OpenRouter, outcome.Source);
        Assert.Equal("1,2,3", outcome.Colors["Pipe"]);
    }

    [Theory]
    [InlineData((int)OpenRouterFailureKind.Unauthorized, 401, (int)AiColorOutcomeKind.Unauthorized)]
    [InlineData((int)OpenRouterFailureKind.Unauthorized, 403, (int)AiColorOutcomeKind.Unauthorized)]
    [InlineData((int)OpenRouterFailureKind.RateLimited, 429, (int)AiColorOutcomeKind.RateLimited)]
    [InlineData((int)OpenRouterFailureKind.Network, null, (int)AiColorOutcomeKind.Network)]
    [InlineData((int)OpenRouterFailureKind.BadRequest, 422, (int)AiColorOutcomeKind.BadRequest)]
    [InlineData((int)OpenRouterFailureKind.ModelUnavailable, 404, (int)AiColorOutcomeKind.ModelUnavailable)]
    [InlineData((int)OpenRouterFailureKind.ResponseRefused, null, (int)AiColorOutcomeKind.ResponseRefused)]
    [InlineData((int)OpenRouterFailureKind.MissingAssistantContent, null, (int)AiColorOutcomeKind.MissingAssistantContent)]
    [InlineData((int)OpenRouterFailureKind.TruncatedResponse, null, (int)AiColorOutcomeKind.TruncatedResponse)]
    [InlineData((int)OpenRouterFailureKind.StructuredPayloadInvalid, null, (int)AiColorOutcomeKind.StructuredPayloadInvalid)]
    [InlineData((int)OpenRouterFailureKind.IncompleteObjectSet, null, (int)AiColorOutcomeKind.IncompleteObjectSet)]
    [InlineData((int)OpenRouterFailureKind.InsufficientOutputBudget, null, (int)AiColorOutcomeKind.InsufficientOutputBudget)]
    [InlineData((int)OpenRouterFailureKind.UnsupportedReasoningPolicy, null, (int)AiColorOutcomeKind.UnsupportedReasoningPolicy)]
    [InlineData((int)OpenRouterFailureKind.WorkerInternalFailure, null, (int)AiColorOutcomeKind.WorkerInternalFailure)]
    public async Task GetColors_MapsWorkerFailuresWithoutFallback(
        int failureValue,
        int? status,
        int expectedValue)
    {
        var failure = (OpenRouterFailureKind)failureValue;
        var expected = (AiColorOutcomeKind)expectedValue;
        var runner = new EnvelopeRunner(request =>
            AiWorkerResponseEnvelope.Failure(request, failure, status));
        var transport = CreateTransport(runner);

        var outcome = await transport.GetColorsAsync(
            "secret",
            ["Pipe"],
            "Architectural",
            "vendor/model",
            ["structured_outputs"],
            0.3,
            CancellationToken.None);

        Assert.Equal(expected, outcome.Kind);
        Assert.Equal(AiColorSource.None, outcome.Source);
        Assert.Empty(outcome.Colors);
        Assert.Equal(status, outcome.HttpStatus);
    }

    [Fact]
    public async Task WorkerAndInProcessGuardsClassifyUnsupportedReasoningIdentically()
    {
        var model = new OpenRouterModelInfo(
            "vendor/model",
            "Model",
            ["structured_outputs", "max_tokens"],
            ["text"],
            ["text"],
            "text->text",
            32000,
            4096,
            new OpenRouterReasoningInfo(
                false, null, null, null, null, false));
        var runner = new EnvelopeRunner(request =>
            throw new InvalidOperationException(
                "The process runner must not be reached by preflight."));
        var transportOutcome = await CreateTransport(runner).GetColorsAsync(
            "secret",
            ["Pipe"],
            "Architectural",
            model,
            0.3,
            CancellationToken.None);
        var handler = new CountingHandler();
        using var http = new HttpClient(handler);
        using var workerClient = new OpenRouterClient(http);
        var workerOutcome = await workerClient.GetColorsAsync(
            "secret",
            ["Pipe"],
            "Architectural",
            model,
            0.3,
            CancellationToken.None);

        Assert.Equal(
            AiColorOutcomeKind.UnsupportedReasoningPolicy,
            transportOutcome.Kind);
        Assert.Equal(transportOutcome.Kind, workerOutcome.Kind);
        Assert.Null(runner.Request);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData((int)AiWorkerRunFailureKind.Missing, (int)OpenRouterFailureKind.WorkerMissing)]
    [InlineData((int)AiWorkerRunFailureKind.StartupFailed, (int)OpenRouterFailureKind.WorkerStartupFailed)]
    [InlineData((int)AiWorkerRunFailureKind.RuntimeMissing, (int)OpenRouterFailureKind.WorkerRuntimeMissing)]
    [InlineData((int)AiWorkerRunFailureKind.NonZeroExit, (int)OpenRouterFailureKind.WorkerFailed)]
    [InlineData((int)AiWorkerRunFailureKind.Cancelled, (int)OpenRouterFailureKind.Cancelled)]
    public async Task ValidateKey_MapsProcessLifecycleFailures(
        int runFailureValue,
        int expectedValue)
    {
        var runFailure = (AiWorkerRunFailureKind)runFailureValue;
        var expected = (OpenRouterFailureKind)expectedValue;
        var transport = CreateTransport(new FixedRunner(
            AiWorkerRunResult.Failure(runFailure, 17)));

        var result = await transport.ValidateKeyAsync(
            "secret",
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.FailureKind);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("request")]
    [InlineData("operation")]
    public async Task ResponseIdentityMismatch_IsRejected(string mismatch)
    {
        var runner = new EnvelopeRunner(request =>
        {
            var response = AiWorkerResponseEnvelope.Success(request);
            if (mismatch == "version")
                response.ProtocolVersion++;
            if (mismatch == "request")
                response.RequestId = Guid.NewGuid().ToString("N");
            if (mismatch == "operation")
                response.Operation = AiWorkerOperation.GetModels.ToString();
            return response;
        });

        var result = await CreateTransport(runner).ValidateKeyAsync(
            "secret",
            CancellationToken.None);

        Assert.Equal(OpenRouterFailureKind.ProtocolMismatch, result.FailureKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{}")]
    public async Task MalformedResponse_IsRejected(string output)
    {
        var transport = CreateTransport(new FixedRunner(
            AiWorkerRunResult.Success(output)));

        var result = await transport.ValidateKeyAsync(
            "secret",
            CancellationToken.None);

        Assert.Contains(
            result.FailureKind,
            new[]
            {
                OpenRouterFailureKind.InvalidResponse,
                OpenRouterFailureKind.ProtocolMismatch
            });
    }

    [Fact]
    public async Task AdditionalStdoutDocument_IsRejected()
    {
        var requestId = string.Empty;
        var runner = new RawResponseRunner(requestJson =>
        {
            var request = JsonConvert.DeserializeObject<AiWorkerRequestEnvelope>(
                requestJson);
            requestId = request.RequestId;
            var json = JsonConvert.SerializeObject(
                AiWorkerResponseEnvelope.Success(request));
            return json + Environment.NewLine + json;
        });

        var result = await CreateTransport(runner).ValidateKeyAsync(
            "secret",
            CancellationToken.None);

        Assert.NotEmpty(requestId);
        Assert.Equal(OpenRouterFailureKind.ProtocolMismatch, result.FailureKind);
    }

    [Fact]
    public async Task ValidateKey_RejectsUnexpectedSuccessPayload()
    {
        var runner = new EnvelopeRunner(request =>
        {
            var response = AiWorkerResponseEnvelope.Success(request);
            response.Colors = new Dictionary<string, string>
            {
                ["unexpected"] = "1,2,3"
            };
            return response;
        });

        var result = await CreateTransport(runner).ValidateKeyAsync(
            "secret",
            CancellationToken.None);

        Assert.Equal(OpenRouterFailureKind.ProtocolMismatch, result.FailureKind);
    }

    [Fact]
    public async Task CancellationAfterWorkerSuccess_BlocksLateResponse()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new LateSuccessRunner(cancellation);
        var transport = CreateTransport(runner);

        var outcome = await transport.GetColorsAsync(
            "secret",
            ["Pipe"],
            "Architectural",
            "vendor/model",
            ["structured_outputs"],
            0.3,
            cancellation.Token);

        Assert.Equal(AiColorOutcomeKind.Cancelled, outcome.Kind);
        Assert.Empty(outcome.Colors);
    }

    [Fact]
    public async Task NewRequestSucceedsAfterCancelledRequest()
    {
        var runner = new SequenceRunner();
        var transport = CreateTransport(runner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = await transport.ValidateKeyAsync(
            "secret",
            cancellation.Token);
        var next = await transport.ValidateKeyAsync(
            "secret",
            CancellationToken.None);

        Assert.Equal(OpenRouterFailureKind.Cancelled, cancelled.FailureKind);
        Assert.True(next.IsSuccess);
    }

    [Fact]
    public void ProcessRunner_StartsConcurrentRedirectReadersBeforeWaiting()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "AI",
            "AiWorkerProcessRunner.cs"));
        var stdout = source.IndexOf("StandardOutput.ReadToEndAsync", StringComparison.Ordinal);
        var stderr = source.IndexOf("StandardError.ReadToEndAsync", StringComparison.Ordinal);
        var wait = source.IndexOf("Task.WhenAny", StringComparison.Ordinal);

        Assert.True(stdout >= 0 && stderr >= 0 && wait > stdout && wait > stderr);
        Assert.DoesNotContain("WaitForExit()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProcessesByName", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("request")]
    [InlineData("operation")]
    public async Task WorkerHandler_RejectsInvalidRequestEnvelope(string invalid)
    {
        var request = AiWorkerRequestEnvelope.Create(
            Guid.NewGuid().ToString("N"),
            AiWorkerOperation.ValidateKey);
        if (invalid == "version")
            request.ProtocolVersion++;
        if (invalid == "request")
            request.RequestId = "not-a-request-id";
        if (invalid == "operation")
            request.Operation = "Unknown";

        var response = await new AiWorkerRequestHandler(
                new SuccessfulOpenRouterTransport())
            .HandleAsync(request, "secret", CancellationToken.None);

        Assert.False(response.IsSuccess);
        Assert.Equal(
            OpenRouterFailureKind.ProtocolMismatch.ToString(),
            response.FailureKind);
    }

    [Theory]
    [InlineData((int)AiWorkerOperation.ValidateKey)]
    [InlineData((int)AiWorkerOperation.GetModels)]
    [InlineData((int)AiWorkerOperation.GetColors)]
    public async Task WorkerHandler_SupportsAllProtocolOperations(
        int operationValue)
    {
        var operation = (AiWorkerOperation)operationValue;
        var request = AiWorkerRequestEnvelope.Create(
            Guid.NewGuid().ToString("N"),
            operation,
            operation == AiWorkerOperation.GetColors
                ? new AiWorkerRequestPayload
                {
                    ObjectNames = ["Pipe"],
                    SchemeName = "Architectural",
                    ModelId = "vendor/model",
                    SupportedParameters = ["structured_outputs"]
                }
                : null);

        var response = await new AiWorkerRequestHandler(
                new SuccessfulOpenRouterTransport())
            .HandleAsync(request, "secret", CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(operation.ToString(), response.Operation);
        if (operation == AiWorkerOperation.GetModels)
            Assert.Single(response.Models);
        if (operation == AiWorkerOperation.GetColors)
            Assert.Equal("1,2,3", response.Colors["Pipe"]);
    }

    private static AiWorkerTransport CreateTransport(
        IAiWorkerProcessRunner runner)
    {
        return new AiWorkerTransport(runner, () => @"C:\worker.exe");
    }

    private sealed class EnvelopeRunner(
        Func<AiWorkerRequestEnvelope, AiWorkerResponseEnvelope> responseFactory)
        : IAiWorkerProcessRunner
    {
        public AiWorkerRequestEnvelope Request { get; private set; }
        public string ReceivedKey { get; private set; }

        public Task<AiWorkerRunResult> RunAsync(
            string workerPath,
            string requestJson,
            string key,
            CancellationToken cancellationToken)
        {
            Request = JsonConvert.DeserializeObject<AiWorkerRequestEnvelope>(
                requestJson);
            ReceivedKey = key;
            var response = responseFactory(Request);
            return Task.FromResult(AiWorkerRunResult.Success(
                JsonConvert.SerializeObject(response)));
        }
    }

    private sealed class FixedRunner(AiWorkerRunResult result)
        : IAiWorkerProcessRunner
    {
        public Task<AiWorkerRunResult> RunAsync(
            string workerPath,
            string requestJson,
            string key,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(
                System.Net.HttpStatusCode.InternalServerError));
        }
    }

    private sealed class RawResponseRunner(Func<string, string> responseFactory)
        : IAiWorkerProcessRunner
    {
        public Task<AiWorkerRunResult> RunAsync(
            string workerPath,
            string requestJson,
            string key,
            CancellationToken cancellationToken) => Task.FromResult(
                AiWorkerRunResult.Success(responseFactory(requestJson)));
    }

    private sealed class LateSuccessRunner(CancellationTokenSource cancellation)
        : IAiWorkerProcessRunner
    {
        public Task<AiWorkerRunResult> RunAsync(
            string workerPath,
            string requestJson,
            string key,
            CancellationToken cancellationToken)
        {
            var request = JsonConvert.DeserializeObject<AiWorkerRequestEnvelope>(
                requestJson);
            var response = AiWorkerResponseEnvelope.Success(request);
            response.Colors = new Dictionary<string, string>
            {
                ["Pipe"] = "1,2,3"
            };
            cancellation.Cancel();
            return Task.FromResult(AiWorkerRunResult.Success(
                JsonConvert.SerializeObject(response)));
        }
    }

    private sealed class SequenceRunner : IAiWorkerProcessRunner
    {
        public Task<AiWorkerRunResult> RunAsync(
            string workerPath,
            string requestJson,
            string key,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(AiWorkerRunResult.Failure(
                    AiWorkerRunFailureKind.Cancelled));
            var request = JsonConvert.DeserializeObject<AiWorkerRequestEnvelope>(
                requestJson);
            return Task.FromResult(AiWorkerRunResult.Success(
                JsonConvert.SerializeObject(
                    AiWorkerResponseEnvelope.Success(request))));
        }
    }

    private sealed class SuccessfulOpenRouterTransport : IOpenRouterTransport
    {
        public Task<OpenRouterValidationResult> ValidateKeyAsync(
            string key,
            CancellationToken cancellationToken) => Task.FromResult(
                OpenRouterValidationResult.Success());

        public Task<OpenRouterCatalogResult> GetModelsAsync(
            string key,
            CancellationToken cancellationToken) => Task.FromResult(
                OpenRouterCatalogResult.Available(
                    new Dictionary<string, OpenRouterModelInfo>
                    {
                        ["vendor/model"] = new OpenRouterModelInfo(
                            "vendor/model",
                            "Model",
                            ["structured_outputs"])
                    }));

        public Task<AiColorOutcome> GetColorsAsync(
            string key,
            IReadOnlyCollection<string> objectNames,
            string schemeName,
            OpenRouterModelInfo model,
            double temperature,
            CancellationToken cancellationToken) => Task.FromResult(
                AiColorOutcome.Success(
                    AiColorSource.OpenRouter,
                    objectNames.ToDictionary(
                        name => name,
                        _ => "1,2,3",
                        StringComparer.Ordinal)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate NavisHelper.sln.");
    }
}
