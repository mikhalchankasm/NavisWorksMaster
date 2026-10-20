using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed class ClashZoneExtraction
    {
        public List<ClashZoneRawClash> Rows = new List<ClashZoneRawClash>();
        public int MatchedTestCount;
        public bool ResultsTruncated;
        public List<string> TestNames = new List<string>();
        public List<string> Warnings = new List<string>();
    }

    internal sealed partial class DocumentCommandService
    {
        public ClashListTestsResponse ClashListTests(Document document, ClashListTestsRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashListTestsRequest();
            var limit = ClampClashListTestsLimit(request.Limit);
            var offset = ClampNonNegative(request.Offset);
            var includeStatusCounts = request.IncludeStatusCounts.GetValueOrDefault(true);
            var tests = ClashApiCompat.GetClashTests(document.GetClash()).ToList();
            var filtered = tests
                .Select((test, index) => new { Test = test, TestIndex = index + 1 })
                .Where(item => string.IsNullOrWhiteSpace(request.NamePrefix) ||
                    (item.Test.DisplayName ?? string.Empty).StartsWith(request.NamePrefix.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(item => string.IsNullOrWhiteSpace(request.NameContains) ||
                    (item.Test.DisplayName ?? string.Empty).IndexOf(request.NameContains.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            var page = ClashResultPagingHelper.CreateLimitPage(offset, limit, filtered.Count);

            var response = new ClashListTestsResponse
            {
                TotalTestCount = tests.Count,
                MatchedTestCount = filtered.Count,
                ReturnedTestCount = page.ReturnedCount,
                Offset = page.Offset,
                NextOffset = page.NextOffset,
                HasMoreTests = page.HasMore,
                Truncated = page.HasMore,
            };

            foreach (var item in filtered.Skip(page.Offset).Take(page.ReturnedCount))
            {
                var test = item.Test;
                var testIndex = item.TestIndex;
                var summary = new ClashTestSummary
                {
                    TestIndex = testIndex,
                    Handle = BuildClashTestHandle(testIndex),
                    TestHandle = BuildClashTestHandle(testIndex),
                    Name = test.DisplayName ?? string.Empty,
                };

                AccumulateClashResults(test.Children, summary, includeStatusCounts);
                response.Tests.Add(summary);
            }

            return response;
        }

        public ClashListResultsResponse ClashListResults(Document document, ClashListResultsRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashListResultsRequest();
            var limit = ClampClashListResultsLimit(request.Limit);
            var resultOffset = ClampNonNegative(request.ResultOffset);
            var includeItemNames = request.IncludeItemNames.GetValueOrDefault(true);
            var includeAssignedTo = request.IncludeAssignedTo.GetValueOrDefault(true);
            var includeIgnored = request.IncludeIgnored == true;
            var statusFilterPlan = ClashStatusFilterHelper.Normalize(request.StatusFilters, request.IncludeAllStatuses == true, false);
            var statusFilters = statusFilterPlan.StatusFilters;
            var includeAllStatuses = statusFilterPlan.IncludeAllStatuses;
            var tests = ClashApiCompat.GetClashTests(document.GetClash()).ToList();
            var matchedTests = ResolveClashTests(tests, request.TestName).ToList();

            var response = new ClashListResultsResponse
            {
                RequestedTestName = request.TestName ?? string.Empty,
                MatchedTestCount = matchedTests.Count,
                ResultOffset = resultOffset,
            };
            if (statusFilterPlan.ShouldWarnIncludeAllOverrides)
                response.Warnings.Add("includeAllStatuses=true overrides the provided statusFilters.");

            var matchingRows = new List<ClashResultSummary>();
            foreach (var test in matchedTests)
            {
                var testIndex = GetClashTestIndex(tests, test);
                var testHandle = BuildClashTestHandle(testIndex);
                var groupHandles = BuildClashGroupHandleMap(test, testIndex);
                var resultIndex = 0;
                foreach (var row in EnumerateClashResultSummaries(test, includeItemNames, includeAssignedTo, request.IncludeComments == true))
                {
                    resultIndex++;
                    row.TestIndex = testIndex;
                    row.TestHandle = testHandle;
                    row.ResultIndex = resultIndex;
                    row.ResultHandle = BuildClashResultHandle(testIndex, resultIndex);
                    row.Handle = row.ResultHandle;
                    string groupHandle;
                    if (!string.IsNullOrWhiteSpace(row.GroupPath) && groupHandles.TryGetValue(row.GroupPath, out groupHandle))
                        row.GroupHandle = groupHandle;
                    response.TotalResultCount++;
                    if (!includeIgnored && row.Ignored)
                        continue;
                    if (!includeAllStatuses && !MatchesClashStatusFilter(statusFilters, row.Status))
                        continue;

                    response.MatchedResultCount++;
                    matchingRows.Add(row);
                }
            }

            if (resultOffset > matchingRows.Count)
                response.Warnings.Add("resultOffset is greater than the filtered result count; this batch will be empty.");

            var sortedRows = SortClashResultSummaries(matchingRows).ToList();
            var page = ClashResultPagingHelper.CreateLimitPage(resultOffset, limit, sortedRows.Count);
            response.HasMoreResults = page.HasMore;
            response.Truncated = response.HasMoreResults;
            response.NextResultOffset = page.NextOffset;
            var index = page.Offset;
            foreach (var row in sortedRows.Skip(page.Offset).Take(page.ReturnedCount))
            {
                index++;
                row.Index = index;
                response.Results.Add(row);
            }

            response.ReturnedResultCount = response.Results.Count;
            return response;
        }

        internal ClashZoneExtraction CollectClashZoneRows(Document document, ClashZoneExportRequest request)
        {
            var tests = ClashApiCompat.GetClashTests(document.GetClash()).ToList();
            var matched = ResolveClashTests(tests, string.Empty, request.TestNames, request.TestHandles,
                null, null, false, "testNames or testHandles", "clash_zones_export").ToList();
            if ((request.TestNames == null || !request.TestNames.Any(n => !string.IsNullOrWhiteSpace(n))) &&
                (request.TestHandles == null || !request.TestHandles.Any(h => !string.IsNullOrWhiteSpace(h))))
                matched = tests;
            var extraction = new ClashZoneExtraction {
                MatchedTestCount = matched.Count, TestNames = matched.Select(t => t.DisplayName ?? string.Empty).ToList() };
            var maximum = ClashZoneExportPolicy.ClampMaxResults(request.MaxResults);
            var missingPointCount = 0;
            var examined = 0;
            foreach (var test in matched)
            {
                foreach (var workItem in EnumerateClashReportWorkItems(test, test.DisplayName ?? string.Empty, string.Empty))
                {
                    var result = workItem.Result;
                    if (request.IncludeIgnored != true && ClashWorkflowService.IsIgnoredResult(result)) continue;
                    // maxResults caps the raw results examined, including ones without a clash point.
                    if (examined >= maximum)
                    {
                        extraction.ResultsTruncated = true;
                        break;
                    }
                    examined++;
                    var row = BuildClashClusterRow(workItem);
                    if (row.ClashPoint == null) { missingPointCount++; continue; }
                    var path1 = row.Item1Path;
                    var path2 = row.Item2Path;
                    var id1 = TryGetModelItemStableId(result.Item1);
                    var id2 = TryGetModelItemStableId(result.Item2);
                    if (string.IsNullOrWhiteSpace(id1)) id1 = path1;
                    if (string.IsNullOrWhiteSpace(id2)) id2 = path2;
                    var side1 = ResolveClashAssociationSide(result.Selection1, result.Item1, "A");
                    var swapped = !StringComparer.OrdinalIgnoreCase.Equals(side1.Key, row.Association.A.Key);
                    // SDK versions may not expose a creation timestamp.
                    var createdTime = SafeString(() =>
                    {
                        var createdProperty = result.GetType().GetProperty("CreatedTime");
                        var created = createdProperty == null ? null : createdProperty.GetValue(result, null);
                        return created is DateTime time ? time.ToString("o", CultureInfo.InvariantCulture) : null;
                    });
                    extraction.Rows.Add(new ClashZoneRawClash {
                        ResultGuid = result.Guid.ToString("D"), ClashName = workItem.ResultName,
                        TestName = test.DisplayName ?? string.Empty, Status = workItem.Status,
                        X = DocUnitsToMm(row.ClashPoint.X), Y = DocUnitsToMm(row.ClashPoint.Y), Z = DocUnitsToMm(row.ClashPoint.Z),
                        DistanceMm = DocUnitsToMm(result.Distance), CreatedTime = string.IsNullOrEmpty(createdTime) ? null : createdTime,
                        AssociationKeyA = row.Association.A.Key, AssociationKeyB = row.Association.B.Key,
                        AssociationNameA = row.Association.A.DisplayName, AssociationNameB = row.Association.B.DisplayName,
                        WeakAssociation = row.Association.Weak,
                        SourceFileA = row.Association.A.SourceFile, SourceFileB = row.Association.B.SourceFile,
                        ItemPathA = swapped ? path2 : path1, ItemPathB = swapped ? path1 : path2,
                        StableIdA = swapped ? id2 : id1, StableIdB = swapped ? id1 : id2 });
                }
                if (extraction.ResultsTruncated) break;
            }
            if (missingPointCount > 0)
                extraction.Warnings.Add("Skipped " + missingPointCount.ToString(CultureInfo.InvariantCulture) + " clash results without a clash point.");
            if (extraction.ResultsTruncated)
                extraction.Warnings.Add("Clash results exceeded maxResults; only the first " + maximum.ToString(CultureInfo.InvariantCulture) + " results were analyzed.");
            return extraction;
        }

        public ClashListClustersResponse ClashListClusters(Document document, ClashListClustersRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashListClustersRequest();
            var groupMode = NormalizeClashClusterMode(request.GroupMode);
            var clusterDistanceMm = NormalizePositiveDouble(request.ClusterDistanceMm, DefaultClashClusterDistanceMm, "clusterDistanceMm");
            var clusterDistanceUnits = SectionBoxHelper.MmToDocUnits(clusterDistanceMm);
            var limit = ClampClashClusterLimit(request.Limit);
            var resultOffset = ClampNonNegative(request.ResultOffset);
            var previewRowsPerCluster = ClampClashClusterPreviewRowsPerCluster(request.PreviewRowsPerCluster);
            var maxResults = ClampClashListResultsLimit(request.MaxResults);
            var excludeItemNameContains = NormalizeTextFilters(request.ExcludeItemNameContains);
            var statusFilterPlan = ClashStatusFilterHelper.Normalize(request.StatusFilters, request.IncludeAllStatuses == true, true);
            var statusFilters = statusFilterPlan.StatusFilters;
            var includeAllStatuses = statusFilterPlan.IncludeAllStatuses;
            var fullVerbosity = IsFullClashVerbosity(request.Verbosity);

            var tests = ClashApiCompat.GetClashTests(document.GetClash()).ToList();
            var matchedTests = ResolveClashTests(tests, request.TestName, request.TestNames).ToList();
            var response = new ClashListClustersResponse
            {
                RequestedTestName = request.TestName ?? string.Empty,
                MatchedTestCount = matchedTests.Count,
                ResultOffset = resultOffset,
                GroupMode = groupMode,
                ClusterDistanceMm = clusterDistanceMm,
                PreviewRowsPerCluster = previewRowsPerCluster,
                MaxResults = maxResults,
            };

            var rows = new List<ClashClusterRow>();
            foreach (var test in matchedTests)
            {
                var testIndex = GetClashTestIndex(tests, test);
                var resultIndex = 0;
                foreach (var workItem in EnumerateClashReportWorkItems(test, test.DisplayName ?? string.Empty, string.Empty))
                {
                    resultIndex++;
                    response.TotalResultCount++;
                    workItem.TestIndex = testIndex;
                    workItem.ResultIndex = resultIndex;
                    IncrementStatusCount(response.TotalStatusCounts, workItem.Status);
                    if (!includeAllStatuses && !MatchesClashStatusFilter(statusFilters, workItem.Status))
                        continue;
                    if (request.IncludeIgnored != true && ClashWorkflowService.IsIgnoredResult(workItem.Result))
                        continue;

                    response.MatchedResultCount++;
                    IncrementStatusCount(response.MatchedStatusCounts, workItem.Status);
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

                    var clusterRow = BuildClashClusterRow(workItem);
                    rows.Add(clusterRow);
                    if (clusterRow.Association != null && clusterRow.Association.Weak)
                        response.WeakAssociationCount++;
                }
            }

            response.RawResultCount = rows.Count;
            if (response.ResultsTruncated)
                response.Warnings.Add("Matched clash results exceeded maxResults; clustering was built from the first " + maxResults.ToString(CultureInfo.InvariantCulture) + " filtered results.");
            if (response.WeakAssociationCount > 0)
                response.Warnings.Add("Some cluster associations were resolved from coarse source/root or leaf-path fallbacks. Review associationLevelA/B before treating clusters as authoritative assignments.");

            var clusters = BuildClashClusters(rows, groupMode, clusterDistanceUnits)
                .Select(cluster => BuildClashClusterSummary(cluster, groupMode, clusterDistanceMm, previewRowsPerCluster))
                .OrderByDescending(cluster => cluster.ClashCount)
                .ThenBy(cluster => cluster.AssociationKeyA ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(cluster => cluster.AssociationKeyB ?? string.Empty, NaturalStringComparer.Instance)
                .ThenBy(cluster => cluster.ClusterId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!fullVerbosity)
                CompactClashPreviewRows(clusters.SelectMany(cluster => cluster.PreviewRows));

            response.ClusterCount = clusters.Count;
            var page = ClashResultPagingHelper.CreateLimitPage(resultOffset, limit, clusters.Count);
            response.HasMoreClusters = page.HasMore;
            response.Truncated = response.HasMoreClusters;
            response.NextResultOffset = page.NextOffset;
            var returned = clusters.Skip(page.Offset).Take(page.ReturnedCount).ToList();
            for (var index = 0; index < returned.Count; index++)
            {
                returned[index].Index = page.Offset + index + 1;
                foreach (var statusCount in returned[index].StatusCounts)
                    IncrementCount(response.ReturnedStatusCounts, statusCount.Key, statusCount.Value);
                response.Clusters.Add(returned[index]);
            }

            response.ReturnedClusterCount = response.Clusters.Count;
            return response;
        }
    }
}
