using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ListSelectionSetsRequest
    {
        public int? Limit { get; set; }
        public int? Offset { get; set; }
        public bool? IncludeItemIds { get; set; }
        public string PathPrefix { get; set; }
        public string NameContains { get; set; }
    }

    public sealed class ListSelectionSetsResponse
    {
        public int TotalItemCount { get; set; }
        public int FilteredItemCount { get; set; }
        public int Offset { get; set; }
        public int? NextOffset { get; set; }
        public int ReturnedItemCount { get; set; }
        public bool Truncated { get; set; }
        public List<SelectionSetItemInfo> Items { get; set; } = new List<SelectionSetItemInfo>();
    }

    public sealed class SelectionSetItemInfo
    {
        public string ItemId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string ParentPath { get; set; }
        public string Type { get; set; }
        public int Depth { get; set; }
        public int Index { get; set; }
        public int ChildCount { get; set; }
        public int ExplicitItemCount { get; set; }
        public bool HasExplicitModelItems { get; set; }
        public bool HasSearch { get; set; }
        public int SearchConditionCount { get; set; }
    }

    public sealed class SelectSelectionSetRequest
    {
        public string PathOrName { get; set; }
        public string ItemId { get; set; }
        public int? Occurrence { get; set; }
        public bool? AllowFolderExpansion { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class SelectSelectionSetResponse
    {
        public bool Apply { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public int SelectionSetCount { get; set; }
        public int SelectedItemCount { get; set; }
        public bool FolderExpansionSkipped { get; set; }
        public bool? Selected { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class CreateSearchSetRequest
    {
        public string Name { get; set; }
        public string FolderPath { get; set; }
        public string CombineOperator { get; set; }
        public List<FindItemsCondition> Conditions { get; set; } = new List<FindItemsCondition>();
        public bool? Overwrite { get; set; }
        public bool? SelectAfterCreate { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class CreateSearchSetResponse
    {
        public bool Apply { get; set; }
        public string Name { get; set; }
        public string FolderPath { get; set; }
        public string Path { get; set; }
        public int ConditionCount { get; set; }
        public int MatchedItemCount { get; set; }
        public int RuntimeResolvedConditionCount { get; set; }
        public bool NameConflict { get; set; }
        public bool FolderExists { get; set; }
        public int? CreatedFolderCount { get; set; }
        public bool? Created { get; set; }
        public bool? Overwritten { get; set; }
        public bool? Selected { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class SelectionSetsManageRequest
    {
        public string Operation { get; set; }
        public string PathOrName { get; set; }
        public string ItemId { get; set; }
        public int? Occurrence { get; set; }
        public string Name { get; set; }
        public string NewName { get; set; }
        public string TargetFolderPath { get; set; }
        public bool? Apply { get; set; }
        public bool? AllowDeleteNonEmptyFolder { get; set; }
    }

    public sealed class SelectionSetsManageResponse
    {
        public bool Apply { get; set; }
        public string Operation { get; set; }
        public string Name { get; set; }
        public string NewName { get; set; }
        public string Path { get; set; }
        public string NewPath { get; set; }
        public string TargetFolderPath { get; set; }
        public string Type { get; set; }
        public int MatchedItemCount { get; set; }
        public bool TargetFolderExists { get; set; }
        public int? CreatedFolderCount { get; set; }
        public bool? Changed { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class SelectionSetsReorderRequest
    {
        public string FolderPath { get; set; }
        public string ItemId { get; set; }
        public bool? Recursive { get; set; }
        public bool? FoldersFirst { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class SelectionSetsReorderResponse
    {
        public bool Apply { get; set; }
        public string FolderPath { get; set; }
        public bool Recursive { get; set; }
        public bool FoldersFirst { get; set; }
        public int ProcessedFolderCount { get; set; }
        public int ReorderedFolderCount { get; set; }
        public int MovedItemCount { get; set; }
        public bool? Changed { get; set; }
        public List<SelectionSetsReorderFolderPlan> Folders { get; set; } = new List<SelectionSetsReorderFolderPlan>();
    }

    public sealed class SelectionSetsReorderFolderPlan
    {
        public string FolderPath { get; set; }
        public int ChildCount { get; set; }
        public bool WouldChange { get; set; }
        public List<string> Before { get; set; } = new List<string>();
        public List<string> After { get; set; } = new List<string>();
    }

    public sealed class CreateSelectionSetRequest
    {
        public string Name { get; set; }
        public List<string> MatchHandles { get; set; } = new List<string>();
        public string FolderPath { get; set; }
        public bool? Overwrite { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class CreateSelectionSetResponse
    {
        public bool Apply { get; set; }
        public string Name { get; set; }
        public string FolderPath { get; set; }
        public string Path { get; set; }
        public int SelectedItemCount { get; set; }
        public string Source { get; set; }
        public bool Partial { get; set; }
        public List<SelectItemsHandleResult> MatchHandleResults { get; set; } = new List<SelectItemsHandleResult>();
        public bool NameConflict { get; set; }
        public bool FolderExists { get; set; }
        public int? CreatedFolderCount { get; set; }
        public bool? Created { get; set; }
        public bool? Overwritten { get; set; }
    }

    public sealed class BuildMtrViewpointsRequest
    {
        public string FolderPrefix { get; set; }
        public bool? CreatePlan { get; set; }
        public bool? CreateSectionBox { get; set; }
        public double? ClusterMaxDistanceMm { get; set; }
        public double? FitMarginFactor { get; set; }
        public List<double> EllipseColor { get; set; }
        public int? Thickness { get; set; }
        public double? PaddingFactor { get; set; }
        public string MarkStyle { get; set; }
        public bool? ArrowCallout { get; set; }
        public double? ArrowLengthMm { get; set; }
        public bool? TargetCrosshair { get; set; }
        public double? HatchAngleDeg { get; set; }
        public double? HatchSpacingMm { get; set; }
        public int? HatchSpacingPx { get; set; }
        public int? HatchThickness { get; set; }
        public double? MinMarkSizeMm { get; set; }
        public double? MarkSoloMinSizeMm { get; set; }
        public double? MarkMergeGapMm { get; set; }
        public double? BoxOffsetMm { get; set; }
        public bool? SkipExisting { get; set; }
        public int? MaxSetCount { get; set; }
        public int? MaxClustersPerSet { get; set; }
        public int? MaxItemsForDistanceClustering { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class BuildMtrViewpointsResponse
    {
        public bool Apply { get; set; }
        public string FolderPrefix { get; set; }
        public int SelectionSetCount { get; set; }
        public int ProcessedSetCount { get; set; }
        public int NonEmptySetCount { get; set; }
        public int SkippedEmptySetCount { get; set; }
        public int SkippedExistingSetCount { get; set; }
        public int CreatedPlanViewpointCount { get; set; }
        public int CreatedSectionBoxViewpointCount { get; set; }
        public bool Truncated { get; set; }
        public List<BuildMtrViewpointsItemResponse> Items { get; set; } = new List<BuildMtrViewpointsItemResponse>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class BuildMtrViewpointsItemResponse
    {
        public string SelectionSetPath { get; set; }
        public string FolderPath { get; set; }
        public int SelectedItemCount { get; set; }
        public int ClusterCount { get; set; }
        public bool SkippedEmpty { get; set; }
        public bool PlanSkippedExisting { get; set; }
        public bool SectionBoxSkippedExisting { get; set; }
        public int CreatedPlanViewpointCount { get; set; }
        public int CreatedSectionBoxViewpointCount { get; set; }
        public List<SelectionViewpointClusterInfo> Clusters { get; set; } = new List<SelectionViewpointClusterInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class SelectionSetsBuildViewpointsRequest
    {
        public string FolderPrefix { get; set; }
        public string NameTemplate { get; set; }
        public List<SelectionSetViewpointStep> Steps { get; set; } = new List<SelectionSetViewpointStep>();
        public bool? SkipExisting { get; set; }
        public int? MaxSetCount { get; set; }
        public string Verbosity { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class SelectionSetViewpointStep : SelectionClusteringOptions
    {
        public string Step { get; set; }
        public string Label { get; set; }
        public List<double> EllipseColor { get; set; }
        public int? Thickness { get; set; }
        public double? PaddingFactor { get; set; }
        public string MarkStyle { get; set; }
        public bool? ArrowCallout { get; set; }
        public double? ArrowLengthMm { get; set; }
        public bool? TargetCrosshair { get; set; }
        public double? HatchAngleDeg { get; set; }
        public double? HatchSpacingMm { get; set; }
        public int? HatchSpacingPx { get; set; }
        public int? HatchThickness { get; set; }
        public double? FitMarginFactor { get; set; }
        public double? MinMarkSizeMm { get; set; }
        public int? MinRadiusPixels { get; set; }
        public double? MarkSoloMinSizeMm { get; set; }
        public double? MarkMergeGapMm { get; set; }
        public double? BoxOffsetMm { get; set; }
        public int? WhenItemCountMin { get; set; }
        public int? WhenItemCountMax { get; set; }
        public bool? Overwrite { get; set; }
    }

    public sealed class SelectionSetsBuildViewpointsResponse
    {
        public bool Apply { get; set; }
        public string FolderPrefix { get; set; }
        public string NameTemplate { get; set; }
        public string Verbosity { get; set; }
        public int SelectionSetCount { get; set; }
        public int ProcessedSetCount { get; set; }
        public int NonEmptySetCount { get; set; }
        public int SkippedEmptySetCount { get; set; }
        public int SkippedExistingStepCount { get; set; }
        public int SkippedConditionStepCount { get; set; }
        public int CreatedViewpointCount { get; set; }
        public bool Truncated { get; set; }
        public List<SelectionSetsBuildViewpointsItemResponse> Items { get; set; } = new List<SelectionSetsBuildViewpointsItemResponse>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class SelectionSetsBuildViewpointsItemResponse
    {
        public string SelectionSetPath { get; set; }
        public string FolderPath { get; set; }
        public int SelectedItemCount { get; set; }
        public bool SkippedEmpty { get; set; }
        public List<SelectionSetViewpointStepResponse> Steps { get; set; } = new List<SelectionSetViewpointStepResponse>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class SelectionSetViewpointStepResponse
    {
        public string Step { get; set; }
        public string Label { get; set; }
        public string BaseName { get; set; }
        public string ClusterBy { get; set; }
        public int ClusterCount { get; set; }
        public int DroppedClusterCount { get; set; }
        public int UncoveredItemCount { get; set; }
        public bool SkippedByCondition { get; set; }
        public bool SkippedExisting { get; set; }
        public int CreatedViewpointCount { get; set; }
        public int? MarkCount { get; set; }
        public int? SoloMarkCount { get; set; }
        public int? MergedMarkCount { get; set; }
        public int? EllipseCount { get; set; }
        public int? ArrowCount { get; set; }
        public int? SkippedItemCount { get; set; }
        public List<SelectionViewpointClusterInfo> Clusters { get; set; } = new List<SelectionViewpointClusterInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
