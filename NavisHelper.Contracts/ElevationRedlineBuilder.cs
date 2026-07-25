using System;
using System.Collections.Generic;
using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    public static class ElevationRedlineBuilder
    {
        public const string LabelMode = "label";
        public const string DimensionMode = "dimension";

        public static ElevationRedlineBuildResult Build(
            string mode,
            IReadOnlyList<ElevationProjectedItem> items,
            double cameraUnitsPerPixelX,
            double cameraUnitsPerPixelY)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            if (items.Count == 0)
                throw new ArgumentException("At least one projected item is required.", nameof(items));
            if (!IsFinite(cameraUnitsPerPixelX) || cameraUnitsPerPixelX <= 0)
                throw new ArgumentOutOfRangeException(nameof(cameraUnitsPerPixelX));
            if (!IsFinite(cameraUnitsPerPixelY) || cameraUnitsPerPixelY <= 0)
                throw new ArgumentOutOfRangeException(nameof(cameraUnitsPerPixelY));

            var normalizedMode = string.IsNullOrWhiteSpace(mode)
                ? LabelMode
                : mode.Trim().ToLowerInvariant();
            if (normalizedMode != LabelMode && normalizedMode != DimensionMode)
                throw new ArgumentException("mode must be label or dimension.", nameof(mode));

            var lines = new List<string>();
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                ValidateItem(item, index);
                if (normalizedMode == DimensionMode)
                    AddDimension(lines, item, cameraUnitsPerPixelX, cameraUnitsPerPixelY);
                else
                    AddLabelCallout(lines, item, cameraUnitsPerPixelX, cameraUnitsPerPixelY);
            }

            return new ElevationRedlineBuildResult
            {
                PrimitiveCount = lines.Count,
                Json = "{\"Type\":\"RedlineCollection\",\"Version\":1,\"Values\":[" +
                    string.Join(",", lines) + "]}",
            };
        }

        public static ElevationRedlineBuildResult BuildNativeText(
            string mode,
            IReadOnlyList<ElevationProjectedItem> items,
            double cameraUnitsPerPixelX,
            double cameraUnitsPerPixelY,
            int thickness = 10)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            if (items.Count == 0)
                throw new ArgumentException("At least one projected item is required.", nameof(items));
            if (!IsFinite(cameraUnitsPerPixelX) || cameraUnitsPerPixelX <= 0)
                throw new ArgumentOutOfRangeException(nameof(cameraUnitsPerPixelX));
            if (!IsFinite(cameraUnitsPerPixelY) || cameraUnitsPerPixelY <= 0)
                throw new ArgumentOutOfRangeException(nameof(cameraUnitsPerPixelY));
            if (thickness < 1)
                throw new ArgumentOutOfRangeException(nameof(thickness));

            var normalizedMode = string.IsNullOrWhiteSpace(mode)
                ? LabelMode
                : mode.Trim().ToLowerInvariant();
            if (normalizedMode != LabelMode && normalizedMode != DimensionMode)
                throw new ArgumentException("mode must be label or dimension.", nameof(mode));

            var values = new List<string>();
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                ValidateItem(item, index);
                if (normalizedMode == DimensionMode)
                    AddNativeDimension(values, item, cameraUnitsPerPixelX, cameraUnitsPerPixelY, thickness);
                else
                    AddNativeLabelCallout(values, item, cameraUnitsPerPixelX, cameraUnitsPerPixelY, thickness);
            }

            return new ElevationRedlineBuildResult
            {
                PrimitiveCount = values.Count,
                Json = "{\"Type\":\"RedlineCollection\",\"Version\":1,\"Values\":[" +
                    string.Join(",", values) + "]}",
            };
        }

        private static void AddNativeLabelCallout(
            ICollection<string> values,
            ElevationProjectedItem item,
            double px,
            double py,
            int thickness)
        {
            AddGreenLine(values, item.TopX - 5 * px, item.TopY, item.TopX + 5 * px, item.TopY, thickness);
            AddGreenLine(values, item.TopX, item.TopY - 5 * py, item.TopX, item.TopY + 5 * py, thickness);

            var textX = item.TopX + 16 * px;
            var textY = item.TopY + 8 * py;
            AddGreenLine(values, item.TopX, item.TopY, textX - 3 * px, textY, thickness);
            AddNativeText(values, item.Label, textX, textY);
        }

        private static void AddNativeDimension(
            ICollection<string> values,
            ElevationProjectedItem item,
            double px,
            double py,
            int thickness)
        {
            var dx = item.BaseX - item.TopX;
            var dy = item.BaseY - item.TopY;
            var length = Math.Sqrt(dx * dx + dy * dy);
            double nx;
            double ny;
            if (length < Math.Max(px, py))
            {
                nx = 1;
                ny = 0;
            }
            else
            {
                nx = -dy / length;
                ny = dx / length;
            }

            AddGreenLine(values, item.TopX, item.TopY, item.BaseX, item.BaseY, thickness);
            AddGreenTick(values, item.TopX, item.TopY, nx, ny, px, py, thickness);
            AddGreenTick(values, item.BaseX, item.BaseY, nx, ny, px, py, thickness);

            var middleX = (item.TopX + item.BaseX) / 2.0;
            var middleY = (item.TopY + item.BaseY) / 2.0;
            var textX = middleX + nx * 14 * px;
            var textY = middleY + ny * 14 * py;
            AddGreenLine(
                values,
                middleX,
                middleY,
                textX - nx * 3 * px,
                textY - ny * 3 * py,
                thickness);
            AddNativeText(values, item.Label, textX, textY);
        }

        private static void AddGreenTick(
            ICollection<string> values,
            double x,
            double y,
            double nx,
            double ny,
            double px,
            double py,
            int thickness)
        {
            AddGreenLine(
                values,
                x - nx * 6 * px,
                y - ny * 6 * py,
                x + nx * 6 * px,
                y + ny * 6 * py,
                thickness);
        }

        private static void AddGreenLine(
            ICollection<string> values,
            double x1,
            double y1,
            double x2,
            double y2,
            int thickness)
        {
            AddLineWithColor(values, x1, y1, x2, y2, thickness, "[0,1,0]");
        }

        private static void AddNativeText(
            ICollection<string> values,
            string text,
            double x,
            double y)
        {
            var c = CultureInfo.InvariantCulture;
            values.Add(string.Format(
                c,
                "{{\"Type\":\"RedlineText\",\"Version\":1,\"Thickness\":1,\"Color\":[0,1,0],\"Origin\":[{0:G17},{1:G17}],\"Text\":\"{2}\"}}",
                x,
                y,
                EscapeJson(text)));
        }

        private static void AddLabelCallout(
            ICollection<string> lines,
            ElevationProjectedItem item,
            double px,
            double py)
        {
            AddLine(lines, item.TopX - 5 * px, item.TopY, item.TopX + 5 * px, item.TopY, 3);
            AddLine(lines, item.TopX, item.TopY - 5 * py, item.TopX, item.TopY + 5 * py, 3);

            var textX = item.TopX + 16 * px;
            var textY = item.TopY + 8 * py;
            AddLine(lines, item.TopX, item.TopY, textX - 3 * px, textY, 2);
            AddVectorText(lines, item.Label, textX, textY, px, py);
        }

        private static void AddDimension(
            ICollection<string> lines,
            ElevationProjectedItem item,
            double px,
            double py)
        {
            var dx = item.BaseX - item.TopX;
            var dy = item.BaseY - item.TopY;
            var length = Math.Sqrt(dx * dx + dy * dy);
            double nx;
            double ny;
            if (length < Math.Max(px, py))
            {
                nx = 1;
                ny = 0;
            }
            else
            {
                nx = -dy / length;
                ny = dx / length;
            }

            AddLine(lines, item.TopX, item.TopY, item.BaseX, item.BaseY, 3);
            AddTick(lines, item.TopX, item.TopY, nx, ny, px, py);
            AddTick(lines, item.BaseX, item.BaseY, nx, ny, px, py);

            var middleX = (item.TopX + item.BaseX) / 2.0;
            var middleY = (item.TopY + item.BaseY) / 2.0;
            var textX = middleX + nx * 14 * px;
            var textY = middleY + ny * 14 * py;
            AddLine(lines, middleX, middleY, textX - nx * 3 * px, textY - ny * 3 * py, 2);
            AddVectorText(lines, item.Label, textX, textY, px, py);
        }

        private static void AddTick(
            ICollection<string> lines,
            double x,
            double y,
            double nx,
            double ny,
            double px,
            double py)
        {
            AddLine(
                lines,
                x - nx * 6 * px,
                y - ny * 6 * py,
                x + nx * 6 * px,
                y + ny * 6 * py,
                3);
        }

        private static void AddVectorText(
            ICollection<string> lines,
            string text,
            double originX,
            double originY,
            double px,
            double py)
        {
            var cursor = originX;
            var glyphWidth = 7 * px;
            // Redline camera space uses positive Y upward. Keep the glyph origin at
            // its visual top edge and draw downward so digits and M are not mirrored.
            var glyphHeight = -12 * py;
            var spacing = 2 * px;
            foreach (var rawCharacter in text)
            {
                var character = char.ToUpperInvariant(rawCharacter);
                AddGlyph(lines, character, cursor, originY, glyphWidth, glyphHeight);
                cursor += glyphWidth + spacing;
            }
        }

        private static void AddGlyph(
            ICollection<string> lines,
            char character,
            double x,
            double y,
            double width,
            double height)
        {
            if (character >= '0' && character <= '9')
            {
                var masks = new[]
                {
                    "ABCDEF", "BC", "ABDEG", "ABCDG", "BCFG",
                    "ACDFG", "ACDEFG", "ABC", "ABCDEFG", "ABCDFG",
                };
                AddSegments(lines, masks[character - '0'], x, y, width, height);
                return;
            }

            switch (character)
            {
                case 'Z':
                    AddStroke(lines, x, y, x + width, y, 2);
                    AddStroke(lines, x + width, y, x, y + height, 2);
                    AddStroke(lines, x, y + height, x + width, y + height, 2);
                    return;
                case 'M':
                    AddStroke(lines, x, y + height, x, y, 2);
                    AddStroke(lines, x, y, x + width / 2, y + height * 0.55, 2);
                    AddStroke(lines, x + width / 2, y + height * 0.55, x + width, y, 2);
                    AddStroke(lines, x + width, y, x + width, y + height, 2);
                    return;
                case '=':
                    AddStroke(lines, x, y + height * 0.38, x + width, y + height * 0.38, 2);
                    AddStroke(lines, x, y + height * 0.68, x + width, y + height * 0.68, 2);
                    return;
                case '+':
                    AddStroke(lines, x, y + height / 2, x + width, y + height / 2, 2);
                    AddStroke(lines, x + width / 2, y + height * 0.2, x + width / 2, y + height * 0.8, 2);
                    return;
                case '-':
                    AddStroke(lines, x, y + height / 2, x + width, y + height / 2, 2);
                    return;
                case '.':
                    AddStroke(lines, x + width * 0.42, y + height, x + width * 0.58, y + height, 3);
                    return;
                case ' ':
                    return;
                default:
                    throw new ArgumentException("Unsupported vector-label character: " + character + ".");
            }
        }

        private static void AddSegments(
            ICollection<string> lines,
            string segments,
            double x,
            double y,
            double width,
            double height)
        {
            var middle = y + height / 2;
            foreach (var segment in segments)
            {
                switch (segment)
                {
                    case 'A': AddStroke(lines, x, y, x + width, y, 2); break;
                    case 'B': AddStroke(lines, x + width, y, x + width, middle, 2); break;
                    case 'C': AddStroke(lines, x + width, middle, x + width, y + height, 2); break;
                    case 'D': AddStroke(lines, x, y + height, x + width, y + height, 2); break;
                    case 'E': AddStroke(lines, x, middle, x, y + height, 2); break;
                    case 'F': AddStroke(lines, x, y, x, middle, 2); break;
                    case 'G': AddStroke(lines, x, middle, x + width, middle, 2); break;
                }
            }
        }

        private static void AddStroke(
            ICollection<string> lines,
            double x1,
            double y1,
            double x2,
            double y2,
            int thickness)
        {
            AddLine(lines, x1, y1, x2, y2, thickness);
        }

        private static void AddLine(
            ICollection<string> lines,
            double x1,
            double y1,
            double x2,
            double y2,
            int thickness)
        {
            AddLineWithColor(lines, x1, y1, x2, y2, thickness, "[1,0,0]");
        }

        private static void AddLineWithColor(
            ICollection<string> lines,
            double x1,
            double y1,
            double x2,
            double y2,
            int thickness,
            string colorJson)
        {
            var c = CultureInfo.InvariantCulture;
            lines.Add(string.Format(
                c,
                "{{\"Type\":\"RedlineLine\",\"Version\":1,\"Thickness\":{0},\"Color\":{1},\"Start\":[{2:G17},{3:G17}],\"End\":[{4:G17},{5:G17}]}}",
                thickness,
                colorJson,
                x1,
                y1,
                x2,
                y2));
        }

        private static string EscapeJson(string text)
        {
            if (text == null)
                return string.Empty;

            return text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static void ValidateItem(ElevationProjectedItem item, int index)
        {
            if (item == null)
                throw new ArgumentException("Projected item at index " + index + " is missing.");
            if (!IsFinite(item.TopX) ||
                !IsFinite(item.TopY) ||
                !IsFinite(item.BaseX) ||
                !IsFinite(item.BaseY))
            {
                throw new ArgumentException("Projected item at index " + index + " is not finite.");
            }
            if (string.IsNullOrWhiteSpace(item.Label))
                throw new ArgumentException("Projected item at index " + index + " has no label.");
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public sealed class ElevationProjectedItem
    {
        public double TopX { get; set; }
        public double TopY { get; set; }
        public double BaseX { get; set; }
        public double BaseY { get; set; }
        public string Label { get; set; }
    }

    public sealed class ElevationRedlineBuildResult
    {
        public string Json { get; set; }
        public int PrimitiveCount { get; set; }
    }
}
