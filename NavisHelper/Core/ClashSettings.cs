using System;
using System.Globalization;
using System.IO;

namespace NavisHelper.Core
{
    /// <summary>Настройки превью коллизий — сохраняются между сессиями.</summary>
    public class ClashSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NavisHelper", "clash_settings.ini");

        public int ColorAIndex { get; set; } = 0;
        public int ColorBIndex { get; set; } = 1;
        public double OffsetMm { get; set; } = 1000;
        public string BoxMode { get; set; } = "point";
        public bool UseSectionBox { get; set; } = true;
        public bool UseContextTransparency { get; set; } = false;
        public bool UseFullBoxTransparencyForCapture { get; set; } = false;
        public bool UseRootContextTransparencyForViewpoints { get; set; } = false;
        public bool UseDualClashViewpoints { get; set; } = false;
        public bool UseGroupCenterMarkersForViewpoints { get; set; } = true;
        public bool UseResetViewpointForViewpoints { get; set; } = true;
        public bool CaptureAppearanceForViewpoints { get; set; } = true;
        public double TransparencyPercent { get; set; } = 70;
        public double TestGridHeightStar { get; set; } = 1;
        public double ClashAreaHeightStar { get; set; } = 2;
        public double ClashGroupPanelWidth { get; set; } = 360;
        public bool ClashSettingsExpanded { get; set; } = true;
        public bool ClashGroupPanelVisible { get; set; } = true;

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var lines = new[]
                {
                    $"ColorAIndex={ColorAIndex}",
                    $"ColorBIndex={ColorBIndex}",
                    $"OffsetMm={FormatDouble(OffsetMm)}",
                    $"BoxMode={BoxMode}",
                    $"UseSectionBox={UseSectionBox}",
                    $"UseContextTransparency={UseContextTransparency}",
                    $"UseFullBoxTransparencyForCapture={UseFullBoxTransparencyForCapture}",
                    $"UseRootContextTransparencyForViewpoints={UseRootContextTransparencyForViewpoints}",
                    $"UseDualClashViewpoints={UseDualClashViewpoints}",
                    $"UseGroupCenterMarkersForViewpoints={UseGroupCenterMarkersForViewpoints}",
                    $"UseResetViewpointForViewpoints={UseResetViewpointForViewpoints}",
                    $"CaptureAppearanceForViewpoints={CaptureAppearanceForViewpoints}",
                    $"TransparencyPercent={FormatDouble(TransparencyPercent)}",
                    $"TestGridHeightStar={FormatDouble(TestGridHeightStar)}",
                    $"ClashAreaHeightStar={FormatDouble(ClashAreaHeightStar)}",
                    $"ClashGroupPanelWidth={FormatDouble(ClashGroupPanelWidth)}",
                    $"ClashSettingsExpanded={ClashSettingsExpanded}",
                    $"ClashGroupPanelVisible={ClashGroupPanelVisible}"
                };
                File.WriteAllLines(SettingsPath, lines);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save Clash settings: " + ex.Message, "ClashSettings");
            }
        }

        public static ClashSettings Load()
        {
            var s = new ClashSettings();
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
                        case "ColorAIndex": int.TryParse(val, out var a); s.ColorAIndex = a; break;
                        case "ColorBIndex": int.TryParse(val, out var b); s.ColorBIndex = b; break;
                        case "OffsetMm": if (TryReadDouble(val, out var o)) s.OffsetMm = o; break;
                        case "BoxMode": s.BoxMode = string.Equals(val, "items", StringComparison.OrdinalIgnoreCase) ? "items" : "point"; break;
                        case "UseSectionBox": bool.TryParse(val, out var sb); s.UseSectionBox = sb; break;
                        case "UseContextTransparency": bool.TryParse(val, out var ct); s.UseContextTransparency = ct; break;
                        case "UseFullBoxTransparencyForCapture": bool.TryParse(val, out var fb); s.UseFullBoxTransparencyForCapture = fb; break;
                        case "UseRootContextTransparencyForViewpoints": bool.TryParse(val, out var rc); s.UseRootContextTransparencyForViewpoints = rc; break;
                        case "UseDualClashViewpoints": bool.TryParse(val, out var dv); s.UseDualClashViewpoints = dv; break;
                        case "UseGroupCenterMarkersForViewpoints": bool.TryParse(val, out var gm); s.UseGroupCenterMarkersForViewpoints = gm; break;
                        case "UseResetViewpointForViewpoints": bool.TryParse(val, out var rv); s.UseResetViewpointForViewpoints = rv; break;
                        case "CaptureAppearanceForViewpoints": bool.TryParse(val, out var ca); s.CaptureAppearanceForViewpoints = ca; break;
                        case "TransparencyPercent": if (TryReadDouble(val, out var tp)) s.TransparencyPercent = tp; break;
                        case "TestGridHeightStar": if (TryReadDouble(val, out var tgs)) s.TestGridHeightStar = tgs; break;
                        case "ClashAreaHeightStar": if (TryReadDouble(val, out var cgs)) s.ClashAreaHeightStar = cgs; break;
                        case "ClashGroupPanelWidth": if (TryReadDouble(val, out var gpw)) s.ClashGroupPanelWidth = gpw; break;
                        case "ClashSettingsExpanded": bool.TryParse(val, out var se); s.ClashSettingsExpanded = se; break;
                        case "ClashGroupPanelVisible": bool.TryParse(val, out var gv); s.ClashGroupPanelVisible = gv; break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load Clash settings: " + ex.Message, "ClashSettings");
            }
            return s;
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool TryReadDouble(string value, out double result)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return true;

            return double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        }
    }
}
