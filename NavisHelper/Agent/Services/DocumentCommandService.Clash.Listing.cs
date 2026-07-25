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
                foreach (var row in EnumerateClashResultSummaries(test, includeItemNames, includeAssignedTo))
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
