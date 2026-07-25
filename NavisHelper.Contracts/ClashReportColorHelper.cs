using System;
using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashRgbColor
    {
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
    }

    public static class ClashReportColorHelper
    {
        public static bool TryParseHexRgb(string text, out ClashRgbColor color)
        {
            color = null;
            if (string.IsNullOrWhiteSpace(text))
                return true;

            var value = text.Trim();
            if (value.StartsWith("#", StringComparison.Ordinal))
                value = value.Substring(1);
            if (value.Length != 6)
                return false;

            int r;
            int g;
            int b;
            if (!int.TryParse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) ||
                !int.TryParse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) ||
                !int.TryParse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
            {
                return false;
            }

            color = new ClashRgbColor
            {
                R = (byte)r,
                G = (byte)g,
                B = (byte)b,
            };
            return true;
        }
    }
}
