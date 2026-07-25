using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal static class ClashRunPreservationService
    {
        private const double MatchToleranceMm = 10.0;

        public static void RunTestPreservingReviewState(DocumentClashTests testsData, ClashTest test)
        {
            if (testsData == null || test == null)
                throw new ArgumentNullException(testsData == null ? nameof(testsData) : nameof(test));

            var snapshots = Capture(test);
            var testGuid = SafeGuid(test);
            var testName = SafeName(test);
            testsData.TestsRunTest(test);

            var refreshed = ResolveTest(testsData, testGuid, testName) ?? test;
            Restore(testsData, refreshed, snapshots);
            ClashIgnoreRuleStore.ApplyRules(testsData, refreshed, ClashIgnoreRuleStore.Load(Autodesk.Navisworks.Api.Application.ActiveDocument));
        }

        private static List<ResultSnapshot> Capture(ClashTest test)
        {
            var snapshots = new List<ResultSnapshot>();
            AddSnapshots(test, string.Empty, snapshots);
            return snapshots;
        }

        private static void AddSnapshots(GroupItem parent, string groupPath, ICollection<ResultSnapshot> snapshots)
        {
            if (parent == null || parent.Children == null)
                return;
            foreach (SavedItem child in parent.Children)
            {
                var result = child as ClashResult;
                if (result != null)
                {
                    snapshots.Add(new ResultSnapshot
                    {
                        PairKey = BuildPairKey(result),
                        X = result.Center == null ? (double?)null : result.Center.X,
                        Y = result.Center == null ? (double?)null : result.Center.Y,
                        Z = result.Center == null ? (double?)null : result.Center.Z,
                        Status = result.Status,
                        AssignedTo = ReadAssignee(result.AssignedTo),
                        Comments = result.Comments == null ? null : new CommentCollection(result.Comments),
                        GroupPath = groupPath,
                    });
                    continue;
                }
                var group = child as GroupItem;
                if (group == null)
                    continue;
                var nextPath = string.IsNullOrEmpty(groupPath)
                    ? SafeName(group)
                    : groupPath + " / " + SafeName(group);
                AddSnapshots(group, nextPath, snapshots);
            }
        }

        private static void Restore(DocumentClashTests testsData, ClashTest test, IList<ResultSnapshot> snapshots)
        {
            if (test == null || snapshots == null || snapshots.Count == 0)
                return;

            var byPair = snapshots
                .GroupBy(snapshot => snapshot.PairKey ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var matchedGroups = new Dictionary<string, List<ClashResult>>(StringComparer.OrdinalIgnoreCase);
            foreach (var result in ClashWorkflowService.EnumerateResults(test))
            {
                List<ResultSnapshot> candidates;
                if (!byPair.TryGetValue(BuildPairKey(result), out candidates) || candidates.Count == 0)
                    continue;
                var snapshot = FindNearestSnapshot(candidates, result.Center);
                if (snapshot == null)
                    continue;
                snapshot.Used = true;
                if (result.Status != snapshot.Status)
                    ClashWorkflowService.EditStatus(testsData, result, snapshot.Status);
                if (!string.IsNullOrWhiteSpace(snapshot.AssignedTo))
                    ClashWorkflowService.EditAssignedTo(testsData, result, snapshot.AssignedTo);
                if (snapshot.Comments != null && snapshot.Comments.Count > 0)
                    testsData.TestsEditResultComments(result, new CommentCollection(snapshot.Comments));

                var topGroupName = FirstGroupName(snapshot.GroupPath);
                if (!string.IsNullOrWhiteSpace(topGroupName))
                {
                    List<ClashResult> groupResults;
                    if (!matchedGroups.TryGetValue(topGroupName, out groupResults))
                    {
                        groupResults = new List<ClashResult>();
                        matchedGroups.Add(topGroupName, groupResults);
                    }
                    groupResults.Add(result);
                }
            }

            foreach (var entry in matchedGroups)
            {
                var group = ClashGroupMutationService.FindOrCreateGroup(testsData, test, entry.Key);
                ClashGroupMutationService.RebuildGroup(testsData, test, group, entry.Value);
            }
        }

        private static string BuildPairKey(ClashResult result)
        {
            if (result == null)
                return string.Empty;
            return BuildModelItemIdentity(result.Item1) + "||" + BuildModelItemIdentity(result.Item2);
        }

        private static ResultSnapshot FindNearestSnapshot(IEnumerable<ResultSnapshot> snapshots, Point3D point)
        {
            var available = (snapshots ?? Enumerable.Empty<ResultSnapshot>()).Where(snapshot => snapshot != null && !snapshot.Used).ToList();
            if (point == null)
                return available.FirstOrDefault(snapshot => !snapshot.X.HasValue);
            var maxDistance = Math.Max(1e-9, SectionBoxHelper.MmToDocUnits(MatchToleranceMm));
            return available
                .Where(snapshot => snapshot.X.HasValue && snapshot.Y.HasValue && snapshot.Z.HasValue)
                .Select(snapshot => new
                {
                    Snapshot = snapshot,
                    Distance = Math.Sqrt(
                        Math.Pow(point.X - snapshot.X.Value, 2) +
                        Math.Pow(point.Y - snapshot.Y.Value, 2) +
                        Math.Pow(point.Z - snapshot.Z.Value, 2)),
                })
                .Where(item => item.Distance <= maxDistance)
                .OrderBy(item => item.Distance)
                .Select(item => item.Snapshot)
                .FirstOrDefault();
        }

        private static string BuildModelItemIdentity(ModelItem item)
        {
            if (item == null)
                return "null";
            foreach (var propertyName in new[] { "InstanceGuid", "Guid", "InstanceId", "Id" })
            {
                try
                {
                    var property = item.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                    var value = property == null ? null : property.GetValue(item, null);
                    var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(text) && text != "0" && text != Guid.Empty.ToString("D"))
                        return propertyName + ":" + text.Trim();
                }
                catch
                {
                }
            }

            var segments = new List<string>();
            var current = item;
            while (current != null)
            {
                var name = SafeModelItemName(current);
                if (!string.IsNullOrWhiteSpace(name))
                    segments.Add(name);
                try { current = current.Parent; }
                catch { current = null; }
            }
            segments.Reverse();
            return "path:" + string.Join("/", segments);
        }

        private static ClashTest ResolveTest(DocumentClashTests testsData, Guid guid, string name)
        {
            if (testsData == null || testsData.Value == null || testsData.Value.TestsRoot == null)
                return null;
            return EnumerateTests(testsData.Value.TestsRoot).FirstOrDefault(test => guid != Guid.Empty && SafeGuid(test) == guid)
                ?? EnumerateTests(testsData.Value.TestsRoot).FirstOrDefault(test => string.Equals(SafeName(test), name, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<ClashTest> EnumerateTests(GroupItem parent)
        {
            if (parent == null || parent.Children == null)
                yield break;
            foreach (SavedItem child in parent.Children)
            {
                var test = child as ClashTest;
                if (test != null)
                {
                    yield return test;
                    continue;
                }
                var group = child as GroupItem;
                if (group == null)
                    continue;
                foreach (var nested in EnumerateTests(group))
                    yield return nested;
            }
        }

        private static string FirstGroupName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            var separator = path.IndexOf(" / ", StringComparison.Ordinal);
            return separator < 0 ? path.Trim() : path.Substring(0, separator).Trim();
        }

        private static string ReadAssignee(object assignee)
        {
            if (assignee == null)
                return string.Empty;
            var text = assignee as string;
            if (text != null)
                return text;
            try
            {
                var property = assignee.GetType().GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance);
                return Convert.ToString(property == null ? assignee : property.GetValue(assignee, null), CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string SafeName(SavedItem item)
        {
            try { return item == null ? string.Empty : item.DisplayName ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeModelItemName(ModelItem item)
        {
            try { return item == null ? string.Empty : item.DisplayName ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static Guid SafeGuid(SavedItem item)
        {
            try { return item == null ? Guid.Empty : item.Guid; }
            catch { return Guid.Empty; }
        }

        private sealed class ResultSnapshot
        {
            public string PairKey;
            public double? X;
            public double? Y;
            public double? Z;
            public bool Used;
            public ClashResultStatus Status;
            public string AssignedTo;
            public CommentCollection Comments;
            public string GroupPath;
        }
    }
}
