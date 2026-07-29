namespace NavisHelper.Core.Localization
{
    internal static class ColorSchemeUiText
    {
        internal static string GetName(ColorSchemeType scheme)
        {
            return GetName(UiLocalizationService.Current, scheme);
        }

        internal static string GetName(
            UiLocalizationService localization,
            ColorSchemeType scheme)
        {
            return localization.GetString(GetNameKey(scheme));
        }

        internal static string GetDescription(ColorSchemeType scheme)
        {
            return GetDescription(UiLocalizationService.Current, scheme);
        }

        internal static string GetDescription(
            UiLocalizationService localization,
            ColorSchemeType scheme)
        {
            return localization.GetString(GetDescriptionKey(scheme));
        }

        private static string GetNameKey(ColorSchemeType scheme)
        {
            switch (scheme)
            {
                case ColorSchemeType.Grayscale: return "ColorSchemeGrayscaleName";
                case ColorSchemeType.GrayPastel: return "ColorSchemeGrayPastelName";
                case ColorSchemeType.Warm: return "ColorSchemeWarmName";
                case ColorSchemeType.Cool: return "ColorSchemeCoolName";
                case ColorSchemeType.Earth: return "ColorSchemeEarthName";
                case ColorSchemeType.Ocean: return "ColorSchemeOceanName";
                case ColorSchemeType.Forest: return "ColorSchemeForestName";
                case ColorSchemeType.Architectural: return "ColorSchemeArchitecturalName";
                case ColorSchemeType.HighContrast: return "ColorSchemeHighContrastName";
                case ColorSchemeType.Random: return "ColorSchemeRandomName";
                case ColorSchemeType.Industrial: return "ColorSchemeIndustrialName";
                case ColorSchemeType.Infrastructure: return "ColorSchemeInfrastructureName";
                case ColorSchemeType.OilGas: return "ColorSchemeOilGasName";
                case ColorSchemeType.PastelRainbow: return "ColorSchemePastelRainbowName";
                default: return "ColorSchemeUnknownName";
            }
        }

        private static string GetDescriptionKey(ColorSchemeType scheme)
        {
            switch (scheme)
            {
                case ColorSchemeType.Grayscale: return "ColorSchemeGrayscaleDescription";
                case ColorSchemeType.GrayPastel: return "ColorSchemeGrayPastelDescription";
                case ColorSchemeType.Warm: return "ColorSchemeWarmDescription";
                case ColorSchemeType.Cool: return "ColorSchemeCoolDescription";
                case ColorSchemeType.Earth: return "ColorSchemeEarthDescription";
                case ColorSchemeType.Ocean: return "ColorSchemeOceanDescription";
                case ColorSchemeType.Forest: return "ColorSchemeForestDescription";
                case ColorSchemeType.Architectural: return "ColorSchemeArchitecturalDescription";
                case ColorSchemeType.HighContrast: return "ColorSchemeHighContrastDescription";
                case ColorSchemeType.Random: return "ColorSchemeRandomDescription";
                case ColorSchemeType.Industrial: return "ColorSchemeIndustrialDescription";
                case ColorSchemeType.Infrastructure: return "ColorSchemeInfrastructureDescription";
                case ColorSchemeType.OilGas: return "ColorSchemeOilGasDescription";
                case ColorSchemeType.PastelRainbow: return "ColorSchemePastelRainbowDescription";
                default: return string.Empty;
            }
        }
    }
}
