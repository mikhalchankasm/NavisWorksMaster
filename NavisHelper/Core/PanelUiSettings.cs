using System;
using System.IO;

namespace NavisHelper.Core
{
    /// <summary>UI-состояние панели NavisHelper (вкладка, сегменты) — сохраняется между сессиями.</summary>
    public class PanelUiSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NavisHelper", "panel_ui.ini");

        public int MainTabIndex { get; set; } = 0;
        public int ColorsSegment { get; set; } = 0;
        public int ViewsSegment { get; set; } = 0;

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var lines = new[]
                {
                    $"MainTabIndex={MainTabIndex}",
                    $"ColorsSegment={ColorsSegment}",
                    $"ViewsSegment={ViewsSegment}"
                };
                File.WriteAllLines(SettingsPath, lines);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save panel UI settings: " + ex.Message, "PanelUiSettings");
            }
        }

        public static PanelUiSettings Load()
        {
            var s = new PanelUiSettings();
            try
            {
                if (!File.Exists(SettingsPath)) return s;
                foreach (var line in File.ReadAllLines(SettingsPath))
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2) continue;
                    var key = parts[0].Trim();
                    var val = parts[1].Trim();

                    switch (key)
                    {
                        case "MainTabIndex": int.TryParse(val, out var t); s.MainTabIndex = t; break;
                        case "ColorsSegment": int.TryParse(val, out var c); s.ColorsSegment = c; break;
                        case "ViewsSegment": int.TryParse(val, out var v); s.ViewsSegment = v; break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load panel UI settings: " + ex.Message, "PanelUiSettings");
            }
            return s;
        }
    }
}
