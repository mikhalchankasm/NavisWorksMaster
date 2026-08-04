using System.Xml.Linq;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class AIColorArchitectureTests
{
    [Fact]
    public void ActiveAiPath_HasNoSyncOverAsync()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
                Path.Combine(root, "NavisHelper", "AI"),
                "*.cs")
            .Concat(Directory.GetFiles(
                Path.Combine(root, "NavisHelper.AiWorker"),
                "*.cs"))
            .Concat(new[]
            {
                Path.Combine(root, "NavisHelper", "AIColorObjects.cs"),
                Path.Combine(
                    root,
                    "NavisHelper",
                    "WPF",
                    "NavisHelperSettingsTabBuilder.cs")
            });

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain(".GetAwaiter().GetResult()", source);
            Assert.DoesNotContain(".Wait(", source);
            Assert.DoesNotContain(".Result", source);
        }
    }

    [Fact]
    public void CompiledAiPath_UsesCoordinatorAndExcludesLegacyColorService()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(
            root,
            "NavisHelper",
            "NavisHelper.csproj");
        var project = XDocument.Load(projectPath);
        var includes = project
            .Descendants()
            .Where(element => element.Name.LocalName == "Compile")
            .Select(element => (string)element.Attribute("Include"))
            .Where(value => value != null)
            .ToArray();

        Assert.Contains(
            @"AI\AIColorOperationCoordinator.cs",
            includes);
        Assert.Contains(@"AI\AIColorWorkflow.cs", includes);
        Assert.Contains(@"AI\AiWorkerTransport.cs", includes);
        Assert.Contains(@"AI\AiWorkerProcessRunner.cs", includes);
        Assert.DoesNotContain(@"AI\OpenRouterClient.cs", includes);
        Assert.DoesNotContain(@"AI\OpenRouterRequestFactory.cs", includes);
        Assert.DoesNotContain(@"AI\OpenRouterColorResponseParser.cs", includes);
        Assert.DoesNotContain("AIColorService.cs", includes);
        Assert.DoesNotContain("LocalColorBridge.cs", includes);

        var entryPoint = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "AIColorObjects.cs"));
        Assert.Contains("AIColorOperationCoordinator.Current", entryPoint);
        Assert.DoesNotContain("AIColorWorkflow().Execute", entryPoint);
    }

    [Fact]
    public void ActivePluginPath_HasNoDirectOpenRouterHttpTransport()
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(
            root,
            "NavisHelper",
            "NavisHelper.csproj"));
        var compiledSources = project.Descendants()
            .Where(element => element.Name.LocalName == "Compile")
            .Select(element => (string)element.Attribute("Include"))
            .Where(value => value != null)
            .Select(value => Path.Combine(root, "NavisHelper", value))
            .Where(File.Exists)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(compiledSources, source =>
            source.Contains("new HttpClient", StringComparison.Ordinal) ||
            source.Contains("openrouter.ai/api/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Worker_IsInSolutionAndPackageInventory()
    {
        var root = FindRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "NavisHelper.sln"));
        var package = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "package_distribution.ps1"));
        var validation = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "validate_distribution.ps1"));
        var installer = File.ReadAllText(Path.Combine(
            root,
            "installer",
            "NavisHelper.iss"));

        Assert.Contains("NavisHelper.AiWorker\\NavisHelper.AiWorker.csproj", solution);
        Assert.Contains("Contents\\AiWorker", package);
        Assert.Contains("NavisHelper.AiWorker.exe", validation);
        Assert.Contains("NavisHelper.bundle\\*", installer);
    }

    [Fact]
    public void Ci_PublishesWorkerBeforeSkipBuildDistributionSmoke()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "ci.yml"));
        var publish = workflow.IndexOf(
            "dotnet publish NavisHelper.AiWorker/NavisHelper.AiWorker.csproj",
            StringComparison.Ordinal);
        var package = workflow.IndexOf(
            @"tools\package_distribution.ps1 -SkipBuild",
            StringComparison.Ordinal);

        Assert.True(publish >= 0, "CI must publish NavisHelper.AiWorker.");
        Assert.True(
            package > publish,
            "CI must publish the worker before -SkipBuild packaging.");

        var publishBlock = workflow.Substring(publish, package - publish);
        Assert.Contains("-r win-x64", publishBlock);
        Assert.Contains("--self-contained false", publishBlock);
        Assert.Contains(
            "-o NavisHelper.bundle/Contents/AiWorker",
            publishBlock);
    }

    [Fact]
    public void AiConfigAndActivePath_HaveSingleKeySource()
    {
        var root = FindRepositoryRoot();
        var config = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "AIConfig.cs"));
        var workflow = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "AI",
            "AIColorWorkflow.cs"));

        Assert.DoesNotContain("ApiKey", config);
        Assert.DoesNotContain("UpdateApiKey", config);
        Assert.DoesNotContain("IsValid", config);
        Assert.Contains("_keyStore.GetKey()", workflow);
        Assert.DoesNotContain(
            "Environment.GetEnvironmentVariable",
            workflow);
    }

    [Fact]
    public void ProductionAiPath_HasNoStaticModelRecommendations()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
                Path.Combine(root, "NavisHelper", "AI"),
                "*.cs")
            .Concat(new[]
            {
                Path.Combine(root, "NavisHelper", "AIConfig.cs"),
                Path.Combine(root, "NavisHelper", "WPF", "NavisHelperSettingsTabBuilder.cs"),
                Path.Combine(root, "NavisHelper", "WPF", "OpenRouterModelSelector.cs")
            });
        var source = string.Join("\n", files.Select(File.ReadAllText));

        Assert.DoesNotContain("AIModels.Available", source);
        Assert.DoesNotContain("class AIModels", source);
        Assert.DoesNotContain("anthropic/", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("openai/", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("google/", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("z-ai/", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsEditable = true", source);
        Assert.Contains("IsReadOnly = true", source);
        Assert.Contains("OpenRouterModelSelection", source);
        Assert.Contains(".CompatibleChoices(catalog)", source);
    }

    [Fact]
    public void DynamicModelRefresh_IsSafeDuringInitialLocalizationBinding()
    {
        var root = FindRepositoryRoot();
        var settings = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperSettingsTabBuilder.cs"));

        Assert.Contains("if (_aiRefreshModelsButton != null)", settings);
        Assert.Contains(
            "Settings.AiConnectionState",
            settings);
    }

    [Fact]
    public void SettingsBuilder_IsOwnedByPanelLifecycle()
    {
        var root = FindRepositoryRoot();
        var panel = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperPanel.cs"));

        Assert.Contains(
            "private NavisHelperSettingsTabBuilder _settingsTabBuilder;",
            panel);
        Assert.Contains(
            "_settingsTabBuilder?.ResumeAfterLoad();",
            panel);
        Assert.Contains(
            "_settingsTabBuilder?.CancelPendingOperations();",
            panel);
        Assert.Contains("_settingsTabBuilder?.Dispose();", panel);
    }

    [Fact]
    public void SettingsAsyncContinuations_UseCapturedGenerations()
    {
        var root = FindRepositoryRoot();
        var settings = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperSettingsTabBuilder.cs"));

        Assert.Contains(
            "_infrastructure.CaptureKeyStateAsync(",
            settings);
        Assert.Contains(
            "_aiOperationLifetime.Begin(-1)",
            settings);
        Assert.Contains("mutation.Generation", settings);
        Assert.Contains(
            "_infrastructure.IsKeyGenerationCurrentAsync(",
            settings);
        Assert.Contains("_infrastructure.ReplaceCatalog(", settings);
        Assert.Contains("_aiOperationLifetime.TryExecuteCurrent(", settings);
        Assert.Contains("keyGeneration,", settings);
        Assert.DoesNotContain(
            "OpenRouterCatalogCache.Current.Store(",
            settings);
    }

    [Fact]
    public void RefreshModels_ContainsItsAsyncExceptionPath()
    {
        var root = FindRepositoryRoot();
        var settings = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperSettingsTabBuilder.cs"));
        var start = settings.IndexOf(
            "private async void RefreshModels()",
            StringComparison.Ordinal);
        var end = settings.IndexOf(
            "private async void DisconnectOpenRouter()",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var refreshPath = settings.Substring(start, end - start);

        Assert.Contains("AISettingsAsyncBoundary.RunAsync(", refreshPath);
        Assert.Contains("RefreshModelsSafelyAsync", refreshPath);
        Assert.Contains("HandleUnexpectedCatalogErrorAsync", refreshPath);
    }

    [Fact]
    public void SettingsValidationAndCatalog_UseIndependentTimeoutScopes()
    {
        var root = FindRepositoryRoot();
        var settings = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperSettingsTabBuilder.cs"));
        var connectStart = settings.IndexOf(
            "private async Task ConnectWithKeyAsync(",
            StringComparison.Ordinal);
        var catalogStart = settings.IndexOf(
            "private async Task RefreshModelCatalogAsync(",
            connectStart,
            StringComparison.Ordinal);
        var refreshStart = settings.IndexOf(
            "private async void RefreshModels()",
            catalogStart,
            StringComparison.Ordinal);
        Assert.True(
            connectStart >= 0 &&
            catalogStart > connectStart &&
            refreshStart > catalogStart);
        var connect = settings.Substring(
            connectStart,
            catalogStart - connectStart);
        var catalog = settings.Substring(
            catalogStart,
            refreshStart - catalogStart);

        Assert.Contains(
            "AISettingsOperationPolicy.KeyValidationTimeout",
            connect);
        Assert.DoesNotContain(
            "AISettingsOperationPolicy.ModelCatalogTimeout",
            connect);
        Assert.Contains(
            "AISettingsOperationPolicy.ModelCatalogTimeout",
            catalog);
        Assert.Contains(
            "operation.CancellationToken",
            catalog);
        Assert.DoesNotContain("MapConnectionFailure", catalog);
        Assert.Contains(
            "AISettingsOperationPolicy.CatalogCompletionState",
            catalog);
        Assert.Contains(
            "case OpenRouterFailureKind.Timeout:",
            settings);
    }

    [Fact]
    public void SettingsDiagnostics_HaveNoSecretOrPayloadInputs()
    {
        var root = FindRepositoryRoot();
        var policy = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "AI",
            "AISettingsOperationPolicy.cs"));

        Assert.Contains("stage=", policy);
        Assert.Contains("elapsed_ms=", policy);
        Assert.Contains("http_status=", policy);
        Assert.DoesNotContain("string key", policy);
        Assert.DoesNotContain("requestJson", policy);
        Assert.DoesNotContain("responseJson", policy);
        Assert.DoesNotContain("payload", policy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingsProductionPath_UsesExplicitBackgroundAndUiBoundaries()
    {
        var root = FindRepositoryRoot();
        var settings = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperSettingsTabBuilder.cs"));
        var executor = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "AI",
            "AISettingsInfrastructureExecutor.cs"));

        Assert.Contains("AISettingsInfrastructureExecutor", settings);
        Assert.Contains("ApplyOnUiThreadAsync", settings);
        Assert.Contains("_dispatcher.BeginInvoke", settings);
        Assert.Contains("Task.Run", executor);
        Assert.Contains("CaptureKeyStateAsync", executor);
        Assert.Contains("PersistKeyAsync", executor);
        Assert.Contains("PrepareModelBindingAsync", executor);
        Assert.DoesNotContain("_keyStore.", settings);
        Assert.DoesNotContain("AIConfig.Instance", settings);
        Assert.Contains(
            "!_aiOperationLifetime.IsCurrent(operation)",
            settings);
        Assert.Contains(
            "_aiOperationLifetime.TryExecuteCurrent(",
            settings);
    }

    [Fact]
    public void SettingsProductionPath_HasNoBlockingUiWaits()
    {
        var root = FindRepositoryRoot();
        var source = string.Join("\n", new[]
        {
            File.ReadAllText(Path.Combine(
                root,
                "NavisHelper",
                "WPF",
                "NavisHelperSettingsTabBuilder.cs")),
            File.ReadAllText(Path.Combine(
                root,
                "NavisHelper",
                "AI",
                "AISettingsInfrastructureExecutor.cs")),
            File.ReadAllText(Path.Combine(
                root,
                "NavisHelper",
                "AI",
                "AiWorkerProcessRunner.cs"))
        });

        Assert.DoesNotContain(".Result", source);
        Assert.DoesNotContain(".Wait()", source);
        Assert.DoesNotContain("Dispatcher.Invoke", source);
        Assert.DoesNotContain(".GetAwaiter().GetResult()", source);
    }

    [Fact]
    public void WorkerCancellationCallback_DoesNotTerminateProcessInline()
    {
        var root = FindRepositoryRoot();
        var runner = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "AI",
            "AiWorkerProcessRunner.cs"));
        var registration = runner.IndexOf(
            "cancellationToken.Register",
            StringComparison.Ordinal);
        var callbackEnd = runner.IndexOf(
            "\n                {\n                    try",
            registration,
            StringComparison.Ordinal);
        var completed = runner.IndexOf(
            "var completed = await Task.WhenAny",
            callbackEnd,
            StringComparison.Ordinal);
        Assert.True(
            registration >= 0 &&
            callbackEnd > registration &&
            completed > callbackEnd);
        var callbackPath = runner.Substring(
            registration,
            callbackEnd - registration);

        Assert.Contains("cancelled.TrySetResult(true)", callbackPath);
        Assert.DoesNotContain("TryKill(process)", callbackPath);
        Assert.Contains("TryKill(process)", runner.Substring(completed));
    }

    [Fact]
    public void ConnectHandler_AwaitsBackgroundCaptureBeforeValidation()
    {
        var root = FindRepositoryRoot();
        var settings = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperSettingsTabBuilder.cs"));
        var connect = settings.IndexOf(
            "private async void ConnectOpenRouter()",
            StringComparison.Ordinal);
        var verify = settings.IndexOf(
            "private async Task VerifyExistingKeyAsync()",
            connect,
            StringComparison.Ordinal);
        Assert.True(connect >= 0 && verify > connect);
        var handler = settings.Substring(connect, verify - connect);

        Assert.Contains("await ConnectWithKeyAsync(", handler);
        Assert.Contains("_aiOperationLifetime.Begin(-1)", handler);
        Assert.DoesNotContain("_keyStore", handler);
    }

    [Fact]
    public void AllSettingsAsyncVoidBoundariesObserveFailures()
    {
        var root = FindRepositoryRoot();
        var settings = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperSettingsTabBuilder.cs"));

        Assert.Equal(
            5,
            settings.Split("AISettingsAsyncBoundary.RunAsync(").Length - 1);
        Assert.Contains("VerifyExistingKey();", settings);
        Assert.DoesNotContain("_ = VerifyExistingKeyAsync()", settings);
        Assert.Contains("private async Task ConnectOpenRouterAsync()", settings);
        Assert.Contains("private async Task DisconnectOpenRouterAsync()", settings);
        Assert.Contains("private async Task CommitSelectedModelAsync()", settings);
    }

    [Fact]
    public void ModelSelectionUpdatesRuntimeBeforeBackgroundPersistence()
    {
        var root = FindRepositoryRoot();
        var settings = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperSettingsTabBuilder.cs"));
        var start = settings.IndexOf(
            "private async Task CommitSelectedModelAsync()",
            StringComparison.Ordinal);
        var end = settings.IndexOf(
            "private void ClearModelChoices()",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var commit = settings.Substring(start, end - start);
        var runtimeUpdate = commit.IndexOf(
            "_infrastructure.UpdateSelectedModelRuntime(selectedModelId)",
            StringComparison.Ordinal);
        var persistence = commit.IndexOf(
            "await _infrastructure.SaveSelectedModelAsync()",
            StringComparison.Ordinal);

        Assert.True(runtimeUpdate >= 0 && persistence > runtimeUpdate);
    }

    [Fact]
    public void AiWorkflowCapturesOneImmutableConfigSnapshot()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "AI",
            "AIColorWorkflow.cs"));

        Assert.Equal(
            1,
            workflow.Split("AIConfig.Instance.CaptureSnapshot()").Length - 1);
        Assert.DoesNotContain("AIConfig.Instance.ModelName", workflow);
        Assert.DoesNotContain("AIConfig.Instance.Temperature", workflow);
        Assert.DoesNotContain("AIConfig.Instance.ColorScheme", workflow);
        Assert.DoesNotContain("config.GetColorSchemeType()", workflow);
    }

    [Fact]
    public void RefreshCancellationMutation_UsesUiGate()
    {
        var root = FindRepositoryRoot();
        var settings = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperSettingsTabBuilder.cs"));
        var refresh = settings.IndexOf(
            "private async Task RefreshModelsSafelyAsync()",
            StringComparison.Ordinal);
        var disconnect = settings.IndexOf(
            "private async void DisconnectOpenRouter()",
            refresh,
            StringComparison.Ordinal);
        Assert.True(refresh >= 0 && disconnect > refresh);
        var path = settings.Substring(refresh, disconnect - refresh);
        var cancellation = path.IndexOf(
            "catch (OperationCanceledException)",
            StringComparison.Ordinal);

        Assert.True(cancellation >= 0);
        Assert.Contains(
            "await ApplyOnUiThreadAsync(operation, () =>",
            path.Substring(cancellation));
    }

    [Fact]
    public void SettingsTimeoutClassificationDoesNotUseCallbackBoolean()
    {
        var root = FindRepositoryRoot();
        var settings = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperSettingsTabBuilder.cs"));

        Assert.DoesNotContain("Token.Register(", settings);
        Assert.Contains("validationTimeout.Token", settings);
        Assert.Contains("catalogTimeout.Token", settings);
        Assert.Contains(
            "AISettingsOperationPolicy.EffectiveFailure(",
            settings);
    }

    [Fact]
    public void WorkerProtocolAndDistributionManifest_AreVersionThree()
    {
        var root = FindRepositoryRoot();
        var protocol = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "AI",
            "AiWorkerProtocol.cs"));
        var package = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "package_distribution.ps1"));
        var validation = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "validate_distribution.ps1"));

        Assert.Contains("CurrentVersion = 3", protocol);
        Assert.Contains("protocol_version = 3", package);
        Assert.Contains("protocol_version -ne 3", validation);
    }

    [Fact]
    public void McpContracts_RemainIndependentOfOpenRouterColoring()
    {
        var root = FindRepositoryRoot();
        var contracts = string.Join("\n", Directory.GetFiles(
                Path.Combine(root, "NavisHelper.Contracts"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                Path.DirectorySeparatorChar + "obj" +
                Path.DirectorySeparatorChar))
            .Select(File.ReadAllText));

        Assert.DoesNotContain("OpenRouter", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("AiWorker", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("structured_outputs", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void Disconnect_CancelsTheActiveOpenRouterColorOperation()
    {
        var root = FindRepositoryRoot();
        var settings = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperSettingsTabBuilder.cs"));
        var disconnectStart = settings.IndexOf(
            "private async void DisconnectOpenRouter()",
            StringComparison.Ordinal);
        Assert.True(disconnectStart >= 0);
        var nextMethod = settings.IndexOf(
            "private async void CommitSelectedModel()",
            disconnectStart,
            StringComparison.Ordinal);

        Assert.True(nextMethod > disconnectStart);
        var disconnectBody = settings.Substring(
            disconnectStart,
            nextMethod - disconnectStart);
        Assert.Contains(
            "AIColorOperationCoordinator.Current.CancelCurrent();",
            disconnectBody);
    }

    [Fact]
    public void AiPanelResources_HaveNeutralRussianParity()
    {
        var root = FindRepositoryRoot();
        var neutral = ResourceKeys(Path.Combine(
            root,
            "NavisHelper",
            "Properties",
            "Resources.resx"));
        var russian = ResourceKeys(Path.Combine(
            root,
            "NavisHelper",
            "Properties",
            "Resources.ru.resx"));

        Assert.Equal(
            neutral.Where(key =>
                    key.StartsWith("Panel_Colors_Ai_") ||
                    key.StartsWith("Settings_Ai_"))
                .Order(),
            russian.Where(key =>
                    key.StartsWith("Panel_Colors_Ai_") ||
                    key.StartsWith("Settings_Ai_"))
                .Order());
    }

    private static HashSet<string> ResourceKeys(string path)
    {
        return XDocument.Load(path)
            .Descendants("data")
            .Select(element => (string)element.Attribute("name"))
            .Where(value => value != null)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "NavisHelper.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate NavisHelper.sln.");
    }
}
