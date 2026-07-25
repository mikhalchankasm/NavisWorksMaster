using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashGenerateReportRequest
    {
        public bool? Apply { get; set; }
        public string TestName { get; set; }
        public List<string> TestNames { get; set; } = new List<string>();
        public List<string> StatusFilters { get; set; } = new List<string>();
        public bool? IncludeAllStatuses { get; set; }
        public int? Limit { get; set; }
        public int? ResultOffset { get; set; }
        public string OutputDirectory { get; set; }
        public bool? Overwrite { get; set; }
        public bool? Append { get; set; }
        public bool? ConfirmLargeReport { get; set; }
        public bool? RunTests { get; set; }
        public double? BoxOffsetMm { get; set; }
        public string BoxMode { get; set; }
        public double? ContextTransparency { get; set; }
        public bool? UseFullBoxTransparency { get; set; }
        public string GroupMode { get; set; }
        public string ArtifactGranularity { get; set; }
        public string Verbosity { get; set; }
        public double? ClusterDistanceMm { get; set; }
        public bool? IncludeClusterMembers { get; set; }
        public int? MaxMembersPerClusterInHtml { get; set; }
        public string ColorAHex { get; set; }
        public string ColorBHex { get; set; }
        public bool? CreateViewpoints { get; set; }
        public bool? CaptureScreenshots { get; set; }
        public bool? IncludeClashPointMarker { get; set; }
        public bool? CaptureTopViewScreenshots { get; set; }
        public string ScreenshotProfile { get; set; }
        public string ScreenshotFormat { get; set; }
        public int? ScreenshotMaxWidth { get; set; }
        public int? ScreenshotMaxHeight { get; set; }
        public int? ScreenshotJpegQuality { get; set; }
        public List<string> ExcludeItemNameContains { get; set; } = new List<string>();
    }

    public sealed class ClashSaveViewpointsRequest
    {
        public bool? Apply { get; set; }
        public string TestName { get; set; }
        public List<string> TestNames { get; set; } = new List<string>();
        public List<string> StatusFilters { get; set; } = new List<string>();
        public bool? IncludeAllStatuses { get; set; }
        public int? Limit { get; set; }
        public int? ResultOffset { get; set; }
        public bool? ConfirmLargeViewpoints { get; set; }
        public string FolderPath { get; set; }
        public bool? CreateResetViewpoint { get; set; }
        public double? BoxOffsetMm { get; set; }
        public string BoxMode { get; set; }
        public double? ContextTransparency { get; set; }
        public bool? UseFullBoxTransparency { get; set; }
        public bool? UseRootContextTransparency { get; set; }
        public bool? CreateOppositeViewpoints { get; set; }
        public string ColorAHex { get; set; }
        public string ColorBHex { get; set; }
        public bool? IncludeClashPointMarker { get; set; }
        public List<string> ExcludeItemNameContains { get; set; } = new List<string>();
    }

    public sealed class ClashGenerateReportResponse
    {
        public bool Applied { get; set; }
        public string OperationId { get; set; }
        public bool Cancelled { get; set; }
        public bool RunTestsRequested { get; set; }
        public bool TestsRun { get; set; }
        public string RequestedTestName { get; set; }
        public int MatchedTestCount { get; set; }
        public int TotalResultCount { get; set; }
        public int MatchedResultCount { get; set; }
        public int ReturnedResultCount { get; set; }
        public int ResultOffset { get; set; }
        public int NextResultOffset { get; set; }
        public bool HasMoreResults { get; set; }
        public int AccumulatedResultCount { get; set; }
        public int LargeReportThreshold { get; set; }
        public bool LargeReportConfirmationRequired { get; set; }
        public bool Truncated { get; set; }
        public Dictionary<string, int> TotalStatusCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> MatchedStatusCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> ReturnedStatusCounts { get; set; } = new Dictionary<string, int>();
        public List<string> ExcludeItemNameContains { get; set; } = new List<string>();
        public int ExcludedByItemNameCount { get; set; }
        public Dictionary<string, int> ExcludedByItemNameCounts { get; set; } = new Dictionary<string, int>();
        public double BoxOffsetMm { get; set; }
        public string BoxMode { get; set; }
        public string OutputDirectory { get; set; }
        public string ReportPath { get; set; }
        public string ManifestPath { get; set; }
        public string ClashBoxesPath { get; set; }
        public int CreatedViewpointCount { get; set; }
        public int ScreenshotCount { get; set; }
        public string ScreenshotProfile { get; set; }
        public string ScreenshotFormat { get; set; }
        public int ScreenshotMaxWidth { get; set; }
        public int ScreenshotMaxHeight { get; set; }
        public int ScreenshotJpegQuality { get; set; }
        public int FullBoxTransparencyItemCount { get; set; }
        public string GroupMode { get; set; }
        public string ArtifactGranularity { get; set; }
        public string Verbosity { get; set; }
        public bool ResponseCompacted { get; set; }
        public List<string> CompactOmittedFields { get; set; } = new List<string>();
        public double ClusterDistanceMm { get; set; }
        public int ClusterCount { get; set; }
        public int ReturnedClusterCount { get; set; }
        public List<ClashClusterSummary> Clusters { get; set; } = new List<ClashClusterSummary>();
        public string Message { get; set; }
        public List<ClashReportItem> Items { get; set; } = new List<ClashReportItem>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashSaveViewpointsResponse
    {
        public bool Applied { get; set; }
        public string RequestedTestName { get; set; }
        public int MatchedTestCount { get; set; }
        public int TotalResultCount { get; set; }
        public int MatchedResultCount { get; set; }
        public int ReturnedResultCount { get; set; }
        public int ResultOffset { get; set; }
        public int NextResultOffset { get; set; }
        public bool HasMoreResults { get; set; }
        public int LargeViewpointsThreshold { get; set; }
        public bool LargeViewpointsConfirmationRequired { get; set; }
        public bool Truncated { get; set; }
        public Dictionary<string, int> TotalStatusCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> MatchedStatusCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> ReturnedStatusCounts { get; set; } = new Dictionary<string, int>();
        public List<string> ExcludeItemNameContains { get; set; } = new List<string>();
        public int ExcludedByItemNameCount { get; set; }
        public Dictionary<string, int> ExcludedByItemNameCounts { get; set; } = new Dictionary<string, int>();
        public double BoxOffsetMm { get; set; }
        public string BoxMode { get; set; }
        public string FolderPath { get; set; }
        public bool ResetViewpointCreated { get; set; }
        public string ResetViewpointName { get; set; }
        public int CreatedViewpointCount { get; set; }
        public int FullBoxTransparencyItemCount { get; set; }
        public string Message { get; set; }
        public List<ClashSavedViewpointItem> Items { get; set; } = new List<ClashSavedViewpointItem>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashSavedViewpointItem
    {
        public int Index { get; set; }
        public int TestIndex { get; set; }
        public int ResultIndex { get; set; }
        public string TestName { get; set; }
        public string GroupPath { get; set; }
        public string ResultName { get; set; }
        public string Status { get; set; }
        public string AssignedTo { get; set; }
        public double? Distance { get; set; }
        public double BoxOffsetMm { get; set; }
        public string BoxMode { get; set; }
        public Point3Info ClashPoint { get; set; }
        public BoundingBoxInfo ClashBox { get; set; }
        public string Item1Name { get; set; }
        public string Item2Name { get; set; }
        public int Item1ItemCount { get; set; }
        public int Item2ItemCount { get; set; }
        public string ViewpointName { get; set; }
        public string ViewpointPath { get; set; }
        public bool ViewpointCreated { get; set; }
        public int FullBoxTransparencyItemCount { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class ClashReportStatusRequest
    {
        public string OperationId { get; set; }
    }

    public sealed class CancelClashReportRequest
    {
        public string OperationId { get; set; }
    }

    public sealed class ClashReportStatusResponse
    {
        public string OperationId { get; set; }
        public string State { get; set; }
        public bool IsRunning { get; set; }
        public bool CancelRequested { get; set; }
        public bool CancelAccepted { get; set; }
        public string OutputDirectory { get; set; }
        public string ReportPath { get; set; }
        public string ManifestPath { get; set; }
        public string CurrentTestName { get; set; }
        public string CurrentResultName { get; set; }
        public int ResultOffset { get; set; }
        public int TotalBatchCount { get; set; }
        public int ProcessedResultCount { get; set; }
        public int CreatedViewpointCount { get; set; }
        public int ScreenshotCount { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public long ElapsedMs { get; set; }
        public string Message { get; set; }
    }

    public sealed class ClashReportItem
    {
        public int Index { get; set; }
        public int TestIndex { get; set; }
        public int ResultIndex { get; set; }
        public string TestName { get; set; }
        public string GroupPath { get; set; }
        public string ResultName { get; set; }
        public string Status { get; set; }
        public string AssignedTo { get; set; }
        public string Description { get; set; }
        public double? Distance { get; set; }
        public double BoxOffsetMm { get; set; }
        public string BoxMode { get; set; }
        public Point3Info ClashPoint { get; set; }
        public BoundingBoxInfo ClashBox { get; set; }
        public string Item1Name { get; set; }
        public string Item2Name { get; set; }
        public string Item1Path { get; set; }
        public string Item2Path { get; set; }
        public int Item1ItemCount { get; set; }
        public int Item2ItemCount { get; set; }
        public string ViewpointName { get; set; }
        public string ViewpointPath { get; set; }
        public bool ViewpointCreated { get; set; }
        public string ScreenshotPath { get; set; }
        public bool ScreenshotCaptured { get; set; }
        public string TopViewScreenshotPath { get; set; }
        public bool TopViewScreenshotCaptured { get; set; }
        public int FullBoxTransparencyItemCount { get; set; }
        public int ClusterIndex { get; set; }
        public string ClusterId { get; set; }
        public string ClusterName { get; set; }
        public string ErrorMessage { get; set; }
    }
}
