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

        private static string NormalizeClashClusterMode(string groupMode)
        {
            var value = ClashClusterKeyHelper.NormalizeMode(groupMode);
            if (value == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "groupMode must be one of: hybrid, object_pair, spatial.");
            return value;
        }

        private static string NormalizeClashReportClusterMode(string groupMode)
        {
            var value = ClashClusterKeyHelper.NormalizeReportMode(groupMode);
            if (value == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "groupMode must be one of: hybrid, object_pair, spatial.");
            return value;
        }

        private static string NormalizeClashReportArtifactGranularity(string artifactGranularity)
        {
            var value = ClashReportOptionHelper.NormalizeArtifactGranularity(artifactGranularity);
            if (value == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "artifactGranularity must be result or cluster.");
            return value;
        }

        private static string NormalizeClashReportVerbosity(string verbosity)
        {
            var value = ClashReportOptionHelper.NormalizeReportVerbosity(verbosity);
            if (value == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "verbosity must be full or compact.");
            return value;
        }

        private static int ClampClashReportClusterMembersInHtml(int? limit)
        {
            return ClashReportOptionHelper.ClampClusterMembersInHtml(limit);
        }

        private static int ClampClashReportLimit(int? limit)
        {
            return ClashReportOptionHelper.ClampReportLimit(limit);
        }

        private static int ClampNonNegative(int? value)
        {
            return ClashNumericOptionHelper.ClampNonNegative(value);
        }

        private static ClashGenerateReportResponse BuildClashReportFileResponse(
            ClashGenerateReportResponse current,
            ClashGenerateReportResponse previous)
        {
            if (current == null)
                return null;

            var fileResponse = JsonConvert.DeserializeObject<ClashGenerateReportResponse>(
                JsonConvert.SerializeObject(current, ClashReportJsonSettings));
            if (fileResponse == null)
                fileResponse = current;

            return ClashReportAccumulationHelper.BuildFileResponse(fileResponse, current, previous);
        }

        private static void AccumulateClashResults(IEnumerable<SavedItem> children, ClashTestSummary summary, bool includeStatusCounts)
        {
            if (children == null)
                return;

            foreach (var child in children)
            {
                var result = child as ClashResult;
                if (result != null)
                {
                    summary.Total++;
                    if (result.Status == ClashResultStatus.New)
                        summary.New++;
                    if (result.Status == ClashResultStatus.Active)
                        summary.Active++;

                    if (includeStatusCounts)
                    {
                        var status = result.Status.ToString();
                        int count;
                        summary.StatusCounts.TryGetValue(status, out count);
                        summary.StatusCounts[status] = count + 1;
                    }

                    continue;
                }

                var group = child as GroupItem;
                if (group != null)
                    AccumulateClashResults(group.Children, summary, includeStatusCounts);
            }
        }

        private static IEnumerable<ClashTest> ResolveClashTests(IEnumerable<ClashTest> tests, string testName)
        {
            return ResolveClashTests(tests, testName, null);
        }

        private static IEnumerable<ClashTest> ResolveClashTests(IEnumerable<ClashTest> tests, string testName, IEnumerable<string> testNames)
        {
            var list = tests == null ? new List<ClashTest>() : tests.ToList();
            var names = new List<string>();
            if (!string.IsNullOrWhiteSpace(testName))
                names.Add(testName);
            if (testNames != null)
                names.AddRange(testNames.Where(name => !string.IsNullOrWhiteSpace(name)));
            if (names.Count == 0)
                return list;

            var result = new List<ClashTest>();
            foreach (var rawName in names)
            {
                var name = rawName.Trim();
                var exact = list
                    .Where(test => string.Equals(test.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var matches = exact.Count > 0
                    ? exact
                    : list.Where(test =>
                        !string.IsNullOrEmpty(test.DisplayName) &&
                        test.DisplayName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                foreach (var match in matches)
                {
                    if (!result.Any(existing => object.ReferenceEquals(existing, match)))
                        result.Add(match);
                }
            }

            return result;
        }

        private static IEnumerable<ClashTest> ResolveClashTests(
            IList<ClashTest> tests,
            string testName,
            IEnumerable<string> testNames,
            IEnumerable<string> testHandles,
            string namePrefix,
            int? firstN,
            bool requireScope)
        {
            var hasNameScope = HasRequestedClashTestScope(testName, testNames);
            var hasPrefixScope = !string.IsNullOrWhiteSpace(namePrefix);
            var hasFirstNScope = firstN.HasValue;
            var handles = testHandles == null
                ? new List<string>()
                : testHandles.Where(handle => !string.IsNullOrWhiteSpace(handle)).ToList();

            if (requireScope && !hasNameScope && !hasPrefixScope && !hasFirstNScope && handles.Count == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Specify testName, testNames, testHandles, namePrefix, or firstN for selected Clash Detective test operations.");

            var result = new List<ClashTest>();
            if (hasNameScope)
            {
                foreach (var test in ResolveClashTests(tests, testName, testNames))
                {
                    if (!result.Any(existing => object.ReferenceEquals(existing, test)))
                        result.Add(test);
                }
            }

            if (hasPrefixScope)
            {
                var prefix = namePrefix.Trim();
                foreach (var test in tests.Where(test => !string.IsNullOrWhiteSpace(test.DisplayName) && test.DisplayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!result.Any(existing => object.ReferenceEquals(existing, test)))
                        result.Add(test);
                }
            }

            foreach (var handle in handles)
            {
                int testIndex;
                if (!TryParseClashTestHandle(handle, out testIndex))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Invalid test handle: " + handle);

                if (testIndex < 1 || testIndex > tests.Count)
                    continue;

                var test = tests[testIndex - 1];
                if (!result.Any(existing => object.ReferenceEquals(existing, test)))
                    result.Add(test);
            }

            if (hasFirstNScope)
            {
                var count = firstN.Value;
                if (count < 1)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "firstN must be greater than zero.");

                var scoped = result.Count > 0
                    ? result
                    : tests == null ? new List<ClashTest>() : tests.ToList();
                return scoped.Take(count).ToList();
            }

            return result;
        }

        private static bool HasRequestedClashTestScope(string testName, IEnumerable<string> testNames)
        {
            if (!string.IsNullOrWhiteSpace(testName))
                return true;

            return testNames != null && testNames.Any(name => !string.IsNullOrWhiteSpace(name));
        }

        private static bool HasOnlyFirstNClashTestScope(ClashManageTestsRequest request)
        {
            if (request == null || !request.FirstN.HasValue)
                return false;

            return string.IsNullOrWhiteSpace(request.TestName) &&
                   (request.TestNames == null || !request.TestNames.Any(name => !string.IsNullOrWhiteSpace(name))) &&
                   (request.TestHandles == null || !request.TestHandles.Any(handle => !string.IsNullOrWhiteSpace(handle))) &&
                   string.IsNullOrWhiteSpace(request.NamePrefix);
        }

        private static void ValidateHandleOnlyClashManageScope(ClashManageTestsRequest request, IList<ClashTest> matchedTests)
        {
            if (request == null)
                return;

            var handles = request.TestHandles == null
                ? new List<string>()
                : request.TestHandles.Where(handle => !string.IsNullOrWhiteSpace(handle)).Select(handle => handle.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (handles.Count == 0)
                return;

            var hasOtherScope =
                !string.IsNullOrWhiteSpace(request.TestName) ||
                (request.TestNames != null && request.TestNames.Any(name => !string.IsNullOrWhiteSpace(name))) ||
                !string.IsNullOrWhiteSpace(request.NamePrefix) ||
                request.FirstN.HasValue;
            if (hasOtherScope)
                return;

            if (matchedTests != null && matchedTests.Count > handles.Count)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Internal scope guard blocked clash_manage_tests: handle-only scope matched more tests than requested handles.");
        }

        private static string FormatRequestedTestNames(string testName, IEnumerable<string> testNames, IEnumerable<string> testHandles, string namePrefix = null, int? firstN = null)
        {
            return ClashScopeLabelHelper.FormatRequestedTestNames(testName, testNames, testHandles, namePrefix, firstN);
        }

        private static bool TryParseClashTestHandle(string handle, out int testIndex)
        {
            return ClashHandleHelper.TryParseTestHandle(handle, out testIndex);
        }

        private static string NormalizeClashTestOperation(string operation)
        {
            var value = ClashManageOperationHelper.NormalizeOperation(operation);
            if (value != null)
                return value;
            throw new AgentCommandException(ErrorCodes.SchemaViolation, "operation must be one of: run, reset, compact, rename, rename_batch, delete, move, sort, set_settings.");
        }

        private static ClashManageTestsResponse ClashManageMoveTest(
            DocumentClashTests testsData,
            IList<ClashTest> tests,
            ClashTest test,
            ClashManageTestsRequest request,
            ClashManageTestsResponse response,
            bool apply)
        {
            var item = BuildClashTestOperationResult(tests, test, "move", apply, request);
            response.Tests.Add(item);

            ClashTestLocation location;
            if (!ClashTestMutationService.TryFindLocation(testsData, test, out location))
            {
                item.Status = "failed";
                item.ErrorMessage = "Cannot resolve Clash Test parent/index for move.";
                response.Warnings.Add((test.DisplayName ?? item.TestHandle) + ": " + item.ErrorMessage);
                response.Message = "No Clash Detective tests were moved.";
                return response;
            }

            var targetIndex = Math.Max(0, Math.Min(request.TargetIndex.Value - 1, location.Parent.Children.Count - 1));
            item.OldIndex = location.Index + 1;
            item.NewIndex = targetIndex + 1;
            if (!apply)
            {
                item.Status = "planned";
                response.Message = "Dry-run only. Pass apply=true to move Clash Detective tests.";
                return response;
            }

            try
            {
                ClashTestMutationService.Move(testsData, location, location.Parent, targetIndex);
                item.Status = "applied";
                response.AffectedTestCount = 1;
                response.Message = "Applied 1 of 1 Clash Detective test operation(s).";
            }
            catch (Exception ex)
            {
                item.Status = "failed";
                item.ErrorMessage = ex.Message;
                response.Warnings.Add((test.DisplayName ?? item.TestHandle) + ": " + ex.Message);
                response.Message = "No Clash Detective tests were moved.";
            }

            return response;
        }

        private static ClashManageTestsResponse ClashManageSortTests(
            DocumentClashTests testsData,
            IList<ClashTest> tests,
            IList<ClashTest> matchedTests,
            ClashManageTestsRequest request,
            ClashManageTestsResponse response,
            bool apply)
        {
            var descending = IsDescendingSortDirection(request.SortDirection);
            var locations = new List<ClashTestLocation>();
            var itemsByTest = new Dictionary<ClashTest, ClashTestOperationResult>();
            foreach (var test in matchedTests)
            {
                ClashTestLocation location;
                if (ClashTestMutationService.TryFindLocation(testsData, test, out location))
                    locations.Add(location);
                else
                    response.Warnings.Add((test.DisplayName ?? string.Empty) + ": Cannot resolve Clash Test parent/index for sort.");
            }

            var sequence = 0;
            foreach (var group in locations.GroupBy(location => location.Parent))
            {
                var orderedSlots = group.OrderBy(location => location.Index).ToList();
                var sortedTests = group
                    .Select(location => location.Test)
                    .OrderBy(test => test.DisplayName ?? string.Empty, NaturalStringComparer.Instance)
                    .ToList();
                if (descending)
                    sortedTests.Reverse();

                for (var i = 0; i < orderedSlots.Count; i++)
                {
                    sequence++;
                    var test = sortedTests[i];
                    var source = group.First(location => object.ReferenceEquals(location.Test, test));
                    var target = orderedSlots[i];
                    var item = BuildClashTestOperationResult(tests, test, "sort", apply, request);
                    item.OldIndex = source.Index + 1;
                    item.NewIndex = target.Index + 1;
                    item.Status = apply ? "pending" : "planned";
                    response.Tests.Add(item);
                    if (test != null && !itemsByTest.ContainsKey(test))
                        itemsByTest.Add(test, item);
                }
            }

            if (!apply)
            {
                response.Message = "Dry-run only. Pass apply=true to sort Clash Detective tests.";
                return response;
            }

            foreach (var group in locations.GroupBy(location => location.Parent))
            {
                var parent = group.Key;
                var targetIndexes = group.Select(location => location.Index).OrderBy(index => index).ToList();
                var sortedTests = group
                    .Select(location => location.Test)
                    .OrderBy(test => test.DisplayName ?? string.Empty, NaturalStringComparer.Instance)
                    .ToList();
                if (descending)
                    sortedTests.Reverse();

                for (var i = 0; i < sortedTests.Count; i++)
                {
                    var test = sortedTests[i];
                    var targetIndex = targetIndexes[i];
                    ClashTestLocation current;
                    if (!ClashTestMutationService.TryFindLocation(testsData, test, out current))
                    {
                        ClashTestOperationResult item;
                        if (test != null && itemsByTest.TryGetValue(test, out item))
                        {
                            item.Status = "failed";
                            item.ErrorMessage = "Cannot resolve current index during sort.";
                        }
                        response.Warnings.Add((test.DisplayName ?? string.Empty) + ": Cannot resolve current index during sort.");
                        continue;
                    }

                    try
                    {
                        ClashTestMutationService.Move(testsData, current, parent, targetIndex);
                        ClashTestOperationResult item;
                        if (test != null && itemsByTest.TryGetValue(test, out item))
                            item.Status = "applied";
                        response.AffectedTestCount++;
                    }
                    catch (Exception ex)
                    {
                        ClashTestOperationResult item;
                        if (test != null && itemsByTest.TryGetValue(test, out item))
                        {
                            item.Status = "failed";
                            item.ErrorMessage = ex.Message;
                        }
                        response.Warnings.Add((test.DisplayName ?? string.Empty) + ": " + ex.Message);
                    }
                }
            }

            foreach (var item in response.Tests)
            {
                if (string.Equals(item.Status, "pending", StringComparison.OrdinalIgnoreCase))
                    item.Status = "cancelled";
            }
            response.Message = "Applied " + response.AffectedTestCount.ToString(CultureInfo.InvariantCulture) + " of " + response.MatchedTestCount.ToString(CultureInfo.InvariantCulture) + " Clash Detective test operation(s).";
            return response;
        }

        private static ClashTestOperationResult BuildClashTestOperationResult(IList<ClashTest> tests, ClashTest test, string operation, bool apply, ClashManageTestsRequest request)
        {
            var testIndex = GetClashTestIndex(tests, test);
            return new ClashTestOperationResult
            {
                TestIndex = testIndex,
                Handle = BuildClashTestHandle(testIndex),
                TestHandle = BuildClashTestHandle(testIndex),
                Name = test == null ? string.Empty : test.DisplayName ?? string.Empty,
                Operation = operation,
                Applied = apply,
                Status = apply ? "pending" : "planned",
                OldToleranceMm = DocUnitsToMm(SafeDouble(() => test.Tolerance)),
                NewToleranceMm = request != null && request.ToleranceMm.HasValue ? request.ToleranceMm.Value : (double?)null,
                OldTestType = SafeString(() => test.TestType.ToString()),
            };
        }

        private static bool IsDescendingSortDirection(string value)
        {
            value = (value ?? string.Empty).Trim();
            return value.Equals("desc", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("descending", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("reverse", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("убывание", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryFindClashTestLocation(GroupItem parent, ClashTest target, out GroupItem targetParent, out int targetIndex)
        {
            targetParent = null;
            targetIndex = -1;
            if (parent == null || parent.Children == null || target == null)
                return false;

            for (var i = 0; i < parent.Children.Count; i++)
            {
                var child = parent.Children[i];
                if (object.ReferenceEquals(child, target))
                {
                    targetParent = parent;
                    targetIndex = i;
                    return true;
                }

                var childGroup = child as GroupItem;
                if (childGroup != null && TryFindClashTestLocation(childGroup, target, out targetParent, out targetIndex))
                    return true;
            }

            return false;
        }

        private static int GetClashTestIndex(IList<ClashTest> tests, ClashTest target)
        {
            if (tests == null || target == null)
                return 0;

            var targetName = SafeString(() => target.DisplayName);
            var targetGuid = SafeString(() => target.Guid.ToString("D"));
            var emptyGuid = Guid.Empty.ToString("D");
            for (var i = 0; i < tests.Count; i++)
            {
                if (object.ReferenceEquals(tests[i], target))
                    return i + 1;
            }
            for (var i = 0; i < tests.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(targetGuid) &&
                    !string.Equals(targetGuid, emptyGuid, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(SafeString(() => tests[i].Guid.ToString("D")), targetGuid, StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            }
            for (var i = 0; i < tests.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(targetName) &&
                    string.Equals(SafeString(() => tests[i].DisplayName), targetName, StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            }

            return 0;
        }

        private static string BuildClashTestHandle(int testIndex)
        {
            return ClashHandleHelper.BuildTestHandle(testIndex);
        }

        private static string BuildClashResultHandle(int testIndex, int resultIndex)
        {
            return ClashHandleHelper.BuildResultHandle(testIndex, resultIndex);
        }

        private static IEnumerable<ClashResultSummary> EnumerateClashResultSummaries(ClashTest test, bool includeItemNames, bool includeAssignedTo)
        {
            if (test == null)
                yield break;

            foreach (var row in EnumerateClashResultSummaries(test.Children, test.DisplayName ?? string.Empty, string.Empty, includeItemNames, includeAssignedTo))
                yield return row;
        }

        private static IEnumerable<ClashReportWorkItem> EnumerateClashReportWorkItems(ClashTest test, string testName, string groupPath)
        {
            if (test == null)
                yield break;

            foreach (var row in EnumerateClashReportWorkItems(test.Children, testName, groupPath))
                yield return row;
        }

    }
}
