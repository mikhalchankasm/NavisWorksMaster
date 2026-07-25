using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class VisibilityRootSummaryInput
    {
        public string RootDisplayName { get; set; }
        public string RootPath { get; set; }
        public string SourceFile { get; set; }
        public int AffectedItemCount { get; set; } = 1;
    }

    public sealed class VisibilityRootSummaryResult
    {
        public int TotalRootCount { get; set; }
        public bool Truncated { get; set; }
        public List<VisibilityRootSummary> Summaries { get; set; } = new List<VisibilityRootSummary>();
    }

    public static class VisibilityRootSummaryHelper
    {
        public static VisibilityRootSummaryResult Summarize(IEnumerable<VisibilityRootSummaryInput> inputs, int limit)
        {
            var grouped = new Dictionary<string, VisibilityRootSummary>(StringComparer.OrdinalIgnoreCase);
            if (inputs != null)
            {
                foreach (var input in inputs)
                {
                    if (input == null)
                        continue;

                    var rootPath = Normalize(input.RootPath);
                    var sourceFile = Normalize(input.SourceFile);
                    var rootDisplayName = Normalize(input.RootDisplayName);
                    var key = BuildKey(rootPath, sourceFile, rootDisplayName);

                    VisibilityRootSummary summary;
                    if (!grouped.TryGetValue(key, out summary))
                    {
                        summary = new VisibilityRootSummary
                        {
                            RootDisplayName = rootDisplayName,
                            RootPath = rootPath,
                            SourceFile = sourceFile,
                        };
                        grouped.Add(key, summary);
                    }

                    summary.AffectedItemCount += input.AffectedItemCount < 1 ? 1 : input.AffectedItemCount;
                }
            }

            var ordered = grouped.Values
                .OrderByDescending(summary => summary.AffectedItemCount)
                .ThenBy(summary => summary.RootPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(summary => summary.SourceFile, StringComparer.OrdinalIgnoreCase)
                .ThenBy(summary => summary.RootDisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var normalizedLimit = limit < 1 ? 1 : limit;

            return new VisibilityRootSummaryResult
            {
                TotalRootCount = ordered.Count,
                Truncated = ordered.Count > normalizedLimit,
                Summaries = ordered.Take(normalizedLimit).ToList(),
            };
        }

        private static string BuildKey(string rootPath, string sourceFile, string rootDisplayName)
        {
            if (!string.IsNullOrWhiteSpace(rootPath))
                return "path:" + rootPath;
            if (!string.IsNullOrWhiteSpace(sourceFile))
                return "source:" + sourceFile;
            return "name:" + rootDisplayName;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
