using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashExportPointsRequest
    {
        public List<string> TestNames { get; set; } = new List<string>();
        public List<string> TestHandles { get; set; } = new List<string>();
        public Point3Info Origin { get; set; }
        public double? RotationDeg { get; set; }
        public List<ClashExportLevel> Levels { get; set; } = new List<ClashExportLevel>();
        public double? GridSizeM { get; set; }
        public string OutputPath { get; set; }
        public bool? Apply { get; set; }
        public bool? IncludeIgnored { get; set; }
    }

    public sealed class ClashExportLevel
    {
        public string Name { get; set; }
        public double ZFrom { get; set; }
        public double ZTo { get; set; }
    }

    public sealed class ClashExportPointsResponse
    {
        public bool Applied { get; set; }
        public string OutputPath { get; set; }
        public string Format { get; set; }
        public int MatchedTestCount { get; set; }
        public int ExportedResultCount { get; set; }
        public long FileSizeBytes { get; set; }
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashBboxPairPlanRequest
    {
        public bool? Apply { get; set; }
        public string RootMode { get; set; }
        public string SourceMode { get; set; }
        public List<string> RootNames { get; set; } = new List<string>();
        public string NameContains { get; set; }
        public List<string> ExcludeNameContains { get; set; } = new List<string>();
        public string TargetRootName { get; set; }
        public int? RefineDepth { get; set; }
        public double? BboxToleranceMm { get; set; }
        public int? MaxRootItems { get; set; }
        public int? MaxCandidatePairs { get; set; }
        public int? PreviewLimit { get; set; }
        public bool? IncludeRejected { get; set; }
        public string OutputPath { get; set; }
        public bool? OverwriteExisting { get; set; }
    }

    public sealed class ClashBboxPairPlanResponse
    {
        public bool Applied { get; set; }
        public string RootMode { get; set; }
        public string SourceMode { get; set; }
        public string TargetRootName { get; set; }
        public int TargetRootMatchCount { get; set; }
        public int TargetFilteredPairCount { get; set; }
        public int RefineDepth { get; set; }
        public double BboxToleranceMm { get; set; }
        public int TotalRootItems { get; set; }
        public int ReturnedRootItems { get; set; }
        public bool RootItemsTruncated { get; set; }
        public int RootPairCount { get; set; }
        public int CandidatePairCount { get; set; }
        public int SkippedPairCount { get; set; }
        public bool CandidatePairsTruncated { get; set; }
        public bool PreviewTruncated { get; set; }
        public long ElapsedMs { get; set; }
        public string OutputPath { get; set; }
        public Dictionary<string, int> SkippedReasonCounts { get; set; } = new Dictionary<string, int>();
        public List<ClashBboxRootItem> RootItems { get; set; } = new List<ClashBboxRootItem>();
        public List<ClashBboxCandidatePair> CandidatePairs { get; set; } = new List<ClashBboxCandidatePair>();
        public List<ClashBboxCandidatePair> Pairs { get; set; } = new List<ClashBboxCandidatePair>();
        public List<ClashBboxRejectedPair> RejectedPairs { get; set; } = new List<ClashBboxRejectedPair>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashBboxRootItem
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string SourceFile { get; set; }
        public int ChildCount { get; set; }
        public BoundingBoxInfo BoundingBox { get; set; }
    }

    public sealed class ClashBboxCandidatePair
    {
        public int Index { get; set; }
        public ClashBboxRootItem A { get; set; }
        public ClashBboxRootItem B { get; set; }
        public int CheckedChildPairCount { get; set; }
        public int ChildIntersectingPairCount { get; set; }
        public string Reason { get; set; }
    }

    public sealed class ClashBboxRejectedPair
    {
        public int Index { get; set; }
        public ClashBboxRootItem A { get; set; }
        public ClashBboxRootItem B { get; set; }
        public string Reason { get; set; }
        public string Warning { get; set; }
    }

    public sealed class ClashPairTestsCreateRequest
    {
        public bool? Apply { get; set; }
        public List<ClashBboxCandidatePair> Pairs { get; set; } = new List<ClashBboxCandidatePair>();
        public string PlanOutputPath { get; set; }
        public string TestNamePrefix { get; set; }
        public int? Limit { get; set; }
        public double? ToleranceMm { get; set; }
        public string TestType { get; set; }
        public bool? OverwriteExisting { get; set; }
        public string SettingsFromTestName { get; set; }
    }

    public sealed class ClashPairTestsCreateResponse
    {
        public bool Applied { get; set; }
        public string TestNamePrefix { get; set; }
        public int InputPairCount { get; set; }
        public int PlannedTestCount { get; set; }
        public int CreatedTestCount { get; set; }
        public int SkippedTestCount { get; set; }
        public int ConflictTestCount { get; set; }
        public double? ToleranceMm { get; set; }
        public string TestType { get; set; }
        public string SettingsSource { get; set; }
        public string Message { get; set; }
        public List<ClashPairTestPlanItem> Tests { get; set; } = new List<ClashPairTestPlanItem>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashPairTestPlanItem
    {
        public int PairIndex { get; set; }
        public string TestName { get; set; }
        public string AName { get; set; }
        public string APath { get; set; }
        public string BName { get; set; }
        public string BPath { get; set; }
        public int SelectionAItemCount { get; set; }
        public int SelectionBItemCount { get; set; }
        public bool Applied { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class ClashCreateMatrixFromSelectionRequest
    {
        public bool? Apply { get; set; }
        public string NamePrefix { get; set; }
        public bool? UseGeneratedPrefix { get; set; }
        public double? ToleranceMm { get; set; }
        public string TestType { get; set; }
        public bool? RunAfterCreate { get; set; }
        public bool? RemovePreviousGenerated { get; set; }
        public List<string> MatrixItemNames { get; set; } = new List<string>();
        public string MatrixNameContains { get; set; }
        public List<string> MatrixExcludeNameContains { get; set; } = new List<string>();
        public int? MaxSelectedItems { get; set; }
        public bool? ConfirmLargeMatrix { get; set; }
        public bool? IncludePairNames { get; set; }
        public string PairNameTemplate { get; set; }
        public int? PairNameStartIndex { get; set; }
    }

    public sealed class ClashCreateMatrixFromSelectionResponse
    {
        public bool Applied { get; set; }
        public string NamePrefix { get; set; }
        public bool UseGeneratedPrefix { get; set; }
        public int SelectedItemCount { get; set; }
        public int PlannedPairCount { get; set; }
        public int PlannedTestCount { get; set; }
        public int CreatedTestCount { get; set; }
        public int RanTestCount { get; set; }
        public int RemovedPreviousTestCount { get; set; }
        public bool PreviousTestsPreviewTruncated { get; set; }
        public List<string> PreviousTestsToRemove { get; set; } = new List<string>();
        public int SkippedTestCount { get; set; }
        public bool LargeMatrixConfirmationRequired { get; set; }
        public int LargeMatrixThreshold { get; set; }
        public double? ToleranceMm { get; set; }
        public string TestType { get; set; }
        public bool RunAfterCreate { get; set; }
        public bool RemovePreviousGenerated { get; set; }
        public string PairNameTemplate { get; set; }
        public int PairNameStartIndex { get; set; }
        public string MatrixInputSource { get; set; }
        public long ElapsedMs { get; set; }
        public bool PlannedTestsTruncated { get; set; }
        public string Message { get; set; }
        public List<ClashMatrixSelectedItem> SelectedItems { get; set; } = new List<ClashMatrixSelectedItem>();
        public List<ClashMatrixTestPlanItem> Tests { get; set; } = new List<ClashMatrixTestPlanItem>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashMatrixSelectedItem
    {
        public int SelectionIndex { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string SourceFile { get; set; }
    }

    public sealed class ClashMatrixTestPlanItem
    {
        public int PairIndex { get; set; }
        public int ASelectionIndex { get; set; }
        public int BSelectionIndex { get; set; }
        public string Handle { get; set; }
        public string TestHandle { get; set; }
        public string TestName { get; set; }
        public string AName { get; set; }
        public string APath { get; set; }
        public string BName { get; set; }
        public string BPath { get; set; }
        public bool Applied { get; set; }
        public bool Ran { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
    }
}
