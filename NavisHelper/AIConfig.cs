using System;
using System.IO;
using System.Threading.Tasks;

using NavisHelper.AI;
using NavisHelper.Core;

namespace NavisHelper
{
    internal sealed class AIConfigFilePersistence :
        IAIConfigSnapshotPersistence
    {
        private readonly string _configPath;

        internal AIConfigFilePersistence(string configPath)
        {
            _configPath = configPath ??
                          throw new ArgumentNullException(nameof(configPath));
        }

        public void Save(AIConfigSnapshot snapshot)
        {
            try
            {
                var directory = Path.GetDirectoryName(_configPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = AIConfigJsonSerializer.Serialize(snapshot.ToData());
                File.WriteAllText(_configPath, json);
                Logger.Info(
                    "Конфигурация ИИ-сервиса сохранена",
                    "AIConfig");
            }
            catch (Exception ex)
            {
                Logger.Error(
                    $"Ошибка сохранения конфигурации: {ex.Message}",
                    "AIConfig");
            }
        }
    }

    public class AIConfig
    {
        private static AIConfig _instance;
        private static readonly object InstanceLock = new object();
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NavisHelper",
            "ai_config.json");

        private readonly AIConfigRuntime _runtime;

        private AIConfig(AIConfigSnapshot initialState)
        {
            _runtime = new AIConfigRuntime(
                initialState,
                new AIConfigFilePersistence(ConfigPath));
        }

        public static AIConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (InstanceLock)
                    {
                        if (_instance == null)
                            _instance = LoadConfig();
                    }
                }
                return _instance;
            }
        }

        public string ModelName => CaptureSnapshot().ModelName;
        public double Temperature => CaptureSnapshot().Temperature;
        public int ColorScheme => CaptureSnapshot().ColorScheme;

        internal AIConfigSnapshot CaptureSnapshot()
        {
            return _runtime.Capture();
        }

        internal void UpdateModelNameRuntime(string modelName)
        {
            _runtime.UpdateModelName(modelName);
        }

        internal Task PersistLatestAsync()
        {
            return _runtime.PersistLatestAsync();
        }

        public void SaveConfig()
        {
            _runtime.PersistLatestAsync();
        }

        public void ResetToDefaults()
        {
            _runtime.Reset();
            _runtime.PersistLatestAsync();
            Logger.Info(
                "Конфигурация сброшена к значениям по умолчанию",
                "AIConfig");
        }

        public void SetColorScheme(int scheme)
        {
            var selectedScheme = _runtime.UpdateColorScheme(scheme);
            _runtime.PersistLatestAsync();
            Logger.Info(
                $"Цветовая схема изменена на {selectedScheme}: " +
                ColorSchemes.GetSchemeNameRu(
                    (ColorSchemeType)selectedScheme),
                "AIConfig");
        }

        public ColorSchemeType GetColorSchemeType()
        {
            return (ColorSchemeType)CaptureSnapshot().ColorScheme;
        }

        private static AIConfig LoadConfig()
        {
            var defaults = new AIConfigSnapshot(string.Empty, 0.3, 8);
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var data = AIConfigJsonSerializer.Parse(
                        json,
                        defaults.ToData());
                    return new AIConfig(new AIConfigSnapshot(
                        OpenRouterModelSelection.MigrationCandidate(
                            data.ModelName),
                        data.Temperature,
                        data.ColorScheme));
                }
            }
            catch (Exception ex)
            {
                Logger.Error(
                    $"Ошибка загрузки конфигурации: {ex.Message}",
                    "AIConfig");
            }

            var defaultConfig = new AIConfig(defaults);
            defaultConfig.SaveConfig();
            return defaultConfig;
        }
    }
}
