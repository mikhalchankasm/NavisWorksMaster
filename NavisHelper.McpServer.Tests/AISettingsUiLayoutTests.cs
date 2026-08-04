using System.Xml.Linq;
using NavisHelper.AI;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class AISettingsUiLayoutTests
{
    [Fact]
    public void SettingsCards_AreBuiltInRequestedOrder()
    {
        var source = SettingsBuilderSource();
        var build = MethodBody(
            source,
            "public TabItem Build()",
            "private UIElement BuildLanguageSettingsSection()");

        var ai = build.IndexOf(
            "BuildLocalizedSection(\"AI\"",
            StringComparison.Ordinal);
        var service = build.IndexOf(
            "\"Settings_Service_Section\"",
            StringComparison.Ordinal);
        var language = build.IndexOf(
            "\"SettingsLanguageSectionTitle\"",
            StringComparison.Ordinal);
        var about = build.IndexOf(
            "\"Settings_About_Section\"",
            StringComparison.Ordinal);

        Assert.True(
            ai >= 0 && service > ai && language > service && about > language);
    }

    [Fact]
    public void SettingsTab_HasOneVersionAndNoFooterDuplicate()
    {
        var source = SettingsBuilderSource();

        Assert.Equal(
            1,
            source.Split("NavisHelper.AppVersion.VersionString").Length - 1);
    }

    [Theory]
    [InlineData(false, false, true, false, false)]
    [InlineData(true, false, false, true, true)]
    public void ConnectionPresentation_UsesProgressiveDisclosure(
        bool connected,
        bool catalogBusy,
        bool showConnect,
        bool showDisconnect,
        bool showModels)
    {
        var presentation = AISettingsConnectionPresentation.Evaluate(
            connected
                ? AiConnectionDisplayState.Connected
                : AiConnectionDisplayState.Disconnected,
            connected,
            catalogBusy);

        Assert.Equal(showConnect, presentation.ShowConnectForm);
        Assert.Equal(showDisconnect, presentation.ShowConnectedSummary);
        Assert.Equal(showModels, presentation.ShowModelBlock);
        Assert.NotEqual(
            presentation.ShowConnectForm,
            presentation.ShowConnectedSummary);
    }

    [Fact]
    public void CatalogRefresh_IsDisabledOnlyWhileConnectedCatalogIsBusy()
    {
        var ready = AISettingsConnectionPresentation.Evaluate(
            AiConnectionDisplayState.Connected,
            hasConnectedKey: true,
            catalogBusy: false);
        var busy = AISettingsConnectionPresentation.Evaluate(
            AiConnectionDisplayState.Connected,
            hasConnectedKey: true,
            catalogBusy: true);

        Assert.True(ready.RefreshEnabled);
        Assert.False(busy.RefreshEnabled);
    }

    [Theory]
    [InlineData(
        0,
        false,
        "Settings_Ai_Status_Disconnected",
        "",
        0)]
    [InlineData(
        4,
        false,
        "Settings_Ai_Status_Disconnected",
        "Settings_Ai_Status_KeyRequired",
        0)]
    [InlineData(
        1,
        false,
        "Settings_Ai_Status_Connecting_Compact",
        "",
        1)]
    [InlineData(
        2,
        true,
        "Settings_Ai_Status_Connected_Compact",
        "",
        2)]
    [InlineData(
        3,
        true,
        "Settings_Ai_Status_Connected_Compact",
        "",
        2)]
    public void ConnectionPresentation_MapsNormalStateClasses(
        int stateValue,
        bool hasConnectedKey,
        string headline,
        string detail,
        int visualValue)
    {
        var state = (AiConnectionDisplayState)stateValue;
        var visual = (AISettingsStatusVisual)visualValue;
        var presentation = AISettingsConnectionPresentation.Evaluate(
            state,
            hasConnectedKey,
            catalogBusy: false);

        Assert.Equal(headline, presentation.HeadlineResource);
        Assert.Equal(detail, presentation.DetailResource);
        Assert.Equal(visual, presentation.StatusVisual);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(9, false)]
    [InlineData(10, false)]
    [InlineData(11, false)]
    [InlineData(12, false)]
    [InlineData(13, false)]
    [InlineData(14, false)]
    [InlineData(15, false)]
    [InlineData(16, true)]
    public void ConnectionPresentation_MapsTypedErrorsToErrorChip(
        int stateValue,
        bool hasConnectedKey)
    {
        var state = (AiConnectionDisplayState)stateValue;
        var presentation = AISettingsConnectionPresentation.Evaluate(
            state,
            hasConnectedKey,
            catalogBusy: false);

        Assert.Equal(
            "Settings_Ai_Status_Error_Compact",
            presentation.HeadlineResource);
        Assert.Equal(
            AiConnectionStatusMapper.ResourceKey(state),
            presentation.DetailResource);
        Assert.Equal(
            AISettingsStatusVisual.Error,
            presentation.StatusVisual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyKey_IsRejectedBeforeInfrastructurePath(string enteredKey)
    {
        Assert.False(
            AISettingsConnectInputPolicy.MayStartConnection(enteredKey));

        var source = SettingsBuilderSource();
        var connect = MethodBody(
            source,
            "private async Task ConnectOpenRouterAsync()",
            "private async void VerifyExistingKey()");
        var guard = connect.IndexOf(
            "AISettingsConnectInputPolicy.MayStartConnection",
            StringComparison.Ordinal);
        var operation = connect.IndexOf(
            "_aiOperationLifetime.Begin(-1)",
            StringComparison.Ordinal);
        var infrastructurePath = connect.IndexOf(
            "await ConnectWithKeyAsync(",
            StringComparison.Ordinal);

        Assert.True(guard >= 0 && operation > guard && infrastructurePath > operation);
        Assert.Contains("AiConnectionDisplayState.MissingKey", connect);
        Assert.Contains("return;", connect.Substring(guard, operation - guard));
        Assert.DoesNotContain("_infrastructure", connect);
        Assert.Equal(
            "API key is required.",
            ResourceValue("Resources.resx", "Settings_Ai_Status_KeyRequired"));
        Assert.Equal(
            "Требуется API-ключ.",
            ResourceValue("Resources.ru.resx", "Settings_Ai_Status_KeyRequired"));
    }

    [Fact]
    public void SettingsAiLayout_HasNoLegacyFixedWidthsOrProviderCombo()
    {
        var root = FindRepositoryRoot();
        var sources = string.Join("\n", new[]
        {
            SettingsBuilderSource(),
            File.ReadAllText(Path.Combine(
                root,
                "NavisHelper",
                "WPF",
                "OpenRouterConnectionPanel.cs")),
            File.ReadAllText(Path.Combine(
                root,
                "NavisHelper",
                "WPF",
                "OpenRouterModelSelector.cs"))
        });
        var connectionPanel = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "OpenRouterConnectionPanel.cs"));

        Assert.DoesNotContain("Width = 280", sources);
        Assert.DoesNotContain("Width = 300", sources);
        Assert.DoesNotContain("new ComboBox", connectionPanel);
        Assert.Contains("TextTrimming.CharacterEllipsis", sources);
        Assert.Contains("GridUnitType.Star", sources);
    }

    [Fact]
    public void ModelSelector_HasSeparateClosedAndDropdownPresentations()
    {
        var root = FindRepositoryRoot();
        var selector = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "OpenRouterModelSelector.cs"));
        var closed = MethodBody(
            selector,
            "private static TextBlock BuildClosedSelectionView",
            "private static DataTemplate BuildDropdownItemTemplate()");
        var dropdown = MethodBody(
            selector,
            "private static DataTemplate BuildDropdownItemTemplate()",
            "private static Style BuildItemContainerStyle()");
        var settings = SettingsBuilderSource();

        Assert.Contains("SelectedItem.DisplayHeader", closed);
        Assert.Contains("TextTrimming.CharacterEllipsis", closed);
        Assert.Contains("TextWrapping.NoWrap", closed);
        Assert.DoesNotContain("CapabilityText", closed);
        Assert.Contains("DisplayHeader", dropdown);
        Assert.Contains("CapabilityText", dropdown);
        Assert.Contains("TextWrapping.Wrap", dropdown);
        Assert.Contains("IsEditable = true", selector);
        Assert.Contains("IsReadOnly = true", selector);
        Assert.Contains("TextSearch.SetTextPath", selector);
        Assert.DoesNotContain("RelativeSourceMode.FindAncestor", selector);
        Assert.DoesNotContain("DataTrigger", selector);
        Assert.Contains(
            "ModelCombo.SelectedItem as OpenRouterModelChoice",
            settings);
    }

    [Fact]
    public void ConnectionHeader_IsBoundedForNarrowDockPanel()
    {
        var root = FindRepositoryRoot();
        var connection = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "OpenRouterConnectionPanel.cs"));

        Assert.Contains("GridUnitType.Star", connection);
        Assert.Contains("MaxWidth = 112", connection);
        Assert.Contains("TextTrimming.CharacterEllipsis", connection);
        Assert.DoesNotContain("HorizontalScrollBarVisibility", connection);
    }

    [Fact]
    public void DevScripts_AreUnderCollapsedLocalizedExpander()
    {
        var source = SettingsBuilderSource();
        var service = MethodBody(
            source,
            "private UIElement BuildServiceSection()",
            "private UIElement BuildAboutSection()");

        Assert.Contains("new Expander", service);
        Assert.Contains("IsExpanded = false", service);
        Assert.Contains("Settings_Service_Developers_Expander", service);
        Assert.Contains("Panel_DevScripts_Load_Action", service);
    }

    [Fact]
    public void NeutralAndRussianResources_HaveCompleteKeyParity()
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

        Assert.Equal(neutral.Order(), russian.Order());
    }

    [Fact]
    public void RelocalizingModelRows_PreservesSelectionAndSearch()
    {
        var google = new OpenRouterModelChoice(new OpenRouterModelInfo(
            "google/example",
            "Google Example",
            new[] { "structured_outputs" }));
        var other = new OpenRouterModelChoice(new OpenRouterModelInfo(
            "provider/other",
            "Other",
            new[] { "structured_outputs" }));
        var picker = new OpenRouterModelPicker();
        picker.Replace(new[] { google, other }, google.Id);

        var before = picker.Filter("google");
        picker.Relocalize(
            key => "localized:" + key,
            (key, args) => "localized:" + key);
        var after = picker.Filter(picker.CurrentQuery);

        Assert.Single(before);
        Assert.Single(after);
        Assert.Equal("google", picker.CurrentQuery);
        Assert.Equal(google.Id, picker.SelectedModelId);
        Assert.Equal(google.Id, after[0].Id);
    }

    [Fact]
    public void LanguageRefresh_HasNoNetworkOrPersistenceSideEffects()
    {
        var source = SettingsBuilderSource();
        var refresh = MethodBody(
            source,
            "private void RefreshModelChoiceLocalization()",
            "private void RelocalizeModelChoices()");

        Assert.Contains("RelocalizeModelChoices();", refresh);
        Assert.Contains("ApplyModelFilterCore();", refresh);
        Assert.DoesNotContain("_infrastructure", refresh);
        Assert.DoesNotContain("Connect", refresh);
        Assert.DoesNotContain("RefreshModels", refresh);
        Assert.DoesNotContain("SaveSelectedModel", refresh);
    }

    [Fact]
    public void PresentationState_HasNoNetworkKeyOrPersistenceDependencies()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "AI",
            "AISettingsConnectionPresentation.cs"));

        Assert.DoesNotContain("OpenRouterKey", source);
        Assert.DoesNotContain("Transport", source);
        Assert.DoesNotContain("Infrastructure", source);
        Assert.DoesNotContain("Persist", source);
        Assert.DoesNotContain("Http", source);
    }

    private static string MethodBody(
        string source,
        string startMarker,
        string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source.Substring(start, end - start);
    }

    private static string SettingsBuilderSource()
    {
        return File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "NavisHelper",
            "WPF",
            "NavisHelperSettingsTabBuilder.cs"));
    }

    private static HashSet<string> ResourceKeys(string path)
    {
        return XDocument.Load(path)
            .Descendants("data")
            .Select(element => (string)element.Attribute("name"))
            .Where(value => value != null)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string ResourceValue(string fileName, string key)
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "NavisHelper",
            "Properties",
            fileName);
        return XDocument.Load(path)
            .Descendants("data")
            .Single(element =>
                string.Equals(
                    (string)element.Attribute("name"),
                    key,
                    StringComparison.Ordinal))
            .Element("value")
            .Value;
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
