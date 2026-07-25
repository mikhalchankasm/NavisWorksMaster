using System;
using System.Drawing;

namespace NavisHelper.Core
{
    public static class ColorParser
    {
        /// <summary>
        /// Парсит строку цвета (#AARRGGBB, #RRGGBB, RAL 3000, 3000, r,g,b) в System.Drawing.Color
        /// </summary>
        /// <param name="colorString">Строка цвета</param>
        /// <returns>Color</returns>
        /// <exception cref="ArgumentException">Если строка невалидна</exception>
        public static Color ParseColor(string colorString)
        {
            if (string.IsNullOrWhiteSpace(colorString))
                throw new ArgumentException("Color string is null or empty");

            colorString = colorString.Trim();
            if (colorString.StartsWith("#"))
            {
                string hex = colorString.TrimStart('#');
                if (hex.Length == 8) // AARRGGBB
                {
                    int a = Convert.ToInt32(hex.Substring(0, 2), 16);
                    int r = Convert.ToInt32(hex.Substring(2, 2), 16);
                    int g = Convert.ToInt32(hex.Substring(4, 2), 16);
                    int b = Convert.ToInt32(hex.Substring(6, 2), 16);
                    return Color.FromArgb(a, r, g, b);
                }
                else if (hex.Length == 6) // RRGGBB
                {
                    int r = Convert.ToInt32(hex.Substring(0, 2), 16);
                    int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                    int b = Convert.ToInt32(hex.Substring(4, 2), 16);
                    return Color.FromArgb(r, g, b);
                }
                else
                {
                    throw new ArgumentException($"Некорректный hex-цвет: {colorString}");
                }
            }

            var ralCandidate = colorString;
            if (colorString.StartsWith("RAL", StringComparison.OrdinalIgnoreCase))
                ralCandidate = colorString.Substring(3).Trim().TrimStart('-', '_').Trim();

            if (colorString.StartsWith("RAL", StringComparison.OrdinalIgnoreCase) ||
                (colorString.IndexOf(',') < 0 && RalColors.Values.ContainsKey(ralCandidate)))
            {
                if (RalColors.TryGet(ralCandidate, out var ralColor))
                    return Color.FromArgb(ralColor.R, ralColor.G, ralColor.B);

                throw new ArgumentException($"Неизвестный RAL-код: {ralCandidate}");
            }

            if (colorString.IndexOf(',') < 0)
                throw new ArgumentException($"Некорректный rgb-цвет: {colorString}");

            var rgb = colorString.Split(',');
            if (rgb.Length < 3)
                throw new ArgumentException($"Некорректный rgb-цвет: {colorString}");
            if (!int.TryParse(rgb[0].Trim(), out int rgbR) ||
                !int.TryParse(rgb[1].Trim(), out int rgbG) ||
                !int.TryParse(rgb[2].Trim(), out int rgbB))
                throw new ArgumentException($"Некорректный rgb-цвет: {colorString}");
            return Color.FromArgb(rgbR, rgbG, rgbB);
        }
    }
}
