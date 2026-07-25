using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashListTestsRequest
    {
        public int? Limit { get; set; }
        public int? Offset { get; set; }
        public string NamePrefix { get; set; }
        public string NameContains { get; set; }
        public bool? IncludeStatusCounts { get; set; }
    }

    public sealed class ClashListTestsResponse
    {
        public int TotalTestCount { get; set; }
        public int MatchedTestCount { get; set; }
        public int ReturnedTestCount { get; set; }
        public int Offset { get; set; }
        public int NextOffset { get; set; }
        public bool HasMoreTests { get; set; }
        public bool Truncated { get; set; }
        public List<ClashTestSummary> Tests { get; set; } = new List<ClashTestSummary>();
    }

    public sealed class ClashTestSummary
    {
        public int TestIndex { get; set; }
        public string Handle { get; set; }
        public string TestHandle { get; set; }
        public string Name { get; set; }
        public int Total { get; set; }
        public int New { get; set; }
        public int Active { get; set; }
        public Dictionary<string, int> StatusCounts { get; set; } = new Dictionary<string, int>();
    }

    public sealed class ClashListResultsRequest
    {
        public string TestName { get; set; }
        public int? Limit { get; set; }
        public int? ResultOffset { get; set; }
        public bool? IncludeItemNames { get; set; }
        public bool? IncludeAssignedTo { get; set; }
        public bool? IncludeIgnored { get; set; }
        public List<string> StatusFilters { get; set; } = new List<string>();
        public bool? IncludeAllStatuses { get; set; }
    }

    public sealed class ClashListResultsResponse
    {
        public string RequestedTestName { get; set; }
        public int MatchedTestCount { get; set; }
        public int TotalResultCount { get; set; }
        public int MatchedResultCount { get; set; }
        public int ReturnedResultCount { get; set; }
        public int ResultOffset { get; set; }
        public int NextResultOffset { get; set; }
        public bool HasMoreResults { get; set; }
        public bool Truncated { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public List<ClashResultSummary> Results { get; set; } = new List<ClashResultSummary>();
    }

    public sealed class ClashResultSummary
    {
        public int Index { get; set; }
        public int TestIndex { get; set; }
        public int ResultIndex { get; set; }
        public string Handle { get; set; }
        public string TestHandle { get; set; }
        public string ResultHandle { get; set; }
        public string TestName { get; set; }
        public string GroupPath { get; set; }
        public string GroupHandle { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public string AssignedTo { get; set; }
        public string Item1Name { get; set; }
        public string Item2Name { get; set; }
        public List<string> Item1Names { get; set; } = new List<string>();
        public List<string> Item2Names { get; set; } = new List<string>();
        public int Item1ItemCount { get; set; }
        public int Item2ItemCount { get; set; }
        public Point3Info ClashPoint { get; set; }
        public double? DistanceMm { get; set; }
        public bool Ignored { get; set; }
    }

    public sealed class ClashListClustersRequest
    {
        public string TestName { get; set; }
        public List<string> TestNames { get; set; } = new List<string>();
        public List<string> StatusFilters { get; set; } = new List<string>();
        public bool? IncludeAllStatuses { get; set; }
        public List<string> ExcludeItemNameContains { get; set; } = new List<string>();
        public string GroupMode { get; set; }
        public double? ClusterDistanceMm { get; set; }
        public int? Limit { get; set; }
        public int? ResultOffset { get; set; }
        public int? PreviewRowsPerCluster { get; set; }
        public int? MaxResults { get; set; }
        public string Verbosity { get; set; }
        public bool? IncludeIgnored { get; set; }
    }

    public sealed class ClashListClustersResponse
    {
        public string RequestedTestName { get; set; }
        public int MatchedTestCount { get; set; }
        public int TotalResultCount { get; set; }
        public int MatchedResultCount { get; set; }
        public int RawResultCount { get; set; }
        public int ClusterCount { get; set; }
        public int ReturnedClusterCount { get; set; }
        public int ResultOffset { get; set; }
        public int NextResultOffset { get; set; }
        public bool HasMoreClusters { get; set; }
        public bool Truncated { get; set; }
        public bool ResultsTruncated { get; set; }
        public int MaxResults { get; set; }
        public string GroupMode { get; set; }
        public double ClusterDistanceMm { get; set; }
        public int PreviewRowsPerCluster { get; set; }
        public int WeakAssociationCount { get; set; }
        public int ExcludedByItemNameCount { get; set; }
        public Dictionary<string, int> TotalStatusCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> MatchedStatusCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> ReturnedStatusCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> ExcludedByItemNameCounts { get; set; } = new Dictionary<string, int>();
        public List<ClashClusterSummary> Clusters { get; set; } = new List<ClashClusterSummary>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashClusterSummary
    {
        public int Index { get; set; }
        public string ClusterId { get; set; }
        public string GroupMode { get; set; }
        public int ClashCount { get; set; }
        public bool WeakAssociation { get; set; }
        public string AssociationKeyA { get; set; }
        public string AssociationKeyB { get; set; }
        public string DisplayNameA { get; set; }
        public string DisplayNameB { get; set; }
        public string AssociationLevelA { get; set; }
        public string AssociationLevelB { get; set; }
        public string SourceFileA { get; set; }
        public string SourceFileB { get; set; }
        public Point3Info Centroid { get; set; }
        public BoundingBoxInfo BoundingBox { get; set; }
        public Dictionary<string, int> StatusCounts { get; set; } = new Dictionary<string, int>();
        public List<string> Tags { get; set; } = new List<string>();
        public List<ClashClusterPreviewRow> PreviewRows { get; set; } = new List<ClashClusterPreviewRow>();
        public bool PreviewRowsTruncated { get; set; }
        public BoundingBoxInfo ArtifactBox { get; set; }
        public string ViewpointName { get; set; }
        public string ViewpointPath { get; set; }
        public bool ViewpointCreated { get; set; }
        public string ScreenshotPath { get; set; }
        public bool ScreenshotCaptured { get; set; }
        public string TopViewScreenshotPath { get; set; }
        public bool TopViewScreenshotCaptured { get; set; }
        public int FullBoxTransparencyItemCount { get; set; }
        public string ArtifactErrorMessage { get; set; }
    }

    public sealed class ClashClusterPreviewRow
    {
        public int TestIndex { get; set; }
        public int ResultIndex { get; set; }
        public string TestHandle { get; set; }
        public string ResultHandle { get; set; }
        public string TestName { get; set; }
        public string GroupPath { get; set; }
        public string ResultName { get; set; }
        public string Status { get; set; }
        public string Item1Name { get; set; }
        public string Item2Name { get; set; }
        public string Item1Path { get; set; }
        public string Item2Path { get; set; }
        public Point3Info ClashPoint { get; set; }
    }
}
