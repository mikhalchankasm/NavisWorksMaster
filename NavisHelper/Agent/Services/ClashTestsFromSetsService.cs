using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace NavisHelper.Agent.Services
{
    internal sealed class ClashTestsFromSetsService
    {
        private const int DefaultLimit = 200;
        private const int MaximumLimit = 500;
        private const long MaximumPlanBytes = 10L * 1024L * 1024L;

        public ClashTestsFromSetsResponse Execute(Document document, ClashTestsFromSetsRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            request = request ?? new ClashTestsFromSetsRequest();
            var apply = request.Apply == true;
            var limit = Math.Max(1, Math.Min(MaximumLimit, request.Limit.GetValueOrDefault(DefaultLimit)));
            List<string> inputWarnings;
            var pairs = LoadPairs(request, out inputWarnings);
            if (pairs.Count > limit)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Pair count exceeds limit=" + limit.ToString(CultureInfo.InvariantCulture) + ".");
            var template = string.IsNullOrWhiteSpace(request.PairNameTemplate)
                ? "{index|zeroPad:3} ИЗОЛ {aName}-{bName}"
                : request.PairNameTemplate.Trim();
            var startIndex = request.PairNameStartIndex.GetValueOrDefault(1);
            if (startIndex < 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "pairNameStartIndex must be non-negative.");
            var defaultTestType = ResolveTestType(request.TestType);
            var defaultToleranceMm = NormalizeTolerance(request.ToleranceMm);
            var continueOnError = request.ContinueOnError.GetValueOrDefault(true);

            var clash = document.GetClash();
            if (clash == null || clash.TestsData == null || clash.TestsData.Value == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective data is not available.");
            var existing = ClashApiCompat.GetClashTests(clash).ToList();
            var plannedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var response = new ClashTestsFromSetsResponse
            {
                Applied = apply,
                SideBinding = "selection_source",
                InputPairCount = pairs.Count,
                PairNameTemplate = template,
                PairNameStartIndex = startIndex,
                ToleranceMm = defaultToleranceMm,
                TestType = defaultTestType.ToString(),
                RunAfterCreate = request.RunAfterCreate == true,
                IgnoreSameFile = (request.IgnoreRules != null && request.IgnoreRules.SameFile == true) ||
                                 pairs.Any(pair => pair != null && pair.IgnoreRules != null && pair.IgnoreRules.SameFile == true),
            };
            response.Warnings.AddRange(inputWarnings);
            var resolvedReferenceCache = new Dictionary<string, ResolvedSelectionSetReference>(StringComparer.OrdinalIgnoreCase);
            var itemCountCache = new Dictionary<ResolvedSelectionSetReference, int>();
            var appliedMutations = new List<AppliedClashSetMutation>();
            var abortRemaining = false;

            for (var pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
            {
                var pair = pairs[pairIndex];
                var item = new ClashSetTestPlanItem { PairIndex = pairIndex + 1, SideBinding = "selection_source" };
                response.Tests.Add(item);
                if (abortRemaining)
                {
                    item.TestName = pair == null ? string.Empty : pair.Name;
                    item.Status = "not_evaluated_after_failure";
                    item.ErrorMessage = "Not evaluated because continueOnError=false and an earlier test failed.";
                    response.SkippedTestCount++;
                    continue;
                }
                try
                {
                    ResolvedSelectionSetReference a;
                    ResolvedSelectionSetReference b;
                    try
                    {
                        a = ResolveCached(document, pair == null ? null : pair.A, resolvedReferenceCache);
                    }
                    catch (Exception ex)
                    {
                        throw new AgentCommandException(GetErrorCode(ex), "Test '" + (pair == null ? string.Empty : pair.Name ?? string.Empty) + "' side A: " + ex.Message);
                    }
                    try
                    {
                        b = ResolveCached(document, pair == null ? null : pair.B, resolvedReferenceCache);
                    }
                    catch (Exception ex)
                    {
                        throw new AgentCommandException(GetErrorCode(ex), "Test '" + (pair == null ? string.Empty : pair.Name ?? string.Empty) + "' side B: " + ex.Message);
                    }
                    var testType = string.IsNullOrWhiteSpace(pair == null ? null : pair.TestType)
                        ? defaultTestType
                        : ResolveTestType(pair.TestType);
                    var toleranceMm = pair != null && pair.ToleranceMm.HasValue
                        ? NormalizeTolerance(pair.ToleranceMm)
                        : defaultToleranceMm;
                    var ignoreRules = pair != null && pair.IgnoreRules != null ? pair.IgnoreRules : request.IgnoreRules;
                    var aSelfIntersect = pair != null && pair.ASelfIntersect.GetValueOrDefault(false);
                    var bSelfIntersect = pair != null && pair.BSelfIntersect.GetValueOrDefault(false);
                    Populate(item, a, b);
                    item.TestType = testType.ToString();
                    item.ToleranceMm = toleranceMm;
                    item.ASelfIntersect = aSelfIntersect;
                    item.BSelfIntersect = bSelfIntersect;
                    item.SelectionAItemCount = GetItemCountCached(document, a, itemCountCache);
                    item.SelectionBItemCount = GetItemCountCached(document, b, itemCountCache);
                    item.TestName = string.IsNullOrWhiteSpace(pair.Name)
                        ? PairNameTemplateFormatter.Format(template, startIndex + pairIndex, a.Name, b.Name)
                        : pair.Name.Trim();
                    if (string.IsNullOrWhiteSpace(item.TestName))
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, "Resolved test name is empty.");
                    if (item.SelectionAItemCount == 0)
                        response.Warnings.Add(item.TestName + ": selection A is empty.");
                    if (item.SelectionBItemCount == 0)
                        response.Warnings.Add(item.TestName + ": selection B is empty.");

                    var previous = existing.FirstOrDefault(candidate => string.Equals(candidate.DisplayName, item.TestName, StringComparison.OrdinalIgnoreCase));
                    if (!plannedNames.Add(item.TestName))
                    {
                        item.Status = "conflict";
                        item.ErrorMessage = "Another pair in this request resolves to the same test name.";
                        response.ConflictTestCount++;
                        response.SkippedTestCount++;
                        if (apply && !continueOnError)
                        {
                            RollbackMutations(clash, appliedMutations, response);
                            abortRemaining = true;
                        }
                        continue;
                    }
                    if (previous != null && request.OverwriteExisting != true)
                    {
                        item.Status = "conflict";
                        item.ErrorMessage = "A Clash Detective test with this name already exists.";
                        response.ConflictTestCount++;
                        response.SkippedTestCount++;
                        if (apply && !continueOnError)
                        {
                            RollbackMutations(clash, appliedMutations, response);
                            abortRemaining = true;
                        }
                        continue;
                    }

                    response.PlannedTestCount++;
                    item.Status = apply ? "pending" : "planned";
                    if (!apply)
                        continue;

                    var previousCopy = previous == null ? null : previous.CreateCopy() as ClashTest;
                    var creationName = previous == null ? item.TestName : item.TestName + " [replacement " + Guid.NewGuid().ToString("N").Substring(0, 8) + "]";
                    var test = BuildTest(document, creationName, a, b, testType, toleranceMm, ignoreRules, aSelfIntersect, bSelfIntersect);
                    ClashTest created;
                    try
                    {
                        ClashApiCompat.AddClashTestCopy(clash.TestsData, test);
                        created = ClashApiCompat.GetClashTests(clash)
                            .LastOrDefault(candidate => string.Equals(candidate.DisplayName, creationName, StringComparison.OrdinalIgnoreCase));
                        if (created == null || !HasPreservedBinding(created.SelectionA.Selection, a) || !HasPreservedBinding(created.SelectionB.Selection, b))
                            throw new AgentCommandException(ErrorCodes.CommandFailed, "Created test did not preserve its Selection Source or explicit model-root binding.");
                        if (ignoreRules != null && ignoreRules.SameFile == true && !ClashNativeIgnoreRuleService.IsApplied(created))
                            throw new AgentCommandException(ErrorCodes.CommandFailed, "Created test did not preserve the native same-file ignore rule.");
                        if (ignoreRules != null && ignoreRules.SameFile == true)
                            response.IgnoreSameFileVerifiedTestCount++;
                    }
                    catch
                    {
                        var orphan = ClashApiCompat.GetClashTests(clash)
                            .LastOrDefault(candidate => string.Equals(candidate.DisplayName, creationName, StringComparison.OrdinalIgnoreCase));
                        if (orphan != null && (previous == null || !ReferenceEquals(orphan, previous)))
                            ClashTestMutationService.RemoveTest(clash.TestsData, orphan);
                        throw;
                    }
                    if (previous != null)
                    {
                        try
                        {
                            var currentPrevious = ClashApiCompat.GetClashTests(clash)
                                .FirstOrDefault(candidate => string.Equals(candidate.DisplayName, item.TestName, StringComparison.OrdinalIgnoreCase));
                            if (currentPrevious == null)
                                throw new InvalidOperationException("Existing same-name test became unavailable during replacement.");
                            ClashTestMutationService.RemoveTest(clash.TestsData, currentPrevious);
                            created = ClashApiCompat.GetClashTests(clash)
                                .FirstOrDefault(candidate => string.Equals(candidate.DisplayName, creationName, StringComparison.OrdinalIgnoreCase));
                            if (created == null)
                                throw new InvalidOperationException("Replacement test became unavailable before final rename.");
                            clash.TestsData.TestsEditDisplayName(created, item.TestName);
                        }
                        catch
                        {
                            var replacement = ClashApiCompat.GetClashTests(clash)
                                .FirstOrDefault(candidate => string.Equals(candidate.DisplayName, creationName, StringComparison.OrdinalIgnoreCase));
                            if (replacement != null)
                                ClashTestMutationService.RemoveTest(clash.TestsData, replacement);
                            if (!ClashApiCompat.GetClashTests(clash).Any(candidate => string.Equals(candidate.DisplayName, item.TestName, StringComparison.OrdinalIgnoreCase)) &&
                                previousCopy != null)
                                ClashApiCompat.AddClashTestCopy(clash.TestsData, previousCopy);
                            throw;
                        }
                        response.ReplacedTestCount++;
                    }
                    existing = ClashApiCompat.GetClashTests(clash).ToList();
                    var createdIndex = existing.FindIndex(candidate => string.Equals(candidate.DisplayName, item.TestName, StringComparison.OrdinalIgnoreCase));
                    item.TestHandle = createdIndex < 0 ? null : ClashHandleHelper.BuildTestHandle(createdIndex + 1);
                    item.Applied = true;
                    item.Replaced = previous != null;
                    item.Status = previous == null ? "created" : "replaced";
                    response.CreatedTestCount++;
                    appliedMutations.Add(new AppliedClashSetMutation
                    {
                        Item = item,
                        TestName = item.TestName,
                        PreviousCopy = previousCopy,
                    });
                }
                catch (Exception ex)
                {
                    item.Status = "failed";
                    item.ErrorMessage = ex.Message;
                    response.SkippedTestCount++;
                    response.Warnings.Add("Pair " + (pairIndex + 1).ToString(CultureInfo.InvariantCulture) + ": " + ex.Message);
                    if (apply && !continueOnError)
                    {
                        RollbackMutations(clash, appliedMutations, response);
                        abortRemaining = true;
                    }
                }
            }

            response.Message = apply
                ? "Created or replaced " + response.CreatedTestCount.ToString(CultureInfo.InvariantCulture) + " live set-bound Clash Detective test(s)."
                : "Dry-run only. Pass apply=true to create live set-bound Clash Detective tests.";
            return response;
        }

        private static ClashTest BuildTest(Document document, string name, ResolvedSelectionSetReference a, ResolvedSelectionSetReference b, ClashTestType type, double? toleranceMm, ClashNativeIgnoreRules ignoreRules, bool aSelfIntersect, bool bSelfIntersect)
        {
            var test = new ClashTest { DisplayName = name, TestType = type, Guid = Guid.Empty };
            if (toleranceMm.HasValue)
                test.Tolerance = SectionBoxHelper.MmToDocUnits(toleranceMm.Value);
            BindSelection(document, test.SelectionA.Selection, a);
            test.SelectionA.SelfIntersect = aSelfIntersect;
            BindSelection(document, test.SelectionB.Selection, b);
            test.SelectionB.SelfIntersect = bSelfIntersect;
            ClashNativeIgnoreRuleService.Apply(document, test, ignoreRules);
            return test;
        }

        private static void BindSelection(Document document, Selection target, ResolvedSelectionSetReference source)
        {
            if (source.ExplicitItems != null)
            {
                target.CopyFrom(source.ExplicitItems);
                return;
            }
            var sources = new SelectionSourceCollection();
            sources.Add(document.SelectionSets.CreateSelectionSource(source.Item));
            target.CopyFrom(sources);
        }

        private static int GetItemCount(Document document, ResolvedSelectionSetReference source)
        {
            return source.ExplicitItems == null ? source.Item.GetSelectedItems(document).Count : source.ExplicitItems.Count;
        }

        private static ResolvedSelectionSetReference ResolveCached(Document document, SelectionSetReference reference, IDictionary<string, ResolvedSelectionSetReference> cache)
        {
            if (reference == null)
                return SelectionSetReferenceResolver.Resolve(document, null);
            var key = string.Join("\n", reference.ItemId ?? string.Empty, reference.Path ?? string.Empty, reference.Name ?? string.Empty,
                reference.RootName ?? string.Empty, reference.SourceFile ?? string.Empty, reference.Occurrence.GetValueOrDefault().ToString(CultureInfo.InvariantCulture));
            ResolvedSelectionSetReference resolved;
            if (!cache.TryGetValue(key, out resolved))
            {
                resolved = SelectionSetReferenceResolver.Resolve(document, reference);
                cache[key] = resolved;
            }
            return resolved;
        }

        private static int GetItemCountCached(Document document, ResolvedSelectionSetReference source, IDictionary<ResolvedSelectionSetReference, int> cache)
        {
            int count;
            if (!cache.TryGetValue(source, out count))
            {
                count = GetItemCount(document, source);
                cache[source] = count;
            }
            return count;
        }

        private static bool HasPreservedBinding(Selection selection, ResolvedSelectionSetReference source)
        {
            return source.ExplicitItems != null ? !selection.IsClear : selection.HasSelectionSources;
        }

        private static void Populate(ClashSetTestPlanItem item, ResolvedSelectionSetReference a, ResolvedSelectionSetReference b)
        {
            item.AItemId = a.ItemId; item.AName = a.Name; item.APath = a.Path; item.AType = a.Type;
            item.BItemId = b.ItemId; item.BName = b.Name; item.BPath = b.Path; item.BType = b.Type;
        }

        private static ClashTestType ResolveTestType(string value)
        {
            var normalized = ClashTestTypeHelper.NormalizeTestType(value);
            if (!string.IsNullOrWhiteSpace(value) && normalized == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Unsupported testType: " + value);
            switch (normalized ?? ClashTestTypeHelper.Hard)
            {
                case ClashTestTypeHelper.HardConservative: return ClashTestType.HardConservative;
                case ClashTestTypeHelper.Clearance: return ClashTestType.Clearance;
                case ClashTestTypeHelper.Duplicate: return ClashTestType.Duplicate;
                default: return ClashTestType.Hard;
            }
        }

        private static List<ClashSetPair> LoadPairs(ClashTestsFromSetsRequest request, out List<string> warnings)
        {
            warnings = new List<string>();
            var inline = request.Pairs ?? new List<ClashSetPair>();
            if (!string.IsNullOrWhiteSpace(request.PlanPath) && inline.Count > 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Pass either pairs or planPath, not both.");
            if (string.IsNullOrWhiteSpace(request.PlanPath))
                return inline;
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.PlanPath.Trim()));
            var info = new FileInfo(path);
            if (!info.Exists)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "planPath does not exist: " + path);
            if (info.Length > MaximumPlanBytes)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "planPath exceeds 10 MB.");
            var token = JToken.Parse(File.ReadAllText(path));
            var schema = token.Type == JTokenType.Object ? (string)token["schema"] : null;
            if (!string.IsNullOrWhiteSpace(schema))
            {
                ClashTestTransferPlan plan;
                try
                {
                    var serializer = JsonSerializer.Create(new JsonSerializerSettings
                    {
                        ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
                    });
                    plan = token.ToObject<ClashTestTransferPlan>(serializer);
                    ClashTransferPlanHelper.Validate(plan);
                }
                catch (ClashTransferParseException ex)
                {
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
                }
                var unsupported = plan.Tests == null ? 0 : plan.Tests.Count(test => test == null || !test.Supported);
                if (unsupported > 0)
                    warnings.Add("Skipped " + unsupported.ToString(CultureInfo.InvariantCulture) + " unsupported transfer-plan test definition(s). See the plan warnings for details.");
                return ClashTransferPlanHelper.ToPairs(plan, false);
            }
            var array = token as JArray ?? token["pairs"] as JArray;
            if (array == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "planPath JSON must be an array or an object with a pairs array.");
            return array.ToObject<List<ClashSetPair>>() ?? new List<ClashSetPair>();
        }

        private static double? NormalizeTolerance(double? value)
        {
            if (!value.HasValue)
                return null;
            if (double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "toleranceMm must be a finite non-negative number.");
            if (value.Value < 0)
                return null;
            return value.Value;
        }

        private static string GetErrorCode(Exception exception)
        {
            var command = exception as AgentCommandException;
            return command == null ? ErrorCodes.CommandFailed : command.ErrorCode;
        }

        private static void RollbackMutations(DocumentClash clash, IList<AppliedClashSetMutation> mutations, ClashTestsFromSetsResponse response)
        {
            if (clash == null || clash.TestsData == null || mutations == null)
                return;
            for (var index = mutations.Count - 1; index >= 0; index--)
            {
                var mutation = mutations[index];
                try
                {
                    var current = ClashApiCompat.GetClashTests(clash)
                        .FirstOrDefault(test => string.Equals(test.DisplayName, mutation.TestName, StringComparison.OrdinalIgnoreCase));
                    if (current != null)
                        ClashTestMutationService.RemoveTest(clash.TestsData, current);
                    if (mutation.PreviousCopy != null)
                        ClashApiCompat.AddClashTestCopy(clash.TestsData, mutation.PreviousCopy);
                    mutation.Item.Applied = false;
                    mutation.Item.RolledBack = true;
                    mutation.Item.Status = "rolled_back";
                    response.RolledBackTestCount++;
                    response.CreatedTestCount = Math.Max(0, response.CreatedTestCount - 1);
                    if (mutation.PreviousCopy != null)
                        response.ReplacedTestCount = Math.Max(0, response.ReplacedTestCount - 1);
                }
                catch (Exception ex)
                {
                    response.Warnings.Add("Rollback failed for '" + mutation.TestName + "': " + ex.Message);
                }
            }
            mutations.Clear();
        }

        private sealed class AppliedClashSetMutation
        {
            public ClashSetTestPlanItem Item { get; set; }
            public string TestName { get; set; }
            public ClashTest PreviousCopy { get; set; }
        }
    }
}
