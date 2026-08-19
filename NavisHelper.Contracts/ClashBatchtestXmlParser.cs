using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashBatchtestXmlParser
    {
        public static ClashTestTransferPlan Parse(Stream input, ClashBatchtestParseOptions options)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            options = options ?? new ClashBatchtestParseOptions();
            if (input.CanSeek && input.Length > options.MaximumCharactersInDocument)
                throw new ClashTransferParseException(ClashTransferParseErrorCodes.InputTooLarge, "batchtest XML exceeds the configured input size limit.");

            XDocument document;
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                ValidationType = ValidationType.None,
                MaxCharactersInDocument = options.MaximumCharactersInDocument,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            };
            try
            {
                using (var reader = XmlReader.Create(input, settings))
                    document = XDocument.Load(reader, LoadOptions.None);
            }
            catch (XmlException ex)
            {
                var unsafeXml = ex.Message.IndexOf("DTD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                ex.Message.IndexOf("entity", StringComparison.OrdinalIgnoreCase) >= 0;
                throw new ClashTransferParseException(
                    unsafeXml ? ClashTransferParseErrorCodes.UnsafeXml : ClashTransferParseErrorCodes.MalformedXml,
                    unsafeXml ? "DTD and external entities are not allowed in batchtest XML." : "Malformed batchtest XML: " + ex.Message,
                    ex);
            }

            var root = document.Root;
            if (root == null)
                throw new ClashTransferParseException(ClashTransferParseErrorCodes.MalformedXml, "batchtest XML has no document element.");
            ValidateSchemaLocation(root);
            var batchtest = NameIs(root, "batchtest") ? root : root.Descendants().FirstOrDefault(element => NameIs(element, "batchtest"));
            if (batchtest == null)
                throw new ClashTransferParseException(ClashTransferParseErrorCodes.UnsupportedSchema, "XML does not contain a Navisworks batchtest element.");

            var testsContainer = batchtest.Elements().FirstOrDefault(element => NameIs(element, "clashtests"));
            if (testsContainer == null)
                throw new ClashTransferParseException(ClashTransferParseErrorCodes.UnsupportedSchema, "batchtest does not contain clashtests.");
            var testElements = testsContainer.Elements().Where(element => NameIs(element, "clashtest")).ToList();
            var maximumTestCount = Math.Max(1, Math.Min(ClashTransferConstants.MaximumTestLimit, options.MaximumTestCount));
            if (testElements.Count > maximumTestCount)
                throw new ClashTransferParseException(ClashTransferParseErrorCodes.TestLimitExceeded, "batchtest contains " + testElements.Count.ToString(CultureInfo.InvariantCulture) + " tests; maximum is " + maximumTestCount.ToString(CultureInfo.InvariantCulture) + ".");

            var units = Attribute(batchtest, "units") ?? Attribute(root, "units") ?? "mm";
            double unitToMm;
            if (!TryGetUnitToMillimeters(units, out unitToMm))
                throw new ClashTransferParseException(ClashTransferParseErrorCodes.UnsupportedSchema, "Unsupported batchtest units: " + units);

            var plan = new ClashTestTransferPlan
            {
                CreatedAtUtc = DateTime.UtcNow,
                SourceDocument = options.SourceDocument,
            };
            if (Attribute(batchtest, "units") == null && Attribute(root, "units") == null)
                plan.Warnings.Add("batchtest units were omitted; tolerance values were interpreted as millimeters.");

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < testElements.Count; index++)
            {
                var test = ParseTest(testElements[index], unitToMm, index + 1);
                if (!string.IsNullOrWhiteSpace(test.Name) && !seenNames.Add(test.Name))
                {
                    test.Supported = false;
                    test.Warnings.Add("Duplicate test name in batchtest: " + test.Name);
                    plan.Warnings.Add("Duplicate test name is not importable without ambiguity: " + test.Name);
                }
                plan.Tests.Add(test);
            }
            return plan;
        }

        private static ClashTestTransferDefinition ParseTest(XElement element, double unitToMm, int index)
        {
            var name = Attribute(element, "name") ?? string.Empty;
            var testType = (Attribute(element, "test_type") ?? "hard").Trim().ToLowerInvariant();
            var tolerance = Attribute(element, "tolerance");
            var result = new ClashTestTransferDefinition
            {
                Name = name,
                TestType = testType,
                ToleranceMm = ParseTolerance(tolerance, unitToMm, name, index),
            };
            if (string.IsNullOrWhiteSpace(tolerance))
                result.Warnings.Add("Tolerance is omitted; the target Navisworks default will be retained.");
            if (string.IsNullOrWhiteSpace(name))
                result.Warnings.Add("Test #" + index.ToString(CultureInfo.InvariantCulture) + " has no name.");
            if (!ClashTransferPlanHelper.IsSupportedTestType(testType))
            {
                result.Supported = false;
                result.Warnings.Add("Unsupported test type: " + testType);
            }

            result.A = ParseSide(element, "left", "A", result);
            result.B = ParseSide(element, "right", "B", result);
            AddUnsupportedSetting(element, "default_assignee", result);
            AddUnsupportedSetting(element, "tolerances", result);
            AddUnsupportedSetting(element, "rules", result);
            AddUnsupportedSetting(element, "summary", result);
            AddUnsupportedSetting(element, "clashresults", result);
            AddUnsupportedAttribute(element, "merge_composites", result);
            AddUnsupportedAttribute(element, "priority", result);
            var linkage = element.Elements().FirstOrDefault(child => NameIs(child, "linkage"));
            if (linkage != null && (!string.IsNullOrWhiteSpace(Attribute(linkage, "mode")) || linkage.Elements().Any()))
                result.UnsupportedSettings.Add("linkage");
            if (result.UnsupportedSettings.Count > 0)
                result.Warnings.Add("Unsupported settings are intentionally not imported: " + string.Join(", ", result.UnsupportedSettings) + ".");
            ClashTransferPlanHelper.RefreshSupport(result);
            return result;
        }

        private static ClashTestTransferSide ParseSide(XElement test, string elementName, string sideName, ClashTestTransferDefinition owner)
        {
            var side = new ClashTestTransferSide { Side = sideName, Kind = ClashTransferSideKinds.Unsupported, Supported = false };
            var sideElement = test.Elements().FirstOrDefault(element => NameIs(element, elementName));
            var selection = sideElement == null ? null : sideElement.Elements().FirstOrDefault(element => NameIs(element, "clashselection"));
            var locatorElement = selection == null ? null : selection.Elements().FirstOrDefault(element => NameIs(element, "locator"));
            var locator = locatorElement == null ? string.Empty : (locatorElement.Value ?? string.Empty).Trim();
            side.Locator = locator;
            if (selection != null)
                side.SelfIntersect = ParseIntegerBoolean(Attribute(selection, "selfintersect"));
            if (string.IsNullOrWhiteSpace(locator))
            {
                side.Warnings.Add("Test '" + (owner.Name ?? string.Empty) + "' side " + sideName + " is missing a locator.");
                owner.Warnings.AddRange(side.Warnings);
                return side;
            }

            if (!locator.StartsWith(ClashTransferConstants.SelectionSetLocatorPrefix, StringComparison.OrdinalIgnoreCase))
            {
                side.Warnings.Add("Test '" + (owner.Name ?? string.Empty) + "' side " + sideName + " uses unsupported locator: " + locator);
                owner.Warnings.AddRange(side.Warnings);
                return side;
            }

            var path = locator.Substring(ClashTransferConstants.SelectionSetLocatorPrefix.Length).Trim('/');
            if (string.IsNullOrWhiteSpace(path))
            {
                side.Warnings.Add("Test '" + (owner.Name ?? string.Empty) + "' side " + sideName + " has an empty Selection Set path in locator: " + locator);
                owner.Warnings.AddRange(side.Warnings);
                return side;
            }

            side.Kind = ClashTransferSideKinds.SelectionSet;
            side.Path = path;
            side.Name = path.Split('/').LastOrDefault() ?? path;
            side.Supported = true;
            side.ResolutionStatus = "unresolved";
            return side;
        }

        private static double? ParseTolerance(string value, double unitToMm, string testName, int index)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            double parsed;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) || parsed < 0 || double.IsNaN(parsed) || double.IsInfinity(parsed))
                throw new ClashTransferParseException(ClashTransferParseErrorCodes.MalformedXml, "Invalid tolerance for test '" + (testName ?? ("#" + index)) + "': " + value);
            return parsed * unitToMm;
        }

        private static bool? ParseIntegerBoolean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            if (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                return false;
            return null;
        }

        private static void ValidateSchemaLocation(XElement root)
        {
            var location = root.Attributes().FirstOrDefault(attribute =>
                string.Equals(attribute.Name.LocalName, "noNamespaceSchemaLocation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(attribute.Name.LocalName, "schemaLocation", StringComparison.OrdinalIgnoreCase));
            if (location == null || string.IsNullOrWhiteSpace(location.Value))
                return;
            if (location.Value.IndexOf("nw-exchange-12.0.xsd", StringComparison.OrdinalIgnoreCase) < 0)
                throw new ClashTransferParseException(ClashTransferParseErrorCodes.UnsupportedSchema, "Only Navisworks nw-exchange-12.0 batchtest XML is supported; schemaLocation was " + location.Value + ".");
        }

        private static void AddUnsupportedSetting(XElement element, string name, ClashTestTransferDefinition result)
        {
            if (element.Elements().Any(child => NameIs(child, name)))
                result.UnsupportedSettings.Add(name);
        }

        private static void AddUnsupportedAttribute(XElement element, string name, ClashTestTransferDefinition result)
        {
            if (element.Attributes().Any(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase)))
                result.UnsupportedSettings.Add(name);
        }

        private static string Attribute(XElement element, string name)
        {
            if (element == null)
                return null;
            var attribute = element.Attributes().FirstOrDefault(candidate => string.Equals(candidate.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            return attribute == null ? null : attribute.Value;
        }

        private static bool NameIs(XElement element, string name)
        {
            return element != null && string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetUnitToMillimeters(string units, out double factor)
        {
            switch ((units ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "m": factor = 1000; return true;
                case "cm": factor = 10; return true;
                case "mm": factor = 1; return true;
                case "ft": factor = 304.8; return true;
                case "in": factor = 25.4; return true;
                case "yrd": factor = 914.4; return true;
                case "km": factor = 1000000; return true;
                case "mi": factor = 1609344; return true;
                case "um": factor = 0.001; return true;
                case "mils": factor = 0.0254; return true;
                case "uin": factor = 0.0000254; return true;
                default: factor = 0; return false;
            }
        }
    }
}
