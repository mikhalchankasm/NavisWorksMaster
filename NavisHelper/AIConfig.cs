using System;
using System.Globalization;
using System.IO;
using System.Text;

using NavisHelper.Core;

namespace NavisHelper
{
    /// <summary>
    /// Конфигурация для ИИ-сервиса
    /// </summary>
    /// <summary>
    /// Доступные AI-модели через OpenRouter
    /// </summary>
    public static class AIModels
    {
        public static readonly (string DisplayName, string FullId, bool SupportsThinking)[] Available = new[]
        {
            ("claude-sonnet-4.6",  "anthropic/claude-sonnet-4.6",      true),
            ("claude-opus-4.6",    "anthropic/claude-opus-4.6",        true),
            ("glm-5-turbo",        "z-ai/glm-5-turbo",                false),
            ("gpt-5.4",            "openai/gpt-5.4",                  false),
            ("gemini-3-flash",     "google/gemini-3-flash-preview",    true),
        };

        public static string GetFullId(string displayName)
        {
            foreach (var m in Available)
                if (m.DisplayName == displayName) return m.FullId;
            return Available[0].FullId;
        }

        public static bool SupportsThinking(string displayName)
        {
            foreach (var m in Available)
                if (m.DisplayName == displayName) return m.SupportsThinking;
            return false;
        }
    }

    public class AIConfig
    {
        private static AIConfig _instance;
        private static readonly object _lock = new object();
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NavisHelper",
            "ai_config.json"
        );

        public string ApiKey { get; set; }
        public string ApiUrl { get; set; }
        public int RequestTimeout { get; set; }
        public int MaxRetries { get; set; }
        public bool EnableLogging { get; set; }
        public string ModelName { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }

        /// <summary>
        /// Текущая цветовая схема (1-10)
        /// </summary>
        public int ColorScheme { get; set; }

        /// <summary>
        /// Включить режим thinking (extended thinking) для поддерживающих моделей
        /// </summary>
        public bool EnableThinking { get; set; }

        private AIConfig()
        {
            // Значения по умолчанию
            ApiKey = Environment.GetEnvironmentVariable("OPEN_ROUTER_NW_KEY") ?? "";
            ApiUrl = "https://openrouter.ai/api/v1/chat/completions";
            RequestTimeout = 60000; // 60 секунд (thinking может быть долгим)
            MaxRetries = 2;
            EnableLogging = true;
            ModelName = "claude-sonnet-4.6";
            Temperature = 0.3;
            MaxTokens = 2000;
            ColorScheme = 8; // Architectural по умолчанию
            EnableThinking = true;
        }

        public static AIConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = LoadConfig();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Загружает конфигурацию из файла
        /// </summary>
        private static AIConfig LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var config = AIConfig.ParseConfigFromJson(json);
                    
                    // Always read API key from environment variable, never from file
                    config.ApiKey = Environment.GetEnvironmentVariable("OPEN_ROUTER_NW_KEY") ?? "";

