using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class FindItemsRequest
    {
        public string Query { get; set; }
        public List<string> Queries { get; set; } = new List<string>();
        public string Comparison { get; set; }
        public string Category { get; set; }
        public string Property { get; set; }
        public List<FindItemsSearch> Searches { get; set; } = new List<FindItemsSearch>();
        public int? PreviewLimit { get; set; }
        public string Scope { get; set; }
        public string ScopeHandle { get; set; }
        public string ScopeNodeName { get; set; }
        public string ScopeNodePath { get; set; }
        public string MatchDepth { get; set; }
        public bool? CountOnly { get; set; }
        public bool? Preflight { get; set; }
        public bool? IgnoreCase { get; set; }
        public bool? IgnoreDiacritics { get; set; }
        public bool? IgnoreCharWidth { get; set; }
    }

    public sealed class FindItemsSearch
    {
        public string Query { get; set; }
        public string CombineOperator { get; set; }
        public List<FindItemsCondition> Conditions { get; set; } = new List<FindItemsCondition>();
    }

    public sealed class FindItemsCondition
    {
        public string Category { get; set; }
        public string Property { get; set; }
        public string CategoryInternal { get; set; }
        public string PropertyInternal { get; set; }
        public string DataType { get; set; }
        public bool? InheritFromAncestor { get; set; }
        public string Operator { get; set; }
        public string Comparison { get; set; }
        public string Value { get; set; }
        // Connects this condition to the preceding condition. The first condition
        // is always the beginning of the native Navisworks condition group.
        public string LogicalOperator { get; set; }
        public bool? Negate { get; set; }
        public bool? IgnoreCase { get; set; }
        public bool? IgnoreDiacritics { get; set; }
        public bool? IgnoreCharWidth { get; set; }
    }

    public sealed class FindItemsResponse
    {
        public List<FindItemsResult> Results { get; set; } = new List<FindItemsResult>();
        public FindItemsSummary Summary { get; set; } = new FindItemsSummary();
        public string Scope { get; set; }
        public string MatchDepth { get; set; }
        public bool CountOnly { get; set; }
        public int MatchedItemCount { get; set; }
        public int ScannedItemCount { get; set; }
        public Dictionary<int, int> DepthHistogram { get; set; } = new Dictionary<int, int>();
        public List<string> SampleValuesFromModel { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public FindItemsPreflight Preflight { get; set; }
    }

    public sealed class FindItemsPreflight
    {
        public bool Ready { get; set; }
        public bool HasCurrentSelection { get; set; }
        public string UnderstoodScope { get; set; }
        public string UnderstoodProperty { get; set; }
        public string UnderstoodComparison { get; set; }
        public string UnderstoodMatchDepth { get; set; }
        public List<FindItemsClarification> Clarifications { get; set; } = new List<FindItemsClarification>();
    }

    public sealed class FindItemsClarification
    {
        public string Id { get; set; }
        public string Question { get; set; }
        public string SuggestedAnswer { get; set; }
        public List<string> Options { get; set; } = new List<string>();
    }

    public static class FindItemsScopes
    {
        public const string WholeModel = "whole_model";
        public const string CurrentSelection = "current_selection";
        public const string UnderHandle = "under_handle";
        public const string UnderNamedNode = "under_named_node";
    }

    public static class FindItemsMatchDepths
    {
        public const string First = "first";
        public const string All = "all";
    }

    public sealed class FindItemsResult
    {
        public string Query { get; set; }
        public string Status { get; set; }
        public List<FindItemsMatch> Matches { get; set; } = new List<FindItemsMatch>();
        public List<MatchPreviewItem> Suggestions { get; set; } = new List<MatchPreviewItem>();
    }

    public sealed class FindItemsMatch
    {
        public string MatchHandle { get; set; }
        public int ItemCount { get; set; }
        public List<MatchPreviewItem> Preview { get; set; } = new List<MatchPreviewItem>();
        public bool PreviewTruncated { get; set; }
    }

    public sealed class MatchPreviewItem
    {
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public string SourceFile { get; set; }
    }

    public sealed class FindItemsSummary
    {
        public int MatchedQueries { get; set; }
        public int NotFoundQueries { get; set; }
        // Reserved for search v2 exact/variant matching semantics.
        public int AmbiguousQueries { get; set; }
        // Reserved for search v2 exact/variant matching semantics.
        public int QueryTooAmbiguousQueries { get; set; }
        // Includes item counts from both matched and ambiguous branches.
        public int TotalItemsInMatches { get; set; }
    }

    public sealed class SelectBySearchRequest
    {
        public List<FindItemsCondition> Conditions { get; set; } = new List<FindItemsCondition>();
        public string Scope { get; set; }
        public List<FindItemsCondition> ParentConditions { get; set; } = new List<FindItemsCondition>();
        public bool? ReplaceSelection { get; set; }
        public int? MaxMatchedItems { get; set; }
        public int? PreviewLimit { get; set; }
    }

    public sealed class SelectBySearchResponse
    {
        public string Scope { get; set; }
        public int MatchedItemCount { get; set; }
        public int SelectedItemCount { get; set; }
        public bool ReplacedSelection { get; set; }
        public List<MatchPreviewItem> Preview { get; set; } = new List<MatchPreviewItem>();
    }

    public static class SelectBySearchScopes
    {
        public const string WholeModel = "whole_model";
        public const string DirectChildrenOf = "direct_children_of";
        public const string DescendantsOf = "descendants_of";
    }

    public sealed class SpatialPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public sealed class FindItemsByBboxRequest
    {
        // Coordinates are global document coordinates in the active Navisworks model units.
        public SpatialPoint Min { get; set; }
        public SpatialPoint Max { get; set; }
        public string MatchMode { get; set; }
        public bool? IncludeHidden { get; set; }
        public bool? IncludeContainers { get; set; }
        public string SourceFileContains { get; set; }
        public int? MaxScannedItems { get; set; }
        public int? MaxResults { get; set; }
        public int? PreviewLimit { get; set; }
    }

    public sealed class FindItemsByBboxResponse
    {
        public string CoordinateSpace { get; set; }
        public SpatialPoint Min { get; set; }
        public SpatialPoint Max { get; set; }
        public string MatchMode { get; set; }
        public int ScannedItemCount { get; set; }
        public int MatchedItemCount { get; set; }
        public int ReturnedItemCount { get; set; }
        public bool TraversalTruncated { get; set; }
        public bool ResultsTruncated { get; set; }
        public string MatchHandle { get; set; }
        public List<SpatialSearchItem> Preview { get; set; } = new List<SpatialSearchItem>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class SpatialSearchItem
    {
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public string SourceFile { get; set; }
        public SpatialPoint Min { get; set; }
        public SpatialPoint Max { get; set; }
    }

    public sealed class FindRootItemsByNameRequest
    {
        public List<string> Names { get; set; } = new List<string>();
        public string Comparison { get; set; }
        public int? PreviewLimit { get; set; }
    }

    public sealed class ListRootItemsRequest
    {
        public int? Limit { get; set; }
        public bool? IncludeAliases { get; set; }
    }

    public sealed class ListRootItemsResponse
    {
        public string DocumentTitle { get; set; }
        public int ModelCount { get; set; }
        public int RootItemCount { get; set; }
        public bool Truncated { get; set; }
        public List<RootItemInfo> Items { get; set; } = new List<RootItemInfo>();
    }

    public sealed class RootItemInfo
    {
        public string DisplayName { get; set; }
        public string SourceFile { get; set; }
        public string FileName { get; set; }
        public string Path { get; set; }
        public List<string> Aliases { get; set; } = new List<string>();
    }

    public sealed class ListItemChildrenRequest
    {
        public string ParentMatchHandle { get; set; }
        public string ParentPath { get; set; }
        public string ParentName { get; set; }
        public string SourceFile { get; set; }
        public string Comparison { get; set; }
        public bool? IncludeHidden { get; set; }
        public int? Limit { get; set; }
    }

    public sealed class ListItemChildrenResponse
    {
        public string DocumentTitle { get; set; }
        public string ParentDisplayName { get; set; }
        public string ParentPath { get; set; }
        public string ParentSourceFile { get; set; }
        public int TotalChildCount { get; set; }
        public int ReturnedChildCount { get; set; }
        public int SkippedHiddenChildCount { get; set; }
        public bool Truncated { get; set; }
        public string ChildrenMatchHandle { get; set; }
        public List<ItemChildInfo> Children { get; set; } = new List<ItemChildInfo>();
    }

    public sealed class ItemChildInfo
    {
        public int Index { get; set; }
        public string DisplayName { get; set; }
        public string ClassDisplayName { get; set; }
        public string Path { get; set; }
        public string SourceFile { get; set; }
        public int ChildCount { get; set; }
        public bool IsHidden { get; set; }
    }
}
