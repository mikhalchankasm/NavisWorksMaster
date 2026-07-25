using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashGroupResultsRequest
    {
        public bool? Apply { get; set; }
        public string TestName { get; set; }
        public List<string> TestNames { get; set; } = new List<string>();
        public List<string> TestHandles { get; set; } = new List<string>();
        public List<string> StatusFilters { get; set; } = new List<string>();
        public bool? IncludeAllStatuses { get; set; }
        public string GroupBySide { get; set; }
        public string GroupBy { get; set; }
        public int? AncestorLevelsUp { get; set; }
        public string GroupNameMode { get; set; }
        public string GroupNamePrefix { get; set; }
        public bool? IncludeNavisHelperSideTag { get; set; }
        public bool? OverwriteExisting { get; set; }
        public bool? UngroupExistingFirst { get; set; }
        public int? MinGroupSize { get; set; }
        public int? MaxResults { get; set; }
        public int? PreviewRowsPerGroup { get; set; }
        public int? GroupOffset { get; set; }
        public int? GroupLimit { get; set; }
        public bool? AggregateOnly { get; set; }
        public bool? ConfirmLargeGrouping { get; set; }
        public List<string> ExcludeItemNameContains { get; set; } = new List<string>();
        public string Verbosity { get; set; }
        public bool? IncludeIgnored { get; set; }
    }

    public sealed class ClashGroupResultsResponse
    {
        public bool Truncated { get; set; }
        public bool GroupsTruncated { get; set; }
        public bool Applied { get; set; }
        public string RequestedTestName { get; set; }
        public int MatchedTestCount { get; set; }
        public int TotalResultCount { get; set; }
        public int MatchedResultCount { get; set; }
        public int AnalyzedResultCount { get; set; }
        public bool ResultsTruncated { get; set; }
        public int MaxResults { get; set; }
        public string GroupBySide { get; set; }
        public string GroupBy { get; set; }
        public int AncestorLevelsUp { get; set; }
        public string GroupNameMode { get; set; }
        public string GroupNamePrefix { get; set; }
        public bool IncludeNavisHelperSideTag { get; set; }
        public bool OverwriteExisting { get; set; }
        public bool UngroupExistingFirst { get; set; }
        public int MinGroupSize { get; set; }
        public int PreviewRowsPerGroup { get; set; }
        public int PlannedGroupCount { get; set; }
        public int ReturnedGroupCount { get; set; }
        public int GroupOffset { get; set; }
        public int GroupLimit { get; set; }
        public int? NextGroupOffset { get; set; }
        public bool HasMoreGroups { get; set; }
        public bool AggregateOnly { get; set; }
        public int AppliedGroupCount { get; set; }
        public int MovedResultCount { get; set; }
        public int SkippedResultCount { get; set; }
        public int ConflictGroupCount { get; set; }
        public int LargeGroupingThreshold { get; set; }
        public bool LargeGroupingConfirmationRequired { get; set; }
        public Dictionary<string, int> TotalStatusCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> MatchedStatusCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> ReturnedStatusCounts { get; set; } = new Dictionary<string, int>();
        public List<string> ExcludeItemNameContains { get; set; } = new List<string>();
        public int ExcludedByItemNameCount { get; set; }
        public Dictionary<string, int> ExcludedByItemNameCounts { get; set; } = new Dictionary<string, int>();
        public string Message { get; set; }
        public List<ClashGroupPlanItem> Groups { get; set; } = new List<ClashGroupPlanItem>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashGroupPlanItem
    {
        public int Index { get; set; }
        public int TestIndex { get; set; }
        public string TestHandle { get; set; }
        public string TestName { get; set; }
        public string GroupName { get; set; }
        public string CleanGroupName { get; set; }
        public string OwnerName { get; set; }
        public string OwnerPath { get; set; }
        public string RootId { get; set; }
        public string RootName { get; set; }
        public string SourceFile { get; set; }
        public int ResultCount { get; set; }
        public bool ExistingGroupFound { get; set; }
        public bool Applied { get; set; }
        public int MovedResultCount { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
        public List<ClashClusterPreviewRow> PreviewRows { get; set; } = new List<ClashClusterPreviewRow>();
    }

    public sealed class ClashRenumberResultsRequest
    {
        public bool? Apply { get; set; }
        public string TestName { get; set; }
        public List<string> TestNames { get; set; } = new List<string>();
        public List<string> TestHandles { get; set; } = new List<string>();
        public string Scope { get; set; }
        public string OrderBy { get; set; }
        public int? StartNumber { get; set; }
        public int? NumberWidth { get; set; }
        public string Separator { get; set; }
        public string Prefix { get; set; }
        public string Suffix { get; set; }
        public bool? PreserveExistingName { get; set; }
        public bool? StripExistingNumber { get; set; }
        public bool? IncludeGroups { get; set; }
        public bool? IncludeResults { get; set; }
        public bool? IncludeEmptyGroups { get; set; }
        public bool? ConfirmRename { get; set; }
        public int? Limit { get; set; }
    }

    public sealed class ClashRenumberResultsResponse
    {
        public bool Applied { get; set; }
        public string RequestedTestName { get; set; }
        public int MatchedTestCount { get; set; }
        public string Scope { get; set; }
        public string OrderBy { get; set; }
        public int StartNumber { get; set; }
        public int NumberWidth { get; set; }
        public string Separator { get; set; }
        public string Prefix { get; set; }
        public string Suffix { get; set; }
        public bool PreserveExistingName { get; set; }
        public bool StripExistingNumber { get; set; }
        public bool IncludeGroups { get; set; }
        public bool IncludeResults { get; set; }
        public bool IncludeEmptyGroups { get; set; }
        public int PlannedRenameCount { get; set; }
        public int RenamedCount { get; set; }
        public int SkippedCount { get; set; }
        public int Limit { get; set; }
        public bool Truncated { get; set; }
        public bool ConfirmationRequired { get; set; }
        public string Message { get; set; }
        public List<ClashRenumberPlanItem> Items { get; set; } = new List<ClashRenumberPlanItem>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashRenumberPlanItem
    {
        public int Index { get; set; }
        public int Number { get; set; }
        public string NumberText { get; set; }
        public int TestIndex { get; set; }
        public string TestHandle { get; set; }
        public string TestName { get; set; }
        public string ItemType { get; set; }
        public string GroupPath { get; set; }
        public string OldName { get; set; }
        public string NewName { get; set; }
        public bool Applied { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class ClashManageTestsRequest
    {
        public bool? Apply { get; set; }
        public string Operation { get; set; }
        public string TestName { get; set; }
        public List<string> TestNames { get; set; } = new List<string>();
        public List<string> TestHandles { get; set; } = new List<string>();
        public string NamePrefix { get; set; }
        public int? FirstN { get; set; }
        public string NewName { get; set; }
        public int? TargetIndex { get; set; }
        public string SortDirection { get; set; }
        public double? ToleranceMm { get; set; }
        public string TestType { get; set; }
        public int? OnlyWithTotal { get; set; }
        public string NamePattern { get; set; }
        public int? RenameStartIndex { get; set; }
        public bool? RenameFromEnd { get; set; }
        public List<ClashTestRenameRequest> Renames { get; set; } = new List<ClashTestRenameRequest>();
    }

    public sealed class ClashManageTestsResponse
    {
        public bool Applied { get; set; }
        public string Operation { get; set; }
        public string RequestedTestName { get; set; }
        public int MatchedTestCount { get; set; }
        public int AffectedTestCount { get; set; }
        public string Message { get; set; }
        public List<ClashTestOperationResult> Tests { get; set; } = new List<ClashTestOperationResult>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashTestOperationResult
    {
        public int TestIndex { get; set; }
        public string Handle { get; set; }
        public string TestHandle { get; set; }
        public string Name { get; set; }
        public string Operation { get; set; }
        public bool Applied { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
        public int? OldIndex { get; set; }
        public int? NewIndex { get; set; }
        public double? OldToleranceMm { get; set; }
        public double? NewToleranceMm { get; set; }
        public string OldTestType { get; set; }
        public string NewTestType { get; set; }
    }

    public sealed class ClashTestRenameRequest
    {
        public string TestHandle { get; set; }
        public string NewName { get; set; }
    }

    public sealed class ClashGroupCustomRequest
    {
        public string TestHandle { get; set; }
        public List<string> ResultHandles { get; set; } = new List<string>();
        public string GroupName { get; set; }
        public bool? Apply { get; set; }
        public bool? OverwriteExisting { get; set; }
    }

    public sealed class ClashGroupCustomResponse
    {
        public bool Applied { get; set; }
        public string TestHandle { get; set; }
        public string TestName { get; set; }
        public string GroupName { get; set; }
        public string GroupHandle { get; set; }
        public int RequestedResultCount { get; set; }
        public int MovedResultCount { get; set; }
        public bool ExistingGroupFound { get; set; }
        public string Message { get; set; }
        public List<string> MissingResultHandles { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashUngroupRequest
    {
        public string TestHandle { get; set; }
        public List<string> GroupHandles { get; set; } = new List<string>();
        public string GroupNamePrefix { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class ClashUngroupResponse
    {
        public bool Applied { get; set; }
        public string TestHandle { get; set; }
        public string TestName { get; set; }
        public int MatchedGroupCount { get; set; }
        public int UngroupedGroupCount { get; set; }
        public int MovedResultCount { get; set; }
        public string Message { get; set; }
        public List<ClashGroupReference> Groups { get; set; } = new List<ClashGroupReference>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashGroupReference
    {
        public string GroupHandle { get; set; }
        public string GroupName { get; set; }
        public string GroupPath { get; set; }
        public int ResultCount { get; set; }
    }

    public sealed class ClashSetStatusRequest
    {
        public string Scope { get; set; }
        public string TestHandle { get; set; }
        public List<string> ResultHandles { get; set; } = new List<string>();
        public List<string> GroupHandles { get; set; } = new List<string>();
        public List<string> TestHandles { get; set; } = new List<string>();
        public string Status { get; set; }
        public string AssignedTo { get; set; }
        public string Comment { get; set; }
        public bool? Apply { get; set; }
        public bool? ConfirmLargeStatusChange { get; set; }
    }

    public sealed class ClashSetStatusResponse
    {
        public bool Applied { get; set; }
        public string Scope { get; set; }
        public string Status { get; set; }
        public int AffectedResultCount { get; set; }
        public int LargeStatusChangeThreshold { get; set; }
        public bool ConfirmationRequired { get; set; }
        public Dictionary<string, int> StatusTransitions { get; set; } = new Dictionary<string, int>();
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashGroupByProximityRequest
    {
        public string TestName { get; set; }
        public List<string> TestNames { get; set; } = new List<string>();
        public List<string> TestHandles { get; set; } = new List<string>();
        public string GroupMode { get; set; }
        public double? ClusterDistanceMm { get; set; }
        public int? MinGroupSize { get; set; }
        public string GroupNameTemplate { get; set; }
        public string GroupNamePrefix { get; set; }
        public bool? UngroupExistingFirst { get; set; }
        public List<string> StatusFilters { get; set; } = new List<string>();
        public bool? IncludeAllStatuses { get; set; }
        public List<string> ExcludeItemNameContains { get; set; } = new List<string>();
        public int? MaxResults { get; set; }
        public bool? Apply { get; set; }
        public bool? ConfirmLargeGrouping { get; set; }
        public bool? IncludeIgnored { get; set; }
    }

    public sealed class ClashGroupByProximityResponse
    {
        public bool Applied { get; set; }
        public int MatchedTestCount { get; set; }
        public int AnalyzedResultCount { get; set; }
        public int PlannedGroupCount { get; set; }
        public int AppliedGroupCount { get; set; }
        public int MovedResultCount { get; set; }
        public int SkippedResultCount { get; set; }
        public bool ResultsTruncated { get; set; }
        public bool ConfirmationRequired { get; set; }
        public string GroupMode { get; set; }
        public double ClusterDistanceMm { get; set; }
        public string Message { get; set; }
        public List<ClashGroupPlanItem> Groups { get; set; } = new List<ClashGroupPlanItem>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashIgnoreRulesRequest
    {
        public string Action { get; set; }
        public ClashIgnoreRule Rule { get; set; }
        public string RuleName { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class ClashIgnoreRulesResponse
    {
        public bool Applied { get; set; }
        public string Action { get; set; }
        public int RuleCount { get; set; }
        public int AffectedResultCount { get; set; }
        public string Message { get; set; }
        public List<ClashIgnoreRule> Rules { get; set; } = new List<ClashIgnoreRule>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashIgnoreRule
    {
        public string Name { get; set; }
        public string TestNamePattern { get; set; }
        public List<string> ItemAContains { get; set; } = new List<string>();
        public List<string> ItemBContains { get; set; } = new List<string>();
        public string Reason { get; set; }
    }
}
