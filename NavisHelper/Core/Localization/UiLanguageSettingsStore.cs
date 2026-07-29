using System;
using System.IO;

namespace NavisHelper.Core.Localization
{
    internal sealed class UiLanguageSettingsStore
    {
        private const string LanguagePrefix = "Language=";

        internal UiLanguageSettingsStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A settings file path is required.", nameof(filePath));

            FilePath = filePath;
        }

        internal string FilePath { get; }

        internal static UiLanguageSettingsStore CreateDefault()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return new UiLanguageSettingsStore(
                Path.Combine(appData, "NavisHelper", "ui_language.ini"));
        }

        internal bool TryRead(out UiLanguage language)
        {
            language = UiLanguage.English;

            try
            {
                if (!File.Exists(FilePath))
                    return false;

                string content = File.ReadAllText(FilePath);
                return TryParse(content, out language);
            }
            catch
            {
                return false;
            }
        }

        internal bool TryWrite(UiLanguage language)
        {
            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string value = language == UiLanguage.Russian ? "ru" : "en";
                File.WriteAllText(FilePath, LanguagePrefix + value + Environment.NewLine);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryParse(string content, out UiLanguage language)
        {
            language = UiLanguage.English;
            if (string.IsNullOrWhiteSpace(content))
                return false;

            string[] lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (!line.StartsWith(LanguagePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = line.Substring(LanguagePrefix.Length).Trim();
                if (string.Equals(value, "en", StringComparison.OrdinalIgnoreCase))
                {
                    language = UiLanguage.English;
                    return true;
                }

                if (string.Equals(value, "ru", StringComparison.OrdinalIgnoreCase))
                {
                    language = UiLanguage.Russian;
                    return true;
                }

                return false;
            }

            return false;
        }
    }
}
