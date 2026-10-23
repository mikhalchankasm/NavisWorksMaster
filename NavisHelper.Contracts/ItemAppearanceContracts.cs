using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ItemAppearanceRequest
    {
        public List<string> MatchHandles { get; set; } = new List<string>();
        public bool? UseSelection { get; set; }
        public int? MaxItems { get; set; }
    }

    public sealed class ItemAppearanceInfo
    {
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public string OriginalColorHex { get; set; }
        public int OriginalTransparency { get; set; }
        public string CurrentColorHex { get; set; }
        public int CurrentTransparency { get; set; }
        public bool ColorOverridden { get; set; }
        public bool TransparencyOverridden { get; set; }
    }

    public sealed class ItemAppearanceResponse
    {
        public int ItemCount { get; set; }
        public int OverriddenItemCount { get; set; }
        public bool Truncated { get; set; }
        public List<ItemAppearanceInfo> Items { get; set; } = new List<ItemAppearanceInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ResetAppearanceRequest
    {
        public List<string> MatchHandles { get; set; } = new List<string>();
        public bool? UseSelection { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class ResetAppearanceResponse
    {
        public bool Apply { get; set; }
        public int ItemCount { get; set; }
        public int OverriddenItemCount { get; set; }
        public int ResetItemCount { get; set; }
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public static class ItemAppearanceRules
    {
        public static int ToPercent(double transparency)
        {
            return (int)Math.Round(Math.Max(0, Math.Min(1, transparency)) * 100, MidpointRounding.AwayFromZero);
        }

        public static string ToHex(double r, double g, double b)
        {
            return "#" + ColorByte(r) + ColorByte(g) + ColorByte(b);
        }

        public static bool ColorDiffers(string a, string b)
        {
            return !string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static string DescribeTargetError(IList<string> handles, bool? useSelection)
        {
            var hasHandles = handles != null && handles.Any(handle => !string.IsNullOrWhiteSpace(handle));
            if (hasHandles && useSelection == true)
                return "Pass matchHandles or useSelection=true, not both.";
            if (!hasHandles && useSelection != true)
                return "Pass matchHandles or useSelection=true.";
            return null;
        }

        public static int ClampMaxItems(int? value)
        {
            return Math.Max(1, Math.Min(500, value ?? 50));
        }

        private static string ColorByte(double value)
        {
            return ((int)Math.Round(Math.Max(0, Math.Min(1, value)) * 255, MidpointRounding.AwayFromZero))
                .ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}
