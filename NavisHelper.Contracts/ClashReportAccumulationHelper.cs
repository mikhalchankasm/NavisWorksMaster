using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashReportAccumulationHelper
    {
        public static ClashGenerateReportResponse BuildFileResponse(
            ClashGenerateReportResponse fileResponse,
            ClashGenerateReportResponse current,
            ClashGenerateReportResponse previous)
        {
            if (current == null)
                return null;

            if (fileResponse == null)
                fileResponse = current;

            fileResponse.Items = MergeItemsByIndex(previous == null ? null : previous.Items, current.Items);
            fileResponse.ReturnedResultCount = fileResponse.Items.Count;
            fileResponse.AccumulatedResultCount = fileResponse.Items.Count;
            fileResponse.ReturnedStatusCounts = BuildStatusCountsFromReportItems(fileResponse.Items);

            if (previous != null)
            {
                fileResponse.CreatedViewpointCount += previous.CreatedViewpointCount;
                fileResponse.ScreenshotCount += previous.ScreenshotCount;
                fileResponse.FullBoxTransparencyItemCount += previous.FullBoxTransparencyItemCount;
                fileResponse.Warnings = MergeWarnings(previous.Warnings, current.Warnings);
                if ((fileResponse.Clusters == null || fileResponse.Clusters.Count == 0) && previous.Clusters != null)
                    fileResponse.Clusters = previous.Clusters;
            }

            fileResponse.ClusterCount = fileResponse.Clusters == null ? 0 : fileResponse.Clusters.Count;
            fileResponse.ReturnedClusterCount = fileResponse.ClusterCount;

            return fileResponse;
        }

        public static List<ClashReportItem> MergeItemsByIndex(
            IEnumerable<ClashReportItem> previous,
            IEnumerable<ClashReportItem> current)
        {
            var itemsByIndex = new SortedDictionary<int, ClashReportItem>();
            AddItems(itemsByIndex, previous);
            AddItems(itemsByIndex, current);
            return itemsByIndex.Values.ToList();
        }

        public static Dictionary<string, int> BuildStatusCountsFromReportItems(IEnumerable<ClashReportItem> items)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (items == null)
                return counts;

            foreach (var item in items)
            {
                if (item == null)
                    continue;

                IncrementStatusCount(counts, item.Status);
            }

            return counts;
        }

        public static List<string> MergeWarnings(IEnumerable<string> previous, IEnumerable<string> current)
        {
            var warnings = new List<string>();
            if (previous != null)
                warnings.AddRange(previous.Where(warning => !string.IsNullOrWhiteSpace(warning)));
            if (current != null)
                warnings.AddRange(current.Where(warning => !string.IsNullOrWhiteSpace(warning)));
            return warnings.Distinct(StringComparer.Ordinal).ToList();
        }

        private static void AddItems(IDictionary<int, ClashReportItem> itemsByIndex, IEnumerable<ClashReportItem> items)
        {
            if (itemsByIndex == null || items == null)
                return;

            foreach (var item in items)
            {
                if (item != null)
                    itemsByIndex[item.Index] = item;
            }
        }

        private static void IncrementStatusCount(IDictionary<string, int> counts, string status)
        {
            if (counts == null)
                return;

            var key = string.IsNullOrWhiteSpace(status) ? "Unknown" : status.Trim();
            int current;
            counts.TryGetValue(key, out current);
            counts[key] = current + 1;
        }
    }
}
