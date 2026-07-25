using System;
using System.IO;

namespace NavisHelper.Core
{
    /// <summary>Настройки section box по выделению — сохраняются между сессиями.</summary>
    public class SelectionBoxSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NavisHelper", "selection_box_settings.ini");

        public double OffsetMm { get; set; } = 1000;
        public bool UseSectionBox { get; set; } = true;
        public bool UseContextTransparency { get; set; } = false;
        public double TransparencyPercent { get; set; } = 70;

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var lines = new[]
                {
                    $"OffsetMm={OffsetMm}",
                    $"UseSectionBox={UseSectionBox}",
                    $"UseContextTransparency={UseContextTransparency}",
                    $"TransparencyPercent={TransparencyPercent}"
                };
                File.WriteAllLines(SettingsPath, lines);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save Selection Box settings: " + ex.Message, "SelectionBoxSettings");
            }
        }

        public static SelectionBoxSettings Load()
        {
            var s = new SelectionBoxSettings();
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
                        case "OffsetMm": double.TryParse(val, out var o); s.OffsetMm = o; break;
                        case "UseSectionBox": bool.TryParse(val, out var sb); s.UseSectionBox = sb; break;
                        case "UseContextTransparency": bool.TryParse(val, out var ct); s.UseContextTransparency = ct; break;
                        case "TransparencyPercent": double.TryParse(val, out var tp); s.TransparencyPercent = tp; break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load Selection Box settings: " + ex.Message, "SelectionBoxSettings");
            }
            return s;
        }
    }
}
