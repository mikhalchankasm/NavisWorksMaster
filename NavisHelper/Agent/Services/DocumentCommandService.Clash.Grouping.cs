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

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        public ClashGroupResultsResponse ClashGroupResults(Document document, ClashGroupResultsRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashGroupResultsRequest();
            if (!HasRequestedClashTestScope(request.TestName, request.TestNames) &&
                (request.TestHandles == null || !request.TestHandles.Any(handle => !string.IsNullOrWhiteSpace(handle))))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Specify testName, testNames, or testHandles for clash_group_results.");

            var apply = request.Apply == true;
            if (apply && request.GroupOffset.GetValueOrDefault() > 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "apply=true may only use groupOffset=0 because paging must not repeat the full grouping mutation. Enumerate pages with apply=false.");
            var side = NormalizeClashGroupSide(request.GroupBySide);
            var groupBy = NormalizeClashGroupBy(request.GroupBy);
            var ancestorLevelsUp = NormalizeClashGroupAncestorLevelsUp(request.AncestorLevelsUp);
            var groupNameMode = NormalizeClashGroupNameMode(request.GroupNameMode);
            var groupNamePrefix = string.IsNullOrWhiteSpace(request.GroupNamePrefix) ? string.Empty : request.GroupNamePrefix.Trim();
            var includeNavisHelperSideTag = request.IncludeNavisHelperSideTag.GetValueOrDefault(true);
            var overwriteExisting = request.OverwriteExisting == true;
            var ungroupExistingFirst = request.UngroupExistingFirst == true;
            var minGroupSize = NormalizeClashGroupMinSize(request.MinGroupSize);
            var maxResults = ClampClashListResultsLimit(request.MaxResults);
            var previewRowsPerGroup = ClampClashGroupPreviewRowsPerGroup(request.PreviewRowsPerGroup);
            var groupOffset = Math.Max(0, request.GroupOffset.GetValueOrDefault(0));
            var groupLimit = Math.Max(1, Math.Min(MaxClashGroupPageLimit, request.GroupLimit.GetValueOrDefault(DefaultClashGroupPageLimit)));
            var aggregateOnly = request.AggregateOnly == true;
            var excludeItemNameContains = NormalizeTextFilters(request.ExcludeItemNameContains);
            var statusFilterPlan = ClashStatusFilterHelper.Normalize(request.StatusFilters, request.IncludeAllStatuses == true, true);
            var statusFilters = statusFilterPlan.StatusFilters;
            var includeAllStatuses = statusFilterPlan.IncludeAllStatuses;
            var fullVerbosity = IsFullClashVerbosity(request.Verbosity);

            var clash = document.GetClash();
            if (clash == null || clash.TestsData == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective data is not available.");

            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            var matchedTests = ResolveClashTests(tests, request.TestName, request.TestNames, request.TestHandles, null, null, true).ToList();
            var response = new ClashGroupResultsResponse
            {
                Applied = apply,
                RequestedTestName = FormatRequestedTestNames(request.TestName, request.TestNames, request.TestHandles),
                MatchedTestCount = matchedTests.Count,
                MaxResults = maxResults,
                GroupBySide = side,
                GroupBy = groupBy,
                AncestorLevelsUp = ancestorLevelsUp,
                GroupNameMode = groupNameMode,
                GroupNamePrefix = groupNamePrefix,
                IncludeNavisHelperSideTag = includeNavisHelperSideTag,
                OverwriteExisting = overwriteExisting,
                UngroupExistingFirst = ungroupExistingFirst,
                MinGroupSize = minGroupSize,
                PreviewRowsPerGroup = previewRowsPerGroup,
                GroupOffset = groupOffset,
                GroupLimit = groupLimit,
                AggregateOnly = aggregateOnly,
                LargeGroupingThreshold = LargeClashGroupingConfirmationThreshold,
                ExcludeItemNameContains = excludeItemNameContains,
            };
            if (statusFilterPlan.ShouldWarnIncludeAllOverrides)
                response.Warnings.Add("includeAllStatuses=true overrides the provided statusFilters.");

            var plansByTest = new List<ClashGroupTestPlan>();
            foreach (var test in matchedTests)
            {
                var testIndex = GetClashTestIndex(tests, test);
                var rows = new List<ClashGroupWorkItem>();
                var resultIndex = 0;
                foreach (var workItem in EnumerateClashReportWorkItems(test, test.DisplayName ?? string.Empty, string.Empty))
                {
                    resultIndex++;
                    workItem.TestIndex = testIndex;
                    workItem.ResultIndex = resultIndex;
                    response.TotalResultCount++;
                    IncrementStatusCount(response.TotalStatusCounts, workItem.Status);

                    if (!includeAllStatuses && !MatchesClashStatusFilter(statusFilters, workItem.Status))
                        continue;
                    if (request.IncludeIgnored != true && ClashWorkflowService.IsIgnoredResult(workItem.Result))
                        continue;

                    IncrementStatusCount(response.MatchedStatusCounts, workItem.Status);
                    response.MatchedResultCount++;

                    if (MatchesExcludedClashItemName(workItem.Result, excludeItemNameContains, response.ExcludedByItemNameCounts))
                    {
                        response.ExcludedByItemNameCount++;
                        continue;
                    }

                    if (rows.Count >= maxResults)
                    {
                        response.ResultsTruncated = true;
                        continue;
                    }

                    var seed = ResolveClashGroupOwner(workItem.Result, side, 0);
                    var owner = string.Equals(groupBy, "ancestor", StringComparison.OrdinalIgnoreCase)
                        ? AscendClashGroupOwner(seed, ancestorLevelsUp)
                        : seed == null ? null : seed.Model == null ? null : seed.Model.RootItem;
                    if (owner == null)
                    {
                        response.SkippedResultCount++;
                        continue;
                    }

                    var row = new ClashGroupWorkItem
                    {
                        WorkItem = workItem,
                        Owner = owner,
                        OwnerName = GetItemDisplayName(owner),
                        OwnerPath = BuildItemPath(owner),
                    };
                    PopulateClashRootIdentity(row, seed ?? owner, groupBy);
                    if ((string.Equals(groupBy, "root", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(row.RootId)) ||
                        (string.Equals(groupBy, "source_file", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(row.SourceFile)))
                    {
                        response.SkippedResultCount++;
                        continue;
                    }
                    rows.Add(row);
                    response.AnalyzedResultCount++;
                    IncrementStatusCount(response.ReturnedStatusCounts, workItem.Status);
                }

                if (response.ResultsTruncated)
                    response.Warnings.Add((test.DisplayName ?? BuildClashTestHandle(testIndex)) + ": matched clash results exceeded maxResults; grouping plan was built from the first " + maxResults.ToString(CultureInfo.InvariantCulture) + " filtered results.");

                var testPlan = BuildClashGroupTestPlan(test, testIndex, rows, side, groupBy, groupNameMode, groupNamePrefix, includeNavisHelperSideTag, minGroupSize, previewRowsPerGroup);
                plansByTest.Add(testPlan);
                foreach (var groupPlan in testPlan.Groups)
                    response.Groups.Add(groupPlan.Item);
            }

            for (var index = 0; index < response.Groups.Count; index++)
                response.Groups[index].Index = index + 1;
            response.PlannedGroupCount = response.Groups.Count;
            if (!fullVerbosity)
                CompactClashPreviewRows(response.Groups.SelectMany(group => group.PreviewRows));

            if (apply && response.AnalyzedResultCount > LargeClashGroupingConfirmationThreshold && request.ConfirmLargeGrouping != true)
            {
                response.Applied = false;
                response.LargeGroupingConfirmationRequired = true;
                response.Message = "Grouping scope exceeds " + LargeClashGroupingConfirmationThreshold.ToString(CultureInfo.InvariantCulture) + " results. Re-run with confirmLargeGrouping=true after reviewing the dry-run plan.";
                foreach (var item in response.Groups)
                    item.Status = "confirmation_required";
                return ShapeClashGroupResponse(response, groupOffset, groupLimit, aggregateOnly);
            }

            foreach (var testPlan in plansByTest)
                MarkClashGroupConflicts(testPlan, overwriteExisting || ungroupExistingFirst, response);

            if (!apply)
            {
                response.Message = "Dry-run only. Re-run with apply=true to create real Clash Detective groups.";
                return ShapeClashGroupResponse(response, groupOffset, groupLimit, aggregateOnly);
            }

            using (var transaction = document.BeginTransaction("NavisHelper MCP Clash Grouping"))
            {
                foreach (var testPlan in plansByTest)
                {
                    if (ungroupExistingFirst)
                        ClashGroupMutationService.RemoveExistingGroups(clash.TestsData, testPlan.Test, side, groupNamePrefix);

                    foreach (var groupPlan in testPlan.Groups)
                    {
                        var item = groupPlan.Item;
                        if (string.Equals(item.Status, "conflict", StringComparison.OrdinalIgnoreCase))
                            continue;

                        try
                        {
                            var group = ClashGroupMutationService.FindOrCreateGroup(clash.TestsData, testPlan.Test, item.GroupName);
                            var moved = ClashGroupMutationService.RebuildGroup(clash.TestsData, testPlan.Test, group, groupPlan.Results);
                            item.Applied = true;
                            item.MovedResultCount = moved;
                            item.Status = "applied";
                            response.AppliedGroupCount++;
                            response.MovedResultCount += moved;
                        }
                        catch (Exception ex)
                        {
                            item.Applied = false;
                            item.Status = "error";
                            item.ErrorMessage = ex.Message;
                            response.Warnings.Add((item.TestName ?? item.TestHandle) + " / " + (item.GroupName ?? string.Empty) + ": " + ex.Message);
                        }
                    }

                    var removedEmptyGroups = ClashGroupMutationService.RemoveEmptyGroups(
                        clash.TestsData,
                        testPlan.Test,
                        side,
                        testPlan.Groups.Select(plan => plan == null || plan.Item == null ? string.Empty : plan.Item.GroupName));
                    if (removedEmptyGroups > 0)
                        response.Warnings.Add((testPlan.Test == null ? BuildClashTestHandle(testPlan.TestIndex) : testPlan.Test.DisplayName ?? BuildClashTestHandle(testPlan.TestIndex)) + ": removed " + removedEmptyGroups.ToString(CultureInfo.InvariantCulture) + " empty NavisHelper group(s) left after regrouping.");
                }

                transaction.Commit();
            }

            response.Message = "Created/updated " + response.AppliedGroupCount.ToString(CultureInfo.InvariantCulture) + " Clash Detective groups; moved " + response.MovedResultCount.ToString(CultureInfo.InvariantCulture) + " results.";
            return ShapeClashGroupResponse(response, groupOffset, groupLimit, aggregateOnly);
        }

        private static ClashGroupResultsResponse ShapeClashGroupResponse(
            ClashGroupResultsResponse response,
            int groupOffset,
            int groupLimit,
            bool aggregateOnly)
        {
            var allGroups = response.Groups ?? new List<ClashGroupPlanItem>();
            var page = allGroups.Skip(groupOffset).Take(groupLimit).ToList();
            if (aggregateOnly)
            {
                foreach (var group in page)
                    group.PreviewRows.Clear();
            }

            response.Groups = page;
            response.ReturnedGroupCount = page.Count;
            response.GroupOffset = groupOffset;
            response.GroupLimit = groupLimit;
            response.PlannedGroupCount = allGroups.Count;
            response.HasMoreGroups = page.Count > 0 && groupOffset + page.Count < allGroups.Count;
            response.NextGroupOffset = response.HasMoreGroups ? (int?)(groupOffset + page.Count) : null;
            response.GroupsTruncated = groupOffset > 0 || response.HasMoreGroups;
            response.Truncated = response.ResultsTruncated || response.GroupsTruncated;
            if (response.HasMoreGroups)
                response.Warnings.Add("The groups array is paged. Continue with groupOffset=" + response.NextGroupOffset.Value.ToString(CultureInfo.InvariantCulture) + ".");
            return response;
        }

        public ClashRenumberResultsResponse ClashRenumberResults(Document document, ClashRenumberResultsRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashRenumberResultsRequest();
            if (!HasRequestedClashTestScope(request.TestName, request.TestNames) &&
                (request.TestHandles == null || !request.TestHandles.Any(handle => !string.IsNullOrWhiteSpace(handle))))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Specify testName, testNames, or testHandles for clash_renumber_results.");

            var apply = request.Apply == true;
            var scope = NormalizeClashRenumberScope(request.Scope);
            var orderBy = NormalizeClashRenumberOrder(request.OrderBy);
            var startNumber = NormalizeClashRenumberStart(request.StartNumber);
            var numberWidth = NormalizeClashRenumberWidth(request.NumberWidth);
            var separator = request.Separator ?? " - ";
            var prefix = request.Prefix ?? string.Empty;
            var suffix = request.Suffix ?? string.Empty;
            var preserveExistingName = request.PreserveExistingName.GetValueOrDefault(true);
            var stripExistingNumber = request.StripExistingNumber.GetValueOrDefault(true);
            var includeGroups = request.IncludeGroups.GetValueOrDefault(true);
            var includeResults = request.IncludeResults.GetValueOrDefault(true);
            var includeEmptyGroups = request.IncludeEmptyGroups.GetValueOrDefault(false);
            var limit = ClampClashRenumberLimit(request.Limit);

            if (!includeGroups && !includeResults)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "At least one of includeGroups/includeResults must be true.");

            var clash = document.GetClash();
            if (clash == null || clash.TestsData == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective data is not available.");

            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            var matchedTests = ResolveClashTests(tests, request.TestName, request.TestNames, request.TestHandles, null, null, true).ToList();
            var response = new ClashRenumberResultsResponse
            {
                Applied = apply,
                RequestedTestName = FormatRequestedTestNames(request.TestName, request.TestNames, request.TestHandles),
                MatchedTestCount = matchedTests.Count,
                Scope = scope,
                OrderBy = orderBy,
                StartNumber = startNumber,
                NumberWidth = numberWidth,
                Separator = separator,
                Prefix = prefix,
                Suffix = suffix,
                PreserveExistingName = preserveExistingName,
                StripExistingNumber = stripExistingNumber,
                IncludeGroups = includeGroups,
                IncludeResults = includeResults,
                IncludeEmptyGroups = includeEmptyGroups,
                Limit = limit,
            };

            var planEntries = new List<ClashRenumberEntry>();
            foreach (var test in matchedTests)
            {
                var testIndex = GetClashTestIndex(tests, test);
                var entries = EnumerateClashRenumberEntries(test, testIndex, scope, includeGroups, includeResults, includeEmptyGroups).ToList();
                if (string.Equals(orderBy, ClashRenumberOrderName, StringComparison.OrdinalIgnoreCase))
                    entries = entries.OrderBy(entry => entry.OldName ?? string.Empty, NaturalStringComparer.Instance).ThenBy(entry => entry.GroupPath ?? string.Empty, NaturalStringComparer.Instance).ToList();

                planEntries.AddRange(entries);
            }

            if (planEntries.Count > limit)
            {
                response.Truncated = true;
                response.Warnings.Add("Renumber scope exceeded limit; only first " + limit.ToString(CultureInfo.InvariantCulture) + " items are planned.");
                planEntries = planEntries.Take(limit).ToList();
            }

            var plan = ClashRenumberPlanHelper.BuildPlan(
                planEntries.Select(entry => new ClashRenumberPlanSource
                {
                    TestIndex = entry.TestIndex,
                    TestHandle = BuildClashTestHandle(entry.TestIndex),
                    TestName = entry.TestName,
                    ItemType = entry.ItemType,
                    GroupPath = entry.GroupPath,
                    OldName = entry.OldName,
                }),
                startNumber,
                numberWidth,
                prefix,
                suffix,
                separator,
                preserveExistingName,
                stripExistingNumber);

            for (var index = 0; index < planEntries.Count; index++)
            {
                var entry = planEntries[index];
                entry.Item = plan.Items[index];
                response.Items.Add(entry.Item);
            }

            response.PlannedRenameCount = plan.PlannedRenameCount;
            response.SkippedCount = plan.SkippedCount;

            if (!apply)
            {
                response.Message = "Dry-run only. Re-run with apply=true and confirmRename=true to rename Clash Detective groups/results.";
                return response;
            }

            if (request.ConfirmRename != true)
            {
                response.Applied = false;
                response.ConfirmationRequired = true;
                response.Message = "Renaming Clash Detective groups/results is destructive. Re-run with confirmRename=true after reviewing the dry-run plan.";
                foreach (var item in response.Items.Where(item => string.Equals(item.Status, "planned", StringComparison.OrdinalIgnoreCase)))
                    item.Status = "confirmation_required";
                return response;
            }

            using (var transaction = document.BeginTransaction("NavisHelper MCP Clash Renumber"))
            {
                var mutationResult = ClashRenumberMutationService.ApplyRenames(
                    clash.TestsData,
                    planEntries.Select(entry => new ClashRenumberMutationItem
                    {
                        SavedItem = entry.SavedItem,
                        Item = entry.Item,
                    }));
                response.RenamedCount += mutationResult.RenamedCount;
                foreach (var warning in mutationResult.Warnings)
                    response.Warnings.Add(warning);

                transaction.Commit();
            }

            response.Message = "Renamed " + response.RenamedCount.ToString(CultureInfo.InvariantCulture) + " Clash Detective item(s).";
            return response;
        }

        public ClashManageTestsResponse ClashManageTests(Document document, ClashManageTestsRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashManageTestsRequest();
            var operation = NormalizeClashTestOperation(request.Operation);
            var apply = request.Apply == true;
            var newTestType = string.Equals(operation, "set_settings", StringComparison.OrdinalIgnoreCase)
                ? NormalizeClashTestType(request.TestType)
                : (ClashTestType?)null;
            var newToleranceUnits = string.Equals(operation, "set_settings", StringComparison.OrdinalIgnoreCase) && request.ToleranceMm.HasValue
                ? SectionBoxHelper.MmToDocUnits(NormalizeNonNegativeDouble(request.ToleranceMm.Value, "toleranceMm"))
                : (double?)null;
            var clash = document.GetClash();
            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            var matchedTests = ResolveClashTests(tests, request.TestName, request.TestNames, request.TestHandles, request.NamePrefix, request.FirstN, true).ToList();
            var notRunExcludedCount = 0;
            if (request.OnlyWithTotal.HasValue)
            {
                var expectedTotal = request.OnlyWithTotal.Value;
                if (expectedTotal < 0)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "onlyWithTotal must be non-negative.");
                notRunExcludedCount = matchedTests.Count(test => !test.LastRun.HasValue);
                matchedTests = matchedTests.Where(test =>
                {
                    if (!test.LastRun.HasValue)
                        return false;
                    var summary = new ClashTestSummary();
                    AccumulateClashResults(test.Children, summary, false);
                    return summary.Total == expectedTotal;
                }).ToList();
            }
            if (string.Equals(operation, "rename_batch", StringComparison.OrdinalIgnoreCase))
            {
                if ((request.Renames?.Count ?? 0) > 0 && !string.IsNullOrWhiteSpace(request.NamePattern))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "rename_batch accepts either renames or namePattern, not both.");
                if ((request.Renames == null || request.Renames.Count == 0) &&
                    !string.IsNullOrWhiteSpace(request.NamePattern))
                {
                    request.Renames = BuildPatternClashTestRenames(
                        tests,
                        matchedTests,
                        request.NamePattern,
                        request.RenameStartIndex.GetValueOrDefault(1),
                        request.RenameFromEnd == true);
                }
                var renameResponse = ClashManageRenameBatch(document, clash.TestsData, tests, request, apply);
                if (notRunExcludedCount > 0)
                    renameResponse.Warnings.Add(notRunExcludedCount + " test(s) were excluded because they have never been run.");
                return renameResponse;
            }
            ValidateHandleOnlyClashManageScope(request, matchedTests);
            var response = new ClashManageTestsResponse
            {
                Applied = apply,
                Operation = operation,
                RequestedTestName = FormatRequestedTestNames(request.TestName, request.TestNames, request.TestHandles, request.NamePrefix, request.FirstN),
                MatchedTestCount = matchedTests.Count,
            };
            if (notRunExcludedCount > 0)
                response.Warnings.Add(notRunExcludedCount + " test(s) were excluded because they have never been run.");

            if (matchedTests.Count == 0)
            {
                response.Message = "No matching Clash Detective tests found.";
                return response;
            }

            if (string.Equals(operation, "rename", StringComparison.OrdinalIgnoreCase))
            {
                if (matchedTests.Count != 1)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "rename requires exactly one matched test.");
                if (string.IsNullOrWhiteSpace(request.NewName))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "rename requires newName.");
            }
            if (string.Equals(operation, "move", StringComparison.OrdinalIgnoreCase))
            {
                if (matchedTests.Count != 1)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "move requires exactly one matched test.");
                if (!request.TargetIndex.HasValue || request.TargetIndex.Value < 1)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "move requires targetIndex greater than zero.");
            }
            if (string.Equals(operation, "set_settings", StringComparison.OrdinalIgnoreCase) && !newToleranceUnits.HasValue && !newTestType.HasValue)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "set_settings requires toleranceMm and/or testType.");
            if ((string.Equals(operation, "delete", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(operation, "reset", StringComparison.OrdinalIgnoreCase)) &&
                apply &&
                HasOnlyFirstNClashTestScope(request))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, operation + " with apply=true requires testName, testNames, testHandles, or namePrefix in addition to firstN.");

            if (string.Equals(operation, "delete", StringComparison.OrdinalIgnoreCase) && !apply)
                response.Warnings.Add("Dry-run only. delete is destructive; pass apply=true only after reviewing matched tests.");
            if (string.Equals(operation, "reset", StringComparison.OrdinalIgnoreCase) && !apply)
                response.Warnings.Add("Dry-run only. reset clears Clash Detective results/status state; pass apply=true only after reviewing matched tests.");
            if (string.Equals(operation, "set_settings", StringComparison.OrdinalIgnoreCase))
                response.Warnings.Add("Changing Clash Test tolerance/type updates Clash Detective test settings; re-run tests before relying on existing results.");

            if (string.Equals(operation, "move", StringComparison.OrdinalIgnoreCase))
                return ClashManageMoveTest(clash.TestsData, tests, matchedTests[0], request, response, apply);
            if (string.Equals(operation, "sort", StringComparison.OrdinalIgnoreCase))
                return ClashManageSortTests(clash.TestsData, tests, matchedTests, request, response, apply);

            var resultsByTest = new Dictionary<ClashTest, ClashTestOperationResult>();
            foreach (var test in matchedTests)
            {
                var testIndex = GetClashTestIndex(tests, test);
                var item = new ClashTestOperationResult
                {
                    TestIndex = testIndex,
                    Handle = BuildClashTestHandle(testIndex),
                    TestHandle = BuildClashTestHandle(testIndex),
                    Name = test.DisplayName ?? string.Empty,
                    Operation = operation,
                    Applied = apply,
                    Status = apply ? "pending" : "planned",
                    OldToleranceMm = DocUnitsToMm(SafeDouble(() => test.Tolerance)),
                    NewToleranceMm = request.ToleranceMm.HasValue ? request.ToleranceMm.Value : (double?)null,
                    OldTestType = SafeString(() => test.TestType.ToString()),
                    NewTestType = newTestType.HasValue ? newTestType.Value.ToString() : string.Empty,
                };
                response.Tests.Add(item);
                if (test != null && !resultsByTest.ContainsKey(test))
                    resultsByTest.Add(test, item);

                if (!apply)
                    continue;
                if (string.Equals(operation, "run", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    ClashTestMutationService.ApplyOperation(clash.TestsData, test, operation, request.NewName, newToleranceUnits, newTestType);
                    item.Status = "applied";
                    response.AffectedTestCount++;
                }
                catch (Exception ex)
                {
                    item.Status = "failed";
                    item.ErrorMessage = ex.Message;
                    response.Warnings.Add((test.DisplayName ?? BuildClashTestHandle(testIndex)) + ": " + ex.Message);
                }
            }

            if (apply && string.Equals(operation, "run", StringComparison.OrdinalIgnoreCase))
            {
                RunClashTestsWithProgress(clash.TestsData, matchedTests, "NavisHelper: run Clash tests",
                    test =>
                    {
                        ClashTestOperationResult item;
                        if (test != null && resultsByTest.TryGetValue(test, out item))
                            item.Status = "applied";
                        response.AffectedTestCount++;
                    },
                    (test, ex) =>
                    {
                        ClashTestOperationResult item;
                        if (test != null && resultsByTest.TryGetValue(test, out item))
                        {
                            item.Status = "failed";
                            item.ErrorMessage = ex.Message;
                        }
                        response.Warnings.Add(((test == null ? null : test.DisplayName) ?? "clash-test") + ": " + ex.Message);
                    },
                    () =>
                    {
                        foreach (var item in response.Tests.Where(item => string.Equals(item.Status, "pending", StringComparison.OrdinalIgnoreCase)))
                            item.Status = "cancelled";
                        response.Warnings.Add("Clash test run was cancelled before all matched tests were run.");
                    });
            }

            if (apply && string.Equals(operation, "delete", StringComparison.OrdinalIgnoreCase) && response.AffectedTestCount > 0)
            {
                var expectedRemainingCount = Math.Max(0, tests.Count - response.AffectedTestCount);
                int actualRemainingCount;
                if (!ClashTestMutationService.WaitForTestCountAtMost(clash, expectedRemainingCount, out actualRemainingCount))
                {
                    response.Warnings.Add(
                        "Clash Detective delete API returned, but the visible test list did not stabilize before timeout. " +
                        "Expected at most " + expectedRemainingCount.ToString(CultureInfo.InvariantCulture) +
                        " remaining test(s), current count is " + actualRemainingCount.ToString(CultureInfo.InvariantCulture) + ".");
                }
            }

            if (!apply)
                response.Message = "Dry-run only. Pass apply=true to modify Clash Detective tests.";
            else
                response.Message = "Applied " + response.AffectedTestCount + " of " + response.MatchedTestCount + " Clash Detective test operation(s).";

            return response;
        }

        private static List<ClashTestRenameRequest> BuildPatternClashTestRenames(
            IList<ClashTest> allTests,
            IList<ClashTest> matchedTests,
            string pattern,
            int startIndex,
            bool fromEnd)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "rename_batch requires renames or namePattern.");
            if (startIndex < 1)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "renameStartIndex must be greater than zero.");

            var result = new List<ClashTestRenameRequest>();
            for (var position = 0; position < matchedTests.Count; position++)
            {
                var test = matchedTests[position];
                var oldName = test == null ? string.Empty : test.DisplayName ?? string.Empty;
                string aName;
                string bName;
                SplitGeneratedClashPairName(oldName, out aName, out bName);
                if ((pattern.IndexOf("{aName", StringComparison.Ordinal) >= 0 ||
                     pattern.IndexOf("{bName", StringComparison.Ordinal) >= 0 ||
                     pattern.IndexOf("{aCode", StringComparison.Ordinal) >= 0 ||
                     pattern.IndexOf("{bCode", StringComparison.Ordinal) >= 0) &&
                    string.IsNullOrWhiteSpace(bName))
                {
                    throw new AgentCommandException(
                        ErrorCodes.SchemaViolation,
                        "namePattern uses {aName}/{bName}, but the current Clash Test name does not contain a recognized ' vs ' or '-vs-' pair: " + oldName);
                }
                var number = fromEnd
                    ? startIndex + matchedTests.Count - position - 1
                    : startIndex + position;
                var newName = PairNameTemplateFormatter
                    .Format(pattern.Replace("{name}", oldName), number, aName, bName)
                    .Trim();
                if (string.IsNullOrWhiteSpace(newName))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "namePattern produced an empty Clash Test name.");

                result.Add(new ClashTestRenameRequest
                {
                    TestHandle = BuildClashTestHandle(GetClashTestIndex(allTests, test)),
                    NewName = newName,
                });
            }
            return result;
        }

        private static void SplitGeneratedClashPairName(string name, out string aName, out string bName)
        {
            var pairText = name ?? string.Empty;
            var versusToken = " vs ";
            var versus = pairText.IndexOf(versusToken, StringComparison.OrdinalIgnoreCase);
            if (versus < 0)
            {
                versusToken = "-vs-";
                versus = pairText.IndexOf(versusToken, StringComparison.OrdinalIgnoreCase);
            }
            if (versus < 0)
            {
                aName = pairText;
                bName = string.Empty;
                return;
            }
            var left = pairText.Substring(0, versus);
            var generatedPrefixSeparator = left.LastIndexOf(" - ", StringComparison.Ordinal);
            if (generatedPrefixSeparator >= 0)
                left = left.Substring(generatedPrefixSeparator + 3);
            aName = left.Trim();
            bName = pairText.Substring(versus + versusToken.Length).Trim();
        }
    }
}
