using System.Globalization;

namespace NavisHelper.Core.Localization
{
    internal static class UiLanguageResolver
    {
        internal static UiLanguage Resolve(CultureInfo hostUiCulture, UiLanguage? manualOverride)
        {
            if (manualOverride.HasValue)
                return manualOverride.Value;

            CultureInfo culture = hostUiCulture ?? CultureInfo.InvariantCulture;
            return string.Equals(
                culture.TwoLetterISOLanguageName,
                "ru",
                System.StringComparison.OrdinalIgnoreCase)
                ? UiLanguage.Russian
                : UiLanguage.English;
        }
    }
}
