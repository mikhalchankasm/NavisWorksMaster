using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using NavisHelper.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class OpenRouterRuntimeRegressionTests
{
    [Fact]
    public async Task CatalogParsesSafeArchitectureBudgetAndReasoningMetadata()
    {
        using var http = new HttpClient(new JsonHandler("""
            {"data":[{"id":"vendor/model","name":"Model","context_length":64000,
            "supported_parameters":["structured_outputs","max_tokens","temperature"],
            "architecture":{"input_modalities":["text","image"],"output_modalities":["text"],"modality":"text+image->text"},
            "top_provider":{"max_completion_tokens":8192},
            "reasoning":{"mandatory":false,"default_enabled":true,"default_effort":"medium","supported_efforts":["high","low","none"],"supports_max_tokens":true}}]}
            """));
        using var client = new OpenRouterClient(http);

        var result = await client.GetModelsAsync("not-a-real-key", CancellationToken.None);
        var model = Assert.Single(result.Models).Value;

        Assert.Contains("text", model.InputModalities);
        Assert.Contains("image", model.InputModalities);
        Assert.Contains("text", model.OutputModalities);
        Assert.Equal("text+image->text", model.ArchitectureModality);
        Assert.Equal(64000, model.ContextLength);
        Assert.Equal(8192, model.MaxCompletionTokens);
        Assert.False(model.Reasoning.Mandatory);
        Assert.True(model.Reasoning.DefaultEnabled);
        Assert.Contains("none", model.Reasoning.SupportedEfforts);
        Assert.True(model.Reasoning.SupportsMaxTokens);
    }

    [Theory]
    [InlineData("image")]
    [InlineData("audio")]
    [InlineData("embeddings")]
    [InlineData("video")]
    public void NonTextOutputModelsAreExcluded(string output)
    {
        Assert.False(Model(["text"], [output]).IsColoringCompatible);
    }

    [Fact]
    public void TextToTextModelIsEligible()
    {
        Assert.True(Model(["text"], ["text"]).IsColoringCompatible);
    }

    [Fact]
    public void TextAndImageToTextIsEligibleAndMarkedMultimodal()
    {
        var choice = new OpenRouterModelChoice(Model(["text", "image"], ["text"]));
        Relocalize(choice, EnglishCapabilities());

        Assert.Contains("Multimodal", choice.CapabilityText);
        Assert.True(Model(["text", "image"], ["text"]).IsColoringCompatible);
    }

    [Fact]
    public void CapabilityFormatterUsesNeutralEnglishResources()
    {
        var choice = new OpenRouterModelChoice(Model(
            ["text", "image"],
            ["text"],
            4096,
            new OpenRouterReasoningInfo(false, false, ["none"], "none", false),
            "google/model"));

        Relocalize(choice, EnglishCapabilities());

        Assert.Equal("google/model", choice.Id);
        Assert.Contains("Text + Image → Text", choice.CapabilityText);
        Assert.Contains("Multimodal", choice.CapabilityText);
        Assert.Contains("Structured output", choice.CapabilityText);
        Assert.Contains("Reasoning optional", choice.CapabilityText);
        Assert.Contains("Context: 32000", choice.CapabilityText);
    }

    [Fact]
    public void CapabilityFormatterUsesRussianResources()
    {
        var choice = new OpenRouterModelChoice(Model(
            ["text", "image"],
            ["text"],
            4096,
            new OpenRouterReasoningInfo(true, true, ["minimal"], "minimal", false),
            "google/model"));

        Relocalize(choice, RussianCapabilities());

        Assert.Contains("Текст + Изображения → Текст", choice.CapabilityText);
        Assert.Contains("Мультимодальная", choice.CapabilityText);
        Assert.Contains("Структурированный ответ", choice.CapabilityText);
        Assert.Contains("Требуется рассуждение", choice.CapabilityText);
        Assert.Contains("Контекст: 32000", choice.CapabilityText);
    }

    [Fact]
    public void UnknownModalityRemainsUnchangedTechnicalIdentifier()
    {
        var model = Model(["text", "vendor_sensor"], ["text"]);
        var choice = new OpenRouterModelChoice(model);

        Relocalize(choice, RussianCapabilities());

        Assert.Contains("vendor_sensor", choice.CapabilityText);
    }

    [Fact]
    public void MissingTextInputBlocksModel()
    {
        Assert.False(Model(["image"], ["text"]).IsColoringCompatible);
    }

    [Fact]
    public void StructuredOutputsAreRequired()
    {
        var model = new OpenRouterModelInfo(
            "vendor/model", "Model", ["max_tokens"], ["text"], ["text"],
            "text->text", 32000, 4096);

        Assert.False(model.IsColoringCompatible);
    }

    [Fact]
    public void OptionalReasoningIsExplicitlyDisabled()
    {
        var model = Model(
            ["text"], ["text"], 4096,
            new OpenRouterReasoningInfo(false, true, ["low", "none"], "low", false));

        var payload = OpenRouterRequestFactory.CreateColorRequest(
            ["Pipe"], "Architectural", model, 0.3);

        Assert.False((bool)payload["reasoning"]["enabled"]);
        Assert.Null(payload["reasoning"]["effort"]);
        Assert.Null(payload["reasoning"]["max_tokens"]);
    }

    [Fact]
    public void MandatoryReasoningIsNeverDisabled()
    {
        var model = Model(
            ["text"], ["text"], 4096,
            new OpenRouterReasoningInfo(true, true, ["high", "minimal"], "high", false));

        var policy = OpenRouterColorRequestPolicy.Evaluate(model, ["Pipe"]);
        var payload = OpenRouterRequestFactory.CreateColorRequest(
            ["Pipe"], "Architectural", model, 0.3);

        Assert.Equal(OpenRouterColorRequestPolicyResult.Allowed, policy.Decision);
        Assert.Equal("minimal", (string)payload["reasoning"]["effort"]);
        Assert.NotEqual("none", (string)payload["reasoning"]["effort"]);
        Assert.Null(payload["reasoning"]["enabled"]);
    }

    [Fact]
    public async Task GeminiFlashCatalogShapeDisablesOptionalReasoningAndPassesPreflight()
    {
        using var http = new HttpClient(new JsonHandler("""
            {"data":[{"id":"google/gemini-2.5-flash","name":"Gemini 2.5 Flash",
            "supported_parameters":["reasoning","max_tokens","structured_outputs"],
            "architecture":{"input_modalities":["text"],"output_modalities":["text"],"modality":"text->text"},
            "top_provider":{"max_completion_tokens":65535},
            "reasoning":{"mandatory":false}}]}
            """));
        using var client = new OpenRouterClient(http);

        var catalog = await client.GetModelsAsync(
            "not-a-real-key",
            CancellationToken.None);
        var model = catalog.Models["google/gemini-2.5-flash"];
        var policy = OpenRouterColorRequestPolicy.Evaluate(model, ["Pipe"]);
        var payload = OpenRouterRequestFactory.CreateColorRequest(
            ["Pipe"],
            "Architectural",
            model,
            0.3);

        Assert.Equal(OpenRouterColorRequestPolicyResult.Allowed, policy.Decision);
        Assert.True(policy.MaySend);
        Assert.NotEqual(
            AiColorOutcomeKind.InsufficientOutputBudget,
            policy.FailureOutcomeKind);
        Assert.False((bool)payload["reasoning"]["enabled"]);
        Assert.Null(payload["reasoning"]["effort"]);
        Assert.Null(payload["reasoning"]["max_tokens"]);
    }

    [Fact]
    public void InsufficientProviderBudgetHasOnlyBudgetClassification()
    {
        var model = Model(["text"], ["text"], 512);
        var policy = OpenRouterColorRequestPolicy.Evaluate(
            model,
            [new string('X', 600)]);

        Assert.False(policy.MaySend);
        Assert.Equal(
            OpenRouterColorRequestPolicyResult.InsufficientOutputBudget,
            policy.Decision);
        Assert.Equal(
            AiColorOutcomeKind.InsufficientOutputBudget,
            policy.FailureOutcomeKind);
        Assert.Throws<InvalidOperationException>(() =>
            OpenRouterRequestFactory.CreateColorRequest(
                [new string('X', 600)], "Architectural", model, 0.3));
    }

    [Fact]
    public void UnsupportedReasoningHasDistinctClassificationAndAdvice()
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

        var policy = OpenRouterColorRequestPolicy.Evaluate(model, ["Pipe"]);

        Assert.Equal(
            OpenRouterColorRequestPolicyResult.UnsupportedReasoningPolicy,
            policy.Decision);
        Assert.Equal(
            AiColorOutcomeKind.UnsupportedReasoningPolicy,
            policy.FailureOutcomeKind);
        Assert.Equal(
            "Panel_Colors_Ai_UnsupportedReasoningPolicy",
            AiPanelOutcomeFormatter.FailureResource(policy.FailureOutcomeKind));
        Assert.Equal(
            "Panel_Colors_Ai_UnsupportedReasoningPolicySuggestion",
            AiPanelOutcomeFormatter.FailureSuggestionResource(
                policy.FailureOutcomeKind));
        Assert.DoesNotContain(
            "reduce",
            NavisHelper.Properties.Resources.ResourceManager.GetString(
                "Panel_Colors_Ai_UnsupportedReasoningPolicySuggestion",
                System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "Panel_Colors_Ai_InsufficientOutputBudgetSuggestion",
            AiPanelOutcomeFormatter.FailureSuggestionResource(
                AiColorOutcomeKind.InsufficientOutputBudget));
    }

    [Fact]
    public void MandatoryReasoningWithoutConfirmedMinimumIsRejectedPrecisely()
    {
        var model = Model(
            ["text"],
            ["text"],
            4096,
            new OpenRouterReasoningInfo(true, true, null, null, false, false));

        var policy = OpenRouterColorRequestPolicy.Evaluate(model, ["Pipe"]);

        Assert.Equal(
            OpenRouterColorRequestPolicyResult.UnsupportedReasoningPolicy,
            policy.Decision);
        Assert.Null(policy.ReasoningEnabled);
        Assert.NotEqual("none", policy.ReasoningEffort);
    }

    [Fact]
    public void MandatoryReasoningMaxTokenCapabilityWithoutMinimumIsRejected()
    {
        var model = Model(
            ["text"],
            ["text"],
            4096,
            new OpenRouterReasoningInfo(true, true, null, null, true, false));

        var policy = OpenRouterColorRequestPolicy.Evaluate(model, ["Pipe"]);

        Assert.Equal(
            OpenRouterColorRequestPolicyResult.UnsupportedReasoningPolicy,
            policy.Decision);
        Assert.Equal(
            AiColorOutcomeKind.UnsupportedReasoningPolicy,
            policy.FailureOutcomeKind);
        Assert.Throws<InvalidOperationException>(() =>
            OpenRouterRequestFactory.CreateColorRequest(
                ["Pipe"], "Architectural", model, 0.3));
    }

    [Fact]
    public void OutputBudgetIncludesPerItemJsonOverheadAndNameLength()
    {
        var shortBudget = OpenRouterColorRequestLimits.CalculateContentBudget(["A"]);
        var longBudget = OpenRouterColorRequestLimits.CalculateContentBudget(
            [new string('X', 1000), "B"]);

        Assert.True(shortBudget >= OpenRouterColorRequestLimits.MinimumOutputBudget);
        Assert.True(longBudget > shortBudget);
    }

    [Fact]
    public void FilterFindsProviderPrefixAndIsCaseInsensitive()
    {
        var picker = Picker("google/gemini", "anthropic/claude");

        Assert.Equal("google/gemini", Assert.Single(picker.Filter("GOOGLE")).Id);
    }

    [Fact]
    public void FilteringDoesNotChangeSelectedFullId()
    {
        var picker = Picker("google/gemini", "anthropic/claude");
        picker.Select("anthropic/claude");

        Assert.Single(picker.Filter("google"));
        Assert.Equal("anthropic/claude", picker.SelectedModelId);
    }

    [Fact]
    public void RefreshPreservesSelectionWhenModelRemainsAvailable()
    {
        var picker = Picker("google/gemini", "anthropic/claude");
        picker.Select("google/gemini");
        picker.Replace(
            [new OpenRouterModelChoice(Model("google/gemini")),
             new OpenRouterModelChoice(Model("vendor/new"))],
            picker.SelectedModelId);

        Assert.Equal("google/gemini", picker.SelectedModelId);
    }

    [Fact]
    public void LanguageRefreshRerendersLoadedChoicesAndPreservesPickerState()
    {
        var picker = Picker("google/gemini", "anthropic/claude");
        picker.Select("google/gemini");
        var filteredBefore = picker.Filter("google");
        var englishResources = EnglishCapabilities();
        picker.Relocalize(englishResources.Get, englishResources.Format);
        var english = Assert.Single(picker.Filter(picker.CurrentQuery));
        var englishCapabilityText = english.CapabilityText;

        var russianResources = RussianCapabilities();
        picker.Relocalize(russianResources.Get, russianResources.Format);
        var russian = Assert.Single(picker.Filter(picker.CurrentQuery));

        Assert.Single(filteredBefore);
        Assert.Equal("google", picker.CurrentQuery);
        Assert.Equal("google/gemini", picker.SelectedModelId);
        Assert.Equal("google/gemini", english.Id);
        Assert.Equal("google/gemini", russian.Id);
        Assert.Contains("Text", englishCapabilityText);
        Assert.Contains("Текст", russian.CapabilityText);
    }

    [Fact]
    public void LanguageRefreshHasNoNetworkOrConfigPersistenceDependency()
    {
        var picker = Picker("google/gemini");
        picker.Select("google/gemini");
        picker.Filter("google");
        var resolverCalls = 0;
        var resources = RussianCapabilities();

        picker.Relocalize(
            key =>
            {
                resolverCalls++;
                return resources.Get(key);
            },
            resources.Format);

        Assert.True(resolverCalls > 0);
        Assert.Equal("google/gemini", picker.SelectedModelId);
        Assert.Equal("google", picker.CurrentQuery);
        Assert.DoesNotContain(typeof(OpenRouterModelPicker).GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public), field =>
                typeof(HttpClient).IsAssignableFrom(field.FieldType) ||
                field.FieldType.Name.Contains("Config", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProductionCapabilityFormatterContainsNoHardcodedDisplayLabels()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "NavisHelper", "AI", "OpenRouterModelSelection.cs"));
        var formatter = source.Split("internal static class OpenRouterModelCapabilities")[1]
            .Split("internal sealed class OpenRouterModelPicker")[0];

        Assert.Contains("Settings_Ai_Model_Capability_", formatter);
        Assert.DoesNotContain("parts.Add(\"multimodal\")", formatter);
        Assert.DoesNotContain("parts.Add(\"reasoning:required\")", formatter);
        Assert.DoesNotContain("parts.Add(\"reasoning:optional\")", formatter);
        Assert.DoesNotContain("parts.Add(\"context:", formatter);
        Assert.DoesNotContain("\"structured_outputs\"", formatter);
    }

    [Fact]
    public void VisibleSearchLabelUsesLocalizedResourceBinding()
    {
        var root = FindRepositoryRoot();
        var selector = File.ReadAllText(Path.Combine(
            root, "NavisHelper", "WPF", "OpenRouterModelSelector.cs"));
        var settings = File.ReadAllText(Path.Combine(
            root, "NavisHelper", "WPF", "NavisHelperSettingsTabBuilder.cs"));

        Assert.Contains("var searchLabel = new TextBlock", selector);
        Assert.Contains("Settings_Ai_Model_Search_Label", selector);
        Assert.Contains("RefreshModelChoiceLocalization", settings);
    }

    [Fact]
    public void ModelCapabilityResourcesHaveNeutralRussianParity()
    {
        var english = LoadResourceFile("Resources.resx");
        var russian = LoadResourceFile("Resources.ru.resx");
        var keys = english.Keys
            .Where(key => key.StartsWith(
                "Settings_Ai_Model_Capability_",
                StringComparison.Ordinal))
            .Append("Settings_Ai_Model_Search_Label")
            .ToArray();

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.True(russian.ContainsKey(key), key));
        Assert.Equal("Search models", english["Settings_Ai_Model_Search_Label"]);
        Assert.Equal("Поиск моделей", russian["Settings_Ai_Model_Search_Label"]);
    }

    [Theory]
    [InlineData((int)AiColorOutcomeKind.Timeout, "timeout")]
    [InlineData((int)AiColorOutcomeKind.TruncatedResponse, "truncated")]
    public void TerminalFailureReplacesStartingAndShowsModelAndZeroApplied(
        int kindValue,
        string localizedReason)
    {
        var kind = (AiColorOutcomeKind)kindValue;
        var starting = AiPanelOutcomeFormatter.Format(
            AiPanelOutcome.Starting("vendor/model", 1, ["Pipe"]),
            Key, Format, _ => "Scheme");
        var failure = AiPanelOutcomeFormatter.Format(
            AiPanelOutcome.Failure(kind, "vendor/model", ["Pipe"]),
            key => key == AiPanelOutcomeFormatter.FailureResource(kind)
                ? localizedReason
                : Key(key),
            Format,
            null);

        Assert.NotEqual(starting, failure);
        Assert.Contains("vendor/model", failure);
        Assert.Contains(localizedReason, failure);
        Assert.Contains("Colors applied: 0", failure);
    }

    [Fact]
    public void SuccessShowsOnlyVerifiedMapping()
    {
        var result = AiPanelOutcomeFormatter.Format(
            AiPanelOutcome.Success(
                AiColorSource.OpenRouter, "vendor/model", 1, ["Pipe"],
                new Dictionary<string, string> { ["Pipe"] = "1,2,3" }, 1),
            Key, Format, _ => "Scheme");

        Assert.Contains("vendor/model", result);
        Assert.Contains("Pipe", result);
        Assert.Contains("1,2,3", result);
    }

    [Fact]
    public void PartialStructuredResponseIsRejected()
    {
        var response = """
            {"choices":[{"finish_reason":"stop","message":{"content":"{\"colors\":[{\"object\":\"A\",\"color\":\"1,2,3\"}]}"}}]}
            """;

        var parsed = OpenRouterColorResponseParser.Parse(response, ["A", "B"]);

        Assert.False(parsed.IsSuccess);
        Assert.Equal(AiColorOutcomeKind.IncompleteObjectSet, parsed.FailureKind);
    }

    [Fact]
    public void ProtocolRoundTripPreservesSafeModelMetadata()
    {
        var envelope = AiWorkerResponseEnvelope.Success(
            AiWorkerRequestEnvelope.Create("0123456789abcdef0123456789abcdef", AiWorkerOperation.GetModels));
        envelope.Models = [new AiWorkerModelDto
        {
            Id = "vendor/model", Name = "Model", InputModalities = ["text", "image"],
            OutputModalities = ["text"], ContextLength = 64000,
            MaxCompletionTokens = 8192, ReasoningMandatory = true,
            ReasoningSupportedEfforts = ["minimal"]
        }];

        var copy = JsonConvert.DeserializeObject<AiWorkerResponseEnvelope>(
            JsonConvert.SerializeObject(envelope));

        Assert.Equal(AiWorkerProtocol.CurrentVersion, copy.ProtocolVersion);
        Assert.Equal(["text", "image"], copy.Models[0].InputModalities);
        Assert.True(copy.Models[0].ReasoningMandatory);
        Assert.Equal(8192, copy.Models[0].MaxCompletionTokens);
    }

    [Fact]
    public void ProductionSourcesContainNoPinnedModelIdsOrAutomaticRetry()
    {
        var root = FindRepositoryRoot();
        var production = string.Join("\n", Directory.GetFiles(
                Path.Combine(root, "NavisHelper"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(
                Path.Combine(root, "NavisHelper.AiWorker"), "*.cs", SearchOption.AllDirectories))
            .Select(File.ReadAllText));

        Assert.DoesNotContain("deepseek/deepseek-v4-pro", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("google/gemini-", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anthropic/claude-", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Polly", production, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PickerHasNoNetworkOrPersistenceDependency()
    {
        var fields = typeof(OpenRouterModelPicker)
            .GetFields(System.Reflection.BindingFlags.Instance |
                       System.Reflection.BindingFlags.NonPublic |
                       System.Reflection.BindingFlags.Public);

        Assert.DoesNotContain(fields, field =>
            typeof(HttpClient).IsAssignableFrom(field.FieldType) ||
            typeof(Task).IsAssignableFrom(field.FieldType));
    }

    private static OpenRouterModelInfo Model(
        string id = "vendor/model") =>
        Model(["text"], ["text"], 4096, null, id);

    private static OpenRouterModelInfo Model(
        string[] inputs,
        string[] outputs,
        int max = 4096,
        OpenRouterReasoningInfo reasoning = null,
        string id = "vendor/model") =>
        new(id, "Model", reasoning == null
                ? ["structured_outputs", "max_tokens", "temperature"]
                : ["structured_outputs", "max_tokens", "temperature", "reasoning"],
            inputs, outputs, string.Join("+", inputs) + "->" + string.Join("+", outputs),
            32000, max, reasoning);

    private static OpenRouterModelPicker Picker(params string[] ids)
    {
        var picker = new OpenRouterModelPicker();
        picker.Replace(ids.Select(id => new OpenRouterModelChoice(Model(id))), string.Empty);
        return picker;
    }

    private static void Relocalize(
        OpenRouterModelChoice choice,
        CapabilityResources resources) =>
        choice.Relocalize(resources.Get, resources.Format);

    private static CapabilityResources EnglishCapabilities() =>
        new(new Dictionary<string, string>
        {
            ["Settings_Ai_Model_Capability_Text"] = "Text",
            ["Settings_Ai_Model_Capability_Image"] = "Image",
            ["Settings_Ai_Model_Capability_Audio"] = "Audio",
            ["Settings_Ai_Model_Capability_Files"] = "Files",
            ["Settings_Ai_Model_Capability_Video"] = "Video",
            ["Settings_Ai_Model_Capability_Multimodal"] = "Multimodal",
            ["Settings_Ai_Model_Capability_StructuredOutput"] = "Structured output",
            ["Settings_Ai_Model_Capability_ReasoningOptional"] = "Reasoning optional",
            ["Settings_Ai_Model_Capability_ReasoningRequired"] = "Reasoning required",
            ["Settings_Ai_Model_Capability_Context_Format"] = "Context: {0}",
            ["Settings_Ai_Model_Capability_Flow_Format"] = "{0} → {1}"
        });

    private static CapabilityResources RussianCapabilities() =>
        new(new Dictionary<string, string>
        {
            ["Settings_Ai_Model_Capability_Text"] = "Текст",
            ["Settings_Ai_Model_Capability_Image"] = "Изображения",
            ["Settings_Ai_Model_Capability_Audio"] = "Аудио",
            ["Settings_Ai_Model_Capability_Files"] = "Файлы",
            ["Settings_Ai_Model_Capability_Video"] = "Видео",
            ["Settings_Ai_Model_Capability_Multimodal"] = "Мультимодальная",
            ["Settings_Ai_Model_Capability_StructuredOutput"] = "Структурированный ответ",
            ["Settings_Ai_Model_Capability_ReasoningOptional"] = "Рассуждение необязательно",
            ["Settings_Ai_Model_Capability_ReasoningRequired"] = "Требуется рассуждение",
            ["Settings_Ai_Model_Capability_Context_Format"] = "Контекст: {0}",
            ["Settings_Ai_Model_Capability_Flow_Format"] = "{0} → {1}"
        });

    private static Dictionary<string, string> LoadResourceFile(string name)
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "NavisHelper",
            "Properties",
            name);
        return XDocument.Load(path)
            .Root.Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name"),
                element => (string)element.Element("value"),
                StringComparer.Ordinal);
    }

    private sealed class CapabilityResources(
        IReadOnlyDictionary<string, string> values)
    {
        internal string Get(string key) => values[key];
        internal string Format(string key, object[] args) =>
            string.Format(values[key], args);
    }

    private static string Key(string key) => key switch
    {
        "Panel_Colors_Ai_Model_Format" => "Model: {0}",
        "Panel_Colors_Ai_ZeroApplied" => "Colors applied: 0",
        "Panel_Colors_Ai_FailureSuggestion" => "Choose another model",
        "Panel_Colors_Ai_Source_Format" => "Source: {0}",
        "Panel_Colors_Ai_ResultSummary_Format" => "Applied: {0}; groups: {1}",
        "Panel_Colors_Ai_GroupDetail_Format" => "Group {0}: {1}",
        "Panel_Colors_Ai_Source_OpenRouter" => "OpenRouter",
        _ => key
    };

    private static string Format(string key, object[] args) =>
        string.Format(Key(key), args);

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
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
        throw new DirectoryNotFoundException();
    }
}
