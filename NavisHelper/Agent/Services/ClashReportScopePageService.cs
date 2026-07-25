using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal static class ClashReportScopePageService
    {
        public static ClashReportScopePage<TWorkItem> Build<TWorkItem>(
            ClashGenerateReportResponse response,
            IList<ClashTest> tests,
            IList<ClashTest> matchedTests,
            int resultOffset,
            int limit,
            bool includeAllStatuses,
            List<string> statusFilters,
            IList<string> excludeItemNameContains,
            int largeReportConfirmationThreshold,
            bool confirmLargeReport,
            Func<ClashTest, IEnumerable<TWorkItem>> enumerateWorkItems,
            Func<IList<ClashTest>, ClashTest, int> getTestIndex,
            Func<TWorkItem, string> getStatus,
            Action<TWorkItem, int> setTestIndex,
            Action<TWorkItem, int> setResultIndex,
            Func<TWorkItem, ClashResult> getResult,
            Func<ClashResult, IList<string>, IDictionary<string, int>, bool> matchesExcludedItemName,
            Func<IEnumerable<TWorkItem>, IEnumerable<TWorkItem>> sortRows,
            Action<IDictionary<string, int>, string> incrementStatusCount)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            return BuildCore(
                CreateReportAdapter(response),
                tests,
                matchedTests,
                resultOffset,
                limit,
                includeAllStatuses,
                statusFilters,
                excludeItemNameContains,
                largeReportConfirmationThreshold,
                confirmLargeReport,
                "Large report confirmation required: filtered report scope contains {0} clashes, threshold is {1}.",
                enumerateWorkItems,
                getTestIndex,
                getStatus,
                setTestIndex,
                setResultIndex,
                getResult,
                matchesExcludedItemName,
                sortRows,
                incrementStatusCount);
        }

        public static ClashReportScopePage<TWorkItem> BuildForSavedViewpoints<TWorkItem>(
            ClashSaveViewpointsResponse response,
            IList<ClashTest> tests,
            IList<ClashTest> matchedTests,
            int resultOffset,
            int limit,
            bool includeAllStatuses,
            List<string> statusFilters,
            IList<string> excludeItemNameContains,
            int largeViewpointsConfirmationThreshold,
            bool confirmLargeViewpoints,
            Func<ClashTest, IEnumerable<TWorkItem>> enumerateWorkItems,
            Func<IList<ClashTest>, ClashTest, int> getTestIndex,
            Func<TWorkItem, string> getStatus,
            Action<TWorkItem, int> setTestIndex,
            Action<TWorkItem, int> setResultIndex,
            Func<TWorkItem, ClashResult> getResult,
            Func<ClashResult, IList<string>, IDictionary<string, int>, bool> matchesExcludedItemName,
            Func<IEnumerable<TWorkItem>, IEnumerable<TWorkItem>> sortRows,
            Action<IDictionary<string, int>, string> incrementStatusCount)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            return BuildCore(
                CreateSavedViewpointsAdapter(response),
                tests,
                matchedTests,
                resultOffset,
                limit,
                includeAllStatuses,
                statusFilters,
                excludeItemNameContains,
                largeViewpointsConfirmationThreshold,
                confirmLargeViewpoints,
                "Large viewpoint confirmation required: filtered scope contains {0} clashes, threshold is {1}.",
                enumerateWorkItems,
                getTestIndex,
                getStatus,
                setTestIndex,
                setResultIndex,
                getResult,
                matchesExcludedItemName,
                sortRows,
                incrementStatusCount);
        }

        private static ClashReportScopePage<TWorkItem> BuildCore<TWorkItem>(
            ScopePageResponseAdapter response,
            IList<ClashTest> tests,
            IList<ClashTest> matchedTests,
            int resultOffset,
            int limit,
            bool includeAllStatuses,
            List<string> statusFilters,
            IList<string> excludeItemNameContains,
            int largeConfirmationThreshold,
            bool confirmedLargeScope,
            string largeConfirmationWarningFormat,
            Func<ClashTest, IEnumerable<TWorkItem>> enumerateWorkItems,
            Func<IList<ClashTest>, ClashTest, int> getTestIndex,
            Func<TWorkItem, string> getStatus,
            Action<TWorkItem, int> setTestIndex,
            Action<TWorkItem, int> setResultIndex,
            Func<TWorkItem, ClashResult> getResult,
            Func<ClashResult, IList<string>, IDictionary<string, int>, bool> matchesExcludedItemName,
            Func<IEnumerable<TWorkItem>, IEnumerable<TWorkItem>> sortRows,
            Action<IDictionary<string, int>, string> incrementStatusCount)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            tests = tests ?? new List<ClashTest>();
            matchedTests = matchedTests ?? new List<ClashTest>();
            var matchedRows = new List<TWorkItem>();
            response.SetMatchedTestCount(matchedTests.Count);

            foreach (var test in matchedTests)
            {
                var testIndex = getTestIndex == null ? 0 : getTestIndex(tests, test);
                var resultIndex = 0;
                var workItems = enumerateWorkItems == null ? Enumerable.Empty<TWorkItem>() : enumerateWorkItems(test);
                foreach (var workItem in workItems)
                {
                    resultIndex++;
                    response.IncrementTotalResultCount();
                    if (setTestIndex != null)
                        setTestIndex(workItem, testIndex);
                    if (setResultIndex != null)
                        setResultIndex(workItem, resultIndex);

                    var status = getStatus == null ? string.Empty : getStatus(workItem);
                    incrementStatusCount(response.TotalStatusCounts, status);
                    if (!includeAllStatuses && !ClashStatusFilterHelper.Matches(statusFilters, status))
                        continue;

                    response.IncrementMatchedResultCount();
                    incrementStatusCount(response.MatchedStatusCounts, status);
                    if (matchesExcludedItemName != null && matchesExcludedItemName(getResult == null ? null : getResult(workItem), excludeItemNameContains, response.ExcludedByItemNameCounts))
                    {
                        response.IncrementExcludedByItemNameCount();
                        continue;
                    }

                    matchedRows.Add(workItem);
                }
            }

            var confirmationRequired = matchedRows.Count > largeConfirmationThreshold && !confirmedLargeScope;
            response.SetLargeConfirmationRequired(confirmationRequired);
            if (confirmationRequired)
            {
                response.AddWarning(string.Format(
                    CultureInfo.InvariantCulture,
                    largeConfirmationWarningFormat,
                    matchedRows.Count.ToString(CultureInfo.InvariantCulture),
                    largeConfirmationThreshold.ToString(CultureInfo.InvariantCulture)));
            }

            if (resultOffset > matchedRows.Count)
                response.AddWarning("resultOffset is greater than the filtered result count; this batch will be empty.");

            var sortedRows = sortRows == null
                ? matchedRows
                : sortRows(matchedRows).ToList();
            var rows = sortedRows.Skip(resultOffset).Take(limit).ToList();
            var page = ClashResultPagingHelper.CreateReturnedPage(resultOffset, limit, matchedRows.Count, rows.Count, false);
            response.SetNextResultOffset(page.NextOffset);
            response.SetHasMoreResults(page.HasMore);
            response.SetTruncated(page.HasMore);
            foreach (var row in rows)
                incrementStatusCount(response.ReturnedStatusCounts, getStatus == null ? string.Empty : getStatus(row));
            response.SetReturnedResultCount(rows.Count);
            response.SetAccumulatedResultCount(rows.Count);

            return new ClashReportScopePage<TWorkItem>(matchedRows, rows, page);
        }

        private static ScopePageResponseAdapter CreateReportAdapter(ClashGenerateReportResponse response)
        {
            return new ScopePageResponseAdapter
            {
                SetMatchedTestCount = value => response.MatchedTestCount = value,
                IncrementTotalResultCount = () => response.TotalResultCount++,
                TotalStatusCounts = response.TotalStatusCounts,
                IncrementMatchedResultCount = () => response.MatchedResultCount++,
                MatchedStatusCounts = response.MatchedStatusCounts,
                ExcludedByItemNameCounts = response.ExcludedByItemNameCounts,
                IncrementExcludedByItemNameCount = () => response.ExcludedByItemNameCount++,
                SetLargeConfirmationRequired = value => response.LargeReportConfirmationRequired = value,
                AddWarning = warning => response.Warnings.Add(warning),
                SetNextResultOffset = value => response.NextResultOffset = value,
                SetHasMoreResults = value => response.HasMoreResults = value,
                SetTruncated = value => response.Truncated = value,
                ReturnedStatusCounts = response.ReturnedStatusCounts,
                SetReturnedResultCount = value => response.ReturnedResultCount = value,
                SetAccumulatedResultCount = value => response.AccumulatedResultCount = value,
            };
        }

        private static ScopePageResponseAdapter CreateSavedViewpointsAdapter(ClashSaveViewpointsResponse response)
        {
            return new ScopePageResponseAdapter
            {
                SetMatchedTestCount = value => response.MatchedTestCount = value,
                IncrementTotalResultCount = () => response.TotalResultCount++,
                TotalStatusCounts = response.TotalStatusCounts,
                IncrementMatchedResultCount = () => response.MatchedResultCount++,
                MatchedStatusCounts = response.MatchedStatusCounts,
                ExcludedByItemNameCounts = response.ExcludedByItemNameCounts,
                IncrementExcludedByItemNameCount = () => response.ExcludedByItemNameCount++,
                SetLargeConfirmationRequired = value => response.LargeViewpointsConfirmationRequired = value,
                AddWarning = warning => response.Warnings.Add(warning),
                SetNextResultOffset = value => response.NextResultOffset = value,
                SetHasMoreResults = value => response.HasMoreResults = value,
                SetTruncated = value => response.Truncated = value,
                ReturnedStatusCounts = response.ReturnedStatusCounts,
                SetReturnedResultCount = value => response.ReturnedResultCount = value,
                SetAccumulatedResultCount = _ => { },
            };
        }

        private sealed class ScopePageResponseAdapter
        {
            public Action<int> SetMatchedTestCount;
            public Action IncrementTotalResultCount;
            public IDictionary<string, int> TotalStatusCounts;
            public Action IncrementMatchedResultCount;
            public IDictionary<string, int> MatchedStatusCounts;
            public IDictionary<string, int> ExcludedByItemNameCounts;
            public Action IncrementExcludedByItemNameCount;
            public Action<bool> SetLargeConfirmationRequired;
            public Action<string> AddWarning;
            public Action<int> SetNextResultOffset;
            public Action<bool> SetHasMoreResults;
            public Action<bool> SetTruncated;
            public IDictionary<string, int> ReturnedStatusCounts;
            public Action<int> SetReturnedResultCount;
            public Action<int> SetAccumulatedResultCount;
        }
    }

    internal sealed class ClashReportScopePage<TWorkItem>
    {
        public ClashReportScopePage(List<TWorkItem> matchedRows, List<TWorkItem> rows, ClashResultPageInfo page)
        {
            MatchedRows = matchedRows ?? new List<TWorkItem>();
            Rows = rows ?? new List<TWorkItem>();
            Page = page;
        }

        public List<TWorkItem> MatchedRows { get; private set; }
        public List<TWorkItem> Rows { get; private set; }
        public ClashResultPageInfo Page { get; private set; }
    }
}
