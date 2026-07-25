using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Parses the redline subset supported by saved_viewpoints_import without depending on Navisworks runtime types.
    /// </summary>
    public static class SavedViewpointRedlineXmlParser
    {
        public static SavedViewpointRedlineParseResult Parse(XElement viewElement)
        {
            var result = new SavedViewpointRedlineParseResult();
            var redlines = viewElement == null
                ? null
                : viewElement.Descendants().FirstOrDefault(element => IsElement(element, "redlines"));
            if (redlines == null)
                return result;

            foreach (var redline in redlines.Elements())
            {
                if (IsElement(redline, "rlellipse"))
                {
                    var start = ReadPos2(redline, "start");
                    var end = ReadPos2(redline, "end");
                    if (start == null || end == null)
                    {
                        result.Warnings.Add("Skipped ellipse redline without start/end coordinates.");
                        continue;
                    }

                    var color = ReadColor(redline, false);
                    result.Redlines.Add(CreateDefinition(
                        RedlineTypeWhitelistHelper.Ellipse,
                        ReadIntAttribute(redline, "thickness", 3),
                        false,
                        color,
                        start,
                        end));
                    continue;
                }

                if (IsElement(redline, "rlarrow"))
                {
                    var start = ReadPos2(redline, "start");
                    var end = ReadPos2(redline, "end");
                    if (start == null || end == null)
                    {
                        result.Warnings.Add("Skipped arrow redline without start/end coordinates.");
                        continue;
                    }

                    var thickness = ReadIntAttribute(redline, "thickness", 3);
                    var color = ReadColor(redline, true);
                    foreach (var line in ArrowLineGeometryHelper.Build(start[0], start[1], end[0], end[1]))
                    {
                        result.Redlines.Add(CreateDefinition(
                            RedlineTypeWhitelistHelper.Line,
                            thickness,
                            true,
                            color,
                            new[] { line.StartX, line.StartY },
                            new[] { line.EndX, line.EndY }));
                    }

                    continue;
                }

                if (IsElement(redline, "rlline"))
                {
                    var start = ReadPos2(redline, "start");
                    var end = ReadPos2(redline, "end");
                    if (start == null || end == null)
                    {
                        result.Warnings.Add("Skipped line redline without start/end coordinates.");
                        continue;
                    }

                    var color = ReadColor(redline, true);
                    result.Redlines.Add(CreateDefinition(
                        RedlineTypeWhitelistHelper.Line,
                        ReadIntAttribute(redline, "thickness", 3),
                        true,
                        color,
                        start,
                        end));
                    continue;
                }

                result.Warnings.Add("Skipped unsupported redline node: " + redline.Name.LocalName);
            }

            return result;
        }

        private static SavedViewpointRedlineDefinition CreateDefinition(
            string type,
            int thickness,
            bool integerColor,
            double[] color,
            double[] start,
            double[] end)
        {
            return new SavedViewpointRedlineDefinition
            {
                Type = type,
                Thickness = thickness,
                IntegerColor = integerColor,
                Red = color[0],
                Green = color[1],
                Blue = color[2],
                StartX = start[0],
                StartY = start[1],
                EndX = end[0],
                EndY = end[1],
            };
        }

        private static bool IsElement(XElement element, string localName)
        {
            return element != null && string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
        }

        private static XElement ChildElement(XElement element, string localName)
        {
            return element == null
                ? null
                : element.Elements().FirstOrDefault(child => IsElement(child, localName));
        }

        private static string ReadStringAttribute(XElement element, string name)
        {
            var attribute = element == null ? null : element.Attribute(name);
            return attribute == null ? string.Empty : attribute.Value ?? string.Empty;
        }

        private static double ReadDoubleAttribute(XElement element, string name, double fallback)
        {
            var value = ReadStringAttribute(element, name);
            double parsed;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static int ReadIntAttribute(XElement element, string name, int fallback)
        {
            var value = ReadStringAttribute(element, name);
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static double[] ReadPos2(XElement redline, string containerName)
        {
            var container = ChildElement(redline, containerName);
            var pos = container == null ? null : container.Descendants().FirstOrDefault(element => IsElement(element, "pos2f"));
            if (pos == null)
                return null;

            return new[]
            {
                ReadDoubleAttribute(pos, "x", 0),
                ReadDoubleAttribute(pos, "y", 0),
            };
        }

        private static double[] ReadColor(XElement redline, bool integerColor)
        {
            var color = ChildElement(redline, "colour") ?? ChildElement(redline, "color");
            if (color == null)
                return new[] { 1.0, 0.0, 0.0 };

            var red = ReadDoubleAttribute(color, "red", 1);
            var green = ReadDoubleAttribute(color, "green", 0);
            var blue = ReadDoubleAttribute(color, "blue", 0);
            if (integerColor)
            {
                return new[]
                {
                    red >= 0.5 ? 1.0 : 0.0,
                    green >= 0.5 ? 1.0 : 0.0,
                    blue >= 0.5 ? 1.0 : 0.0,
                };
            }

            return new[] { red, green, blue };
        }
    }

    public sealed class SavedViewpointRedlineParseResult
    {
        public List<SavedViewpointRedlineDefinition> Redlines { get; } = new List<SavedViewpointRedlineDefinition>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public sealed class SavedViewpointRedlineDefinition
    {
        public string Type { get; set; }
        public int Thickness { get; set; }
        public bool IntegerColor { get; set; }
        public double Red { get; set; }
        public double Green { get; set; }
        public double Blue { get; set; }
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
    }
}
