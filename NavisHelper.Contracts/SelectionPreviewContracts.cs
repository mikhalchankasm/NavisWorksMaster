using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class SelectedItemsPreviewRequest
    {
        public int? Limit { get; set; }
        public bool? IncludeBoundingBoxes { get; set; }
    }

    public sealed class SelectedItemsPreviewResponse
    {
        public int SelectedItemCount { get; set; }
        public bool Truncated { get; set; }
        public List<SelectedItemPreview> Items { get; set; } = new List<SelectedItemPreview>();
    }

    public sealed class SelectedItemPreview
    {
        public string DisplayName { get; set; }
        public string ClassDisplayName { get; set; }
        public string Path { get; set; }
        public string SourceFile { get; set; }
        public bool IsHidden { get; set; }
        public int ChildCount { get; set; }
        public BoundingBoxInfo BoundingBox { get; set; }
    }

    public sealed class SelectedItemsAncestryRequest
    {
        public int? Limit { get; set; }
        public bool? IncludeBoundingBoxes { get; set; }
    }

    public sealed class SelectedItemsAncestryResponse
    {
        public int SelectedItemCount { get; set; }
        public bool Truncated { get; set; }
        public List<SelectedItemAncestry> Items { get; set; } = new List<SelectedItemAncestry>();
    }

    public sealed class SelectedItemAncestry
    {
        public int SelectionIndex { get; set; }
        public SelectedItemHierarchyNode Item { get; set; }
        public List<SelectedItemHierarchyNode> Ancestors { get; set; } = new List<SelectedItemHierarchyNode>();
        public List<SelectedItemHierarchyNode> Chain { get; set; } = new List<SelectedItemHierarchyNode>();
    }

    public sealed class SelectedItemHierarchyNode
    {
        public int Depth { get; set; }
        public bool IsSelectedItem { get; set; }
        public string DisplayName { get; set; }
        public string ClassDisplayName { get; set; }
        public string Path { get; set; }
        public string SourceFile { get; set; }
        public bool IsHidden { get; set; }
        public int ChildCount { get; set; }
        public BoundingBoxInfo BoundingBox { get; set; }
    }

    public sealed class SelectedItemsTreeRequest
    {
        public int? MaxItems { get; set; }
        public int? MaxDepth { get; set; }
        public string Format { get; set; }
        public bool? IncludeBoundingBoxes { get; set; }
    }

    public sealed class SelectedItemsTreeResponse
    {
        public string DocumentTitle { get; set; }
        public string Format { get; set; }
        public int SelectedItemCount { get; set; }
        public int ReturnedItemCount { get; set; }
        public bool Truncated { get; set; }
        public bool DepthTruncated { get; set; }
        public List<SelectedItemsTreeNode> Roots { get; set; } = new List<SelectedItemsTreeNode>();
        public List<SelectedItemsTreeFlatItem> Items { get; set; } = new List<SelectedItemsTreeFlatItem>();
    }

    public sealed class SelectedItemsTreeNode
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public int Depth { get; set; }
        public string RootName { get; set; }
        public string SourceFile { get; set; }
        public bool IsSelectedLeaf { get; set; }
        public BoundingBoxInfo BoundingBox { get; set; }
        public List<SelectedItemsTreeNode> Children { get; set; } = new List<SelectedItemsTreeNode>();
    }

    public sealed class SelectedItemsTreeFlatItem
    {
        public int SelectionIndex { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public int Depth { get; set; }
        public string RootName { get; set; }
        public string SourceFile { get; set; }
        public bool IsSelectedLeaf { get; set; }
        public BoundingBoxInfo BoundingBox { get; set; }
        public List<SelectedItemsTreePathNode> Chain { get; set; } = new List<SelectedItemsTreePathNode>();
    }

    public sealed class SelectedItemsTreePathNode
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public int Depth { get; set; }
        public string RootName { get; set; }
        public string SourceFile { get; set; }
        public bool IsSelectedLeaf { get; set; }
        public BoundingBoxInfo BoundingBox { get; set; }
    }

    public sealed class ItemPropertiesByHandleRequest
    {
        public List<string> MatchHandles { get; set; } = new List<string>();
        public int? ItemLimit { get; set; }
        public int? PropertyLimit { get; set; }
        public bool? IncludeInternalNames { get; set; }
        public List<string> CategoryFilters { get; set; } = new List<string>();
    }

    public sealed class ItemPropertiesByHandleResponse
    {
        public bool Partial { get; set; }
        public List<ItemPropertiesHandleResult> Results { get; set; } = new List<ItemPropertiesHandleResult>();
    }

    public sealed class ItemPropertiesHandleResult
    {
        public string MatchHandle { get; set; }
        public string Status { get; set; }
        public int ItemCount { get; set; }
        public int ReturnedItemCount { get; set; }
        public bool ItemsTruncated { get; set; }
        public bool PropertiesTruncated { get; set; }
        public List<ItemPropertiesPreview> Items { get; set; } = new List<ItemPropertiesPreview>();
    }

    public sealed class ItemPropertiesPreview
    {
        public string DisplayName { get; set; }
        public string ClassDisplayName { get; set; }
        public string Path { get; set; }
        public string SourceFile { get; set; }
        public List<ItemPropertyCategoryInfo> Categories { get; set; } = new List<ItemPropertyCategoryInfo>();
    }

    public sealed class ItemPropertyCategoryInfo
    {
        public string DisplayName { get; set; }
        public string InternalName { get; set; }
        public List<ItemPropertyInfo> Properties { get; set; } = new List<ItemPropertyInfo>();
    }

    public sealed class ItemPropertyInfo
    {
        public string DisplayName { get; set; }
        public string InternalName { get; set; }
        public string Value { get; set; }
        public string ValueType { get; set; }
    }
}
