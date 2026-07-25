using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        private const int LargeClashStatusChangeThreshold = 500;

        public ClashGroupCustomResponse ClashGroupCustom(Document document, ClashGroupCustomRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashGroupCustomRequest();
            var testsData = document.GetClash().TestsData;
            var tests = ClashApiCompat.GetClashTests(document.GetClash()).ToList();
            int testIndex;
            var test = ResolveRequiredClashTestHandle(tests, request.TestHandle, out testIndex);
            var response = new ClashGroupCustomResponse
            {
                Applied = false,
                TestHandle = BuildClashTestHandle(testIndex),
                TestName = test.DisplayName ?? string.Empty,
                GroupName = (request.GroupName ?? string.Empty).Trim(),
            };

            if (string.IsNullOrWhiteSpace(response.GroupName))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "groupName is required.");

            var requestedHandles = NormalizeRequiredHandles(request.ResultHandles, "resultHandles");
            response.RequestedResultCount = requestedHandles.Count;
            var indexedResults = BuildIndexedClashResults(test, testIndex);
            var resultByHandle = indexedResults.ToDictionary(item => item.Handle, item => item.Result, StringComparer.OrdinalIgnoreCase);
            var resolvedResults = new List<ClashResult>();
            foreach (var handle in requestedHandles)
            {
                int handleTestIndex;
                int ignoredResultIndex;
                if (!ClashHandleHelper.TryParseResultHandle(handle, out handleTestIndex, out ignoredResultIndex) || handleTestIndex != testIndex)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Result handle does not belong to " + response.TestHandle + ": " + handle);

                ClashResult result;
                if (!resultByHandle.TryGetValue(handle, out result))
                    response.MissingResultHandles.Add(handle);
                else
                    resolvedResults.Add(result);
            }

            if (response.MissingResultHandles.Count > 0)
            {
                response.Message = "No changes were applied because one or more result handles were not found.";
                return response;
            }

            var existing = ClashGroupMutationService.FindGroup(test, response.GroupName);
            response.ExistingGroupFound = existing != null;
            if (existing != null && request.OverwriteExisting != true)
            {
                response.Message = "A ClashResultGroup with this name already exists. Re-run with overwriteExisting=true to rebuild it.";
                return response;
            }

            if (request.Apply != true)
            {
                response.Message = "Dry-run only. Pass apply=true to create the group.";
                return response;
            }

            ClashResultGroup group;
            using (var transaction = document.BeginTransaction("NavisHelper MCP Custom Clash Group"))
            {
                group = existing ?? ClashGroupMutationService.FindOrCreateGroup(testsData, test, response.GroupName);
                response.MovedResultCount = ClashGroupMutationService.RebuildGroup(testsData, test, group, resolvedResults);
                transaction.Commit();
            }
            response.Applied = true;
            response.GroupHandle = FindClashGroupHandle(test, testIndex, group);
            response.Message = "Grouped " + resolvedResults.Count.ToString(CultureInfo.InvariantCulture) + " clash result(s).";
            return response;
        }

        public ClashUngroupResponse ClashUngroup(Document document, ClashUngroupRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashUngroupRequest();
            var clash = document.GetClash();
            var testsData = clash.TestsData;
            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            int testIndex;
            var test = ResolveRequiredClashTestHandle(tests, request.TestHandle, out testIndex);
            var response = new ClashUngroupResponse
            {
                TestHandle = BuildClashTestHandle(testIndex),
                TestName = test.DisplayName ?? string.Empty,
            };

            var groups = BuildIndexedClashGroups(test, testIndex);
            var requestedHandles = (request.GroupHandles ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var selected = new List<IndexedClashGroup>();
            if (requestedHandles.Count > 0)
            {
                foreach (var handle in requestedHandles)
                {
                    int handleTestIndex;
                    int ignoredGroupIndex;
                    if (!TryParseClashGroupHandle(handle, out handleTestIndex, out ignoredGroupIndex) || handleTestIndex != testIndex)
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, "Group handle does not belong to " + response.TestHandle + ": " + handle);
                    var match = groups.FirstOrDefault(item => string.Equals(item.Handle, handle, StringComparison.OrdinalIgnoreCase));
                    if (match == null)
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, "Clash group was not found: " + handle);
                    selected.Add(match);
                }
            }
            else if (!string.IsNullOrWhiteSpace(request.GroupNamePrefix))
            {
                selected.AddRange(groups.Where(item =>
                    (item.Group.DisplayName ?? string.Empty).StartsWith(request.GroupNamePrefix.Trim(), StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Specify groupHandles or groupNamePrefix.");
            }

            selected = selected.Distinct(IndexedClashGroupComparer.Instance).ToList();
            response.MatchedGroupCount = selected.Count;
            foreach (var item in selected)
            {
                response.Groups.Add(new ClashGroupReference
                {
                    GroupHandle = item.Handle,
                    GroupName = item.Group.DisplayName ?? string.Empty,
                    GroupPath = item.Path,
                    ResultCount = ClashWorkflowService.EnumerateResults(item.Group).Count,
                });
            }

            if (request.Apply != true)
            {
                response.Message = "Dry-run only. Pass apply=true to ungroup the matched groups.";
                return response;
            }

            using (var transaction = document.BeginTransaction("NavisHelper MCP Clash Ungroup"))
            {
                foreach (var item in selected.OrderByDescending(value => value.Index))
                {
                    response.MovedResultCount += ClashGroupMutationService.UngroupGroup(testsData, test, item.Group);
                    response.UngroupedGroupCount++;
                }
                transaction.Commit();
            }

            response.Applied = true;
            response.Message = "Ungrouped " + response.UngroupedGroupCount.ToString(CultureInfo.InvariantCulture) + " group(s).";
            return response;
        }

        public ClashSetStatusResponse ClashSetStatus(Document document, ClashSetStatusRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashSetStatusRequest();
            var scope = (request.Scope ?? string.Empty).Trim().ToLowerInvariant();
            if (scope != "results" && scope != "group" && scope != "test")
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "scope must be results, group, or test.");

            ClashResultStatus status;
            if (!Enum.TryParse((request.Status ?? string.Empty).Trim(), true, out status) || !Enum.IsDefined(typeof(ClashResultStatus), status))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "status must be New, Active, Reviewed, Approved, or Resolved.");

            var clash = document.GetClash();
            var testsData = clash.TestsData;
            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            var targets = ResolveClashStatusTargets(tests, request, scope);
            var response = new ClashSetStatusResponse
            {
                Scope = scope,
                Status = status.ToString(),
                AffectedResultCount = targets.Count,
                LargeStatusChangeThreshold = LargeClashStatusChangeThreshold,
            };
            foreach (var target in targets)
            {
                var transition = target.Status + " -> " + status;
                int count;
                response.StatusTransitions.TryGetValue(transition, out count);
                response.StatusTransitions[transition] = count + 1;
            }

            if (request.Apply == true && targets.Count > LargeClashStatusChangeThreshold && request.ConfirmLargeStatusChange != true)
            {
                response.ConfirmationRequired = true;
                response.Message = "Status change exceeds 500 clash results. Re-run with confirmLargeStatusChange=true after reviewing the dry-run.";
                return response;
            }

            if (request.Apply != true)
            {
                response.Message = "Dry-run only. Pass apply=true to update clash results.";
                return response;
            }

            using (var transaction = document.BeginTransaction("NavisHelper MCP Clash Status"))
            {
                ClashWorkflowService.ApplyResultUpdates(testsData, targets, status, request.AssignedTo, request.Comment);
                transaction.Commit();
            }
            response.Applied = true;
            response.Message = "Updated " + targets.Count.ToString(CultureInfo.InvariantCulture) + " clash result(s).";
            return response;
        }

        public ClashGroupByProximityResponse ClashGroupByProximity(Document document, ClashGroupByProximityRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashGroupByProximityRequest();
            var groupMode = NormalizeClashClusterMode(request.GroupMode);
            var clusterDistanceMm = NormalizePositiveDouble(request.ClusterDistanceMm, 500, "clusterDistanceMm");
            var distanceUnits = SectionBoxHelper.MmToDocUnits(clusterDistanceMm);
            var minGroupSize = Math.Max(2, request.MinGroupSize.GetValueOrDefault(2));
            var maxResults = ClampClashListResultsLimit(request.MaxResults);
            var nameTemplate = string.IsNullOrWhiteSpace(request.GroupNameTemplate)
                ? "Зона {index:D2} ({count} колл.)"
                : request.GroupNameTemplate.Trim();
            var prefix = request.GroupNamePrefix ?? string.Empty;
            var statusPlan = ClashStatusFilterHelper.Normalize(request.StatusFilters, request.IncludeAllStatuses == true, true);
            var excludeFilters = NormalizeTextFilters(request.ExcludeItemNameContains);
            var clash = document.GetClash();
            var testsData = clash.TestsData;
            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            var matchedTests = ResolveClashTests(tests, request.TestName, request.TestNames, request.TestHandles, null, null, true).ToList();
            var response = new ClashGroupByProximityResponse
            {
                GroupMode = groupMode,
                ClusterDistanceMm = clusterDistanceMm,
                MatchedTestCount = matchedTests.Count,
            };
            var plans = new List<ClashGroupPlan>();

            foreach (var test in matchedTests)
            {
                var testIndex = GetClashTestIndex(tests, test);
                var rows = new List<ClashClusterRow>();
                var resultIndex = 0;
                foreach (var workItem in EnumerateClashReportWorkItems(test, test.DisplayName ?? string.Empty, string.Empty))
                {
                    resultIndex++;
                    workItem.TestIndex = testIndex;
                    workItem.ResultIndex = resultIndex;
                    if (!statusPlan.IncludeAllStatuses && !MatchesClashStatusFilter(statusPlan.StatusFilters, workItem.Status))
                        continue;
                    if (request.IncludeIgnored != true && ClashWorkflowService.IsIgnoredResult(workItem.Result))
                        continue;
                    if (MatchesExcludedClashItemName(workItem.Result, excludeFilters, null))
                        continue;
                    if (response.AnalyzedResultCount >= maxResults)
                    {
                        response.ResultsTruncated = true;
                        continue;
                    }
                    rows.Add(BuildClashClusterRow(workItem));
                    response.AnalyzedResultCount++;
                }

                var clusters = BuildClashClusters(rows, groupMode, distanceUnits)
                    .Select(cluster => new { Cluster = cluster, Summary = BuildClashClusterSummary(cluster, groupMode, clusterDistanceMm, 5) })
                    .Where(item => item.Summary.ClashCount >= minGroupSize)
                    .OrderByDescending(item => item.Summary.ClashCount)
                    .ThenBy(item => item.Summary.ClusterId, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (var index = 0; index < clusters.Count; index++)
                {
                    var summary = clusters[index].Summary;
                    var groupName = prefix + FormatProximityGroupName(nameTemplate, index + 1, summary);
                    var planItem = new ClashGroupPlanItem
                    {
                        Index = plans.Count + 1,
                        TestIndex = testIndex,
                        TestHandle = BuildClashTestHandle(testIndex),
                        TestName = test.DisplayName ?? string.Empty,
                        GroupName = groupName,
                        CleanGroupName = groupName,
                        OwnerName = summary.DisplayNameA + " / " + summary.DisplayNameB,
                        ResultCount = summary.ClashCount,
                        ExistingGroupFound = ClashGroupMutationService.FindGroup(test, groupName) != null,
                        Status = request.Apply == true ? "pending" : "planned",
                    };
                    planItem.PreviewRows.AddRange(summary.PreviewRows);
                    plans.Add(new ClashGroupPlan
                    {
                        Item = planItem,
                        Results = clusters[index].Cluster.Rows.Select(row => row.WorkItem.Result).Where(result => result != null).ToList(),
                    });
                }
            }

            response.PlannedGroupCount = plans.Count;
            response.SkippedResultCount = Math.Max(0, response.AnalyzedResultCount - plans.Sum(plan => plan.Results.Count));
            response.Groups.AddRange(plans.Select(plan => plan.Item));
            if (request.Apply == true && response.AnalyzedResultCount > LargeClashGroupingConfirmationThreshold && request.ConfirmLargeGrouping != true)
            {
                response.ConfirmationRequired = true;
                response.Message = "Grouping scope exceeds 1000 results. Re-run with confirmLargeGrouping=true after reviewing the dry-run plan.";
                return response;
            }

            if (request.Apply != true)
            {
                response.Message = "Dry-run only. Pass apply=true to create proximity groups.";
                return response;
            }

            foreach (var test in matchedTests)
            {
                if (request.UngroupExistingFirst == true)
                {
                    var plannedNames = new HashSet<string>(
                        plans.Where(plan => plan.Item.TestIndex == GetClashTestIndex(tests, test)).Select(plan => plan.Item.GroupName),
                        StringComparer.OrdinalIgnoreCase);
                    var existingGroups = BuildIndexedClashGroups(test, GetClashTestIndex(tests, test))
                        .Where(item => !string.IsNullOrEmpty(prefix)
                            ? (item.Group.DisplayName ?? string.Empty).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            : plannedNames.Contains(item.Group.DisplayName ?? string.Empty))
                        .OrderByDescending(item => item.Index)
                        .ToList();
                    foreach (var existingGroup in existingGroups)
                        ClashGroupMutationService.UngroupGroup(testsData, test, existingGroup.Group);
                }

                foreach (var plan in plans.Where(item => item.Item.TestIndex == GetClashTestIndex(tests, test)))
                {
                    try
                    {
                        var group = ClashGroupMutationService.FindOrCreateGroup(testsData, test, plan.Item.GroupName);
                        plan.Item.MovedResultCount = ClashGroupMutationService.RebuildGroup(testsData, test, group, plan.Results);
                        plan.Item.Applied = true;
                        plan.Item.Status = "applied";
                        response.AppliedGroupCount++;
                        response.MovedResultCount += plan.Item.MovedResultCount;
                    }
                    catch (Exception ex)
                    {
                        plan.Item.Status = "failed";
                        plan.Item.ErrorMessage = ex.Message;
                        response.Warnings.Add(plan.Item.GroupName + ": " + ex.Message);
                    }
                }
            }

            response.Applied = response.AppliedGroupCount > 0 || plans.Count == 0;
            response.Message = "Applied " + response.AppliedGroupCount.ToString(CultureInfo.InvariantCulture) + " proximity group(s).";
            return response;
        }

        public ClashIgnoreRulesResponse ClashIgnoreRules(Document document, ClashIgnoreRulesRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            request = request ?? new ClashIgnoreRulesRequest();
            var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action != "list" && action != "add" && action != "remove")
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "action must be list, add, or remove.");
            var existing = ClashIgnoreRuleStore.Load(document);
            var planned = existing.ToList();
            var response = new ClashIgnoreRulesResponse { Action = action };
            if (action == "list")
            {
                response.Rules.AddRange(existing);
                response.RuleCount = existing.Count;
                response.Message = "Returned " + existing.Count.ToString(CultureInfo.InvariantCulture) + " document ignore rule(s).";
                return response;
            }

            if (action == "add")
            {
                ValidateIgnoreRule(request.Rule);
                if (planned.Any(rule => string.Equals(rule.Name, request.Rule.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "An ignore rule with this name already exists: " + request.Rule.Name);
                request.Rule.Name = request.Rule.Name.Trim();
                request.Rule.TestNamePattern = (request.Rule.TestNamePattern ?? string.Empty).Trim();
                request.Rule.Reason = (request.Rule.Reason ?? string.Empty).Trim();
                planned.Add(request.Rule);
            }
            else
            {
                var name = string.IsNullOrWhiteSpace(request.RuleName)
                    ? request.Rule == null ? string.Empty : request.Rule.Name
                    : request.RuleName;
                if (string.IsNullOrWhiteSpace(name))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "remove requires ruleName.");
                var removed = planned.RemoveAll(rule => string.Equals(rule.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Ignore rule was not found: " + name);
            }

            response.Rules.AddRange(planned);
            response.RuleCount = planned.Count;
            if (request.Apply != true)
            {
                response.Message = "Dry-run only. Pass apply=true to persist the rule change in the document.";
                return response;
            }

            ClashIgnoreRuleStore.Save(document, planned);
            response.Applied = true;
            if (action == "add")
            {
                var clash = document.GetClash();
                foreach (var test in ClashApiCompat.GetClashTests(clash))
                    response.AffectedResultCount += ClashIgnoreRuleStore.ApplyRules(clash.TestsData, test, new[] { request.Rule });
            }
            response.Message = action == "add"
                ? "Ignore rule saved; approved " + response.AffectedResultCount.ToString(CultureInfo.InvariantCulture) + " matching clash result(s)."
                : "Ignore rule removed from the document. Existing Approved results were not reverted.";
            return response;
        }

        public ClashExportPointsResponse ClashExportPoints(Document document, ClashExportPointsRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            request = request ?? new ClashExportPointsRequest();
            if (string.IsNullOrWhiteSpace(request.OutputPath))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "outputPath is required.");
            var outputPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath.Trim()));
            var extension = Path.GetExtension(outputPath).ToLowerInvariant();
            if (extension != ".csv" && extension != ".xlsx")
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "outputPath must end with .csv or .xlsx.");
            var gridSizeM = request.GridSizeM.GetValueOrDefault(6);
            if (gridSizeM <= 0 || double.IsNaN(gridSizeM) || double.IsInfinity(gridSizeM))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "gridSizeM must be a finite positive number.");
            foreach (var level in request.Levels ?? new List<ClashExportLevel>())
            {
                if (level == null || string.IsNullOrWhiteSpace(level.Name) || level.ZTo <= level.ZFrom)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Each level requires name and zTo > zFrom (meters). ");
            }

            var clash = document.GetClash();
            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            var matchedTests = ResolveClashTests(tests, string.Empty, request.TestNames, request.TestHandles, null, null, true).ToList();
            var origin = request.Origin ?? new Point3Info();
            var angle = request.RotationDeg.GetValueOrDefault(0) * Math.PI / 180.0;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var rows = new List<ClashPointExportRow>();
            foreach (var test in matchedTests)
            {
                var testIndex = GetClashTestIndex(tests, test);
                var resultIndex = 0;
                foreach (var workItem in EnumerateClashReportWorkItems(test, test.DisplayName ?? string.Empty, string.Empty))
                {
                    resultIndex++;
                    if (workItem.Result == null || workItem.Result.Center == null)
                        continue;
                    if (request.IncludeIgnored != true && ClashWorkflowService.IsIgnoredResult(workItem.Result))
                        continue;
                    var point = workItem.Result.Center;
                    var dxM = DocUnitsToMm(point.X - origin.X) / 1000.0;
                    var dyM = DocUnitsToMm(point.Y - origin.Y) / 1000.0;
                    var localX = cos * dxM + sin * dyM;
                    var localY = -sin * dxM + cos * dyM;
                    var elevation = DocUnitsToMm(point.Z - origin.Z) / 1000.0;
                    var levelName = (request.Levels ?? new List<ClashExportLevel>())
                        .Where(level => level != null && elevation >= level.ZFrom && elevation < level.ZTo)
                        .Select(level => level.Name ?? string.Empty)
                        .FirstOrDefault() ?? string.Empty;
                    rows.Add(new ClashPointExportRow
                    {
                        TestName = workItem.TestName ?? string.Empty,
                        GroupPath = workItem.GroupPath ?? string.Empty,
                        Status = workItem.Status ?? string.Empty,
                        DisciplinePair = workItem.TestName ?? string.Empty,
                        ResultHandle = BuildClashResultHandle(testIndex, resultIndex),
                        GlobalX = point.X,
                        GlobalY = point.Y,
                        GlobalZ = point.Z,
                        LocalXM = localX,
                        LocalYM = localY,
                        ElevationM = elevation,
                        Level = levelName,
                        GridCell = BuildGridCell(localX, localY, gridSizeM),
                        ItemA = FirstOrEmpty(GetClashItemNames(workItem.Result.Selection1, workItem.Result.Item1, 1)),
                        ItemB = FirstOrEmpty(GetClashItemNames(workItem.Result.Selection2, workItem.Result.Item2, 1)),
                    });
                }
            }

            var response = new ClashExportPointsResponse
            {
                OutputPath = outputPath,
                Format = extension.Substring(1),
                MatchedTestCount = matchedTests.Count,
                ExportedResultCount = rows.Count,
            };
            if (request.Apply != true)
            {
                response.Message = "Dry-run only. Pass apply=true to write the coordinate report.";
                return response;
            }

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            if (extension == ".csv")
                ClashPointExportWriter.WriteCsv(outputPath, rows);
            else
                ClashPointExportWriter.WriteXlsx(outputPath, rows);
            response.Applied = true;
            response.FileSizeBytes = new FileInfo(outputPath).Length;
            response.Message = "Exported " + rows.Count.ToString(CultureInfo.InvariantCulture) + " clash point(s).";
            return response;
        }

        private static ClashManageTestsResponse ClashManageRenameBatch(
            Document document,
            DocumentClashTests testsData,
            IList<ClashTest> tests,
            ClashManageTestsRequest request,
            bool apply)
        {
            var renames = (request.Renames ?? new List<ClashTestRenameRequest>()).Where(item => item != null).ToList();
            if (renames.Count == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "rename_batch requires a non-empty renames list.");

            var duplicateHandles = renames
                .Where(item => !string.IsNullOrWhiteSpace(item.TestHandle))
                .GroupBy(item => item.TestHandle.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateHandles != null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "rename_batch contains duplicate testHandle: " + duplicateHandles.Key);

            var duplicateNames = renames
                .Where(item => !string.IsNullOrWhiteSpace(item.NewName))
                .GroupBy(item => item.NewName.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateNames != null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "rename_batch contains duplicate newName: " + duplicateNames.Key);

            var response = new ClashManageTestsResponse
            {
                Applied = apply,
                Operation = "rename_batch",
                RequestedTestName = "rename_batch:" + renames.Count.ToString(CultureInfo.InvariantCulture),
                MatchedTestCount = renames.Count,
            };
            var targets = new List<Tuple<ClashTest, string, ClashTestOperationResult>>();
            foreach (var rename in renames)
            {
                int testIndex;
                var test = ResolveRequiredClashTestHandle(tests, rename.TestHandle, out testIndex);
                if (string.IsNullOrWhiteSpace(rename.NewName))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "rename_batch newName is required for " + rename.TestHandle + ".");
                var item = new ClashTestOperationResult
                {
                    TestIndex = testIndex,
                    Handle = BuildClashTestHandle(testIndex),
                    TestHandle = BuildClashTestHandle(testIndex),
                    Name = test.DisplayName ?? string.Empty,
                    Operation = "rename_batch",
                    Applied = false,
                    Status = apply ? "pending" : "planned",
                };
                response.Tests.Add(item);
                targets.Add(Tuple.Create(test, rename.NewName.Trim(), item));
            }

            if (!apply)
            {
                response.Message = "Dry-run only. Pass apply=true to rename all validated tests.";
                return response;
            }

            using (var transaction = document.BeginTransaction("NavisHelper MCP Clash Batch Rename"))
            {
                foreach (var target in targets)
                {
                    testsData.TestsEditDisplayName(target.Item1, target.Item2);
                    target.Item3.Status = "applied";
                    target.Item3.Applied = true;
                    response.AffectedTestCount++;
                }
                transaction.Commit();
            }
            response.Message = "Renamed " + response.AffectedTestCount.ToString(CultureInfo.InvariantCulture) + " Clash Detective test(s).";
            return response;
        }

        private static void ValidateIgnoreRule(ClashIgnoreRule rule)
        {
            if (rule == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "rule is required.");
            if (string.IsNullOrWhiteSpace(rule.Name))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "rule.name is required.");
            if (string.IsNullOrWhiteSpace(rule.TestNamePattern))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "rule.testNamePattern is required.");
            if ((rule.ItemAContains == null || rule.ItemAContains.All(string.IsNullOrWhiteSpace)) &&
                (rule.ItemBContains == null || rule.ItemBContains.All(string.IsNullOrWhiteSpace)))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "At least one of itemAContains/itemBContains must contain a value.");
        }

        private static string BuildGridCell(double localX, double localY, double gridSizeM)
        {
            var x = (int)Math.Floor(localX / gridSizeM);
            var y = (int)Math.Floor(localY / gridSizeM);
            var column = x < 0 ? "X" + x.ToString(CultureInfo.InvariantCulture) : ToSpreadsheetColumn(x + 1);
            var row = y < 0 ? "Y" + y.ToString(CultureInfo.InvariantCulture) : (y + 1).ToString(CultureInfo.InvariantCulture);
            return column + "-" + row;
        }

        private static string ToSpreadsheetColumn(int index)
        {
            var result = string.Empty;
            while (index > 0)
            {
                index--;
                result = (char)('A' + index % 26) + result;
                index /= 26;
            }
            return result;
        }

        private static ClashTest ResolveRequiredClashTestHandle(IList<ClashTest> tests, string handle, out int testIndex)
        {
            if (!TryParseClashTestHandle(handle, out testIndex) || testIndex < 1 || testIndex > (tests == null ? 0 : tests.Count))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Clash test was not found for handle: " + (handle ?? string.Empty));
            return tests[testIndex - 1];
        }

        private static List<string> NormalizeRequiredHandles(IEnumerable<string> handles, string parameterName)
        {
            var values = (handles ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (values.Count == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, parameterName + " must contain at least one handle.");
            return values;
        }

        private static List<IndexedClashResult> BuildIndexedClashResults(ClashTest test, int testIndex)
        {
            var result = new List<IndexedClashResult>();
            var index = 0;
            foreach (var workItem in EnumerateClashReportWorkItems(test, test == null ? string.Empty : test.DisplayName ?? string.Empty, string.Empty))
            {
                index++;
                result.Add(new IndexedClashResult
                {
                    Index = index,
                    Handle = BuildClashResultHandle(testIndex, index),
                    Result = workItem.Result,
                    GroupPath = workItem.GroupPath,
                });
            }
            return result;
        }

        private static Dictionary<string, string> BuildClashGroupHandleMap(ClashTest test, int testIndex)
        {
            return BuildIndexedClashGroups(test, testIndex)
                .GroupBy(item => item.Path ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Handle, StringComparer.OrdinalIgnoreCase);
        }

        private static List<IndexedClashGroup> BuildIndexedClashGroups(ClashTest test, int testIndex)
        {
            var groups = new List<IndexedClashGroup>();
            var index = 0;
            AddIndexedClashGroups(test, string.Empty, testIndex, groups, ref index);
            return groups;
        }

        private static void AddIndexedClashGroups(GroupItem parent, string parentPath, int testIndex, ICollection<IndexedClashGroup> groups, ref int index)
        {
            if (parent == null || parent.Children == null)
                return;
            foreach (SavedItem child in parent.Children)
            {
                var group = child as ClashResultGroup;
                if (group == null)
                    continue;
                index++;
                var path = string.IsNullOrEmpty(parentPath) ? group.DisplayName ?? string.Empty : parentPath + " / " + (group.DisplayName ?? string.Empty);
                groups.Add(new IndexedClashGroup
                {
                    Index = index,
                    Handle = BuildClashGroupHandle(testIndex, index),
                    Group = group,
                    Path = path,
                });
                AddIndexedClashGroups(group, path, testIndex, groups, ref index);
            }
        }

        private static string BuildClashGroupHandle(int testIndex, int groupIndex)
        {
            return "clash-group:" + testIndex.ToString(CultureInfo.InvariantCulture) + ":" + groupIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryParseClashGroupHandle(string handle, out int testIndex, out int groupIndex)
        {
            testIndex = 0;
            groupIndex = 0;
            var parts = (handle ?? string.Empty).Split(':');
            return parts.Length == 3 &&
                   string.Equals(parts[0], "clash-group", StringComparison.OrdinalIgnoreCase) &&
                   int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out testIndex) &&
                   int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out groupIndex) &&
                   testIndex > 0 && groupIndex > 0;
        }

        private static string FindClashGroupHandle(ClashTest test, int testIndex, ClashResultGroup target)
        {
            var targetGuid = target == null ? Guid.Empty : target.Guid;
            var match = BuildIndexedClashGroups(test, testIndex).FirstOrDefault(item =>
                object.ReferenceEquals(item.Group, target) || (targetGuid != Guid.Empty && item.Group.Guid == targetGuid));
            return match == null ? string.Empty : match.Handle;
        }

        private static List<ClashResult> ResolveClashStatusTargets(IList<ClashTest> tests, ClashSetStatusRequest request, string scope)
        {
            var targets = new List<ClashResult>();
            if (scope == "test")
            {
                var handles = new List<string>();
                if (!string.IsNullOrWhiteSpace(request.TestHandle))
                    handles.Add(request.TestHandle);
                handles.AddRange(request.TestHandles ?? new List<string>());
                foreach (var handle in NormalizeRequiredHandles(handles, "testHandles"))
                {
                    int testIndex;
                    var test = ResolveRequiredClashTestHandle(tests, handle, out testIndex);
                    targets.AddRange(ClashWorkflowService.EnumerateResults(test));
                }
                return targets.Distinct().ToList();
            }

            int selectedTestIndex;
            var selectedTest = ResolveRequiredClashTestHandle(tests, request.TestHandle, out selectedTestIndex);
            if (scope == "results")
            {
                var map = BuildIndexedClashResults(selectedTest, selectedTestIndex).ToDictionary(item => item.Handle, item => item.Result, StringComparer.OrdinalIgnoreCase);
                foreach (var handle in NormalizeRequiredHandles(request.ResultHandles, "resultHandles"))
                {
                    int handleTestIndex;
                    int ignoredResultIndex;
                    if (!ClashHandleHelper.TryParseResultHandle(handle, out handleTestIndex, out ignoredResultIndex) || handleTestIndex != selectedTestIndex)
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, "Result handle does not belong to " + request.TestHandle + ": " + handle);
                    ClashResult result;
                    if (!map.TryGetValue(handle, out result))
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, "Clash result was not found: " + handle);
                    targets.Add(result);
                }
                return targets.Distinct().ToList();
            }

            var groups = BuildIndexedClashGroups(selectedTest, selectedTestIndex).ToDictionary(item => item.Handle, item => item.Group, StringComparer.OrdinalIgnoreCase);
            foreach (var handle in NormalizeRequiredHandles(request.GroupHandles, "groupHandles"))
            {
                int handleTestIndex;
                int ignoredGroupIndex;
                if (!TryParseClashGroupHandle(handle, out handleTestIndex, out ignoredGroupIndex) || handleTestIndex != selectedTestIndex)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Group handle does not belong to " + request.TestHandle + ": " + handle);
                ClashResultGroup group;
                if (!groups.TryGetValue(handle, out group))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Clash group was not found: " + handle);
                targets.AddRange(ClashWorkflowService.EnumerateResults(group));
            }
            return targets.Distinct().ToList();
        }

        private static string FormatProximityGroupName(string template, int index, ClashClusterSummary summary)
        {
            var value = template ?? string.Empty;
            value = Regex.Replace(value, @"\{index(?::D(?<width>\d+))?\}", match =>
            {
                int width;
                return int.TryParse(match.Groups["width"].Value, out width)
                    ? index.ToString("D" + width.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
                    : index.ToString(CultureInfo.InvariantCulture);
            }, RegexOptions.IgnoreCase);
            value = value.Replace("{count}", summary.ClashCount.ToString(CultureInfo.InvariantCulture));
            value = value.Replace("{x}", summary.Centroid == null ? string.Empty : summary.Centroid.X.ToString("0.##", CultureInfo.InvariantCulture));
            value = value.Replace("{y}", summary.Centroid == null ? string.Empty : summary.Centroid.Y.ToString("0.##", CultureInfo.InvariantCulture));
            value = value.Replace("{z}", summary.Centroid == null ? string.Empty : summary.Centroid.Z.ToString("0.##", CultureInfo.InvariantCulture));
            value = value.Replace("{ownerA}", summary.DisplayNameA ?? string.Empty);
            value = value.Replace("{ownerB}", summary.DisplayNameB ?? string.Empty);
            return value.Trim();
        }

        private static bool IsFullClashVerbosity(string verbosity)
        {
            var value = string.IsNullOrWhiteSpace(verbosity) ? "compact" : verbosity.Trim().ToLowerInvariant();
            if (value != "compact" && value != "full")
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "verbosity must be compact or full.");
            return value == "full";
        }

        private static void CompactClashPreviewRows(IEnumerable<ClashClusterPreviewRow> rows)
        {
            foreach (var row in rows ?? Enumerable.Empty<ClashClusterPreviewRow>())
            {
                if (row == null)
                    continue;
                row.Item1Path = string.Empty;
                row.Item2Path = string.Empty;
            }
        }

        private sealed class IndexedClashResult
        {
            public int Index;
            public string Handle;
            public ClashResult Result;
            public string GroupPath;
        }

        private sealed class IndexedClashGroup
        {
            public int Index;
            public string Handle;
            public ClashResultGroup Group;
            public string Path;
        }

        private sealed class IndexedClashGroupComparer : IEqualityComparer<IndexedClashGroup>
        {
            public static readonly IndexedClashGroupComparer Instance = new IndexedClashGroupComparer();
            public bool Equals(IndexedClashGroup x, IndexedClashGroup y)
            {
                return x != null && y != null && object.ReferenceEquals(x.Group, y.Group);
            }
            public int GetHashCode(IndexedClashGroup obj)
            {
                return obj == null || obj.Group == null ? 0 : obj.Group.GetHashCode();
            }
        }
    }
}