                    return config;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка загрузки конфигурации: {ex.Message}", "AIConfig");
            }

            // Возвращаем конфигурацию по умолчанию
            var defaultConfig = new AIConfig();
            defaultConfig.SaveConfig();
            return defaultConfig;
        }

        /// <summary>
        /// Сохраняет конфигурацию в файл
        /// </summary>
        public void SaveConfig()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = SerializeConfigToJson();
                File.WriteAllText(ConfigPath, json);
                
                Logger.Info("Конфигурация ИИ-сервиса сохранена", "AIConfig");
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка сохранения конфигурации: {ex.Message}", "AIConfig");
            }
        }

        /// <summary>
        /// Обновляет API ключ
        /// </summary>
        public void UpdateApiKey(string newApiKey)
        {
            ApiKey = newApiKey;
            SaveConfig();
            Logger.Info("API ключ обновлен", "AIConfig");
        }

        /// <summary>
        /// Проверяет валидность конфигурации
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(ApiKey) && 
                   !string.IsNullOrEmpty(ApiUrl) && 
                   RequestTimeout > 0 && 
                   MaxRetries > 0;
        }

        /// <summary>
        /// Сбрасывает конфигурацию к значениям по умолчанию
        /// </summary>
        public void ResetToDefaults()
        {
            ApiKey = Environment.GetEnvironmentVariable("OPEN_ROUTER_NW_KEY") ?? "";
            ApiUrl = "https://openrouter.ai/api/v1/chat/completions";
            RequestTimeout = 60000;
            MaxRetries = 2;
            EnableLogging = true;
            ModelName = "claude-sonnet-4.6";
            Temperature = 0.3;
            MaxTokens = 2000;
            ColorScheme = 8;
            EnableThinking = true;

            SaveConfig();
            Logger.Info("Конфигурация сброшена к значениям по умолчанию", "AIConfig");
        }

        private static AIConfig ParseConfigFromJson(string json)
        {
            try
            {
                var lines = json.Split('\n');
                var config = new AIConfig();
                
                foreach (var line in lines)
                {
                    if (line.Contains("\"ApiUrl\""))
                        config.ApiUrl = ExtractValue(line);
                    else if (line.Contains("\"RequestTimeout\""))
                    {
                        int value;
                        if (int.TryParse(ExtractValue(line), out value))
                            config.RequestTimeout = value;
                    }
                    else if (line.Contains("\"MaxRetries\""))
                    {
                        int value;
                        if (int.TryParse(ExtractValue(line), out value))
                            config.MaxRetries = value;
                    }
                    else if (line.Contains("\"ModelName\""))
                        config.ModelName = ExtractValue(line);
                    else if (line.Contains("\"Temperature\""))
                    {
                        double value;
                        if (double.TryParse(ExtractValue(line), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                            double.TryParse(ExtractValue(line), NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                            config.Temperature = value;
                    }
                    else if (line.Contains("\"MaxTokens\""))
                    {
                        int value;
                        if (int.TryParse(ExtractValue(line), out value))
                            config.MaxTokens = value;
                    }
                    else if (line.Contains("\"ColorScheme\""))
                    {
                        int value;
                        if (int.TryParse(ExtractValue(line), out value))
                            config.ColorScheme = Math.Max(1, Math.Min(14, value));
                    }
                    else if (line.Contains("\"EnableThinking\""))
                    {
                        var val = ExtractValue(line).ToLowerInvariant();
                        config.EnableThinking = val == "true";
                    }
                }
                
                return config;
            }
            catch
            {
                return new AIConfig();
            }
        }

        private string SerializeConfigToJson()
        {
            return $@"{{
  ""ApiUrl"": ""{EscapeJsonString(ApiUrl)}"",
  ""RequestTimeout"": {RequestTimeout},
  ""MaxRetries"": {MaxRetries},
  ""EnableLogging"": {EnableLogging.ToString().ToLower()},
  ""ModelName"": ""{EscapeJsonString(ModelName)}"",
  ""Temperature"": {Temperature.ToString(CultureInfo.InvariantCulture)},
  ""MaxTokens"": {MaxTokens},
  ""ColorScheme"": {ColorScheme},
  ""EnableThinking"": {EnableThinking.ToString().ToLower()}
}}";
        }
        
        /// <summary>
        /// Устанавливает цветовую схему (1-10)
        /// </summary>
        public void SetColorScheme(int scheme)
        {
            ColorScheme = Math.Max(1, Math.Min(14, scheme));
            SaveConfig();
            Logger.Info($"Цветовая схема изменена на {ColorScheme}: {ColorSchemes.GetSchemeNameRu((ColorSchemeType)ColorScheme)}", "AIConfig");
        }
        
        /// <summary>
        /// Получает текущую цветовую схему
        /// </summary>
        public ColorSchemeType GetColorSchemeType()
        {
            return (ColorSchemeType)Math.Max(1, Math.Min(14, ColorScheme));
        }

        private static string ExtractValue(string line)
        {
            var match = System.Text.RegularExpressions.Regex.Match(line, @""":\s*""?([^""\s,}]+)""?");
            return match.Success ? match.Groups[1].Value : "";
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length + 16);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (char.IsControl(ch))
                            sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else
                            sb.Append(ch);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
