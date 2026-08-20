using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {

        private static string NormalizeClashBboxRootMode(string rootMode)
        {
            var value = ClashBboxOptionHelper.NormalizeRootMode(rootMode);
            if (value != null)
                return value;
            throw new AgentCommandException(ErrorCodes.SchemaViolation, "rootMode must be top_level_files.");
        }

        private static int NormalizeClashBboxRefineDepth(int? refineDepth)
        {
            int value;
            if (!ClashBboxOptionHelper.TryNormalizeRefineDepth(refineDepth, out value))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "refineDepth must be 0, 1, or 2.");
            return value;
        }

        private static int ClampClashBboxRootItems(int? value)
        {
            return ClashBboxOptionHelper.ClampRootItems(value);
        }

        private static int ClampClashBboxCandidatePairs(int? value)
        {
            return ClashBboxOptionHelper.ClampCandidatePairs(value);
        }

        private static int ClampClashBboxPreviewLimit(int? value)
        {
            return ClashBboxOptionHelper.ClampPreviewLimit(value);
        }

        private static int ClampClashPairTestsCreateLimit(int? value)
        {
            return ClashBboxOptionHelper.ClampPairTestsCreateLimit(value);
        }

        private static int ClampClashMatrixSelectedItems(int? value)
        {
            return ClashBboxOptionHelper.ClampMatrixSelectedItems(value);
        }

        private static bool HasExplicitClashMatrixInput(ClashCreateMatrixFromSelectionRequest request)
        {
            if (request == null)
                return false;

            return (request.MatrixItemNames != null && request.MatrixItemNames.Any(name => !string.IsNullOrWhiteSpace(name))) ||
                   !string.IsNullOrWhiteSpace(request.MatrixNameContains);
        }

        private static List<ModelItem> ResolveClashMatrixItems(Document document, ClashCreateMatrixFromSelectionRequest request, int maxItems, out List<string> warnings)
        {
            warnings = new List<string>();
            if (!HasExplicitClashMatrixInput(request))
            {
                return document.CurrentSelection == null || document.CurrentSelection.SelectedItems == null
                    ? new List<ModelItem>()
                    : document.CurrentSelection.SelectedItems.Cast<ModelItem>().Where(item => item != null).ToList();
            }

            var rootNames = request.MatrixItemNames == null
                ? new List<string>()
                : request.MatrixItemNames.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToList();
            var excludes = request.MatrixExcludeNameContains == null
                ? new List<string>()
                : request.MatrixExcludeNameContains.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToList();
            var nameContains = request.MatrixNameContains ?? string.Empty;
            var matches = new List<ModelItem>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var totalMatches = 0;
            var scannedItems = 0;
            var traversalStopwatch = Stopwatch.StartNew();
            var traversalTruncated = false;

            if (document != null && document.Models != null)
            {
                foreach (ModelItem item in document.Models.RootItemDescendantsAndSelf)
                {
                    scannedItems++;
                    if (scannedItems > MaxClashMatrixTraversalItems || traversalStopwatch.ElapsedMilliseconds > MaxClashMatrixTraversalMs)
                    {
                        traversalTruncated = true;
                        break;
                    }

                    if (item == null)
                        continue;

                    var path = BuildItemPath(item);
                    if (!seenPaths.Add(path))
                        continue;

                    var name = GetItemDisplayName(item);
                    var sourceFile = TryGetSourceFile(item) ?? string.Empty;
                    if (!ClashBboxPlanHelper.MatchesRootFilters(name, path, sourceFile, rootNames, nameContains, excludes))
                        continue;

                    totalMatches++;
                    if (matches.Count < maxItems)
                        matches.Add(item);
                }
            }

            if (totalMatches == 0)
                warnings.Add("No matrix items matched matrixItemNames or matrixNameContains.");
            if (traversalTruncated)
                warnings.Add("Matrix item traversal stopped after " + scannedItems.ToString(CultureInfo.InvariantCulture) + " item(s) or " + MaxClashMatrixTraversalMs.ToString(CultureInfo.InvariantCulture) + " ms. Narrow matrixNameContains or pass exact matrixItemNames.");
            if (totalMatches > matches.Count)
                warnings.Add("Matrix input item limit reached; matched " + totalMatches.ToString(CultureInfo.InvariantCulture) + " item(s), returned first " + matches.Count.ToString(CultureInfo.InvariantCulture) + ". Increase maxSelectedItems or narrow matrix filters.");

            return matches;
        }

        private static ClashBboxRootCandidateSet BuildClashBboxRootCandidates(Document document, ClashBboxPairPlanRequest request, int maxRootItems)
        {
            request = request ?? new ClashBboxPairPlanRequest();
            var all = new List<ClashBboxRootCandidate>();
            var warnings = new List<string>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rootNames = request.RootNames == null
                ? new List<string>()
                : request.RootNames.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var excludes = request.ExcludeNameContains == null
                ? new List<string>()
                : request.ExcludeNameContains.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToList();
            var nameContains = request.NameContains ?? string.Empty;
            var index = 0;

            var sourceMode = string.IsNullOrWhiteSpace(request.SourceMode)
                ? ClashBboxOptionHelper.RootModeTopLevelFiles
                : request.SourceMode.Trim().ToLowerInvariant();
            if (sourceMode == "selection")
            {
                var selectedItems = document == null || document.CurrentSelection == null
                    ? null
                    : document.CurrentSelection.SelectedItems;
                if (selectedItems != null)
                {
                    foreach (ModelItem item in selectedItems)
                        AddClashBboxRootCandidate(all, seenPaths, item, rootNames, nameContains, excludes, warnings, ref index);
                }
            }
            else if (document != null && document.Models != null)
            {
                foreach (Model model in document.Models)
                {
                    if (model == null || model.RootItem == null)
                        continue;

                    AddClashBboxRootCandidate(all, seenPaths, model.RootItem, rootNames, nameContains, excludes, warnings, ref index);
                    foreach (ModelItem child in model.RootItem.Children)
                        AddClashBboxRootCandidate(all, seenPaths, child, rootNames, nameContains, excludes, warnings, ref index);
                }
            }

            var truncated = all.Count > maxRootItems;
            var items = all.Take(maxRootItems).ToList();
            if (truncated)
                warnings.Add("Root item limit reached; increase maxRootItems to include all matched roots.");
            var outcomes = ClashBboxPlanHelper.BuildRootNameOutcomes(all.Select(candidate => candidate.Info), rootNames, false);
            if (outcomes.Unmatched.Count > 0)
                warnings.Add("Exact rootNames not found: " + string.Join(", ", outcomes.Unmatched) + ".");

            return new ClashBboxRootCandidateSet
            {
                TotalCount = all.Count,
                Truncated = truncated,
                Items = items,
                Warnings = warnings,
                RequestedRootNames = outcomes.Requested,
                MatchedRootNames = outcomes.Matched,
                UnmatchedRootNames = outcomes.Unmatched,
                NotEvaluatedRootNames = outcomes.NotEvaluatedDueToLimit,
            };
        }

        private static void AddClashBboxRootCandidate(
            ICollection<ClashBboxRootCandidate> result,
            ISet<string> seenPaths,
            ModelItem item,
            IList<string> rootNames,
            string nameContains,
            IList<string> excludeNameContains,
            ICollection<string> warnings,
            ref int index)
        {
            if (item == null)
                return;

            var path = BuildItemPath(item);
            if (seenPaths != null && !seenPaths.Add(path))
                return;

            var name = GetItemDisplayName(item);
            var sourceFile = TryGetSourceFile(item) ?? string.Empty;
            if (!ClashBboxPlanHelper.MatchesRootFilters(name, path, sourceFile, rootNames, nameContains, excludeNameContains))
                return;

            BoundingBox3D box = null;
            BoundingBoxInfo boxInfo = null;
            try
            {
                box = item.BoundingBox();
                boxInfo = ToBoundingBoxInfo(box);
            }
            catch (Exception ex)
            {
                if (warnings != null)
                    warnings.Add("Could not read bounding box for root '" + name + "': " + ex.Message);
            }

            index++;
            result.Add(new ClashBboxRootCandidate
            {
                Item = item,
                Box = box,
                Info = new ClashBboxRootItem
                {
                    Index = index,
                    Name = name,
                    Path = path,
                    SourceFile = sourceFile,
                    ChildCount = item.Children == null ? 0 : item.Children.Count(),
                    BoundingBox = boxInfo,
                },
            });
        }

        private static bool EvaluateClashBboxPair(
            ClashBboxRootCandidate a,
            ClashBboxRootCandidate b,
            int refineDepth,
            double toleranceUnits,
            out string reason,
            out string warning,
            out ClashBboxPairRefineStats stats)
        {
            stats = new ClashBboxPairRefineStats();
            warning = string.Empty;
            if (a == null || b == null || a.Box == null || b.Box == null)
            {
                reason = "missing_root_bbox";
                return false;
            }

            if (!BoxesIntersect(a.Box, b.Box, toleranceUnits))
            {
                reason = "root_bbox_disjoint";
                return false;
            }

            if (refineDepth == 0)
            {
                reason = "root_bbox_intersects";
                return true;
            }

            var boxCache = new Dictionary<ModelItem, BoundingBox3D>();
            var current = new List<Tuple<ModelItem, ModelItem>> { Tuple.Create(a.Item, b.Item) };
            for (var depth = 1; depth <= refineDepth; depth++)
            {
                var next = new List<Tuple<ModelItem, ModelItem>>();
                foreach (var pair in current)
                {
                    var childrenA = GetClashBboxRefineNodes(pair.Item1, boxCache);
                    var childrenB = GetClashBboxRefineNodes(pair.Item2, boxCache);
                    foreach (var childA in childrenA)
                    {
                        foreach (var childB in childrenB)
                        {
                            stats.CheckedPairCount++;
                            if (stats.CheckedPairCount > MaxClashBboxRefinePairChecksPerRootPair)
                            {
                                stats.LimitReached = true;
                                warning = "Refinement check limit reached for pair '" + a.Info.Name + "' vs '" + b.Info.Name + "'; pair kept conservatively.";
                                reason = "refine_check_limit_reached";
                                return true;
                            }

                            if (!BoxesIntersect(childA.Box, childB.Box, toleranceUnits))
                                continue;

                            stats.IntersectingPairCount++;
                            if (depth == refineDepth)
                            {
                                reason = "refined_bbox_intersects";
                                return true;
                            }

                            if (next.Count >= MaxClashBboxRefineSurvivorPairsPerDepth)
                            {
                                stats.LimitReached = true;
                                warning = "Refinement survivor limit reached for pair '" + a.Info.Name + "' vs '" + b.Info.Name + "'; pair kept conservatively.";
                                reason = "refine_survivor_limit_reached";
                                return true;
                            }

                            next.Add(Tuple.Create(childA.Item, childB.Item));
                        }
                    }
                }

                if (next.Count == 0)
                {
                    reason = "refined_bbox_disjoint";
                    return false;
                }

                current = next;
            }

            reason = "refined_bbox_intersects";
            return true;
        }

        private static List<ClashBboxRefineNode> GetClashBboxRefineNodes(ModelItem item, IDictionary<ModelItem, BoundingBox3D> boxCache)
        {
            var result = new List<ClashBboxRefineNode>();
            if (item == null)
                return result;

            if (item.Children != null && item.Children.Count() > 0)
            {
                foreach (ModelItem child in item.Children)
                    AddClashBboxRefineNode(result, child, boxCache);
            }
            else
            {
                AddClashBboxRefineNode(result, item, boxCache);
            }

            return result;
        }

        private static void AddClashBboxRefineNode(ICollection<ClashBboxRefineNode> result, ModelItem item, IDictionary<ModelItem, BoundingBox3D> boxCache)
        {
            if (result == null || item == null)
                return;

            var box = TryGetBoundingBox3D(item, boxCache);
            if (box == null)
                return;

            result.Add(new ClashBboxRefineNode
            {
                Item = item,
                Box = box,
            });
        }

        private static BoundingBox3D TryGetBoundingBox3D(ModelItem item)
        {
            return TryGetBoundingBox3D(item, null);
        }

        private static BoundingBox3D TryGetBoundingBox3D(ModelItem item, IDictionary<ModelItem, BoundingBox3D> boxCache)
        {
            if (item == null)
                return null;
            BoundingBox3D cached;
            if (boxCache != null && boxCache.TryGetValue(item, out cached))
                return cached;

            try
            {
                var box = item.BoundingBox();
                if (boxCache != null)
                    boxCache[item] = box;
                return box;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsAncestorOrDescendant(ModelItem a, ModelItem b)
        {
            if (a == null || b == null)
                return false;

            return IsAncestorOf(a, b) || IsAncestorOf(b, a);
        }

        private static bool IsAncestorOf(ModelItem ancestor, ModelItem item)
        {
            var current = item == null ? null : item.Parent;
            while (current != null)
            {
                if (object.ReferenceEquals(current, ancestor))
                    return true;
                current = current.Parent;
            }

            return false;
        }

        private static bool BoxesIntersect(BoundingBox3D a, BoundingBox3D b, double tolerance)
        {
            if (a == null || b == null)
                return false;

            return a.Min.X - tolerance <= b.Max.X && a.Max.X + tolerance >= b.Min.X &&
                   a.Min.Y - tolerance <= b.Max.Y && a.Max.Y + tolerance >= b.Min.Y &&
                   a.Min.Z - tolerance <= b.Max.Z && a.Max.Z + tolerance >= b.Min.Z;
        }

        private static void IncrementCount(IDictionary<string, int> counts, string key)
        {
            if (counts == null)
                return;

            key = string.IsNullOrWhiteSpace(key) ? "unknown" : key;
            int count;
            counts.TryGetValue(key, out count);
            counts[key] = count + 1;
        }

        private static void IncrementCount(IDictionary<string, int> counts, string key, int amount)
        {
            if (counts == null || amount == 0)
                return;

            key = string.IsNullOrWhiteSpace(key) ? "unknown" : key;
            int count;
            counts.TryGetValue(key, out count);
            counts[key] = count + amount;
        }

        private static VerifiedFileArtifact WriteClashBboxPlanOutput(
            ClashBboxPairPlanResponse response,
            string outputPath,
            bool overwriteExisting)
        {
            if (response == null || string.IsNullOrWhiteSpace(outputPath))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "apply=true requires an absolute outputPath for clash_bbox_pair_plan.");

            var expanded = Environment.ExpandEnvironmentVariables(outputPath.Trim());
            if (!Path.IsPathRooted(expanded))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "outputPath must be absolute.");
            var path = Path.GetFullPath(expanded);
            response.OutputPath = path;
            var extension = Path.GetExtension(path);
            var content = string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase)
                ? BuildClashBboxPlanCsv(response)
                : BuildClashBboxPlanJson(response);
            try
            {
                return VerifiedFileArtifactWriter.WriteUtf8(path, content, overwriteExisting);
            }
            catch (Exception ex)
            {
                response.OutputPath = string.Empty;
                response.OutputWritten = false;
                throw new AgentCommandException(ErrorCodes.ArtifactWriteFailed, "Failed to write and verify Clash bbox plan artifact: " + ex.Message);
            }
        }

        private static string BuildClashBboxPlanCsv(ClashBboxPairPlanResponse response)
        {
            var builder = new StringBuilder();
            builder.AppendLine(ClashBboxPlanHelper.CsvHeader);
            foreach (var pair in response.CandidatePairs)
                builder.AppendLine(ClashBboxPlanHelper.BuildCsvRow(pair));
            return builder.ToString();
        }

        private static string BuildClashBboxPlanJson(ClashBboxPairPlanResponse response)
        {
            var artifact = JObject.FromObject(response, JsonSerializer.Create(ClashReportJsonSettings));
            // Verification metadata belongs to the authoritative MCP response. It cannot
            // truthfully describe the file from inside that same file before write/read-back.
            artifact.Remove(nameof(ClashBboxPairPlanResponse.OutputWritten));
            artifact.Remove(nameof(ClashBboxPairPlanResponse.ArtifactStatus));
            artifact.Remove(nameof(ClashBboxPairPlanResponse.BytesWritten));
            artifact.Remove(nameof(ClashBboxPairPlanResponse.Sha256));
            return artifact.ToString(Formatting.Indented);
        }

        private static List<ClashBboxCandidatePair> LoadClashPairTestInputPairs(ClashPairTestsCreateRequest request)
        {
            var result = new List<ClashBboxCandidatePair>();
            if (request != null && request.Pairs != null)
                result.AddRange(request.Pairs.Where(pair => pair != null));

            if (request != null && !string.IsNullOrWhiteSpace(request.PlanOutputPath))
            {
                var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.PlanOutputPath.Trim()));
                var text = File.ReadAllText(path, Encoding.UTF8);
                var plan = JsonConvert.DeserializeObject<ClashBboxPairPlanResponse>(text);
                if (plan != null && plan.CandidatePairs != null)
                    result.AddRange(plan.CandidatePairs.Where(pair => pair != null));
            }

            if (result.Count == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Provide pairs or planOutputPath from clash_bbox_pair_plan.");

            return result;
        }

        private static string NormalizeClashPairTestPrefix(string prefix)
        {
            return ClashTestNamePrefixHelper.NormalizePairTestPrefix(prefix);
        }

        private static string NormalizeClashMatrixPrefix(string prefix, bool useGeneratedPrefix)
        {
            return ClashTestNamePrefixHelper.NormalizeMatrixPrefix(prefix, useGeneratedPrefix, DateTime.Now);
        }

        private static void AddClashMatrixPreviewTests(
            ClashCreateMatrixFromSelectionResponse response,
            IList<ModelItem> selectedItems,
            string prefix,
            string pairNameTemplate,
            int pairNameStartIndex,
            bool includePairNames,
            int previewLimit)
        {
            if (response == null || selectedItems == null || !includePairNames)
                return;

            var pairIndex = 0;
            for (var i = 0; i < selectedItems.Count; i++)
            {
                for (var j = i + 1; j < selectedItems.Count; j++)
                {
                    pairIndex++;
                    if (response.Tests.Count >= previewLimit)
                    {
                        response.PlannedTestsTruncated = true;
                        return;
                    }

                    var item = BuildClashMatrixTestPlanItem(pairIndex, i, j, selectedItems[i], selectedItems[j], prefix, pairNameTemplate, pairNameStartIndex);
                    if (IsAncestorOrDescendant(selectedItems[i], selectedItems[j]))
                    {
                        item.Status = "skipped";
                        item.ErrorMessage = "Selected items are ancestor/descendant; skipped to avoid self-overlap clash noise.";
                    }

                    response.Tests.Add(item);
                }
            }
        }

        private static ClashMatrixTestPlanItem BuildClashMatrixTestPlanItem(
            int pairIndex,
            int aIndex,
            int bIndex,
            ModelItem itemA,
            ModelItem itemB,
            string prefix,
            string pairNameTemplate,
            int pairNameStartIndex)
        {
            var aName = GetItemDisplayName(itemA);
            var bName = GetItemDisplayName(itemB);
            var testName = string.IsNullOrWhiteSpace(pairNameTemplate)
                ? prefix + SanitizeClashTestNamePart(aName, 80) + " vs " + SanitizeClashTestNamePart(bName, 80)
                : PairNameTemplateFormatter.Format(pairNameTemplate, pairNameStartIndex + pairIndex - 1, aName, bName);
            return new ClashMatrixTestPlanItem
            {
                PairIndex = pairIndex,
                ASelectionIndex = aIndex + 1,
                BSelectionIndex = bIndex + 1,
                TestName = testName,
                AName = aName,
                APath = BuildItemPath(itemA),
                BName = bName,
                BPath = BuildItemPath(itemB),
                Status = "planned",
            };
        }

        private static ClashTest AddClashTestCopyAndResolve(DocumentClash clash, ClashTest test, string expectedName)
        {
            if (clash == null || clash.TestsData == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective data is not available.");

            var before = ClashApiCompat.GetClashTests(clash).ToList();
            var beforeSet = new HashSet<ClashTest>(before);
            ClashApiCompat.AddClashTestCopy(clash.TestsData, test);
            var after = ClashApiCompat.GetClashTests(clash).ToList();
            var created = after
                .LastOrDefault(candidate => !beforeSet.Contains(candidate) &&
                                            string.Equals(candidate.DisplayName, expectedName, StringComparison.OrdinalIgnoreCase))
                          ?? after.LastOrDefault(candidate => string.Equals(candidate.DisplayName, expectedName, StringComparison.OrdinalIgnoreCase));

            if (created == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Created Clash Detective test could not be resolved: " + expectedName);

            return created;
        }

        private static ClashTest FindClashTestByName(IList<ClashTest> tests, string expectedName)
        {
            if (tests == null || string.IsNullOrWhiteSpace(expectedName))
                return null;

            for (var i = tests.Count - 1; i >= 0; i--)
            {
                var test = tests[i];
                if (test != null && string.Equals(SafeString(() => test.DisplayName), expectedName, StringComparison.OrdinalIgnoreCase))
                    return test;
            }

            return null;
        }

        private static int GetClashTestIndexByName(IList<ClashTest> tests, string expectedName)
        {
            if (tests == null || string.IsNullOrWhiteSpace(expectedName))
                return 0;

            for (var i = tests.Count - 1; i >= 0; i--)
            {
                var test = tests[i];
                if (test != null && string.Equals(SafeString(() => test.DisplayName), expectedName, StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            }

            return 0;
        }

        private static bool TryRestoreClashTestCopy(DocumentClashTests testsData, ClashTest copy)
        {
            string ignored;
            return TryRestoreClashTestCopy(testsData, copy, out ignored);
        }

        private static bool TryRestoreClashTestCopy(DocumentClashTests testsData, ClashTest copy, out string errorMessage)
        {
            if (testsData == null || copy == null)
            {
                errorMessage = "Clash Detective data or Clash Test copy is not available.";
                return false;
            }

            try
            {
                ClashApiCompat.AddClashTestCopy(testsData, copy);
                errorMessage = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static bool TryRemoveClashTest(DocumentClashTests testsData, ClashTest test)
        {
            try
            {
                ClashTestMutationService.RemoveTest(testsData, test);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static ClashPairTestPlanItem BuildClashPairTestPlanItem(ClashBboxCandidatePair pair, string prefix, int sequence)
        {
            var aName = pair == null || pair.A == null ? string.Empty : pair.A.Name;
            var bName = pair == null || pair.B == null ? string.Empty : pair.B.Name;
            var pairIndex = pair == null || pair.Index < 1 ? sequence : pair.Index;
            return new ClashPairTestPlanItem
            {
                PairIndex = pairIndex,
                TestName = prefix + " " + sequence.ToString("0000", CultureInfo.InvariantCulture) + " - " +
                           SanitizeClashTestNamePart(aName, 36) + " vs " + SanitizeClashTestNamePart(bName, 36),
                AName = aName,
                APath = pair == null || pair.A == null ? string.Empty : pair.A.Path,
                BName = bName,
                BPath = pair == null || pair.B == null ? string.Empty : pair.B.Path,
            };
        }

        private static string SanitizeClashTestNamePart(string value, int maxLength)
        {
            return ClashRenumberNameHelper.SanitizeNamePart(value, maxLength);
        }

        private static bool TryResolveClashPairRoot(IDictionary<string, ClashBboxRootCandidate> rootsByPath, ClashBboxRootItem item, out ClashBboxRootCandidate root)
        {
            root = null;
            if (rootsByPath == null || item == null || string.IsNullOrWhiteSpace(item.Path))
                return false;

            return rootsByPath.TryGetValue(item.Path, out root) && root != null && root.Item != null;
        }

        private static string BuildClashRootResolutionError(ClashRootResolutionDiagnostic a, ClashRootResolutionDiagnostic b)
        {
            var messages = new List<string>();
            if (a != null && !string.Equals(a.Status, "resolved", StringComparison.OrdinalIgnoreCase))
                messages.Add("side=A; status=" + a.Status + "; path='" + (a.ProvidedPath ?? string.Empty) + "'; name='" + (a.ProvidedName ?? string.Empty) + "'; sourceFile='" + (a.ProvidedSourceFile ?? string.Empty) + "'; " + a.Message);
            if (b != null && !string.Equals(b.Status, "resolved", StringComparison.OrdinalIgnoreCase))
                messages.Add("side=B; status=" + b.Status + "; path='" + (b.ProvidedPath ?? string.Empty) + "'; name='" + (b.ProvidedName ?? string.Empty) + "'; sourceFile='" + (b.ProvidedSourceFile ?? string.Empty) + "'; " + b.Message);
            return string.Join(" | ", messages);
        }
    }
}
