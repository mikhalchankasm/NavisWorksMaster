using System.Collections.Generic;
using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    public sealed class SetAppearanceStep
    {
        public string SetPath { get; set; }
        public string ColorHex { get; set; }
        public int? Transparency { get; set; }
        public bool? Hide { get; set; }
    }

    public sealed class SelectionSetsApplyAppearanceRequest
    {
        public List<SetAppearanceStep> Steps { get; set; } = new List<SetAppearanceStep>();
        public bool? Apply { get; set; }
    }

    public sealed class SetAppearanceStepResult
    {
        public string SetPath { get; set; }
        public bool Resolved { get; set; }
        public int ItemCount { get; set; }
        public string ColorHex { get; set; }
        public int? Transparency { get; set; }
        public bool Hide { get; set; }
        public string Error { get; set; }
    }

    public sealed class SelectionSetsApplyAppearanceResponse
    {
        public bool Apply { get; set; }
        public bool Applied { get; set; }
        public int AppliedStepCount { get; set; }
        public List<SetAppearanceStepResult> Steps { get; set; } = new List<SetAppearanceStepResult>();
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public static class SetAppearanceStepRules
    {
        public const int MaxSteps = 200;

        public static string Describe(SetAppearanceStep step)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.SetPath))
                return "setPath is required.";
            if (step.ColorHex == null && !step.Transparency.HasValue && step.Hide != true)
                return "Each step needs colorHex, transparency or hide=true.";
            double r, g, b;
            if (step.ColorHex != null && !TryParseColorHex(step.ColorHex, out r, out g, out b))
                return "colorHex must be #RRGGBB.";
            if (step.Transparency.HasValue && (step.Transparency.Value < 0 || step.Transparency.Value > 100))
                return "transparency must be a whole percent 0-100.";
            return null;
        }

        public static string DescribeSteps(IList<SetAppearanceStep> steps)
        {
            if (steps == null || steps.Count == 0)
                return "Pass at least one of steps.";
            if (steps.Count > MaxSteps)
                return "At most 200 steps per call.";
            return null;
        }

        public static bool TryParseColorHex(string hex, out double r, out double g, out double b)
        {
            r = g = b = 0;
            if (hex == null || hex.Length != 7 || hex[0] != '#')
                return false;
            for (var i = 1; i < hex.Length; i++)
            {
                var c = hex[i];
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F'))
                    return false;
            }
            r = int.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            g = int.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            b = int.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return true;
        }
    }
}
