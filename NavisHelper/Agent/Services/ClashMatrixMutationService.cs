using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal static class ClashMatrixMutationService
    {
        public static int CountMatchingPreviousTests(DocumentClash clash, string matchText, bool matchAnywhere)
        {
            if (clash == null || clash.TestsData == null || clash.TestsData.Value == null || clash.TestsData.Value.TestsRoot == null ||
                string.IsNullOrWhiteSpace(matchText))
            {
                return 0;
            }
            var normalized = matchText.Trim();
            return EnumerateTests(clash.TestsData.Value.TestsRoot.Children)
                .Count(test => !string.IsNullOrWhiteSpace(test.DisplayName) &&
                               (matchAnywhere
                                   ? test.DisplayName.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0
                                   : test.DisplayName.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)));
        }

        public static List<string> FindMatchingPreviousTestNames(
            DocumentClash clash,
            string matchText,
            bool matchAnywhere,
            int limit)
        {
            if (clash == null || clash.TestsData == null || clash.TestsData.Value == null || clash.TestsData.Value.TestsRoot == null ||
                string.IsNullOrWhiteSpace(matchText))
            {
                return new List<string>();
            }

            var normalized = matchText.Trim();
            return EnumerateTests(clash.TestsData.Value.TestsRoot.Children)
                .Where(test => !string.IsNullOrWhiteSpace(test.DisplayName) &&
                               (matchAnywhere
                                   ? test.DisplayName.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0
                                   : test.DisplayName.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)))
                .Select(test => test.DisplayName)
                .Take(Math.Max(0, limit))
                .ToList();
        }

        public static ClashMatrixMutationResult Apply(
            DocumentClash clash,
            IList<ClashMatrixMutationItem> items,
            bool removePreviousGenerated,
            string removePreviousPrefix,
            bool removePreviousAnywhere,
            double? toleranceUnits,
            ClashTestType testType,
            ClashCreateMatrixFromSelectionResponse response)
        {
            if (clash == null || clash.TestsData == null || clash.TestsData.Value == null || clash.TestsData.Value.TestsRoot == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective data is not available.");
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            var result = new ClashMatrixMutationResult();
            var removedPreviousCopies = new List<ClashTest>();
            try
            {
                if (removePreviousGenerated)
                {
                    if (string.IsNullOrWhiteSpace(removePreviousPrefix))
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, "removePreviousGenerated=true requires a generated prefix or an explicit non-empty namePrefix.");

                    response.RemovedPreviousTestCount = RemoveGeneratedTests(clash.TestsData, removePreviousPrefix, removePreviousAnywhere, removedPreviousCopies);
                }

                var existingNames = new HashSet<string>(
                    ClashApiCompat.GetClashTests(clash)
                        .Where(test => !string.IsNullOrWhiteSpace(test.DisplayName))
                        .Select(test => test.DisplayName),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var entry in items ?? new List<ClashMatrixMutationItem>())
                {
                    if (entry == null || entry.PlanItem == null || entry.ItemA == null || entry.ItemB == null)
                        continue;

                    if (existingNames.Contains(entry.PlanItem.TestName))
                    {
                        entry.PlanItem.Status = "conflict";
                        entry.PlanItem.ErrorMessage = "A Clash Detective test with this name already exists.";
                        response.SkippedTestCount++;
                        response.PlannedTestCount--;
                        continue;
                    }

                    var test = BuildTest(entry.PlanItem.TestName, entry.ItemA, entry.ItemB, toleranceUnits, testType);
                    var testIndex = AddCopyAndResolveIndex(clash, test, entry.PlanItem.TestName);
                    result.CreatedTestNames.Add(entry.PlanItem.TestName);
                    existingNames.Add(entry.PlanItem.TestName);
                    entry.PlanItem.Handle = ClashHandleHelper.BuildTestHandle(testIndex);
                    entry.PlanItem.TestHandle = entry.PlanItem.Handle;
                    entry.PlanItem.Applied = true;
                    entry.PlanItem.Status = "created";
                    response.CreatedTestCount++;
                }
            }
            catch (Exception ex)
            {
                RollBackCreatedTests(clash, result.CreatedTestNames, response);
                RestoreRemovedTests(clash.TestsData, removedPreviousCopies, response);

                foreach (var item in response.Tests.Where(item => item != null && item.Applied))
                {
                    item.Applied = false;
                    item.Ran = false;
                    item.Status = "rolled_back";
                    item.ErrorMessage = "Created test was removed during rollback: " + ex.Message;
                }

                response.CreatedTestCount = 0;
                response.RemovedPreviousTestCount = 0;
                response.Warnings.Add("Rolled back created Clash Detective tests after failure: " + ex.Message);
                response.Message = "Failed while creating Clash Detective matrix tests; created tests from this operation were rolled back and previous generated tests were restored when possible.";
                result.Failed = true;
            }

            return result;
        }

        private static ClashTest BuildTest(string testName, ModelItem itemA, ModelItem itemB, double? toleranceUnits, ClashTestType testType)
        {
            var test = new ClashTest
            {
                DisplayName = testName,
                Guid = Guid.Empty,
                TestType = testType,
            };
            if (toleranceUnits.HasValue)
                test.Tolerance = toleranceUnits.Value;

            var selectionA = new ModelItemCollection();
            selectionA.Add(itemA);
            test.SelectionA.Selection.CopyFrom(selectionA);
            test.SelectionA.SelfIntersect = false;

            var selectionB = new ModelItemCollection();
            selectionB.Add(itemB);
            test.SelectionB.Selection.CopyFrom(selectionB);
            test.SelectionB.SelfIntersect = false;

            return test;
        }

        private static int AddCopyAndResolveIndex(DocumentClash clash, ClashTest test, string expectedName)
        {
            ClashApiCompat.AddClashTestCopy(clash.TestsData, test);
            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            for (var index = tests.Count - 1; index >= 0; index--)
            {
                if (tests[index] != null && string.Equals(ReadDisplayName(tests[index]), expectedName, StringComparison.OrdinalIgnoreCase))
                    return index + 1;
            }

            throw new AgentCommandException(ErrorCodes.SchemaViolation, "Created Clash Detective test could not be resolved: " + expectedName);
        }

        private static int RemoveGeneratedTests(
            DocumentClashTests testsData,
            string prefix,
            bool matchAnywhere,
            ICollection<ClashTest> removedCopies)
        {
            prefix = string.IsNullOrWhiteSpace(prefix) ? ClashTestNamePrefixHelper.MatrixGeneratedPrefix : prefix.Trim();
            var generated = EnumerateTests(testsData.Value.TestsRoot.Children)
                .Where(test => !string.IsNullOrWhiteSpace(test.DisplayName) &&
                               (matchAnywhere
                                   ? test.DisplayName.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0
                                   : test.DisplayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var test in generated)
            {
                var copy = test.CreateCopy() as ClashTest;
                if (copy != null && removedCopies != null)
                    removedCopies.Add(copy);
                ClashTestMutationService.RemoveTest(testsData, test);
            }

            return generated.Count;
        }

        private static IEnumerable<ClashTest> EnumerateTests(IEnumerable<SavedItem> items)
        {
            if (items == null)
                yield break;

            foreach (var item in items)
            {
                var test = item as ClashTest;
                if (test != null)
                {
                    yield return test;
                    continue;
                }

                var group = item as GroupItem;
                if (group == null)
                    continue;

                foreach (var childTest in EnumerateTests(group.Children))
                    yield return childTest;
            }
        }

        private static void RollBackCreatedTests(DocumentClash clash, IList<string> createdTestNames, ClashCreateMatrixFromSelectionResponse response)
        {
            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            for (var index = createdTestNames.Count - 1; index >= 0; index--)
            {
                var name = createdTestNames[index];
                try
                {
                    var test = tests.FirstOrDefault(candidate => string.Equals(candidate.DisplayName, name, StringComparison.OrdinalIgnoreCase));
                    if (test != null)
                        ClashTestMutationService.RemoveTest(clash.TestsData, test);
                }
                catch (Exception ex)
                {
                    var warning = name + ": failed to remove created Clash Matrix test during rollback: " + ex.Message;
                    response.Warnings.Add(warning);
                    Logger.Error(warning, "ClashMcp");
                }
            }
        }

        private static void RestoreRemovedTests(DocumentClashTests testsData, IEnumerable<ClashTest> removedCopies, ClashCreateMatrixFromSelectionResponse response)
        {
            foreach (var copy in removedCopies ?? new List<ClashTest>())
            {
                try
                {
                    ClashApiCompat.AddClashTestCopy(testsData, copy);
                }
                catch (Exception ex)
                {
                    var copyName = ReadDisplayName(copy);
                    var warning = (string.IsNullOrWhiteSpace(copyName) ? "previous generated Clash Matrix test" : copyName) +
                                  ": failed to restore removed generated test during rollback: " + ex.Message;
                    response.Warnings.Add(warning);
                    Logger.Error(warning, "ClashMcp");
                }
            }
        }

        private static string ReadDisplayName(ClashTest test)
        {
            if (test == null)
                return string.Empty;

            try
            {
                return test.DisplayName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    internal sealed class ClashMatrixMutationItem
    {
        public ModelItem ItemA;
        public ModelItem ItemB;
        public ClashMatrixTestPlanItem PlanItem;
    }

    internal sealed class ClashMatrixMutationResult
    {
        public bool Failed;
        public List<string> CreatedTestNames = new List<string>();
    }
}
