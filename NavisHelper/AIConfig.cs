using System;
using System.IO;
using System.Threading.Tasks;

using NavisHelper.AI;
using NavisHelper.Agent.Contracts;
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

        internal AIConfigSnapshot Load(AIConfigSnapshot defaults)
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var data = AIConfigJsonSerializer.Parse(
                        File.ReadAllText(_configPath), defaults.ToData());
                    return new AIConfigSnapshot(
                        OpenRouterModelSelection.MigrationCandidate(data.ModelName),
                        data.Temperature, data.ColorScheme);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(
                    $"Ошибка загрузки конфигурации: {ex.Message}",
                    "AIConfig");
            }
            // A transient read failure must never schedule a defaults overwrite.
            return defaults;
        }

        public void Save(AIConfigSnapshot snapshot)
        {
            try
            {
                var json = AIConfigJsonSerializer.Serialize(snapshot.ToData());
                VerifiedFileArtifactWriter.WriteUtf8(_configPath, json, true);
                Logger.Info(
                    "Конфигурация ИИ-сервиса сохранена",
                    "AIConfig");
            }
            catch (Exception ex)
            {
                Logger.Error(
                    $"Ошибка сохранения конфигурации: {ex.Message}",
                    "AIConfig");
                throw;
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
            _ = PersistInBackgroundAsync();
        }

        public void ResetToDefaults()
        {
            _runtime.Reset();
            _ = PersistInBackgroundAsync();
            Logger.Info(
                "Конфигурация сброшена к значениям по умолчанию",
                "AIConfig");
        }

        public void SetColorScheme(int scheme)
        {
            var selectedScheme = _runtime.UpdateColorScheme(scheme);
            _ = PersistInBackgroundAsync();
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

        private async Task PersistInBackgroundAsync()
        {
            try
            {
                await _runtime.PersistLatestAsync().ConfigureAwait(false);
            }
            catch
            {
                // File persistence logged the error. Observe failures for legacy
                // void callers; PersistLatestAsync still reports them to awaiters.
            }
        }

        private static AIConfig LoadConfig()
        {
            var defaults = new AIConfigSnapshot(string.Empty, 0.3, 8);
            return new AIConfig(new AIConfigFilePersistence(ConfigPath).Load(defaults));
        }
    }
}
