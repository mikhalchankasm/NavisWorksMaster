using System.Globalization;
using System.Diagnostics;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NavisHelper.Core;
using NavisHelper.Core.Localization;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("ru-RU", 1)]
    [InlineData("ru-BY", 1)]
    [InlineData("en-US", 0)]
    [InlineData("de-DE", 0)]
    public void ResolverUsesHostUiCultureWithoutOverride(string cultureName, int expected)
    {
        Assert.Equal((UiLanguage)expected, UiLanguageResolver.Resolve(new CultureInfo(cultureName), null));
    }

    [Fact]
    public void ManualEnglishOverridesRussianHostCulture()
    {
        Assert.Equal(
            UiLanguage.English,
            UiLanguageResolver.Resolve(new CultureInfo("ru-RU"), UiLanguage.English));
    }

    [Fact]
    public void ManualRussianOverridesEnglishHostCulture()
    {
        Assert.Equal(
            UiLanguage.Russian,
            UiLanguageResolver.Resolve(new CultureInfo("en-US"), UiLanguage.Russian));
    }

    [Fact]
    public void InvalidPersistedValueFallsBackToHostCulture()
    {
        Assert.False(UiLanguageSettingsStore.TryParse("Language=not-a-language", out _));
        Assert.Equal(
            UiLanguage.Russian,
            UiLanguageResolver.Resolve(new CultureInfo("ru-RU"), null));
    }

    [Fact]
    public void MissingSettingsFileHasNoManualOverride()
    {
        using var temp = new TempDirectory();
        var store = new UiLanguageSettingsStore(Path.Combine(temp.Path, "missing.ini"));

        Assert.False(store.TryRead(out _));
    }

    [Theory]
    [InlineData("Language=en", 0)]
    [InlineData("Language=ru", 1)]
    public void SettingsStoreReadsValidValues(string content, int expected)
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "ui_language.ini");
        File.WriteAllText(path, content);
        var store = new UiLanguageSettingsStore(path);

        Assert.True(store.TryRead(out UiLanguage actual));
        Assert.Equal((UiLanguage)expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Language=")]
    [InlineData("Language=fr")]
    [InlineData("not ini content")]
    [InlineData("Language=ru\0broken")]
    public void SettingsStoreRejectsEmptyUnknownOrCorruptedContent(string content)
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "ui_language.ini");
        File.WriteAllText(path, content);
        var store = new UiLanguageSettingsStore(path);

        Assert.False(store.TryRead(out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SettingsStoreRoundTrips(int languageValue)
    {
        var language = (UiLanguage)languageValue;
        using var temp = new TempDirectory();
        var store = new UiLanguageSettingsStore(Path.Combine(temp.Path, "nested", "ui_language.ini"));

        Assert.True(store.TryWrite(language));
        Assert.True(store.TryRead(out UiLanguage actual));
        Assert.Equal(language, actual);
    }

    [Fact]
    public void SettingsStoreReadAndWriteFailuresDoNotEscape()
    {
        using var temp = new TempDirectory();
        string lockedPath = Path.Combine(temp.Path, "locked.ini");
        File.WriteAllText(lockedPath, "Language=ru");
        var readStore = new UiLanguageSettingsStore(lockedPath);
        using var locked = new FileStream(
            lockedPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        Assert.False(readStore.TryRead(out _));

        var writeStore = new UiLanguageSettingsStore(temp.Path);
        Assert.False(writeStore.TryWrite(UiLanguage.Russian));
    }

    [Fact]
    public void SessionLanguageChangesEvenWhenPersistenceFails()
    {
        using var temp = new TempDirectory();
        var store = new UiLanguageSettingsStore(temp.Path);
        var service = new UiLocalizationService(store, new CultureInfo("en-US"));

        Assert.False(service.SetManualLanguage(UiLanguage.Russian));
        Assert.Equal(UiLanguage.Russian, service.CurrentLanguage);
    }

    [Fact]
    public void SettingsFileIsCreatedOnlyAfterManualSelection()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "ui_language.ini");
        var service = new UiLocalizationService(
            new UiLanguageSettingsStore(path),
            new CultureInfo("en-US"));

        Assert.False(File.Exists(path));
        service.SetManualLanguage(UiLanguage.English);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void RepeatingCurrentLanguageStillRaisesRefreshEvent()
    {
        using var temp = new TempDirectory();
        var service = new UiLocalizationService(
            new UiLanguageSettingsStore(Path.Combine(temp.Path, "ui_language.ini")),
            new CultureInfo("en-US"));
        int refreshCount = 0;
        service.LanguageChanged += (_, _) => refreshCount++;

        service.SetManualLanguage(UiLanguage.English);

        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public void ThrowingLanguageSubscriberDoesNotBlockRemainingSurfaces()
    {
        using var temp = new TempDirectory();
        var service = new UiLocalizationService(
            new UiLanguageSettingsStore(Path.Combine(temp.Path, "ui_language.ini")),
            new CultureInfo("en-US"));
        int refreshCount = 0;
        service.LanguageChanged += (_, _) => throw new InvalidOperationException("surface failed");
        service.LanguageChanged += (_, _) => refreshCount++;

        bool persisted = service.SetManualLanguage(UiLanguage.Russian);

        Assert.True(persisted);
        Assert.Equal(UiLanguage.Russian, service.CurrentLanguage);
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public void NeutralAndRussianResourcesHaveUniqueMatchingKeySets()
    {
        string repoRoot = FindRepositoryRoot();
        string neutralPath = Path.Combine(repoRoot, "NavisHelper", "Properties", "Resources.resx");
        string russianPath = Path.Combine(repoRoot, "NavisHelper", "Properties", "Resources.ru.resx");

        string[] neutralKeys = GetResourceKeys(neutralPath);
        string[] russianKeys = GetResourceKeys(russianPath);

        Assert.Equal(neutralKeys.Length, neutralKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(russianKeys.Length, russianKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            neutralKeys.OrderBy(key => key, StringComparer.Ordinal),
            russianKeys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void ResourceFormatPlaceholdersMatchAcrossLanguages()
    {
        (Dictionary<string, string> neutral, Dictionary<string, string> russian) =
            LoadProductionResourceValues();

        foreach (string key in neutral.Keys)
        {
            Assert.Equal(
                GetPlaceholderIndexes(neutral[key]),
                GetPlaceholderIndexes(russian[key]));
        }
    }

    [Fact]
    public void FileDialogFiltersAreWellFormedInBothLanguages()
    {
        (Dictionary<string, string> neutral, Dictionary<string, string> russian) =
            LoadProductionResourceValues();

        foreach (Dictionary<string, string> values in new[] { neutral, russian })
        {
            foreach (KeyValuePair<string, string> entry in values.Where(
                entry => entry.Key.Contains("FileFilter", StringComparison.Ordinal)))
            {
                string[] segments = entry.Value.Split('|');
                Assert.NotEmpty(segments);
                Assert.Equal(0, segments.Length % 2);
                Assert.DoesNotContain(segments, string.IsNullOrWhiteSpace);
            }
        }
    }

    [Fact]
    public void ResourceManagerFallsBackToNeutralEnglish()
    {
        var manager = new ResourceManager(
            "NavisHelper.Properties.Resources",
            typeof(LocalizationTests).Assembly);

        Assert.Equal(
            "About NavisHelper",
            manager.GetString("AboutWindowTitle", new CultureInfo("de-DE")));
    }

    [Fact]
    public void ServiceUsesExplicitLanguageWithoutChangingProcessUiCulture()
    {
        using var temp = new TempDirectory();
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        var service = new UiLocalizationService(
            new UiLanguageSettingsStore(Path.Combine(temp.Path, "ui_language.ini")),
            new CultureInfo("ru-RU"));

        Assert.Equal("О программе NavisHelper", service.GetString("AboutWindowTitle"));
        Assert.Equal(
            "Версия 2.8.9.0",
            service.Format("AboutVersionFormat", "2.8.9.0"));
        service.SetManualLanguage(UiLanguage.English);
        Assert.Equal("About NavisHelper", service.GetString("AboutWindowTitle"));
        Assert.Same(originalUiCulture, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public void AllFourteenColorSchemesHaveNamesAndDescriptionsInBothLanguages()
    {
        using var temp = new TempDirectory();
        var service = new UiLocalizationService(
            new UiLanguageSettingsStore(Path.Combine(temp.Path, "ui_language.ini")),
            new CultureInfo("en-US"));
        ColorSchemeType[] schemes = Enum.GetValues<ColorSchemeType>();

        Assert.Equal(14, schemes.Length);
        Assert.Equal(
            Enumerable.Range(1, 14),
            schemes.Select(scheme => (int)scheme));

        var englishNames = new Dictionary<ColorSchemeType, string>();
        var englishDescriptions = new Dictionary<ColorSchemeType, string>();
        foreach (UiLanguage language in new[] { UiLanguage.English, UiLanguage.Russian })
        {
            service.SetManualLanguage(language);
            foreach (ColorSchemeType scheme in schemes)
            {
                string name = ColorSchemeUiText.GetName(service, scheme);
                string description = ColorSchemeUiText.GetDescription(service, scheme);
                Assert.False(string.IsNullOrWhiteSpace(name));
                Assert.False(string.IsNullOrWhiteSpace(description));

                if (language == UiLanguage.English)
                {
                    englishNames[scheme] = name;
                    englishDescriptions[scheme] = description;
                }
                else
                {
                    Assert.NotEqual(englishNames[scheme], name);
                    Assert.NotEqual(englishDescriptions[scheme], description);
                }
            }
        }
    }

    [Fact]
    public void RibbonIdsAreInvariantAndUnique()
    {
        string[] ids =
        {
            RibbonIds.Tab,
            RibbonIds.Panel,
            RibbonIds.PanelSource,
            RibbonIds.ShowPanelButton
        };

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            new[] { "Panel", "PanelSource", "ShowPanelButton", "Tab" },
            typeof(RibbonIds)
                .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
        Assert.Equal("NavisHelper.Tab", RibbonIds.Tab);
        Assert.Equal("NavisHelper.Panel", RibbonIds.Panel);
        Assert.Equal("NavisHelper.Panel_Source", RibbonIds.PanelSource);
        Assert.Equal("NavisHelper.Button.ShowPanel", RibbonIds.ShowPanelButton);
        Assert.DoesNotContain(ids, id => id.Any(character => character > 127));
    }

    [Fact]
    public void RibbonVisibleTextIsDirectInvariantNavisHelper()
    {
        string repoRoot = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "NavisHelper",
            "RibbonLoader.cs"));

        Assert.Contains("tab.Title = \"NavisHelper\";", source, StringComparison.Ordinal);
        Assert.Contains("panel.Source.Title = \"NavisHelper\";", source, StringComparison.Ordinal);
        Assert.Contains("showPanelButton.Text = \"NavisHelper\";", source, StringComparison.Ordinal);
        Assert.Contains("showPanelButton.ToolTip = \"NavisHelper\";", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LanguageChanged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UiLocalizationService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Open NavisHelper", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Открыть NavisHelper", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(0, false, true)]
    public void LanguageRadioStateShowsExactlyOneCurrentLanguage(
        int currentLanguageValue,
        bool expectedRussian,
        bool expectedEnglish)
    {
        var currentLanguage = (UiLanguage)currentLanguageValue;
        var state = new UiLanguageRadioState(currentLanguage);

        Assert.Equal(currentLanguage, state.SelectedLanguage);
        Assert.Equal(expectedRussian, state.IsRussianChecked);
        Assert.Equal(expectedEnglish, state.IsEnglishChecked);
        Assert.NotEqual(state.IsRussianChecked, state.IsEnglishChecked);
    }

    [Fact]
    public void LanguageRadioStateDoesNotApplyInitialCheckedState()
    {
        var state = new UiLanguageRadioState(UiLanguage.Russian);
        int applyCount = 0;

        state.Refresh(
            UiLanguage.Russian,
            (russian, english) =>
            {
                Assert.True(russian);
                Assert.False(english);
            });
        bool applied = state.TrySelect(
            UiLanguage.Russian,
            language =>
            {
                applyCount++;
                return true;
            },
            out bool persisted);

        Assert.False(applied);
        Assert.False(persisted);
        Assert.Equal(0, applyCount);
    }

    [Fact]
    public void LanguageRadioStateAppliesRealUserChoiceExactlyOnce()
    {
        var state = new UiLanguageRadioState(UiLanguage.Russian);
        int applyCount = 0;
        state.CompleteInitialization();

        bool applied = state.TrySelect(
            UiLanguage.English,
            language =>
            {
                applyCount++;
                Assert.Equal(UiLanguage.English, language);
                return false;
            },
            out bool persisted);
        bool repeated = state.TrySelect(
            UiLanguage.English,
            language =>
            {
                applyCount++;
                return true;
            },
            out bool repeatedPersisted);

        Assert.True(applied);
        Assert.False(persisted);
        Assert.False(repeated);
        Assert.False(repeatedPersisted);
        Assert.Equal(1, applyCount);
        Assert.Equal(UiLanguage.English, state.SelectedLanguage);
        Assert.False(state.IsRussianChecked);
        Assert.True(state.IsEnglishChecked);
    }

    [Fact]
    public void LanguageRadioStateProgrammaticRefreshDoesNotReenterSelection()
    {
        var state = new UiLanguageRadioState(UiLanguage.Russian);
        int applyCount = 0;
        state.CompleteInitialization();

        state.Refresh(
            UiLanguage.English,
            (russian, english) =>
            {
                Assert.False(russian);
                Assert.True(english);
                Assert.False(state.TrySelect(
                    UiLanguage.English,
                    language =>
                    {
                        applyCount++;
                        return true;
                    },
                    out bool persisted));
                Assert.False(persisted);
            });

        Assert.Equal(0, applyCount);
        Assert.Equal(UiLanguage.English, state.SelectedLanguage);
        Assert.True(state.IsEnglishChecked);
        Assert.False(state.IsRussianChecked);
    }

    [Fact]
    public void LanguageRadioStateRollsBackWhenLanguageChangeThrows()
    {
        var state = new UiLanguageRadioState(UiLanguage.Russian);
        state.CompleteInitialization();

        Assert.Throws<InvalidOperationException>(() =>
            state.TrySelect(
                UiLanguage.English,
                language => throw new InvalidOperationException("failed"),
                out _));

        Assert.Equal(UiLanguage.Russian, state.SelectedLanguage);
        Assert.True(state.IsRussianChecked);
        Assert.False(state.IsEnglishChecked);
    }

    [Fact]
    public void BindingRegistryRefreshesRegisteredSemanticSlot()
    {
        using var registry = new UiLocalizationBindingRegistry();
        var target = new object();
        string language = "English";
        string displayed = null!;

        Assert.True(registry.Register(target, "Text", () => displayed = language));
        Assert.Equal("English", displayed);

        language = "Русский";
        registry.Refresh();

        Assert.Equal("Русский", displayed);
    }

    [Fact]
    public void BindingRegistryDoesNotAccumulateDuplicateTargetSlots()
    {
        using var registry = new UiLocalizationBindingRegistry();
        var target = new object();
        int refreshCount = 0;

        Assert.True(registry.Register(target, "Header", () => refreshCount++));
        Assert.False(registry.Register(target, "Header", () => refreshCount += 100));
        registry.Refresh();

        Assert.Equal(2, refreshCount);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void BindingRegistryRemovesClosedDynamicSurface()
    {
        using var registry = new UiLocalizationBindingRegistry();
        var target = new object();
        int refreshCount = 0;
        registry.Register(target, "Title", () => refreshCount++);

        Assert.True(registry.Unregister(target, "Title"));
        registry.Refresh();

        Assert.Equal(1, refreshCount);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void BindingRegistryRefreshDoesNotInvokeUnregisteredCommand()
    {
        using var registry = new UiLocalizationBindingRegistry();
        var label = new object();
        int commandCount = 0;
        string displayed = null!;

        registry.Register(label, "Text", () => displayed = "refreshed");
        registry.Refresh();

        Assert.Equal("refreshed", displayed);
        Assert.Equal(0, commandCount);
    }

    [Fact]
    public void BindingRegistryNeverTouchesUnregisteredModelValue()
    {
        using var registry = new UiLocalizationBindingRegistry();
        var label = new object();
        string displayed = "Ready";
        string modelValue = "Пользовательское имя модели";

        registry.Register(label, "Text", () => displayed = "Готово");
        registry.Refresh();

        Assert.Equal("Готово", displayed);
        Assert.Equal("Пользовательское имя модели", modelValue);
    }

    [Fact]
    public void BindingRegistryDisposeStopsFurtherRefreshes()
    {
        var registry = new UiLocalizationBindingRegistry();
        var target = new object();
        int refreshCount = 0;
        registry.Register(target, "Text", () => refreshCount++);

        registry.Dispose();
        registry.Refresh();

        Assert.Equal(1, refreshCount);
        Assert.Equal(0, registry.Count);
        Assert.Throws<ObjectDisposedException>(
            () => registry.Register(target, "Header", () => refreshCount++));
    }

    [Fact]
    public void BindingRegistryIsolatesRefreshFailureAndRecoversNextTime()
    {
        using var registry = new UiLocalizationBindingRegistry();
        var failingTarget = new object();
        var healthyTarget = new object();
        bool shouldFail = false;
        int failingRefreshes = 0;
        int healthyRefreshes = 0;

        Assert.True(registry.Register(failingTarget, "Text", () =>
        {
            failingRefreshes++;
            if (shouldFail)
                throw new InvalidOperationException("transient");
        }));
        Assert.True(registry.Register(
            healthyTarget,
            "Text",
            () => healthyRefreshes++));

        shouldFail = true;
        registry.Refresh();
        Assert.Equal(2, failingRefreshes);
        Assert.Equal(2, healthyRefreshes);

        shouldFail = false;
        registry.Refresh();
        Assert.Equal(3, failingRefreshes);
        Assert.Equal(3, healthyRefreshes);
        Assert.Equal(2, registry.Count);
    }

    [Fact]
    public void BindingRegistryRollsBackFailedInitialRefresh()
    {
        using var registry = new UiLocalizationBindingRegistry();
        var target = new object();

        Assert.False(registry.Register(
            target,
            "Text",
            () => throw new InvalidOperationException("initial")));
        Assert.Equal(0, registry.Count);

        int refreshCount = 0;
        Assert.True(registry.Register(target, "Text", () => refreshCount++));
        registry.Refresh();
        Assert.Equal(2, refreshCount);
    }

    [Fact]
    public void BindingRegistryAllowsRemovalDuringRefresh()
    {
        using var registry = new UiLocalizationBindingRegistry();
        var first = new object();
        var removed = new object();
        bool removeDuringRefresh = false;
        int removedRefreshes = 0;

        registry.Register(first, "Text", () =>
        {
            if (removeDuringRefresh)
                registry.Unregister(removed, "Text");
        });
        registry.Register(removed, "Text", () => removedRefreshes++);

        removeDuringRefresh = true;
        registry.Refresh();

        Assert.Equal(1, removedRefreshes);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void FormattedBindingKeepsArgumentsAcrossLanguageRefresh()
    {
        using var temp = new TempDirectory();
        var service = new UiLocalizationService(
            new UiLanguageSettingsStore(Path.Combine(temp.Path, "ui_language.ini")),
            new CultureInfo("en-US"));
        using var registry = new UiLocalizationBindingRegistry();
        var target = new object();
        int millimetres = 125;
        string displayed = string.Empty;
        registry.Register(
            target,
            "Text:Panel_Common_Millimetres_Format",
            () => displayed = service.Format("Panel_Common_Millimetres_Format", millimetres));

        Assert.Equal("125 mm", displayed);
        service.SetManualLanguage(UiLanguage.Russian);
        registry.Refresh();

        Assert.Equal("125 мм", displayed);
        Assert.Equal(125, millimetres);
    }

    [Fact]
    public void DynamicMessageUsesCurrentSelectedLanguage()
    {
        using var temp = new TempDirectory();
        var service = new UiLocalizationService(
            new UiLanguageSettingsStore(Path.Combine(temp.Path, "ui_language.ini")),
            new CultureInfo("en-US"));

        Assert.Equal(
            "Clash Test: no tests to run",
            service.GetString("Panel_Clash_RunAll_None"));
        service.SetManualLanguage(UiLanguage.Russian);
        Assert.Equal(
            "Clash Test: нет тестов для запуска",
            service.GetString("Panel_Clash_RunAll_None"));
    }

    [Fact]
    public void LocalizedFormatArgumentRefreshesWithoutChangingModelValues()
    {
        object modelValue = "user/model value";
        object localized = UiLocalizedArgument.FromResource("Panel_Clash_GroupingDisabled");

        object[] english = UiLocalizedArgument.Resolve(
            new[] { localized, modelValue },
            key => key == "Panel_Clash_GroupingDisabled" ? "disabled" : key);
        object[] russian = UiLocalizedArgument.Resolve(
            new[] { localized, modelValue },
            key => key == "Panel_Clash_GroupingDisabled" ? "отключено" : key);

        Assert.Equal("disabled", english[0]);
        Assert.Equal("отключено", russian[0]);
        Assert.Same(modelValue, english[1]);
        Assert.Same(modelValue, russian[1]);
    }

    [Fact]
    public void NestedLocalizedFormatArgumentRefreshesWithRawValuesIntact()
    {
        using var temp = new TempDirectory();
        var service = new UiLocalizationService(
            new UiLanguageSettingsStore(Path.Combine(temp.Path, "ui_language.ini")),
            new CultureInfo("en-US"));
        object modelValue = "Clash-42";
        object nested = UiLocalizedArgument.FromResource(
            "Panel_Clash_Preview_NoItems_Format",
            modelValue);

        object[] english = UiLocalizedArgument.Resolve(
            new[] { nested },
            (key, arguments) => service.Format(key, arguments));
        service.SetManualLanguage(UiLanguage.Russian);
        object[] russian = UiLocalizedArgument.Resolve(
            new[] { nested },
            (key, arguments) => service.Format(key, arguments));

        Assert.Equal("No items: Clash-42", english[0]);
        Assert.Equal("Нет элементов: Clash-42", russian[0]);
        Assert.Equal("Clash-42", modelValue);
    }

    [Fact]
    public void BatchViewpointStatusRefreshesOuterAndNestedTextWithoutChangingRawValues()
    {
        using var temp = new TempDirectory();
        var service = new UiLocalizationService(
            new UiLanguageSettingsStore(Path.Combine(temp.Path, "ui_language.ini")),
            new CultureInfo("en-US"));
        const string expectedFailureName = "User clash α";
        const string exceptionFailureName = "User clash β";
        const string rawException = "raw exception 42";
        var records = new[]
        {
            ClashViewpointErrorRecord.FromExpectedFailure(
                1,
                2,
                expectedFailureName,
                new UiStatusResourceDescriptor(
                    "Panel_Clash_Preview_NoItems_Format",
                    expectedFailureName)),
            ClashViewpointErrorRecord.FromException(
                2,
                2,
                exceptionFailureName,
                new InvalidOperationException(rawException))
        };
        object nestedSummary = UiLocalizedArgument.FromResource(
            "Panel_Clash_Viewpoints_FirstErrors_Format",
            UiLocalizedArgument.Join(
                " | ",
                records.Select(record =>
                    (object)record.ToStatusDescriptor().AsLocalizedArgument())));
        var completed = new UiStatusResourceDescriptor(
            "Panel_Clash_Viewpoints_Completed_Format",
            0,
            2,
            1,
            1d,
            0d,
            0d,
            0d,
            0d,
            0d,
            nestedSummary);

        string english = FormatDescriptor(service, completed);
        service.SetManualLanguage(UiLanguage.Russian);
        string russian = FormatDescriptor(service, completed);

        Assert.Contains("Viewpoints created:", english, StringComparison.Ordinal);
        Assert.Contains("first errors:", english, StringComparison.Ordinal);
        Assert.Contains("No items:", english, StringComparison.Ordinal);
        Assert.Contains("Создано VP:", russian, StringComparison.Ordinal);
        Assert.Contains("первые ошибки:", russian, StringComparison.Ordinal);
        Assert.Contains("Нет элементов:", russian, StringComparison.Ordinal);
        Assert.Contains(expectedFailureName, english, StringComparison.Ordinal);
        Assert.Contains(expectedFailureName, russian, StringComparison.Ordinal);
        Assert.Contains(exceptionFailureName, english, StringComparison.Ordinal);
        Assert.Contains(exceptionFailureName, russian, StringComparison.Ordinal);
        Assert.Contains(rawException, english, StringComparison.Ordinal);
        Assert.Contains(rawException, russian, StringComparison.Ordinal);
        Assert.Equal(expectedFailureName, records[0].DisplayName);
        Assert.Equal(exceptionFailureName, records[1].DisplayName);
        Assert.Equal(rawException, records[1].RawExceptionMessage);
    }

    [Fact]
    public void SavedViewpointStatusRefreshesSuffixWithoutChangingSavedName()
    {
        using var temp = new TempDirectory();
        var service = new UiLocalizationService(
            new UiLanguageSettingsStore(Path.Combine(temp.Path, "ui_language.ini")),
            new CultureInfo("en-US"));
        const string savedName = "Owner viewpoint 7";
        object savedEntry = UiLocalizedArgument.FromResource(
            "Panel_Clash_Viewpoint_SavedNameEntry_Format",
            savedName,
            UiLocalizedArgument.FromResource(
                "Panel_Clash_Viewpoint_NoCenterSuffix"));
        var descriptor = new UiStatusResourceDescriptor(
            "Panel_Clash_Viewpoint_SavedNames_Format",
            savedEntry);

        string english = FormatDescriptor(service, descriptor);
        service.SetManualLanguage(UiLanguage.Russian);
        string russian = FormatDescriptor(service, descriptor);

        Assert.Equal("Viewpoint: Owner viewpoint 7 | no clash center", english);
        Assert.Equal("VP: Owner viewpoint 7 | нет центра коллизии", russian);
        Assert.Contains(savedName, english, StringComparison.Ordinal);
        Assert.Contains(savedName, russian, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewOutcomeMapsStableKindAndRawArgumentsToCurrentUiCulture()
    {
        using var temp = new TempDirectory();
        var service = new UiLocalizationService(
            new UiLanguageSettingsStore(Path.Combine(temp.Path, "ui_language.ini")),
            new CultureInfo("en-US"));
        var outcome = new PreviewManagerUiOutcome(
            PreviewManagerUiOutcomeKind.ClashResultNoItems,
            "User clash name");
        UiStatusResourceDescriptor descriptor =
            PreviewManagerUiStatusMapper.ForClashPreview(outcome);

        Assert.Equal(
            "No items: User clash name",
            service.Format(descriptor.ResourceKey, descriptor.Arguments));
        service.SetManualLanguage(UiLanguage.Russian);
        Assert.Equal(
            "Нет элементов: User clash name",
            service.Format(descriptor.ResourceKey, descriptor.Arguments));
        Assert.Equal(PreviewManagerUiOutcomeKind.ClashResultNoItems, outcome.Kind);
        Assert.Equal("User clash name", outcome.Arguments[0]);
    }

    [Theory]
    [InlineData("run", "Clash Test: выполнено")]
    [InlineData("reset", "Clash Test: сброшено")]
    [InlineData("compact", "Clash Test: сжато")]
    [InlineData("delete", "Clash Test: удалено")]
    public void ClashOperationProtocolReasonPreservesBaselineAcrossUiLanguages(
        string operation,
        string expected)
    {
        using var temp = new TempDirectory();
        var service = new UiLocalizationService(
            new UiLanguageSettingsStore(Path.Combine(temp.Path, "ui_language.ini")),
            new CultureInfo("en-US"));

        Assert.Equal(expected, ClashOperationProtocolReason.For(operation));
        service.SetManualLanguage(UiLanguage.Russian);
        Assert.Equal(expected, ClashOperationProtocolReason.For(operation));
    }

    [Fact]
    public void PersistedModelNamesAreInvariantBaselineValues()
    {
        Assert.Equal("0000 Базовый вид", PersistedModelNames.ClashResetViewpoint);
        Assert.Equal("Группа", PersistedModelNames.ClashGroupDefault);
        Assert.Equal("Search Set", PersistedModelNames.SearchSetFallback);
    }

    [Fact]
    public void ActivePanelUsesInvariantOperationReasonAndPersistedNames()
    {
        string repoRoot = FindRepositoryRoot();
        string operations = File.ReadAllText(Path.Combine(
            repoRoot,
            "NavisHelper",
            "WPF",
            "NavisHelperPanel.Clash.Operations.cs"));
        string lifecycle = File.ReadAllText(Path.Combine(
            repoRoot,
            "NavisHelper",
            "WPF",
            "NavisHelperPanel.Clash.Lifecycle.cs"));
        string colors = File.ReadAllText(Path.Combine(
            repoRoot,
            "NavisHelper",
            "WPF",
            "NavisHelperPanel.Colors.cs"));

        Assert.Contains(
            "string protocolReason = ClashOperationProtocolReason.For(operation);",
            operations);
        Assert.Contains("RejectClashInteractiveBusy(protocolReason)", operations);
        Assert.Contains("BeginInteractiveOperation(protocolReason)", operations);
        Assert.DoesNotMatch(
            @"BeginInteractiveOperation\s*\([^;]*(?:OperationLabel|PanelUi|UiLocalizationService)",
            operations);
        Assert.Contains("PersistedModelNames.ClashResetViewpoint", operations);
        Assert.Contains("PersistedModelNames.ClashGroupDefault", lifecycle);
        Assert.Contains("PersistedModelNames.SearchSetFallback", colors);
        Assert.DoesNotContain("Panel_Clash_Viewpoints_ResetName", operations);
    }

    [Fact]
    public void ClashDocumentChangeResetClearsLocalizedGroupContentsCachesBeforeUiWork()
    {
        string repoRoot = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "NavisHelper",
            "WPF",
            "NavisHelperPanel.Clash.Lifecycle.cs"));
        Match reset = Regex.Match(
            source,
            @"private void ResetClashUiForDocumentChange\(\)\s*\{(?<body>.*?)\n\s*\}\s*\n\s*private void BeginInvokeForCurrentClashDocument",
            RegexOptions.Singleline);

        Assert.True(reset.Success);
        string body = reset.Groups["body"].Value;
        int rowObjectClear = body.IndexOf(
            "_clashGroupContentsRowObject = null;",
            StringComparison.Ordinal);
        int resultsClear = body.IndexOf(
            "_clashGroupContentsResults = null;",
            StringComparison.Ordinal);
        int existingManagedStateClear = body.IndexOf(
            "_pendingClashDataRefreshReason = null;",
            StringComparison.Ordinal);

        Assert.True(rowObjectClear >= 0);
        Assert.True(resultsClear >= 0);
        Assert.True(existingManagedStateClear >= 0);
        Assert.True(rowObjectClear < existingManagedStateClear);
        Assert.True(resultsClear < existingManagedStateClear);
        Assert.Contains("_clashGroupContentsGrid.ItemsSource = null;", body);
        Assert.Contains(
            "_clashGroupContentsStatus.Text = PanelUi(\"Panel_Clash_SelectResultOrGroup\");",
            body);
    }

    [Fact]
    public void ManagerDiagnosticStatusesRemainBaselineAgentInputs()
    {
        string repoRoot = FindRepositoryRoot();
        string selectionManager = File.ReadAllText(Path.Combine(
            repoRoot,
            "NavisHelper",
            "SelectionPreviewManager.cs"));
        string clashManager = File.ReadAllText(Path.Combine(
            repoRoot,
            "NavisHelper",
            "ClashPreviewManager.cs"));
        string isolationService = File.ReadAllText(Path.Combine(
            repoRoot,
            "NavisHelper",
            "Agent",
            "Services",
            "ClashIsolationService.cs"));

        Assert.Contains("LastStatus = \"Нет активного документа\";", selectionManager);
        Assert.Contains("LastStatus = \"Не удалось определить габариты\";", selectionManager);
        Assert.Contains(
            "LastStatus = \"Нет выделенных объектов и сохранённой области Section Box\";",
            selectionManager);
        Assert.Contains(
            "LastStatus = \"Не удалось восстановить предыдущий режим «Только пара»\";",
            clashManager);
        Assert.Contains(
            "LastPairIsolationStatus = \"изоляция пропущена: объекты A/B не принадлежат активной модели\";",
            clashManager);
        Assert.Contains("preview.LastStatus", isolationService);
        Assert.Contains("preview.LastPairIsolationStatus", isolationService);
    }

    [Fact]
    public void ProductionResourcesContainNoRejectedPanelCatalog()
    {
        (Dictionary<string, string> neutral, Dictionary<string, string> russian) =
            LoadProductionResourceValues();

        Assert.DoesNotContain("PanelTextCatalog", neutral.Keys);
        Assert.DoesNotContain("PanelTextCatalog", russian.Keys);
        Assert.DoesNotContain(neutral.Keys, key => Regex.IsMatch(key, @"^P\d{3}$"));
        Assert.DoesNotContain(russian.Keys, key => Regex.IsMatch(key, @"^P\d{3}$"));

        string repoRoot = FindRepositoryRoot();
        Assert.False(File.Exists(Path.Combine(
            repoRoot,
            "NavisHelper",
            "Core",
            "Localization",
            "UiTextCatalog.cs")));
        Assert.False(File.Exists(Path.Combine(
            repoRoot,
            "NavisHelper",
            "WPF",
            "NavisHelperPanelUiLocalizer.cs")));
    }

    [Fact]
    public void PanelResourceKeysRejectOrdinalAndSentenceDerivedNames()
    {
        (Dictionary<string, string> neutral, _) = LoadProductionResourceValues();

        string[] panelKeys = neutral.Keys
            .Where(key => key.StartsWith("Panel_", StringComparison.Ordinal))
            .ToArray();

        Assert.DoesNotContain(panelKeys, key => Regex.IsMatch(key, @"^Panel_\d"));
        Assert.DoesNotContain(
            panelKeys,
            key => Regex.IsMatch(key, @"^Panel_[^_]+$")
                && Regex.Matches(key["Panel_".Length..], "[A-Z]").Count >= 4);
    }

    [Fact]
    public void DynamicPanelResourceFamiliesAreComplete()
    {
        (Dictionary<string, string> neutral, _) = LoadProductionResourceValues();
        string repoRoot = FindRepositoryRoot();
        string panelSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "NavisHelper",
            "WPF",
            "NavisHelperPanel.cs"));

        string[] paletteIds = Regex.Matches(
                panelSource,
                @"RegisterPaletteCommand\(""(?<id>[A-Za-z0-9]+)""")
            .Select(match => match.Groups["id"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(paletteIds);
        foreach (string id in paletteIds)
        {
            Assert.Contains($"Panel_CommandPalette_{id}_Title", neutral.Keys);
            Assert.Contains($"Panel_CommandPalette_{id}_Description", neutral.Keys);
        }

        Match colorsBlock = Regex.Match(
            panelSource,
            @"ClashColors = new\[\]\s*\{(?<body>.*?)\};",
            RegexOptions.Singleline);
        Assert.True(colorsBlock.Success);
        string[] colorIds = Regex.Matches(
                colorsBlock.Groups["body"].Value,
                @"\(""(?<id>[A-Za-z]+)""")
            .Select(match => match.Groups["id"].Value)
            .ToArray();
        Assert.NotEmpty(colorIds);
        foreach (string id in colorIds)
            Assert.Contains($"Panel_Color_{id}", neutral.Keys);

        foreach (string status in new[] { "New", "Active", "Reviewed", "Approved", "Resolved" })
            Assert.Contains($"Panel_Clash_Status_{status}", neutral.Keys);
    }

    [Fact]
    public void PaletteExecutedStatusKeepsCommandTitleAsLocalizedArgument()
    {
        string repoRoot = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "NavisHelper",
            "WPF",
            "NavisHelperPanel.cs"));
        Match status = Regex.Match(
            source,
            @"SetGlobalStatusResource\(\s*""Panel_CommandPalette_Executed_Format""(?<body>.*?)\);",
            RegexOptions.Singleline);

        Assert.True(status.Success);
        Assert.Contains(
            "PaletteCommandTitleStatusArgument(command)",
            status.Groups["body"].Value,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PaletteCommandTitle(command)",
            status.Groups["body"].Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StaticPanelLocalizationAuditPasses()
    {
        string repoRoot = FindRepositoryRoot();
        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(Path.Combine(
            repoRoot,
            "scripts",
            "check_panel_localization.py"));
        using Process process = Process.Start(startInfo)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"Static panel localization audit failed.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
    }

    [Fact]
    public void SettingsLanguageSectionUsesTwoRadioButtonsInOneGroup()
    {
        string repoRoot = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "NavisHelper",
            "WPF",
            "NavisHelperSettingsTabBuilder.cs"));
        Match section = Regex.Match(
            source,
            @"private UIElement BuildLanguageSettingsSection\(\)(?<body>.*?)private UIElement BuildAiSettingsSection",
            RegexOptions.Singleline);

        Assert.True(section.Success);
        Assert.Equal(2, Regex.Matches(section.Groups["body"].Value, @"new RadioButton").Count);
        Assert.Equal(
            2,
            Regex.Matches(
                section.Groups["body"].Value,
                @"GroupName = ""NavisHelper\.InterfaceLanguage""").Count);
        Assert.DoesNotContain("new ComboBox", section.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("radioState.CompleteInitialization()", section.Groups["body"].Value);
    }

    private static string[] GetResourceKeys(string path)
    {
        return XDocument.Load(path)
            .Root!
            .Elements("data")
            .Select(element => (string)element.Attribute("name")!)
            .ToArray();
    }

    private static (Dictionary<string, string> Neutral, Dictionary<string, string> Russian)
        LoadProductionResourceValues()
    {
        string repoRoot = FindRepositoryRoot();
        return (
            GetResourceValues(Path.Combine(
                repoRoot,
                "NavisHelper",
                "Properties",
                "Resources.resx")),
            GetResourceValues(Path.Combine(
                repoRoot,
                "NavisHelper",
                "Properties",
                "Resources.ru.resx")));
    }

    private static Dictionary<string, string> GetResourceValues(string path)
    {
        return XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => (string)element.Element("value")!,
                StringComparer.Ordinal);
    }

    private static int[] GetPlaceholderIndexes(string format)
    {
        return Regex.Matches(format, @"(?<!\{)\{(\d+)(?:[^}]*)\}(?!\})")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
    }

    private static string FormatDescriptor(
        UiLocalizationService service,
        UiStatusResourceDescriptor descriptor)
    {
        object[] arguments = UiLocalizedArgument.Resolve(
            descriptor.Arguments,
            (resourceKey, nestedArguments) =>
                service.Format(resourceKey, nestedArguments));
        return service.Format(descriptor.ResourceKey, arguments);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed class TempDirectory : IDisposable
    {
        internal TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "NavisHelper-localization-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
