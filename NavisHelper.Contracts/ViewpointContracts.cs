using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class CurrentViewpointInfoRequest
    {
    }

    public sealed class CurrentViewpointInfoResponse
    {
        public bool HasActiveView { get; set; }
        public bool HasCurrentViewpoint { get; set; }
        public Point3Info Position { get; set; }
        public RotationInfo Rotation { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
    }

    public sealed class RotationInfo
    {
        public double A { get; set; }
        public double B { get; set; }
        public double C { get; set; }
        public double D { get; set; }
    }

    public sealed class ListSavedViewpointsRequest
    {
        public int? Limit { get; set; }
        public bool? IncludeItemIds { get; set; }
    }

    public sealed class ListSavedViewpointsResponse
    {
        public int TotalItemCount { get; set; }
        public int ReturnedItemCount { get; set; }
        public bool Truncated { get; set; }
        public List<SavedViewpointItemInfo> Items { get; set; } = new List<SavedViewpointItemInfo>();
    }

    public sealed class SavedViewpointItemInfo
    {
        public string ItemId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string ParentPath { get; set; }
        public string Type { get; set; }
        public int Depth { get; set; }
        public int Index { get; set; }
        public int ChildCount { get; set; }
        public bool? ContainsVisibilityOverrides { get; set; }
        public bool? ContainsAppearanceOverrides { get; set; }
    }

    public sealed class SavedViewpointsExportRequest
    {
        public string OutputPath { get; set; }
        public string Format { get; set; }
        public bool? IncludeItemIds { get; set; }
        public bool? Overwrite { get; set; }
    }

    public sealed class SavedViewpointsExportResponse
    {
        public string OutputPath { get; set; }
        public string Format { get; set; }
        public int ExportedItemCount { get; set; }
        public int FolderCount { get; set; }
        public int ViewpointCount { get; set; }
        public bool Written { get; set; }
    }

    public sealed class SavedViewpointsImportRequest
    {
        public string InputPath { get; set; }
        public string TargetFolderPath { get; set; }
        public bool? PreserveXmlFolders { get; set; }
        public int? PreviewLimit { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class SavedViewpointsImportResponse
    {
        public bool Apply { get; set; }
        public string InputPath { get; set; }
        public string TargetFolderPath { get; set; }
        public bool PreserveXmlFolders { get; set; }
        public bool TargetFolderExists { get; set; }
        public int? CreatedFolderCount { get; set; }
        public int ParsedFolderCount { get; set; }
        public int ParsedViewpointCount { get; set; }
        public int ImportedViewpointCount { get; set; }
        public int SkippedItemCount { get; set; }
        public bool Truncated { get; set; }
        public bool? Changed { get; set; }
        public List<SavedViewpointsImportItem> Items { get; set; } = new List<SavedViewpointsImportItem>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class SavedViewpointsImportItem
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string SourcePath { get; set; }
        public string TargetPath { get; set; }
        public bool Imported { get; set; }
        public string Warning { get; set; }
    }

    public sealed class SavedViewpointsManageRequest
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
        public List<SavedViewpointsManageTarget> Items { get; set; } = new List<SavedViewpointsManageTarget>();
    }

    public sealed class SavedViewpointsManageTarget
    {
        public string PathOrName { get; set; }
        public string ItemId { get; set; }
        public int? Occurrence { get; set; }
    }

    public sealed class SavedViewpointsManageResponse
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
        public int DeletedItemCount { get; set; }
        public List<SavedViewpointsManageItemResponse> Items { get; set; } = new List<SavedViewpointsManageItemResponse>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class SavedViewpointsManageItemResponse
    {
        public string ItemId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public bool Deleted { get; set; }
    }

    public sealed class SavedViewpointsReorderRequest
    {
        public string FolderPath { get; set; }
        public string ItemId { get; set; }
        public bool? Recursive { get; set; }
        public bool? FoldersFirst { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class SavedViewpointsReorderResponse
    {
        public bool Apply { get; set; }
        public string FolderPath { get; set; }
        public bool Recursive { get; set; }
        public bool FoldersFirst { get; set; }
        public int ProcessedFolderCount { get; set; }
        public int ReorderedFolderCount { get; set; }
        public int MovedItemCount { get; set; }
        public bool? Changed { get; set; }
        public List<SavedViewpointsReorderFolderPlan> Folders { get; set; } = new List<SavedViewpointsReorderFolderPlan>();
    }

    public sealed class SavedViewpointsReorderFolderPlan
    {
        public string FolderPath { get; set; }
        public int ChildCount { get; set; }
        public bool WouldChange { get; set; }
        public List<string> Before { get; set; } = new List<string>();
        public List<string> After { get; set; } = new List<string>();
    }

    public sealed class ActivateSavedViewpointRequest
    {
        public string PathOrName { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class ActivateSavedViewpointResponse
    {
        public bool Apply { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public bool? Activated { get; set; }
    }

    public sealed class BoundingBoxInfo
    {
        public Point3Info Min { get; set; }
        public Point3Info Max { get; set; }
        public Point3Info Center { get; set; }
        public Point3Info Size { get; set; }
    }

    public sealed class Point3Info
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public sealed class CreateViewpointRequest
    {
        public string Name { get; set; }
        public string FolderPath { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class CreateViewpointResponse
    {
        public bool Apply { get; set; }
        public string Name { get; set; }
        public string FolderPath { get; set; }
        public bool NameConflict { get; set; }
        public bool FolderExists { get; set; }
        public int? CreatedFolderCount { get; set; }
        public bool? Created { get; set; }
    }
}
