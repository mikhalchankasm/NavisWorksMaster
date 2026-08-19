using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.DocumentParts;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace NavisHelper.Agent.Services
{
    internal sealed class ClashTestTransferExporterService
    {
        private static readonly JsonSerializerSettings TransferJsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
        };

        public ClashTestsExportResponse Execute(Document document, ClashTestsExportRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            request = request ?? new ClashTestsExportRequest();
            var apply = request.Apply == true;
            var format = string.IsNullOrWhiteSpace(request.Format) ? ClashTransferConstants.JsonFormat : request.Format.Trim().ToLowerInvariant();
            if (!string.Equals(format, ClashTransferConstants.JsonFormat, StringComparison.Ordinal))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "format must be navishelper_json.");

            var calculatedPath = NormalizeOptionalAbsolutePath(request.OutputPath);
            if (apply && string.IsNullOrWhiteSpace(calculatedPath))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "apply=true requires an absolute outputPath.");

            var clash = document.GetClash();
            if (clash == null || clash.TestsData == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective data is not available.");
            var all = ClashApiCompat.GetClashTests(clash).ToList();
            var selected = ResolveTests(all, request);
            var plan = new ClashTestTransferPlan
            {
                CreatedAtUtc = DateTime.UtcNow,
                SourceDocument = document.FileName,
            };
            plan.Warnings.Add("Clash results, saved result viewpoints, comments, calculation status, and historical review data are not transferred.");

            foreach (var entry in selected)
            {
                var definition = ExportTest(document, entry.Item1, entry.Item2);
                plan.Tests.Add(definition);
            }

            var response = new ClashTestsExportResponse
            {
                Applied = apply,
                Format = format,
                FoundTestCount = selected.Count,
                ExportableTestCount = plan.Tests.Count(test => test != null && test.Supported),
                UnsupportedTestCount = plan.Tests.Count(test => test == null || !test.Supported),
                CalculatedOutputPath = calculatedPath,
                OutputWritten = false,
                ArtifactStatus = string.IsNullOrWhiteSpace(calculatedPath)
                    ? ClashTransferArtifactStatuses.NotRequested
                    : ClashTransferArtifactStatuses.NotWrittenDryRun,
                Plan = plan,
            };
            response.Warnings.AddRange(plan.Warnings);
            foreach (var test in plan.Tests.Where(test => test != null && !test.Supported))
                response.Warnings.Add("Test '" + (test.Name ?? string.Empty) + "' is not portable: " + string.Join("; ", test.Warnings));

            if (apply)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(plan, TransferJsonSettings);
                    var artifact = VerifiedFileArtifactWriter.WriteUtf8(calculatedPath, json, request.OverwriteExisting == true);
                    response.OutputPath = artifact.OutputPath;
                    response.OutputWritten = true;
                    response.ArtifactStatus = ClashTransferArtifactStatuses.WrittenVerified;
                    response.BytesWritten = artifact.BytesWritten;
                    response.Sha256 = artifact.Sha256;
                }
                catch (Exception ex)
                {
                    throw new AgentCommandException(ErrorCodes.ArtifactWriteFailed, "Failed to write and verify Clash transfer plan: " + ex.Message);
                }
            }

            response.Message = apply
                ? "Wrote and verified a Clash transfer plan containing " + response.ExportableTestCount.ToString(CultureInfo.InvariantCulture) + " portable test definition(s)."
                : "Dry-run only. No output file was written; pass apply=true to write the transfer plan.";
            return response;
        }

        private static ClashTestTransferDefinition ExportTest(Document document, ClashTest test, int testIndex)
        {
            var definition = new ClashTestTransferDefinition
            {
                Name = test == null ? string.Empty : test.DisplayName,
                SourceTestHandle = ClashHandleHelper.BuildTestHandle(testIndex),
                TestType = NormalizeTestType(test == null ? ClashTestType.Hard : test.TestType),
                ToleranceMm = test == null ? 0 : DocumentUnitsToMillimeters(document, test.Tolerance),
                IgnoreRules = new ClashNativeIgnoreRules { SameFile = test != null && ClashNativeIgnoreRuleService.IsApplied(test) },
            };
            if (test == null)
            {
                definition.Supported = false;
                definition.Warnings.Add("Clash Test is unavailable.");
                return definition;
            }

            definition.A = TryExportSide(document, test.SelectionA.Selection, "A", test.SelectionA.SelfIntersect);
            definition.B = TryExportSide(document, test.SelectionB.Selection, "B", test.SelectionB.SelfIntersect);
            var unsupportedRules = test.IgnoreRules.Cast<Rule>()
                .Where(rule => rule != null && rule.IsEnabled && !ClashNativeIgnoreRuleService.IsSameFileRule(rule.DisplayName))
                .Select(rule => "ignore_rule:" + (rule.DisplayName ?? string.Empty))
                .ToList();
            definition.UnsupportedSettings.AddRange(unsupportedRules);
            if (unsupportedRules.Count > 0)
                definition.Warnings.Add("Enabled ignore rules other than same-file are not transferred.");
            definition.Warnings.Add("Results, viewpoints, comments, and calculation history are intentionally excluded.");
            ClashTransferPlanHelper.RefreshSupport(definition);
            return definition;
        }

        private static ClashTestTransferSide ExportSide(Document document, Selection selection, string sideName, bool selfIntersect)
        {
            var side = new ClashTestTransferSide
            {
                Side = sideName,
                SelfIntersect = selfIntersect,
                Supported = false,
                Kind = ClashTransferSideKinds.Unsupported,
            };
            if (selection == null)
            {
                side.Warnings.Add("Side " + sideName + " selection is unavailable.");
                return side;
            }

            try
            {
                side.CurrentMemberCount = selection.GetSelectedItems(document).Count;
            }
            catch (Exception ex)
            {
                side.Warnings.Add("Could not evaluate current member count: " + ex.Message);
            }

            if (selection.HasSelectionSources)
            {
                if (selection.SelectionSources.Count != 1)
                {
                    side.Warnings.Add("Side " + sideName + " contains " + selection.SelectionSources.Count.ToString(CultureInfo.InvariantCulture) + " Selection Sources; only one exact Selection Set/Search Set source is portable.");
                    return side;
                }

                var source = selection.SelectionSources[0];
                var savedItem = document.SelectionSets.ResolveSelectionSource(source);
                var set = savedItem as SelectionSet;
                if (set == null)
                {
                    side.Warnings.Add(savedItem is GroupItem
                        ? "Side " + sideName + " Selection Source resolves to a folder, not a Selection Set/Search Set."
                        : "Side " + sideName + " Selection Source could not be resolved to a Selection Set/Search Set.");
                    return side;
                }

                var resolved = SelectionSetReferenceResolver.Describe(document, set);
                side.Kind = string.Equals(resolved.Type, "SearchSet", StringComparison.OrdinalIgnoreCase)
                    ? ClashTransferSideKinds.SearchSet
                    : ClashTransferSideKinds.SelectionSet;
                side.ItemId = resolved.ItemId;
                side.Name = resolved.Name;
                side.Path = resolved.Path;
                side.Locator = ClashTransferConstants.SelectionSetLocatorPrefix + resolved.Path;
                side.Supported = true;
                side.ResolutionStatus = "source_resolved";
                return side;
            }

            if (selection.HasExplicitSelection && selection.ExplicitSelection.Count == 1)
            {
                var explicitItem = selection.ExplicitSelection[0];
                var models = document.Models.Cast<Model>()
                    .Where(model => model != null && model.RootItem != null && (object.ReferenceEquals(model.RootItem, explicitItem) || model.RootItem.Equals(explicitItem)))
                    .ToList();
                if (models.Count == 1)
                {
                    var model = models[0];
                    side.Kind = ClashTransferSideKinds.ModelRoot;
                    side.RootName = model.RootItem.DisplayName;
                    side.Name = side.RootName;
                    side.SourceFile = !string.IsNullOrWhiteSpace(model.SourceFileName) ? model.SourceFileName : model.FileName;
                    side.Supported = !string.IsNullOrWhiteSpace(side.RootName) || !string.IsNullOrWhiteSpace(side.SourceFile);
                    side.ResolutionStatus = "source_resolved";
                    return side;
                }
            }

            side.Warnings.Add("Side " + sideName + " is an explicit-item snapshot or another unsupported selection form; it was not converted to a Selection Source.");
            return side;
        }

        private static ClashTestTransferSide TryExportSide(Document document, Selection selection, string sideName, bool selfIntersect)
        {
            try
            {
                return ExportSide(document, selection, sideName, selfIntersect);
            }
            catch (Exception ex)
            {
                return new ClashTestTransferSide
                {
                    Side = sideName,
                    Kind = ClashTransferSideKinds.Unsupported,
                    SelfIntersect = selfIntersect,
                    Supported = false,
                    Warnings = new List<string> { "Side " + sideName + " could not be represented portably: " + ex.Message },
                };
            }
        }

        private static List<Tuple<ClashTest, int>> ResolveTests(IList<ClashTest> all, ClashTestsExportRequest request)
        {
            var selected = new List<Tuple<ClashTest, int>>();
            var names = (request.TestNames ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList();
            var handles = (request.TestHandles ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            var prefix = (request.NamePrefix ?? string.Empty).Trim();
            var hasScope = names.Count > 0 || handles.Count > 0 || prefix.Length > 0;
            for (var index = 0; index < all.Count; index++)
            {
                var test = all[index];
                if (!hasScope || names.Any(name => string.Equals(test.DisplayName, name, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(prefix) && (test.DisplayName ?? string.Empty).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    AddUnique(selected, test, index + 1);
            }
            foreach (var handle in handles)
            {
                int index;
                if (!ClashHandleHelper.TryParseTestHandle(handle, out index))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Invalid Clash Test handle: " + handle);
                if (index < 1 || index > all.Count)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Clash Test handle is outside the current test range: " + handle);
                AddUnique(selected, all[index - 1], index);
            }
            return selected;
        }

        private static void AddUnique(ICollection<Tuple<ClashTest, int>> result, ClashTest test, int index)
        {
            if (test != null && !result.Any(entry => object.ReferenceEquals(entry.Item1, test)))
                result.Add(Tuple.Create(test, index));
        }

        private static string NormalizeOptionalAbsolutePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
            if (!Path.IsPathRooted(expanded))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "outputPath must be an exact absolute path.");
            return Path.GetFullPath(expanded);
        }

        private static string NormalizeTestType(ClashTestType type)
        {
            switch (type)
            {
                case ClashTestType.HardConservative: return ClashTestTypeHelper.HardConservative;
                case ClashTestType.Clearance: return ClashTestTypeHelper.Clearance;
                case ClashTestType.Duplicate: return ClashTestTypeHelper.Duplicate;
                default: return ClashTestTypeHelper.Hard;
            }
        }

        private static double DocumentUnitsToMillimeters(Document document, double value)
        {
            switch (document.Units)
            {
                case Units.Centimeters: return value * 10.0;
                case Units.Meters: return value * 1000.0;
                case Units.Kilometers: return value * 1000000.0;
                case Units.Inches: return value * 25.4;
                case Units.Feet: return value * 304.8;
                case Units.Yards: return value * 914.4;
                case Units.Miles: return value * 1609344.0;
                case Units.Millimeters: return value;
                default: return value * 1000.0;
            }
        }
    }
}
