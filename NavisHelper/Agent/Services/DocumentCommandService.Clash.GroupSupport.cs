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

        private static int ClampClashListTestsLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultClashListTestsLimit);
            if (value < 1)
                return 1;
            if (value > MaxClashListTestsLimit)
                return MaxClashListTestsLimit;
            return value;
        }

        private static int ClampClashListResultsLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultClashListResultsLimit);
            if (value < 1)
                return 1;
            if (value > MaxClashListResultsLimit)
                return MaxClashListResultsLimit;
            return value;
        }

        private static int ClampClashClusterLimit(int? limit)
        {
            return ClashClusterKeyHelper.ClampClusterLimit(limit);
        }

        private static int ClampClashClusterPreviewRowsPerCluster(int? limit)
        {
            return ClashClusterKeyHelper.ClampPreviewRowsPerCluster(limit);
        }

        private static int ClampClashGroupPreviewRowsPerGroup(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultClashGroupPreviewRowsPerGroup);
            if (value < 0)
                return 0;
            if (value > MaxClashGroupPreviewRowsPerGroup)
                return MaxClashGroupPreviewRowsPerGroup;
            return value;
        }

        private static int ClampClashRenumberLimit(int? limit)
        {
            return ClashRenumberPlanHelper.ClampLimit(limit);
        }

        private static int NormalizeClashRenumberStart(int? value)
        {
            int result;
            if (!ClashRenumberPlanHelper.TryNormalizeStartNumber(value, out result))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "startNumber must be non-negative.");
            return result;
        }

        private static int NormalizeClashRenumberWidth(int? value)
        {
            return ClashRenumberPlanHelper.NormalizeNumberWidth(value);
        }

        private static string NormalizeClashRenumberScope(string value)
        {
            var normalized = ClashRenumberPlanHelper.NormalizeScope(value);
            if (normalized == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "scope must be top_level or recursive.");
            return normalized;
        }

        private static string NormalizeClashRenumberOrder(string value)
        {
            var normalized = ClashRenumberPlanHelper.NormalizeOrder(value);
            if (normalized == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "orderBy must be current or name.");
            return normalized;
        }

        private static IEnumerable<ClashRenumberEntry> EnumerateClashRenumberEntries(
            ClashTest test,
            int testIndex,
            string scope,
            bool includeGroups,
            bool includeResults,
            bool includeEmptyGroups)
        {
            if (test == null || test.Children == null)
                yield break;

            foreach (var entry in EnumerateClashRenumberEntries(test.Children, testIndex, test.DisplayName ?? string.Empty, string.Empty, scope, includeGroups, includeResults, includeEmptyGroups))
                yield return entry;
        }

        private static IEnumerable<ClashRenumberEntry> EnumerateClashRenumberEntries(
            IEnumerable<SavedItem> children,
            int testIndex,
            string testName,
            string groupPath,
            string scope,
            bool includeGroups,
            bool includeResults,
            bool includeEmptyGroups)
        {
            if (children == null)
                yield break;

            foreach (var child in children)
            {
                var result = child as ClashResult;
                if (result != null)
                {
                    if (includeResults)
                    {
                        yield return new ClashRenumberEntry
                        {
                            SavedItem = result,
                            TestIndex = testIndex,
                            TestName = testName,
                            GroupPath = groupPath,
                            OldName = result.DisplayName ?? string.Empty,
                            ItemType = "result",
                        };
                    }

                    continue;
                }

                var group = child as ClashResultGroup;
                if (group == null)
                    continue;

                var childResults = EnumerateClashResultsFlat(group.Children).Take(1).Count();
                if (includeGroups && (includeEmptyGroups || childResults > 0))
                {
                    yield return new ClashRenumberEntry
                    {
                        SavedItem = group,
                        TestIndex = testIndex,
                        TestName = testName,
                        GroupPath = groupPath,
                        OldName = group.DisplayName ?? string.Empty,
                        ItemType = "group",
                    };
                }

                if (!string.Equals(scope, ClashRenumberScopeRecursive, StringComparison.OrdinalIgnoreCase))
                    continue;

                var nextPath = string.IsNullOrEmpty(groupPath)
                    ? group.DisplayName ?? string.Empty
                    : groupPath + " / " + (group.DisplayName ?? string.Empty);
                foreach (var nested in EnumerateClashRenumberEntries(group.Children, testIndex, testName, nextPath, scope, includeGroups, includeResults, includeEmptyGroups))
                    yield return nested;
            }
        }

        private static IEnumerable<ClashResult> EnumerateClashResultsFlat(IEnumerable<SavedItem> children)
        {
            if (children == null)
                yield break;

            foreach (var child in children)
            {
                var result = child as ClashResult;
                if (result != null)
                {
                    yield return result;
                    continue;
                }

                var group = child as GroupItem;
                if (group == null)
                    continue;

                foreach (var nested in EnumerateClashResultsFlat(group.Children))
                    yield return nested;
            }
        }

        private static string ExtractClashGroupSideTag(string value)
        {
            return ClashGroupNameHelper.ExtractSideTag(value);
        }

        private static string RemoveClashGroupSideTag(string value)
        {
            return ClashGroupNameHelper.RemoveSideTag(value);
        }

        private static string StripLeadingClashNumber(string value)
        {
            return ClashGroupNameHelper.StripLeadingNumber(value);
        }

        private static int NormalizeClashGroupMinSize(int? value)
        {
            var result = value.GetValueOrDefault(DefaultClashGroupMinSize);
            if (result < 1)
                return 1;
            return result;
        }

        private static int NormalizeClashGroupAncestorLevelsUp(int? value)
        {
            var result = value.GetValueOrDefault(1);
            if (result < 0)
                return 0;
            if (result > MaxClashGroupAncestorLevelsUp)
                return MaxClashGroupAncestorLevelsUp;
            return result;
        }

        private static string NormalizeClashGroupSide(string side)
        {
            if (string.IsNullOrWhiteSpace(side))
                return "B";

            var value = side.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            switch (value)
            {
                case "a":
                case "1":
                case "item1":
                case "item_1":
                case "side_a":
                case "selection_a":
                    return "A";
                case "b":
                case "2":
                case "item2":
                case "item_2":
                case "side_b":
                case "selection_b":
                    return "B";
                default:
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "groupBySide must be A or B.");
            }
        }

        private static string NormalizeClashGroupNameMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ClashGroupNameModeOwnerName;

            var normalized = value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            switch (normalized)
            {
                case ClashGroupNameModeOwnerName:
                case "name":
                case "owner":
                    return ClashGroupNameModeOwnerName;
                case ClashGroupNameModeOwnerPath:
                case "path":
                case "full_path":
                    return ClashGroupNameModeOwnerPath;
                default:
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "groupNameMode must be owner_name or owner_path.");
            }
        }

        private static string NormalizeClashGroupBy(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "ancestor";
            var normalized = value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            if (normalized == "ancestor" || normalized == "root" || normalized == "source_file")
                return normalized;
            throw new AgentCommandException(ErrorCodes.SchemaViolation, "groupBy must be ancestor, root, or source_file.");
        }

        private static void PopulateClashRootIdentity(ClashGroupWorkItem row, ModelItem item, string groupBy)
        {
            if (row == null || item == null || item.Model == null)
                return;
            var model = item.Model;
            row.RootId = BuildClashModelRootId(model);
            row.RootName = GetItemDisplayName(model.RootItem);
            row.SourceFile = !string.IsNullOrWhiteSpace(model.SourceFileName) ? model.SourceFileName : model.FileName;
            if (string.Equals(groupBy, "root", StringComparison.OrdinalIgnoreCase))
            {
                row.OwnerName = row.RootName;
                row.OwnerPath = row.RootName;
            }
            else if (string.Equals(groupBy, "source_file", StringComparison.OrdinalIgnoreCase))
            {
                row.OwnerName = GetPortableFileName(row.SourceFile);
                row.OwnerPath = row.SourceFile;
            }
        }

        private static string BuildClashModelRootId(Model model)
        {
            if (model == null)
                return string.Empty;
            if (model.Guid != Guid.Empty)
                return "guid:" + model.Guid.ToString("D");
            if (model.SourceGuid != Guid.Empty)
                return "source-guid:" + model.SourceGuid.ToString("D");
            var source = !string.IsNullOrWhiteSpace(model.SourceFileName) ? model.SourceFileName : model.FileName;
            return "model:" + (source ?? string.Empty) + ":" + model.GetHashCode().ToString(CultureInfo.InvariantCulture);
        }

        private static string GetPortableFileName(string value)
        {
            var text = value ?? string.Empty;
            var index = Math.Max(text.LastIndexOf('/'), text.LastIndexOf('\\'));
            return index >= 0 && index + 1 < text.Length ? text.Substring(index + 1) : text;
        }

        private static ModelItem AscendClashGroupOwner(ModelItem item, int levels)
        {
            var current = item;
            for (var index = 0; index < levels && current != null && current.Parent != null; index++)
                current = current.Parent;
            return current;
        }

        private static ClashGroupTestPlan BuildClashGroupTestPlan(
            ClashTest test,
            int testIndex,
            IList<ClashGroupWorkItem> rows,
            string side,
            string groupBy,
            string groupNameMode,
            string groupNamePrefix,
            bool includeNavisHelperSideTag,
            int minGroupSize,
            int previewRowsPerGroup)
        {
            var testPlan = new ClashGroupTestPlan
            {
                Test = test,
                TestIndex = testIndex,
            };

            var groupedRows = (rows ?? new List<ClashGroupWorkItem>())
                .GroupBy(row => BuildClashGroupKey(row, groupBy, groupNameMode), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() >= minGroupSize)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key ?? string.Empty, NaturalStringComparer.Instance)
                .ToList();

            var testHandle = BuildClashTestHandle(testIndex);
            foreach (var group in groupedRows)
            {
                var groupRows = group.ToList();
                var first = groupRows.FirstOrDefault();
                var ownerName = first == null ? string.Empty : first.OwnerName ?? string.Empty;
                var ownerPath = first == null ? string.Empty : first.OwnerPath ?? string.Empty;
                var cleanName = BuildMcpClashGroupCleanName(groupBy == "root" ? ClashGroupNameModeOwnerName : groupNameMode, groupNamePrefix, ownerName, ownerPath);
                var groupName = BuildMcpClashGroupName(side, cleanName, includeNavisHelperSideTag);
                var item = new ClashGroupPlanItem
                {
                    TestIndex = testIndex,
                    TestHandle = testHandle,
                    TestName = test == null ? string.Empty : test.DisplayName ?? string.Empty,
                    GroupName = groupName,
                    CleanGroupName = cleanName,
                    OwnerName = ownerName,
                    OwnerPath = ownerPath,
                    RootId = first == null ? string.Empty : first.RootId ?? string.Empty,
                    RootName = first == null ? string.Empty : first.RootName ?? string.Empty,
                    SourceFile = first == null ? string.Empty : first.SourceFile ?? string.Empty,
                    ResultCount = groupRows.Count,
                    Status = "planned",
                };

                foreach (var row in groupRows.Take(previewRowsPerGroup))
                    item.PreviewRows.Add(BuildClashGroupPreviewRow(row));

                testPlan.Groups.Add(new ClashGroupPlan
                {
                    Item = item,
                    Results = groupRows.Select(row => row.WorkItem == null ? null : row.WorkItem.Result).Where(result => result != null).ToList(),
                });
            }

            return testPlan;
        }

        private static string BuildClashGroupKey(ClashGroupWorkItem row, string groupBy, string groupNameMode)
        {
            if (row == null)
                return string.Empty;

            if (string.Equals(groupBy, "root", StringComparison.OrdinalIgnoreCase))
                return row.RootId ?? string.Empty;
            return ClashGroupNameHelper.BuildGroupKey(groupNameMode, row.OwnerName, row.OwnerPath);
        }

        private static string BuildMcpClashGroupCleanName(string groupNameMode, string prefix, string ownerName, string ownerPath)
        {
            return ClashGroupNameHelper.BuildCleanName(groupNameMode, prefix, ownerName, ownerPath);
        }

        private static string BuildMcpClashGroupName(string side, string cleanName, bool includeNavisHelperSideTag)
        {
            return ClashGroupNameHelper.BuildGroupName(side, cleanName, includeNavisHelperSideTag);
        }

        private static ClashClusterPreviewRow BuildClashGroupPreviewRow(ClashGroupWorkItem row)
        {
            var workItem = row == null ? null : row.WorkItem;
            var result = workItem == null ? null : workItem.Result;
            return new ClashClusterPreviewRow
            {
                TestIndex = workItem == null ? 0 : workItem.TestIndex,
                ResultIndex = workItem == null ? 0 : workItem.ResultIndex,
                TestHandle = workItem == null ? string.Empty : BuildClashTestHandle(workItem.TestIndex),
                ResultHandle = workItem == null ? string.Empty : BuildClashResultHandle(workItem.TestIndex, workItem.ResultIndex),
                TestName = workItem == null ? string.Empty : workItem.TestName ?? string.Empty,
                GroupPath = workItem == null ? string.Empty : workItem.GroupPath ?? string.Empty,
                ResultName = workItem == null ? string.Empty : workItem.ResultName ?? string.Empty,
                Status = workItem == null ? string.Empty : workItem.Status ?? string.Empty,
                Item1Name = result == null ? string.Empty : FirstOrEmpty(GetClashItemNames(result.Selection1, result.Item1, 1)),
                Item2Name = result == null ? string.Empty : FirstOrEmpty(GetClashItemNames(result.Selection2, result.Item2, 1)),
                Item1Path = result == null ? string.Empty : BuildItemPath(result.Item1),
                Item2Path = result == null ? string.Empty : BuildItemPath(result.Item2),
                ClashPoint = result == null || result.Center == null ? null : ToPoint3Info(result.Center),
            };
        }

        private static void MarkClashGroupConflicts(ClashGroupTestPlan testPlan, bool overwriteExisting, ClashGroupResultsResponse response)
        {
            if (testPlan == null || testPlan.Test == null || testPlan.Groups == null)
                return;

            foreach (var plan in testPlan.Groups)
            {
                var item = plan.Item;
                if (item == null)
                    continue;

                var existing = ClashGroupMutationService.FindGroup(testPlan.Test, item.GroupName);
                item.ExistingGroupFound = existing != null;
                if (existing != null && !overwriteExisting)
                {
                    item.Status = "conflict";
                    item.ErrorMessage = "A ClashResultGroup with this name already exists. Re-run with overwriteExisting=true to rebuild it.";
                    if (response != null)
                    {
                        response.ConflictGroupCount++;
                        response.Warnings.Add((item.TestName ?? item.TestHandle) + " / " + item.GroupName + ": existing group conflict.");
                    }
                }
            }
        }

        private static ModelItem ResolveClashGroupOwner(ClashResult result, string side, int ancestorLevelsUp)
        {
            var item = string.Equals(side, "A", StringComparison.OrdinalIgnoreCase)
                ? ResolveClashSideSeed(result == null ? null : result.Item1, result == null ? null : result.Selection1)
                : ResolveClashSideSeed(result == null ? null : result.Item2, result == null ? null : result.Selection2);
            if (item == null)
                return null;

            var current = item;
            for (var level = 0; level < ancestorLevelsUp; level++)
            {
                try
                {
                    if (current.Parent == null)
                        break;
                    current = current.Parent;
                }
                catch
                {
                    break;
                }
            }

            return current;
        }

        private static ModelItem ResolveClashSideSeed(ModelItem primaryItem, ModelItemCollection sideItems)
        {
            if (primaryItem != null)
                return primaryItem;

            return FirstModelItem(sideItems);
        }
    }
}
