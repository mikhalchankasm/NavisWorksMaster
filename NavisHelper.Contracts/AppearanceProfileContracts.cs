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
        public double Transparency { get; set; }
        public string ForceVisibility { get; set; }
    }

    public sealed class AppearanceProfileSummary
    {
        public string Name { get; set; }
        public int Version { get; set; }
        public List<AppearanceProfileRuleInfo> Rules { get; set; } = new List<AppearanceProfileRuleInfo>();
        public int SetRuleCount { get; set; }
        public int HiddenRuleCount { get; set; }
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
                    var type = (string)element.Attribute(xsi + "type") ?? "(missing)";
                    var rule = new AppearanceProfileRuleInfo
                    {
                        Kind = "unknown",
                        ForceVisibility = Text(element, "ForceVisibility"),
                        Transparency = Number(element, "Transparency"),
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
                        summary.Warnings.Add("Unknown Appearance Profiler rule type: " + type);

                    var color = element.Element("Color");
                    if (color != null)
                        rule.ColorHex = "#" + ColorByte(color, "R") + ColorByte(color, "G") + ColorByte(color, "B");
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

        private static double Number(XElement element, string name)
        {
            var text = Text(element, name);
            var value = string.IsNullOrEmpty(text) ? 0 : double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException("Non-finite " + name + ".");
            return value;
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
