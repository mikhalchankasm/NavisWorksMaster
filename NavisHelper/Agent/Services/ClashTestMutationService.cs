using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal static class ClashTestMutationService
    {
        private const int DeleteStabilizeTimeoutMs = 5000;
        private const int DeleteStabilizePollMs = 100;

        public static void ApplyOperation(
            DocumentClashTests testsData,
            ClashTest test,
            string operation,
            string newName,
            double? toleranceUnits,
            ClashTestType? testType)
        {
            if (testsData == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective data is not available.");
            if (test == null)
                throw new ArgumentNullException(nameof(test));

            switch (operation)
            {
                case "run":
                    ClashRunPreservationService.RunTestPreservingReviewState(testsData, test);
                    return;
                case "reset":
                    testsData.TestsClearResults(test);
                    return;
                case "compact":
                    testsData.TestsCompactTest(test);
                    return;
                case "rename":
                    testsData.TestsEditDisplayName(test, newName.Trim());
                    return;
                case "delete":
                    RemoveTest(testsData, test);
                    return;
                case "set_settings":
                    ApplySettings(testsData, test, toleranceUnits, testType);
                    return;
                default:
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Unsupported Clash Detective operation: " + operation);
            }
        }

        public static void Move(
            DocumentClashTests testsData,
            ClashTestLocation source,
            GroupItem targetParent,
            int targetIndex)
        {
            if (testsData == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective data is not available.");
            if (source == null || source.Parent == null || targetParent == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Cannot resolve Clash Test parent/index for move.");

            testsData.TestsMove(source.Parent, source.Index, targetParent, targetIndex);
        }

        public static void RemoveTest(DocumentClashTests testsData, ClashTest test)
        {
            if (testsData == null || testsData.Value == null || testsData.Value.TestsRoot == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective tests root is not available.");

            ClashTestLocation location;
            if (!TryFindLocation(testsData, test, out location))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Cannot resolve Clash Test parent/index for delete.");

            testsData.TestsRemoveAt(location.Parent, location.Index);
        }

        public static bool WaitForTestCountAtMost(DocumentClash clash, int expectedMaxCount, out int actualCount)
        {
            actualCount = int.MaxValue;
            if (clash == null)
                return false;

            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    actualCount = ClashApiCompat.GetClashTests(clash).Count();
                    if (actualCount <= expectedMaxCount)
                        return true;
                }
                catch
                {
                    actualCount = int.MaxValue;
                }

                if (stopwatch.ElapsedMilliseconds >= DeleteStabilizeTimeoutMs)
                    return false;

                Thread.Sleep(DeleteStabilizePollMs);
            }
        }

        public static bool TryFindLocation(DocumentClashTests testsData, ClashTest target, out ClashTestLocation location)
        {
            location = null;
            if (testsData == null || testsData.Value == null || testsData.Value.TestsRoot == null || target == null)
                return false;

            var locations = EnumerateLocations(testsData.Value.TestsRoot).ToList();
            location = locations.FirstOrDefault(candidate => object.ReferenceEquals(candidate.Test, target));
            if (location != null)
                return true;

            if (target.Guid != Guid.Empty)
            {
                var guidMatches = locations.Where(candidate => candidate.Test != null && candidate.Test.Guid == target.Guid).ToList();
                if (guidMatches.Count == 1)
                {
                    location = guidMatches[0];
                    return true;
                }
            }

            var name = target.DisplayName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                var nameMatches = locations
                    .Where(candidate => string.Equals(candidate.Test == null ? string.Empty : candidate.Test.DisplayName ?? string.Empty, name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (nameMatches.Count == 1)
                {
                    location = nameMatches[0];
                    return true;
                }
            }

            return false;
        }

        private static void ApplySettings(DocumentClashTests testsData, ClashTest test, double? toleranceUnits, ClashTestType? testType)
        {
            var copy = test.CreateCopy() as ClashTest;
            if (copy == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Cannot create editable Clash Test copy.");

            if (toleranceUnits.HasValue)
                copy.Tolerance = toleranceUnits.Value;
            if (testType.HasValue)
                copy.TestType = testType.Value;

            testsData.TestsEditTestFromCopy(test, copy);
        }

        private static IEnumerable<ClashTestLocation> EnumerateLocations(GroupItem parent)
        {
            if (parent == null || parent.Children == null)
                yield break;

            for (var i = 0; i < parent.Children.Count; i++)
            {
                var child = parent.Children[i];
                var test = child as ClashTest;
                if (test != null)
                {
                    yield return new ClashTestLocation
                    {
                        Parent = parent,
                        Index = i,
                        Test = test,
                    };
                    continue;
                }

                var group = child as GroupItem;
                if (group == null)
                    continue;

                foreach (var nested in EnumerateLocations(group))
                    yield return nested;
            }
        }
    }

    internal sealed class ClashTestLocation
    {
        public GroupItem Parent { get; set; }
        public int Index { get; set; }
        public ClashTest Test { get; set; }
    }
}
