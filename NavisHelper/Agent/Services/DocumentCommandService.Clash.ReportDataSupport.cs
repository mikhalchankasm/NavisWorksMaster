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

        private static IEnumerable<ClashResultSummary> SortClashResultSummaries(IEnumerable<ClashResultSummary> rows)
        {
            return (rows ?? Enumerable.Empty<ClashResultSummary>())
                .OrderBy(row => row.TestName ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(row => row.GroupPath ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(row => row.Name ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(row => row.ResultHandle ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<ClashReportWorkItem> SortClashReportRows(IEnumerable<ClashReportWorkItem> rows)
        {
            return (rows ?? Enumerable.Empty<ClashReportWorkItem>())
                .OrderBy(row => row.TestName ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(row => row.GroupPath ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(row => row.ResultName ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(row => row.TestIndex)
                .ThenBy(row => row.ResultIndex);
        }

        private static IEnumerable<ClashReportWorkItem> EnumerateClashReportWorkItems(IEnumerable<SavedItem> children, string testName, string groupPath)
        {
            if (children == null)
                yield break;

            foreach (var child in children)
            {
                var result = child as ClashResult;
                if (result != null)
                {
                    yield return new ClashReportWorkItem
                    {
                        Result = result,
                        TestName = testName,
                        GroupPath = groupPath,
                        ResultName = result.DisplayName ?? string.Empty,
                        Status = result.Status.ToString(),
                        AssignedTo = GetClashAssignee(result.AssignedTo),
                    };
                    continue;
                }

                var group = child as GroupItem;
                if (group == null)
                    continue;

                var nextPath = string.IsNullOrEmpty(groupPath)
                    ? group.DisplayName ?? string.Empty
                    : groupPath + " / " + (group.DisplayName ?? string.Empty);
                foreach (var row in EnumerateClashReportWorkItems(group.Children, testName, nextPath))
                    yield return row;
            }
        }

        private static IEnumerable<ClashResultSummary> EnumerateClashResultSummaries(IEnumerable<SavedItem> children, string testName, string groupPath, bool includeItemNames, bool includeAssignedTo)
        {
            if (children == null)
                yield break;

            foreach (var child in children)
            {
                var result = child as ClashResult;
                if (result != null)
                {
                    var item1Names = includeItemNames ? GetClashItemNames(result.Selection1, result.Item1, 10) : new List<string>();
                    var item2Names = includeItemNames ? GetClashItemNames(result.Selection2, result.Item2, 10) : new List<string>();
                    yield return new ClashResultSummary
                    {
                        TestName = testName,
                        GroupPath = groupPath,
                        Name = result.DisplayName ?? string.Empty,
                        Status = result.Status.ToString(),
                        AssignedTo = includeAssignedTo ? GetClashAssignee(result.AssignedTo) : string.Empty,
                        Item1Name = item1Names.Count > 0 ? item1Names[0] : string.Empty,
                        Item2Name = item2Names.Count > 0 ? item2Names[0] : string.Empty,
                        Item1Names = item1Names,
                        Item2Names = item2Names,
                        Item1ItemCount = includeItemNames ? GetClashItemCount(result.Selection1, result.Item1) : 0,
                        Item2ItemCount = includeItemNames ? GetClashItemCount(result.Selection2, result.Item2) : 0,
                        ClashPoint = result.Center == null ? null : ToPoint3Info(result.Center),
                        DistanceMm = DocUnitsToMm(SafeDouble(() => result.Distance)),
                        Ignored = ClashWorkflowService.IsIgnoredResult(result),
                    };
                    continue;
                }

                var group = child as GroupItem;
                if (group == null)
                    continue;

                var nextPath = string.IsNullOrEmpty(groupPath)
                    ? group.DisplayName ?? string.Empty
                    : groupPath + " / " + (group.DisplayName ?? string.Empty);
                foreach (var row in EnumerateClashResultSummaries(group.Children, testName, nextPath, includeItemNames, includeAssignedTo))
                    yield return row;
            }
        }

        private static bool MatchesClashStatusFilter(List<string> statusFilters, string status)
        {
            return ClashStatusFilterHelper.Matches(statusFilters, status);
        }

        private static List<string> NormalizeTextFilters(IEnumerable<string> filters)
        {
            return ClashReportValueHelper.NormalizeTextFilters(filters);
        }

        private static bool MatchesExcludedClashItemName(ClashResult result, IList<string> filters, IDictionary<string, int> counts)
        {
            if (result == null || filters == null || filters.Count == 0)
                return false;

            var values = new List<string>();
            values.AddRange(GetClashItemNames(result.Selection1, result.Item1, 20));
            values.AddRange(GetClashItemNames(result.Selection2, result.Item2, 20));
            values.Add(BuildItemPath(result.Item1));
            values.Add(BuildItemPath(result.Item2));

            var matched = false;
            foreach (var filter in filters)
            {
                if (values.Any(value => !string.IsNullOrEmpty(value) && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    matched = true;
                    if (counts != null)
                    {
                        int current;
                        counts.TryGetValue(filter, out current);
                        counts[filter] = current + 1;
                    }
                }
            }

            return matched;
        }

        private static void IncrementStatusCount(IDictionary<string, int> counts, string status)
        {
            ClashReportValueHelper.IncrementStatusCount(counts, status);
        }

        private static string GetClashItemName(ModelItem item)
        {
            return item == null ? string.Empty : item.DisplayName ?? string.Empty;
        }

        private static string GetClashAssignee(object assignee)
        {
            if (assignee == null)
                return string.Empty;

            var assigneeText = assignee as string;
            if (!string.IsNullOrWhiteSpace(assigneeText))
                return assigneeText;

            foreach (var propertyName in new[] { "DisplayName", "Name", "UserName", "LoginName", "Email" })
            {
                try
                {
                    var property = assignee.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                    var value = property == null ? null : property.GetValue(assignee, null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to read clash assignee property '" + propertyName + "': " + ex.Message, "ClashMcp");
                }
            }

            return assignee.ToString() ?? string.Empty;
        }

        private static List<string> GetClashItemNames(ModelItemCollection items, ModelItem fallback, int limit)
        {
            return ClashItemNameService.GetNames(items, fallback, limit);
        }

        private static int GetClashItemCount(ModelItemCollection items, ModelItem fallback)
        {
            if (items == null)
                return fallback == null ? 0 : 1;

            var count = 0;
            foreach (ModelItem item in items)
                count++;

            return count == 0 && fallback != null ? 1 : count;
        }

        private static ClashClusterRow BuildClashClusterRow(ClashReportWorkItem workItem)
        {
            var result = workItem == null ? null : workItem.Result;
            var box = BuildClashResultBox(result, 0, ClashBoxModeItems);
            var point = result == null ? null : result.Center;
            if (point == null && box != null)
                point = box.Center;

            return new ClashClusterRow
            {
                WorkItem = workItem,
                Association = ResolveClashAssociation(result),
                ClashPoint = point,
                ClashBox = box,
                Item1Path = result == null ? string.Empty : BuildItemPath(result.Item1),
                Item2Path = result == null ? string.Empty : BuildItemPath(result.Item2),
                Item1Name = result == null ? string.Empty : FirstOrEmpty(GetClashItemNames(result.Selection1, result.Item1, 1)),
                Item2Name = result == null ? string.Empty : FirstOrEmpty(GetClashItemNames(result.Selection2, result.Item2, 1)),
            };
        }

        private static ClashAssociation ResolveClashAssociation(ClashResult result)
        {
            var side1 = ResolveClashAssociationSide(result == null ? null : result.Selection1, result == null ? null : result.Item1, "A");
            var side2 = ResolveClashAssociationSide(result == null ? null : result.Selection2, result == null ? null : result.Item2, "B");
            if (StringComparer.OrdinalIgnoreCase.Compare(side1.Key, side2.Key) > 0)
            {
                var tmp = side1;
                side1 = side2;
                side2 = tmp;
            }

            return new ClashAssociation
            {
                PairKey = (side1.Key ?? string.Empty) + "||" + (side2.Key ?? string.Empty),
                A = side1,
                B = side2,
                Weak = side1.Weak || side2.Weak,
            };
        }

        private static ClashAssociationSide ResolveClashAssociationSide(ModelItemCollection items, ModelItem fallback, string sideName)
        {
            var representative = fallback ?? FirstModelItem(items);
            var sourceFile = TryGetSourceFile(representative);
            var namedNode = FindAssociationNode(representative);
            var identityNode = namedNode ?? representative;
            var stableId = TryGetModelItemStableId(identityNode);
            if (!string.IsNullOrWhiteSpace(stableId))
            {
                return new ClashAssociationSide
                {
                    Key = ClashClusterKeyHelper.BuildTypedKey(ClashClusterKeyHelper.StableIdentityPrefix, stableId),
                    DisplayName = GetAssociationDisplayName(identityNode, sourceFile, sideName),
                    Level = "stable_identity",
                    SourceFile = sourceFile,
                    Weak = false,
                };
            }

            if (namedNode != null)
            {
                var path = BuildItemPath(namedNode);
                return new ClashAssociationSide
                {
                    Key = ClashClusterKeyHelper.BuildTypedKey(ClashClusterKeyHelper.NamedPathPrefix, path),
                    DisplayName = GetAssociationDisplayName(namedNode, sourceFile, sideName),
                    Level = "named_parent",
                    SourceFile = sourceFile,
                    Weak = false,
                };
            }

            if (!string.IsNullOrWhiteSpace(sourceFile))
            {
                return new ClashAssociationSide
                {
                    Key = ClashClusterKeyHelper.BuildTypedKey(ClashClusterKeyHelper.SourceFilePrefix, sourceFile),
                    DisplayName = Path.GetFileName(sourceFile),
                    Level = "source_root",
                    SourceFile = sourceFile,
                    Weak = true,
                };
            }

            var itemPath = BuildItemPath(representative);
            if (!string.IsNullOrWhiteSpace(itemPath))
            {
                return new ClashAssociationSide
                {
                    Key = ClashClusterKeyHelper.BuildTypedKey(ClashClusterKeyHelper.LeafPathPrefix, itemPath),
                    DisplayName = GetAssociationDisplayName(representative, sourceFile, sideName),
                    Level = "leaf_path",
                    SourceFile = sourceFile,
                    Weak = true,
                };
            }

            return new ClashAssociationSide
            {
                Key = "unknown:" + sideName,
                DisplayName = sideName,
                Level = "unknown",
                SourceFile = string.Empty,
                Weak = true,
            };
        }

        private static ModelItem FirstModelItem(ModelItemCollection items)
        {
            if (items == null)
                return null;

            foreach (ModelItem item in items)
                return item;

            return null;
        }

        private static ModelItem FindAssociationNode(ModelItem item)
        {
            var current = item == null ? null : item.Parent ?? item;
            while (current != null)
            {
                var name = SafeString(() => current.DisplayName);
                if (IsUsableAssociationName(name))
                    return current;

                current = current.Parent;
            }

            return null;
        }

        private static bool IsUsableAssociationName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var value = name.Trim();
            if (value.Length < 2)
                return false;

            var normalized = value.ToLowerInvariant();
            switch (normalized)
            {
                case "item":
                case "items":
                case "geometry":
                case "solid":
                case "body":
                case "fragment":
                case "composite object":
                case "object":
                case "объект":
                case "геометрия":
                case "тело":
                    return false;
                default:
                    return true;
            }
        }

        private static string TryGetModelItemStableId(ModelItem item)
        {
            if (item == null)
                return string.Empty;

            foreach (var propertyName in new[] { "InstanceGuid", "Guid", "InstanceId", "Id" })
            {
                try
                {
                    var property = item.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                    if (property == null)
                        continue;

                    var value = property.GetValue(item, null);
                    if (value == null)
                        continue;

                    var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(text) ||
                        string.Equals(text, Guid.Empty.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(text, "0", StringComparison.OrdinalIgnoreCase))
                        continue;

                    return text.Trim();
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to read clash item property '" + propertyName + "': " + ex.Message, "ClashMcp");
                }
            }

            return string.Empty;
        }

        private static string GetAssociationDisplayName(ModelItem item, string sourceFile, string fallback)
        {
            var name = item == null ? string.Empty : SafeString(() => item.DisplayName);
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();

            if (!string.IsNullOrWhiteSpace(sourceFile))
                return Path.GetFileName(sourceFile);

            return fallback ?? string.Empty;
        }

        private static string NormalizeClusterKey(string value)
        {
            return ClashClusterKeyHelper.NormalizeKey(value);
        }

        private static List<ClashClusterBuild> BuildClashClusters(IList<ClashClusterRow> rows, string groupMode, double clusterDistanceUnits)
        {
            return ClashClusterConstructionService.Build(
                    rows,
                    groupMode,
                    clusterDistanceUnits,
                    row => row == null || row.Association == null ? "unknown" : row.Association.PairKey,
                    row => row == null ? null : row.ClashPoint)
                .Select(clusterRows => new ClashClusterBuild
                {
                    GroupMode = groupMode,
                    Rows = clusterRows,
                })
                .ToList();
        }

        private static ClashClusterSummary BuildClashClusterSummary(ClashClusterBuild cluster, string groupMode, double clusterDistanceMm, int previewRowsPerCluster)
        {
            var rows = cluster == null || cluster.Rows == null ? new List<ClashClusterRow>() : SortClashClusterRows(cluster.Rows).ToList();
            var association = BuildClusterAssociation(rows);
            var summary = new ClashClusterSummary
            {
                GroupMode = groupMode,
                ClashCount = rows.Count,
                WeakAssociation = association == null || association.Weak,
                AssociationKeyA = association == null || association.A == null ? string.Empty : association.A.Key,
                AssociationKeyB = association == null || association.B == null ? string.Empty : association.B.Key,
                DisplayNameA = association == null || association.A == null ? string.Empty : association.A.DisplayName,
                DisplayNameB = association == null || association.B == null ? string.Empty : association.B.DisplayName,
                AssociationLevelA = association == null || association.A == null ? string.Empty : association.A.Level,
                AssociationLevelB = association == null || association.B == null ? string.Empty : association.B.Level,
                SourceFileA = association == null || association.A == null ? string.Empty : association.A.SourceFile,
                SourceFileB = association == null || association.B == null ? string.Empty : association.B.SourceFile,
                Centroid = BuildClusterCentroid(rows),
                BoundingBox = BuildClusterBoundingBox(rows),
            };

            foreach (var row in rows)
                IncrementStatusCount(summary.StatusCounts, row.WorkItem == null ? string.Empty : row.WorkItem.Status);

            summary.Tags.AddRange(BuildClusterTags(rows));
            var previewRows = rows.Take(previewRowsPerCluster).Select(BuildClashClusterPreviewRow).ToList();
            summary.PreviewRows.AddRange(previewRows);
            summary.PreviewRowsTruncated = rows.Count > previewRows.Count;
            summary.ClusterId = BuildClashClusterId(summary, rows, clusterDistanceMm);
            return summary;
        }

        private static ClashReportClusterContext BuildClashReportClusterContext(
            IEnumerable<ClashReportWorkItem> workItems,
            string groupMode,
            double clusterDistanceMm,
            int memberRowsPerCluster)
        {
            var context = new ClashReportClusterContext();
            var rows = (workItems ?? Enumerable.Empty<ClashReportWorkItem>())
                .Where(item => item != null)
                .Select(BuildClashClusterRow)
                .ToList();
            if (rows.Count == 0 || string.Equals(groupMode, ClashClusterModeNone, StringComparison.OrdinalIgnoreCase))
                return context;

            var clusterDistanceUnits = SectionBoxHelper.MmToDocUnits(clusterDistanceMm);
            var summariesAndBuilds = BuildClashClusters(rows, groupMode, clusterDistanceUnits)
                .Select(cluster => new
                {
                    Build = cluster,
                    Summary = BuildClashClusterSummary(cluster, groupMode, clusterDistanceMm, memberRowsPerCluster),
                })
                .OrderByDescending(item => item.Summary.ClashCount)
                .ThenBy(item => item.Summary.AssociationKeyA ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(item => item.Summary.AssociationKeyB ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(item => item.Summary.ClusterId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var index = 0; index < summariesAndBuilds.Count; index++)
            {
                var summary = summariesAndBuilds[index].Summary;
                summary.Index = index + 1;
                context.Summaries.Add(summary);
                context.Executions.Add(new ClashReportClusterExecution
                {
                    Summary = summary,
                    WorkItems = (summariesAndBuilds[index].Build.Rows ?? new List<ClashClusterRow>())
                        .Where(row => row != null && row.WorkItem != null)
                        .Select(row => row.WorkItem)
                        .ToList(),
                });
                if (summary.WeakAssociation)
                    context.WeakAssociationCount++;

                var assignment = new ClashReportClusterAssignment
                {
                    ClusterIndex = summary.Index,
                    ClusterId = summary.ClusterId ?? string.Empty,
                    ClusterName = BuildClashClusterDisplayName(summary),
                };

                foreach (var row in summariesAndBuilds[index].Build.Rows ?? new List<ClashClusterRow>())
                {
                    if (row == null || row.WorkItem == null)
                        continue;

                    var handle = BuildClashResultHandle(row.WorkItem.TestIndex, row.WorkItem.ResultIndex);
                    if (!string.IsNullOrWhiteSpace(handle))
                        context.Assignments[handle] = assignment;
                }
            }

            return context;
        }

        private static ClashReportClusterAssignment FindClashReportClusterAssignment(ClashReportClusterContext context, ClashReportWorkItem row)
        {
            if (context == null || row == null)
                return null;

            var handle = BuildClashResultHandle(row.TestIndex, row.ResultIndex);
            ClashReportClusterAssignment assignment;
            return context.Assignments.TryGetValue(handle, out assignment) ? assignment : null;
        }

        private static string BuildClashClusterDisplayName(ClashClusterSummary summary)
        {
            if (summary == null)
                return string.Empty;

            var left = string.IsNullOrWhiteSpace(summary.DisplayNameA) ? "A" : summary.DisplayNameA.Trim();
            var right = string.IsNullOrWhiteSpace(summary.DisplayNameB) ? "B" : summary.DisplayNameB.Trim();
            return "#" + summary.Index.ToString(CultureInfo.InvariantCulture) + " " + left + " / " + right;
        }

        private static ClashAssociation BuildClusterAssociation(IList<ClashClusterRow> rows)
        {
            var associations = (rows ?? new List<ClashClusterRow>())
                .Where(row => row.Association != null)
                .Select(row => row.Association)
                .ToList();
            if (associations.Count == 0)
                return null;

            var first = associations[0];
            if (associations.All(item => string.Equals(item.PairKey, first.PairKey, StringComparison.OrdinalIgnoreCase)))
                return first;

            return new ClashAssociation
            {
                PairKey = "mixed",
                Weak = true,
                A = new ClashAssociationSide
                {
                    Key = "mixed",
                    DisplayName = "Mixed associations",
                    Level = "mixed",
                    SourceFile = string.Empty,
                    Weak = true,
                },
                B = new ClashAssociationSide
                {
                    Key = string.Empty,
                    DisplayName = string.Empty,
                    Level = "mixed",
                    SourceFile = string.Empty,
                    Weak = true,
                },
            };
        }

        private static IEnumerable<ClashClusterRow> SortClashClusterRows(IEnumerable<ClashClusterRow> rows)
        {
            return (rows ?? Enumerable.Empty<ClashClusterRow>())
                .OrderBy(row => row.WorkItem == null ? string.Empty : row.WorkItem.TestName ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(row => row.WorkItem == null ? string.Empty : row.WorkItem.GroupPath ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(row => row.WorkItem == null ? string.Empty : row.WorkItem.ResultName ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(row => row.WorkItem == null ? 0 : row.WorkItem.TestIndex)
                .ThenBy(row => row.WorkItem == null ? 0 : row.WorkItem.ResultIndex);
        }

        private static Point3Info BuildClusterCentroid(IList<ClashClusterRow> rows)
        {
            var points = (rows ?? new List<ClashClusterRow>())
                .Where(row => row.ClashPoint != null)
                .Select(row => row.ClashPoint)
                .ToList();
            if (points.Count == 0)
                return null;

            return new Point3Info
            {
                X = points.Average(point => point.X),
                Y = points.Average(point => point.Y),
                Z = points.Average(point => point.Z),
            };
        }

        private static BoundingBoxInfo BuildClusterBoundingBox(IList<ClashClusterRow> rows)
        {
            var boxes = (rows ?? new List<ClashClusterRow>())
                .Where(row => row.ClashBox != null)
                .Select(row => row.ClashBox)
                .ToList();
            if (boxes.Count == 0)
                return null;

            var minX = boxes.Min(box => box.Min.X);
            var minY = boxes.Min(box => box.Min.Y);
            var minZ = boxes.Min(box => box.Min.Z);
            var maxX = boxes.Max(box => box.Max.X);
            var maxY = boxes.Max(box => box.Max.Y);
            var maxZ = boxes.Max(box => box.Max.Z);
            return ToBoundingBoxInfo(new BoundingBox3D(new Point3D(minX, minY, minZ), new Point3D(maxX, maxY, maxZ)));
        }

        private static List<string> BuildClusterTags(IList<ClashClusterRow> rows)
        {
            var tags = new List<string>();
            foreach (var value in (rows ?? new List<ClashClusterRow>())
                .Select(row => row.WorkItem == null ? string.Empty : row.WorkItem.TestName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, NaturalStringComparer.Instance)
                .Take(6))
            {
                tags.Add("test:" + value);
            }

            foreach (var value in (rows ?? new List<ClashClusterRow>())
                .SelectMany(row => new[]
                {
                    row.Association == null || row.Association.A == null ? string.Empty : row.Association.A.SourceFile,
                    row.Association == null || row.Association.B == null ? string.Empty : row.Association.B.SourceFile,
                })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(Path.GetFileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, NaturalStringComparer.Instance)
                .Take(6))
            {
                tags.Add("source:" + value);
            }

            return tags;
        }

        private static ClashClusterPreviewRow BuildClashClusterPreviewRow(ClashClusterRow row)
        {
            var workItem = row == null ? null : row.WorkItem;
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
                Item1Name = row == null ? string.Empty : row.Item1Name ?? string.Empty,
                Item2Name = row == null ? string.Empty : row.Item2Name ?? string.Empty,
                Item1Path = row == null ? string.Empty : row.Item1Path ?? string.Empty,
                Item2Path = row == null ? string.Empty : row.Item2Path ?? string.Empty,
                ClashPoint = row == null || row.ClashPoint == null ? null : ToPoint3Info(row.ClashPoint),
            };
        }

        private static string BuildClashClusterId(ClashClusterSummary summary, IList<ClashClusterRow> rows, double clusterDistanceMm)
        {
            var basis = new StringBuilder();
            basis.Append(summary == null ? string.Empty : summary.GroupMode).Append("|")
                .Append(summary == null ? string.Empty : summary.AssociationKeyA).Append("|")
                .Append(summary == null ? string.Empty : summary.AssociationKeyB).Append("|")
                .Append(clusterDistanceMm.ToString("0.###", CultureInfo.InvariantCulture)).Append("|")
                .Append(summary == null ? 0 : summary.ClashCount).Append("|");
            if (summary != null && summary.Centroid != null)
            {
                basis.Append(Math.Round(summary.Centroid.X, 3).ToString(CultureInfo.InvariantCulture)).Append(",")
                    .Append(Math.Round(summary.Centroid.Y, 3).ToString(CultureInfo.InvariantCulture)).Append(",")
                    .Append(Math.Round(summary.Centroid.Z, 3).ToString(CultureInfo.InvariantCulture)).Append("|");
            }

            foreach (var handle in SortClashClusterRows(rows).Select(row => row.WorkItem == null ? string.Empty : BuildClashResultHandle(row.WorkItem.TestIndex, row.WorkItem.ResultIndex)).Take(50))
                basis.Append(handle).Append(";");

            return "clash-cluster:" + StableHexHash(basis.ToString());
        }

        private static string StableHexHash(string value)
        {
            return ClashClusterKeyHelper.StableHexHash(value);
        }

        private static string FirstOrEmpty(IList<string> values)
        {
            return ClashReportValueHelper.FirstOrEmpty(values);
        }

    }
}
