using System;
using System.Globalization;
using System.Resources;

namespace NavisHelper.Core.Localization
{
    internal sealed class UiLocalizationService
    {
        private static readonly Lazy<UiLocalizationService> LazyCurrent =
            new Lazy<UiLocalizationService>(CreateDefault);

        private readonly object _sync = new object();
        private readonly UiLanguageSettingsStore _settingsStore;
        private readonly ResourceManager _resourceManager;
        private UiLanguage _currentLanguage;

        internal UiLocalizationService(
            UiLanguageSettingsStore settingsStore,
            CultureInfo hostUiCulture,
            ResourceManager resourceManager = null)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            _resourceManager = resourceManager ?? Properties.Resources.ResourceManager;

            UiLanguage persistedLanguage;
            UiLanguage? manualOverride = settingsStore.TryRead(out persistedLanguage)
                ? persistedLanguage
                : (UiLanguage?)null;
            _currentLanguage = UiLanguageResolver.Resolve(hostUiCulture, manualOverride);
        }

        internal static UiLocalizationService Current => LazyCurrent.Value;

        internal event EventHandler LanguageChanged;

        internal UiLanguage CurrentLanguage
        {
            get
            {
                lock (_sync)
                    return _currentLanguage;
            }
        }

        internal CultureInfo SelectedCulture =>
            CurrentLanguage == UiLanguage.Russian
                ? CultureInfo.GetCultureInfo("ru-RU")
                : CultureInfo.InvariantCulture;

        internal string GetString(string resourceKey)
        {
            if (string.IsNullOrWhiteSpace(resourceKey))
                return string.Empty;

            string value = _resourceManager.GetString(resourceKey, SelectedCulture);
            return value ?? resourceKey;
        }

        internal string Format(string resourceKey, params object[] args)
        {
            return string.Format(SelectedCulture, GetString(resourceKey), args);
        }

        internal bool SetManualLanguage(UiLanguage language)
        {
            lock (_sync)
            {
                _currentLanguage = language;
            }

            bool persisted = _settingsStore.TryWrite(language);
            EventHandler handlers = LanguageChanged;
            if (handlers != null)
            {
                foreach (EventHandler handler in handlers.GetInvocationList())
                {
                    try
                    {
                        handler(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        // One UI surface must not prevent the remaining localized
                        // surfaces from refreshing or undo the active language.
                        global::NavisHelper.Core.Logger.Error(
                            ex.ToString(),
                            "UiLocalizationService.LanguageChanged");
                    }
                }
            }

            return persisted;
        }

        private static UiLocalizationService CreateDefault()
        {
            return new UiLocalizationService(
                UiLanguageSettingsStore.CreateDefault(),
                CultureInfo.CurrentUICulture);
        }
    }
}
