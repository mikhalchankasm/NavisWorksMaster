using System;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashReportTransportCompactionHelper
    {
        public static void Apply(ClashGenerateReportResponse response, string verbosity)
        {
            if (response == null)
                return;

            var normalized = ClashReportOptionHelper.NormalizeReportVerbosity(verbosity);
            if (normalized == null)
                throw new ArgumentException("verbosity must be full or compact.", nameof(verbosity));

            response.Verbosity = normalized;
            if (!string.Equals(normalized, ClashReportOptionHelper.VerbosityCompact, StringComparison.Ordinal))
                return;

            response.ResponseCompacted = true;
            if (response.CompactOmittedFields == null)
                response.CompactOmittedFields = new System.Collections.Generic.List<string>();
            response.CompactOmittedFields.Clear();
            response.CompactOmittedFields.Add("items[].description");
            response.CompactOmittedFields.Add("items[].item1Path");
            response.CompactOmittedFields.Add("items[].item2Path");
            if (response.Clusters != null && response.Clusters.Count > 0)
            {
                response.CompactOmittedFields.Add("clusters[].associationKeyA");
                response.CompactOmittedFields.Add("clusters[].associationKeyB");
                response.CompactOmittedFields.Add("clusters[].previewRows");
            }

            foreach (var item in response.Items ?? Enumerable.Empty<ClashReportItem>())
            {
                if (item == null)
                    continue;
                item.Description = string.Empty;
                item.Item1Path = string.Empty;
                item.Item2Path = string.Empty;
            }

            foreach (var cluster in response.Clusters ?? Enumerable.Empty<ClashClusterSummary>())
            {
                if (cluster == null)
                    continue;
                cluster.AssociationKeyA = string.Empty;
                cluster.AssociationKeyB = string.Empty;
                if (cluster.PreviewRows != null)
                    cluster.PreviewRows.Clear();
            }
        }
    }
}
