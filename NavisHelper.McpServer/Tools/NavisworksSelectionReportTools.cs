using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksSelectionReportTools : NavisworksToolBase
{
    public NavisworksSelectionReportTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Returns a structured property report for the current Navisworks selection. Read-only replacement for UI/Excel property quick reports.")]
    public Task<SelectionPropertyReportResponse> SelectionPropertyReport(
        [Description("Maximum selected items to inspect. Default is 100, maximum is 10000.")] int itemLimit = 100,
        [Description("Maximum properties per selected item. Default is 1000, maximum is 20000.")] int propertyLimitPerItem = 1000,
        [Description("Maximum total report rows. Default is 10000, maximum is 200000.")] int rowLimit = 10000,
        [Description("Include internal category/property names. Default is false.")] bool includeInternalNames = false,
        [Description("Include rows with empty property values. Default is false.")] bool includeEmptyValues = false,
        [Description("Optional category display/internal name filters. Contains-match, case-insensitive.")] List<string> categoryFilters = null,
        [Description("Optional property display/internal name filters. Contains-match, case-insensitive.")] List<string> propertyFilters = null,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectionPropertyReportAsync(new SelectionPropertyReportRequest
        {
            ItemLimit = itemLimit,
            PropertyLimitPerItem = propertyLimitPerItem,
            RowLimit = rowLimit,
            IncludeInternalNames = includeInternalNames,
            IncludeEmptyValues = includeEmptyValues,
            CategoryFilters = categoryFilters ?? new List<string>(),
            PropertyFilters = propertyFilters ?? new List<string>(),
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Exports the current Navisworks selection property report to an explicit CSV or XLSX file path. Defaults to dry-run; pass apply=true to write.")]
    public Task<SelectionExportPropertiesResponse> SelectionExportProperties(
        [Description("Absolute or relative output CSV path. Required.")] string outputPath,
        [Description("Export format: csv or xlsx. Default is csv.")] string format = "csv",
        [Description("False previews row counts and target path, true writes the file. Default is false/dry-run.")] bool apply = false,
        [Description("Allow replacing an existing output file. Default is false.")] bool overwrite = false,
        [Description("Maximum selected items to inspect. Default is 100, maximum is 10000.")] int itemLimit = 100,
        [Description("Maximum properties per selected item. Default is 1000, maximum is 20000.")] int propertyLimitPerItem = 1000,
        [Description("Maximum total report rows. Default is 10000, maximum is 200000.")] int rowLimit = 10000,
        [Description("Include internal category/property names in the CSV. Default is false.")] bool includeInternalNames = false,
        [Description("Include rows with empty property values. Default is false.")] bool includeEmptyValues = false,
        [Description("Optional category display/internal name filters. Contains-match, case-insensitive.")] List<string> categoryFilters = null,
        [Description("Optional property display/internal name filters. Contains-match, case-insensitive.")] List<string> propertyFilters = null,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectionExportPropertiesAsync(new SelectionExportPropertiesRequest
        {
            OutputPath = outputPath,
            Format = format,
            Apply = apply,
            Overwrite = overwrite,
            ItemLimit = itemLimit,
            PropertyLimitPerItem = propertyLimitPerItem,
            RowLimit = rowLimit,
            IncludeInternalNames = includeInternalNames,
            IncludeEmptyValues = includeEmptyValues,
            CategoryFilters = categoryFilters ?? new List<string>(),
            PropertyFilters = propertyFilters ?? new List<string>(),
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Returns distinct property values in the current Navisworks selection with counts. Read-only helper for reporting and future color_by_property workflows.")]
    public Task<SelectionDistinctPropertyValuesResponse> SelectionDistinctPropertyValues(
        [Description("Maximum selected items to inspect. Default is 100, maximum is 10000.")] int itemLimit = 100,
        [Description("Maximum distinct values to return. Default is 1000, maximum is 50000.")] int valueLimit = 1000,
        [Description("Include empty property values. Default is false.")] bool includeEmptyValues = false,
        [Description("Optional category display/internal name filters. Contains-match, case-insensitive.")] List<string> categoryFilters = null,
        [Description("Optional property display/internal name filters. Contains-match, case-insensitive.")] List<string> propertyFilters = null,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectionDistinctPropertyValuesAsync(new SelectionDistinctPropertyValuesRequest
        {
            ItemLimit = itemLimit,
            ValueLimit = valueLimit,
            IncludeEmptyValues = includeEmptyValues,
            CategoryFilters = categoryFilters ?? new List<string>(),
            PropertyFilters = propertyFilters ?? new List<string>(),
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Auto-colors the current selection with a deterministic palette derived from property values. It does not accept explicit colors. For exact color mappings, source-file/name fragments, a one-color selection, and runtime reset, use model_color_scheme instead. Defaults to dry-run; pass apply=true to write permanent color overrides.")]
    public Task<SelectionColorByPropertyResponse> SelectionColorByProperty(
        [Description("False previews groups/colors, true applies permanent color overrides. Default is false/dry-run.")] bool apply = false,
        [Description("Maximum selected items to inspect. Default is 100, maximum is 10000.")] int itemLimit = 100,
        [Description("Maximum unique property-value groups allowed/returned. Default is 1000, maximum is 50000. apply=true fails if groups exceed this limit.")] int groupLimit = 1000,
        [Description("Optional permanent transparency override from 0.0 to 1.0. Omit to keep transparency unchanged.")] float? transparency = null,
        [Description("Include empty property values as their own color group. Default is false.")] bool includeEmptyValues = false,
        [Description("Category display/internal name filters. Contains-match, case-insensitive. At least categoryFilters or propertyFilters is required.")] List<string> categoryFilters = null,
        [Description("Property display/internal name filters. Contains-match, case-insensitive. At least categoryFilters or propertyFilters is required.")] List<string> propertyFilters = null,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectionColorByPropertyAsync(new SelectionColorByPropertyRequest
        {
            Apply = apply,
            ItemLimit = itemLimit,
            GroupLimit = groupLimit,
            Transparency = transparency,
            IncludeEmptyValues = includeEmptyValues,
            CategoryFilters = categoryFilters ?? new List<string>(),
            PropertyFilters = propertyFilters ?? new List<string>(),
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
