using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public class SelectionClusteringOptions
    {
        public string ClusterBy { get; set; }
        public double? ClusterMaxDistanceMm { get; set; }
        public int? ClusterTargetSize { get; set; }
        public int? ClusterCount { get; set; }
        public double? ClusterGridSizeMm { get; set; }
        public int? MaxClusters { get; set; }
        public int? MaxItemsForClustering { get; set; }
        public int? MaxItemsForDistanceClustering { get; set; }
    }

    public sealed class MarkupSelectionRequest : SelectionClusteringOptions
    {
        public string Name { get; set; }
        public string FolderPath { get; set; }
        public string Source { get; set; }
        public bool? AutoTopView { get; set; }
        public bool? FitToSelection { get; set; }
        public double? FitMarginFactor { get; set; }
        public List<double> EllipseColor { get; set; }
        public int? Thickness { get; set; }
        public double? PaddingFactor { get; set; }
        public double? MinMarkSizeMm { get; set; }
        public int? MinRadiusPixels { get; set; }
        public string MarkStyle { get; set; }
        public bool? ArrowCallout { get; set; }
        public double? ArrowLengthMm { get; set; }
        public bool? TargetCrosshair { get; set; }
        public double? HatchAngleDeg { get; set; }
        public double? HatchSpacingMm { get; set; }
        public int? HatchSpacingPx { get; set; }
        public int? HatchThickness { get; set; }
        public double? MarkSoloMinSizeMm { get; set; }
        public double? MarkMergeGapMm { get; set; }
        public bool? Overwrite { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class MarkupSelectionResponse
    {
        public bool Apply { get; set; }
        public string Name { get; set; }
        public string FolderPath { get; set; }
        public string Path { get; set; }
        public string Source { get; set; }
        public bool AutoTopView { get; set; }
        public bool FitToSelection { get; set; }
        public double FitMarginFactor { get; set; }
        public string MarkStyle { get; set; }
        public bool ArrowCallout { get; set; }
        public double ArrowLengthMm { get; set; }
        public bool TargetCrosshair { get; set; }
        public double MarkSoloMinSizeMm { get; set; }
        public double MarkMergeGapMm { get; set; }
        public int SelectedItemCount { get; set; }
        public int? MarkCount { get; set; }
        public int? SoloMarkCount { get; set; }
        public int? MergedMarkCount { get; set; }
        public int? EllipseCount { get; set; }
        public int? ArrowCount { get; set; }
        public int? SkippedItemCount { get; set; }
        public bool NameConflict { get; set; }
        public bool Overwrite { get; set; }
        public bool FolderExists { get; set; }
        public int? CreatedFolderCount { get; set; }
        public bool? Created { get; set; }
        public string ClusterBy { get; set; }
        public int ClusterCount { get; set; }
        public int DroppedClusterCount { get; set; }
        public int UncoveredItemCount { get; set; }
        public List<SelectionViewpointClusterInfo> Clusters { get; set; } = new List<SelectionViewpointClusterInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class LiveMarkersRequest
    {
        public string Style { get; set; }
        public bool? Visible { get; set; }
        public int? MarkerRadiusPixels { get; set; }
        public double? MarkSoloMinSizeMm { get; set; }
        public double? MarkMergeGapMm { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class LiveMarkersResponse
    {
        public bool Apply { get; set; }
        public bool Visible { get; set; }
        public bool Active { get; set; }
        public bool Persistent { get; set; }
        public string Style { get; set; }
        public int MarkerRadiusPixels { get; set; }
        public double MarkSoloMinSizeMm { get; set; }
        public double MarkMergeGapMm { get; set; }
        public int SelectedItemCount { get; set; }
        public int MarkerCount { get; set; }
        public int SoloMarkerCount { get; set; }
        public int MergedMarkerCount { get; set; }
        public int SkippedItemCount { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class SelectionViewpointClusterInfo
    {
        public int Index { get; set; }
        public int ItemCount { get; set; }
        public string ViewpointName { get; set; }
        public string ViewpointPath { get; set; }
        public List<string> PreviewItemNames { get; set; } = new List<string>();
        public bool PreviewItemNamesTruncated { get; set; }
    }

    public sealed class SectionBoxViewpointRequest : SelectionClusteringOptions
    {
        public string Name { get; set; }
        public string FolderPath { get; set; }
        public string Source { get; set; }
        public double? BoxOffsetMm { get; set; }
        public List<double> EllipseColor { get; set; }
        public int? Thickness { get; set; }
        public double? PaddingFactor { get; set; }
        public double? MinMarkSizeMm { get; set; }
        public int? MinRadiusPixels { get; set; }
        public string MarkStyle { get; set; }
        public bool? ArrowCallout { get; set; }
        public double? ArrowLengthMm { get; set; }
        public bool? TargetCrosshair { get; set; }
        public double? HatchAngleDeg { get; set; }
        public double? HatchSpacingMm { get; set; }
        public int? HatchSpacingPx { get; set; }
        public int? HatchThickness { get; set; }
        public double? MarkSoloMinSizeMm { get; set; }
        public double? MarkMergeGapMm { get; set; }
        public bool? Overwrite { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class SectionBoxViewpointResponse
    {
        public bool Apply { get; set; }
        public string Name { get; set; }
        public string FolderPath { get; set; }
        public string Path { get; set; }
        public string Source { get; set; }
        public double BoxOffsetMm { get; set; }
        public int SelectedItemCount { get; set; }
        public bool NameConflict { get; set; }
        public bool Overwrite { get; set; }
        public bool FolderExists { get; set; }
        public int? CreatedFolderCount { get; set; }
        public bool? Created { get; set; }
        public string ClusterBy { get; set; }
        public int ClusterCount { get; set; }
        public int DroppedClusterCount { get; set; }
        public int UncoveredItemCount { get; set; }
        public string MarkStyle { get; set; }
        public bool ArrowCallout { get; set; }
        public int? MarkCount { get; set; }
        public int? SoloMarkCount { get; set; }
        public int? MergedMarkCount { get; set; }
        public int? EllipseCount { get; set; }
        public int? ArrowCount { get; set; }
        public int? SkippedItemCount { get; set; }
        public List<SelectionViewpointClusterInfo> Clusters { get; set; } = new List<SelectionViewpointClusterInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ZoomToSelectionRequest
    {
    }

    public sealed class ZoomToSelectionResponse
    {
        public int SelectedItemCount { get; set; }
        public bool ZoomApplied { get; set; }
    }

    public sealed class FocusOnSelectionRequest
    {
    }

    public sealed class FocusOnSelectionResponse
    {
        public int SelectedItemCount { get; set; }
        public bool FocusApplied { get; set; }
    }

    public sealed class FitAllRequest
    {
    }

    public sealed class FitAllResponse
    {
        public bool FitApplied { get; set; }
    }
}
