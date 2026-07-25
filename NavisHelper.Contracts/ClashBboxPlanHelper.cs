using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashBboxPlanHelper
    {
        public const string CsvHeader = "index,a_name,a_path,b_name,b_path,checked_child_pair_count,child_intersecting_pair_count,reason";

        public static bool MatchesRootFilters(
            string name,
            string path,
            string sourceFile,
            IList<string> rootNames,
            string nameContains,
            IList<string> excludeNameContains)
        {
            var values = new[] { name ?? string.Empty, path ?? string.Empty, sourceFile ?? string.Empty };
            if (rootNames != null && rootNames.Count > 0 && !rootNames.Any(query => values.Any(value => string.Equals(value, query, StringComparison.OrdinalIgnoreCase))))
                return false;
            if (!string.IsNullOrWhiteSpace(nameContains) && !values.Any(value => value.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0))
                return false;
            if (excludeNameContains != null && excludeNameContains.Any(query => values.Any(value => value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)))
                return false;
            return true;
        }

        public static ClashBboxPairPlanResponse BuildPreview(ClashBboxPairPlanResponse full, int previewLimit, bool includeRejected)
        {
            if (full == null)
                return null;

            return new ClashBboxPairPlanResponse
            {
                Applied = full.Applied,
                RootMode = full.RootMode,
                SourceMode = full.SourceMode,
                TargetRootName = full.TargetRootName,
                TargetRootMatchCount = full.TargetRootMatchCount,
                TargetFilteredPairCount = full.TargetFilteredPairCount,
                RefineDepth = full.RefineDepth,
                BboxToleranceMm = full.BboxToleranceMm,
                TotalRootItems = full.TotalRootItems,
                ReturnedRootItems = full.ReturnedRootItems,
                RootItemsTruncated = full.RootItemsTruncated,
                RootPairCount = full.RootPairCount,
                CandidatePairCount = full.CandidatePairCount,
                SkippedPairCount = full.SkippedPairCount,
                CandidatePairsTruncated = full.CandidatePairsTruncated,
                PreviewTruncated = full.RootItems.Count > previewLimit ||
                                   full.CandidatePairs.Count > previewLimit ||
                                   (includeRejected && full.RejectedPairs.Count > previewLimit),
                ElapsedMs = full.ElapsedMs,
                OutputPath = full.OutputPath,
                SkippedReasonCounts = new Dictionary<string, int>(full.SkippedReasonCounts, StringComparer.OrdinalIgnoreCase),
                RootItems = full.RootItems.Take(previewLimit).ToList(),
                CandidatePairs = full.CandidatePairs.Take(previewLimit).ToList(),
                RejectedPairs = includeRejected ? full.RejectedPairs.Take(previewLimit).ToList() : new List<ClashBboxRejectedPair>(),
                Warnings = full.Warnings.ToList(),
            };
        }

        public static string BuildCsvRow(ClashBboxCandidatePair pair)
        {
            return string.Join(",", new[]
            {
                pair.Index.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(pair.A == null ? string.Empty : pair.A.Name),
                EscapeCsv(pair.A == null ? string.Empty : pair.A.Path),
                EscapeCsv(pair.B == null ? string.Empty : pair.B.Name),
                EscapeCsv(pair.B == null ? string.Empty : pair.B.Path),
                pair.CheckedChildPairCount.ToString(CultureInfo.InvariantCulture),
                pair.ChildIntersectingPairCount.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(pair.Reason),
            });
        }

        public static string EscapeCsv(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
