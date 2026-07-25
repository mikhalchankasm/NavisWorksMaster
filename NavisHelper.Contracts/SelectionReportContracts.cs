using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class SelectionPropertyReportRequest
    {
        public int? ItemLimit { get; set; }
        public int? PropertyLimitPerItem { get; set; }
        public int? RowLimit { get; set; }
        public bool? IncludeInternalNames { get; set; }
        public bool? IncludeEmptyValues { get; set; }
        public List<string> CategoryFilters { get; set; } = new List<string>();
        public List<string> PropertyFilters { get; set; } = new List<string>();
    }

    public sealed class SelectionPropertyReportResponse
    {
        public int SelectedItemCount { get; set; }
        public int ReturnedItemCount { get; set; }
        public int RowCount { get; set; }
        public bool ItemsTruncated { get; set; }
        public bool PropertiesTruncated { get; set; }
        public bool RowsTruncated { get; set; }
        public List<SelectionPropertyReportRow> Rows { get; set; } = new List<SelectionPropertyReportRow>();
    }

    public sealed class SelectionPropertyReportRow
    {
        public int ItemIndex { get; set; }
        public string Path { get; set; }
        public string DisplayName { get; set; }
        public string Category { get; set; }
        public string CategoryInternalName { get; set; }
        public string Property { get; set; }
        public string PropertyInternalName { get; set; }
        public string Value { get; set; }
        public string ValueType { get; set; }
    }

    public sealed class SelectionExportPropertiesRequest
    {
        public string OutputPath { get; set; }
        public string Format { get; set; }
        public bool? Apply { get; set; }
        public bool? Overwrite { get; set; }
        public int? ItemLimit { get; set; }
        public int? PropertyLimitPerItem { get; set; }
        public int? RowLimit { get; set; }
        public bool? IncludeInternalNames { get; set; }
        public bool? IncludeEmptyValues { get; set; }
        public List<string> CategoryFilters { get; set; } = new List<string>();
        public List<string> PropertyFilters { get; set; } = new List<string>();
    }

    public sealed class SelectionExportPropertiesResponse
    {
        public bool Applied { get; set; }
        public string OutputPath { get; set; }
        public string Format { get; set; }
        public int SelectedItemCount { get; set; }
        public int ReturnedItemCount { get; set; }
        public int RowCount { get; set; }
        public bool ItemsTruncated { get; set; }
        public bool PropertiesTruncated { get; set; }
        public bool RowsTruncated { get; set; }
        public long FileSizeBytes { get; set; }
        public string Message { get; set; }
    }

    public sealed class SelectionDistinctPropertyValuesRequest
    {
        public int? ItemLimit { get; set; }
        public int? ValueLimit { get; set; }
        public bool? IncludeEmptyValues { get; set; }
        public List<string> CategoryFilters { get; set; } = new List<string>();
        public List<string> PropertyFilters { get; set; } = new List<string>();
    }

    public sealed class SelectionDistinctPropertyValuesResponse
    {
        public int SelectedItemCount { get; set; }
        public int ScannedItemCount { get; set; }
        public int MatchedPropertyCount { get; set; }
        public int DistinctValueCount { get; set; }
        public int ReturnedValueCount { get; set; }
        public bool ItemsTruncated { get; set; }
        public bool ValuesTruncated { get; set; }
        public List<SelectionDistinctPropertyValue> Values { get; set; } = new List<SelectionDistinctPropertyValue>();
    }

    public sealed class SelectionDistinctPropertyValue
    {
        public string Value { get; set; }
        public int Count { get; set; }
        public string Category { get; set; }
        public string Property { get; set; }
        public string SampleItemPath { get; set; }
        public string SampleItemName { get; set; }
    }

    public sealed class SelectionColorByPropertyRequest
    {
        public bool? Apply { get; set; }
        public int? ItemLimit { get; set; }
        public int? GroupLimit { get; set; }
        public float? Transparency { get; set; }
        public bool? IncludeEmptyValues { get; set; }
        public List<string> CategoryFilters { get; set; } = new List<string>();
        public List<string> PropertyFilters { get; set; } = new List<string>();
    }

    public sealed class SelectionColorByPropertyResponse
    {
        public bool Applied { get; set; }
        public int SelectedItemCount { get; set; }
        public int ScannedItemCount { get; set; }
        public int MatchedItemCount { get; set; }
        public int ColoredItemCount { get; set; }
        public int DistinctValueCount { get; set; }
        public int ReturnedGroupCount { get; set; }
        public bool ItemsTruncated { get; set; }
        public bool GroupsTruncated { get; set; }
        public string Message { get; set; }
        public List<SelectionColorByPropertyGroup> Groups { get; set; } = new List<SelectionColorByPropertyGroup>();
    }

    public sealed class SelectionColorByPropertyGroup
    {
        public string Value { get; set; }
        public int Count { get; set; }
        public string ColorHex { get; set; }
        public int R { get; set; }
        public int G { get; set; }
        public int B { get; set; }
        public string Category { get; set; }
        public string Property { get; set; }
        public string SampleItemPath { get; set; }
        public string SampleItemName { get; set; }
    }
}
