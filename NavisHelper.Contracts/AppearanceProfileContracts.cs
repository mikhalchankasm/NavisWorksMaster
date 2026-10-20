using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class AppearanceProfileRuleInfo
    {
        public string Kind { get; set; }
        public string Category { get; set; }
        public string Property { get; set; }
        public string Condition { get; set; }
        public string Value { get; set; }
        public string SetName { get; set; }
        public string SetPath { get; set; }
        public string ColorHex { get; set; }
        public int Transparency { get; set; }
        public string ForceVisibility { get; set; }
    }

    public sealed class AppearanceProfileSummary
    {
        public string Name { get; set; }
        public int Version { get; set; }
        public List<AppearanceProfileRuleInfo> Rules { get; set; } = new List<AppearanceProfileRuleInfo>();
        public int SetRuleCount { get; set; }
        public int HiddenRuleCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public static class AppearanceProfileFile
    {
        public static AppearanceProfileSummary Parse(string xml)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(xml))
                    throw new FormatException("Appearance Profiler profile is empty.");
                XDocument document;
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (var text = new StringReader(xml))
                using (var reader = XmlReader.Create(text, settings))
                    document = XDocument.Load(reader);
                var root = document.Element("Root");
                var persisted = root == null ? null : root.Element("SingleFilePersistedItemAppearanceProfileV3");
                var profile = persisted == null ? null : persisted.Element("Profile");
                if (profile == null)
                    throw new FormatException("Appearance Profiler profile structure is missing.");

                var summary = new AppearanceProfileSummary { Name = Text(persisted, "Name") };
                var version = root.Element("SingleFilePersistedItemVersion");
                var versionText = version == null ? null : Text(version, "Version");
                summary.Version = string.IsNullOrEmpty(versionText) ? 0 : int.Parse(versionText, CultureInfo.InvariantCulture);
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
                foreach (var element in profile.Elements("PersistedItemAppearanceProfileRuleV3"))
                {
                    var errorPrefix = "rule " + (summary.Rules.Count + 1).ToString(CultureInfo.InvariantCulture) + ": ";
                    var type = (string)element.Attribute(xsi + "type") ?? "(missing)";
                    var rule = new AppearanceProfileRuleInfo
                    {
                        Kind = "unknown",
                        ForceVisibility = Text(element, "ForceVisibility"),
                        Transparency = Transparency(element, summary.Errors, errorPrefix),
                    };
                    if (type.EndsWith("PropertyRuleV3", StringComparison.Ordinal))
                    {
                        rule.Kind = "property";
                        rule.Category = Text(element, "Category");
                        rule.Property = Text(element, "Property");
                        rule.Condition = Text(element, "Condition");
                        var value = element.Element("Value");
                        rule.Value = value == null ? null : Text(value, "data");
                    }
                    else if (type.EndsWith("SetsRuleV3", StringComparison.Ordinal))
                    {
                        rule.Kind = "set";
                        rule.SetName = Text(element, "Name");
                        rule.SetPath = string.Join("/", (Text(element, "SavedItemId") ?? "")
                            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(line => line.Trim()).Where(line => line.Length > 0));
                        summary.SetRuleCount++;
                    }
                    else
                    {
                        // Navisworks' reader may reject the whole file for one rule it cannot read.
                        summary.Warnings.Add("Unknown Appearance Profiler rule type: " + type);
                        summary.Errors.Add(errorPrefix + "rule type " + type +
                            " is not one NavisHelper knows Navisworks can read; it may drop the whole profile");
                    }

                    var color = element.Element("Color");
                    if (color != null)
                    {
                        rule.ColorHex = "#" + ColorByte(color, "R") + ColorByte(color, "G") + ColorByte(color, "B");
                        if (!ScRgbAgrees(color, "R") || !ScRgbAgrees(color, "G") || !ScRgbAgrees(color, "B"))
                            summary.Errors.Add(errorPrefix + "scRGB colour disagrees with R/G/B; Navisworks would use the scRGB values");
                    }
                    if (string.Equals(rule.ForceVisibility, "Hidden", StringComparison.OrdinalIgnoreCase))
                        summary.HiddenRuleCount++;
                    summary.Rules.Add(rule);
                }
                return summary;
            }
            catch (XmlException exception)
            {
                throw new FormatException("Invalid Appearance Profiler XML: " + exception.Message, exception);
            }
            catch (FormatException exception)
            {
                throw new FormatException("Invalid Appearance Profiler profile: " + exception.Message, exception);
            }
            catch (OverflowException exception)
            {
                throw new FormatException("Invalid Appearance Profiler numeric value.", exception);
            }
        }

        public static string DescribePathError(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                return "Appearance Profiler profilePath must be rooted.";
            if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                return "Appearance Profiler profilePath must end in .xml or .dat.";
            return null;
        }

        private static string Text(XElement element, string name)
        {
            return (string)element.Element(name);
        }

        private static int Transparency(XElement element, List<string> errors, string errorPrefix)
        {
            var text = Text(element, "Transparency");
            if (text == null)
                return 0;
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0 || value > 100)
            {
                errors.Add(errorPrefix + "transparency '" + text + "' must be a whole number 0-100; Navisworks drops the whole profile otherwise");
                return 0;
            }
            return value;
        }

        private static bool ScRgbAgrees(XElement color, string channel)
        {
            var text = Text(color, "Sc" + channel);
            if (text == null)
                return true;
            double scRgb;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out scRgb) ||
                double.IsNaN(scRgb) || double.IsInfinity(scRgb))
                return false;
            // Compare in sRGB bytes, the colour Navisworks will paint: a fixed linear tolerance is
            // too loose near zero (ScR 0.002 is already byte 07).
            var linear = Math.Max(0.0, Math.Min(1.0, scRgb));
            var srgb = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
            var painted = (int)Math.Round(srgb * 255.0, MidpointRounding.AwayFromZero);
            var declared = byte.Parse(Text(color, channel), CultureInfo.InvariantCulture);
            return Math.Abs(painted - declared) <= 1;
        }

        private static string ColorByte(XElement color, string name)
        {
            return byte.Parse(Text(color, name) ?? "", CultureInfo.InvariantCulture).ToString("X2", CultureInfo.InvariantCulture);
        }
    }

    public sealed class AppearanceProfileApplyRequest
    {
        public string ProfilePath { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class AppearanceProfileApplyResponse
    {
        public string ProfilePath { get; set; }
        public bool Apply { get; set; }
        public bool Applied { get; set; }
        public AppearanceProfileSummary Profile { get; set; }
        public bool RulesTruncated { get; set; }
        public int? PluginResult { get; set; }
        public long ElapsedMs { get; set; }
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
