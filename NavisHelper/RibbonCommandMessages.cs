using System.Windows;
using NavisHelper.Core.Localization;

namespace NavisHelper
{
    internal static class RibbonCommandMessages
    {
        internal static void ShowPluginMissing(string pluginId)
        {
            UiLocalizationService localization = UiLocalizationService.Current;
            MessageBox.Show(
                localization.Format("RibbonPluginMissingMessageFormat", pluginId),
                "NavisHelper",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        internal static void ShowDockPaneLoadFailed(string loadedType)
        {
            UiLocalizationService localization = UiLocalizationService.Current;
            MessageBox.Show(
                localization.Format("RibbonDockPaneLoadFailedFormat", loadedType),
                "NavisHelper",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        internal static void ShowError(string detail)
        {
            UiLocalizationService localization = UiLocalizationService.Current;
            MessageBox.Show(
                localization.Format("CommonErrorMessageFormat", detail),
                "NavisHelper",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
