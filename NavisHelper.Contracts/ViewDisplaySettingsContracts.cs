using System;
using System.Collections.Generic;
using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ViewBackgroundSettings
    {
        public string Mode { get; set; }
        public string[] Colors { get; set; }
    }

    public sealed class ViewDisplaySettingsRequest
    {
        public ViewBackgroundSettings Background { get; set; }
        public string Lighting { get; set; }
        public string RenderStyle { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class ViewDisplaySettingsState
    {
        public string Lighting { get; set; }
        public string RenderStyle { get; set; }
        public bool SupportsHorizon { get; set; }
        public bool BackgroundReadbackAvailable { get; set; }
        public bool DocumentModified { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ViewDisplaySettingsResponse
    {
        public bool Apply { get; set; }
        public bool Applied { get; set; }
        public ViewDisplaySettingsRequest Requested { get; set; }
        public ViewDisplaySettingsState Before { get; set; }
        public ViewDisplaySettingsState After { get; set; }
        public bool BackgroundSetterCompleted { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public static class ViewDisplaySettingsValidation
    {
        public static ViewDisplaySettingsRequest Validate(ViewDisplaySettingsRequest request, bool supportsHorizon)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var result = new ViewDisplaySettingsRequest
            {
                Apply = request.Apply == true,
                Lighting = Normalize(request.Lighting, "lighting", "none", "scene_lights", "headlight", "full_lights"),
                RenderStyle = Normalize(request.RenderStyle, "renderStyle", "full_render", "preview", "shaded", "wireframe", "hidden_line"),
            };
            if (request.Background != null)
            {
                var mode = Normalize(request.Background.Mode, "background.mode", "plain", "graduated", "horizon");
                if (mode == null)
                    throw new ArgumentException("background.mode is required.");
                if (mode == "horizon" && !supportsHorizon)
                    throw new ArgumentException("Horizon background requires a perspective 3D view.");
                int count = mode == "plain" ? 1 : mode == "graduated" ? 2 : 4;
                if (request.Background.Colors == null || request.Background.Colors.Length != count)
                    throw new ArgumentException("background.colors must contain exactly " + count + " #RRGGBB colors for " + mode + ".");
                var colors = new string[count];
                for (int i = 0; i < count; i++)
                {
                    var color = request.Background.Colors[i];
                    uint rgb;
                    if (color == null || color.Length != 7 || color[0] != '#' ||
                        !uint.TryParse(color.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb))
                        throw new ArgumentException("Each background color must be exactly #RRGGBB (no alpha).");
                    colors[i] = color.ToUpperInvariant();
                }
                result.Background = new ViewBackgroundSettings { Mode = mode, Colors = colors };
            }
            if (result.Background == null && result.Lighting == null && result.RenderStyle == null)
                throw new ArgumentException("Supply at least one of background, lighting or renderStyle.");
            return result;
        }

        private static string Normalize(string value, string field, params string[] allowed)
        {
            if (value == null)
                return null;
            var normalized = value.Trim().ToLowerInvariant();
            if (Array.IndexOf(allowed, normalized) < 0)
                throw new ArgumentException(field + " must be one of: " + string.Join(", ", allowed) + ".");
            return normalized;
        }
    }
}
