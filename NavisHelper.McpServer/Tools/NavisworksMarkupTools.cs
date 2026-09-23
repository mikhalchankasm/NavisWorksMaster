using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksMarkupTools : NavisworksToolBase
{
    public NavisworksMarkupTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Builds configurable saved-viewpoint steps for every non-empty Search Set or Selection Set below a required folder prefix. Each overview, markup, or sectionBox step has its own label, clustering strategy, and optional persistent markup including arrow callouts. Defaults to dry-run.")]
    public Task<SelectionSetsBuildViewpointsResponse> SelectionSetsBuildViewpoints(
        [Description("Required Selection Sets folder prefix to process recursively. There is no domain-specific default.")] string folderPrefix,
        [Description("One or more configurable steps. step is overview, markup, or sectionBox; label is substituted into {step}. Each step supports whenItemCountMin/whenItemCountMax and clusterBy=none|distance|count|grid; clusterCount requests an exact count.")] List<SelectionSetViewpointStep> steps,
        [Description("Viewpoint base-name template containing both {set} and {step}. Default is '{set} - {step}'. Cluster suffixes are appended automatically.")] string nameTemplate = "{set} - {step}",
        [Description("Skip a whole step when any target viewpoint name already exists. Default is true.")] bool skipExisting = true,
        [Description("Maximum concrete sets to process in one call, from 1 to 1000. Default is 500.")] int maxSetCount = 500,
        [Description("Response detail: summary omits per-cluster preview item names; full includes cluster previews. Default is summary.")] string verbosity = "summary",
        [Description("False previews sets, steps, clusters, and names. True creates viewpoints. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectionSetsBuildViewpointsAsync(new SelectionSetsBuildViewpointsRequest
        {
            FolderPrefix = folderPrefix,
            NameTemplate = nameTemplate,
            Steps = steps ?? new List<SelectionSetViewpointStep>(),
            SkipExisting = skipExisting,
            MaxSetCount = maxSetCount,
            Verbosity = verbosity,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Deprecated compatibility alias for selection_sets_build_viewpoints. Preserves the legacy MTR defaults and fixed markup/sectionBox pair. New callers should use the neutral tool.")]
    public Task<BuildMtrViewpointsResponse> BuildMtrViewpoints(
        [Description("Selection Sets folder prefix to process recursively. Default is MTR.")] string folderPrefix = "MTR",
        [Description("Create top-view markup points named '<set name> — план'. Default is true.")] bool createPlan = true,
        [Description("Create ISO section-box points named '<set name> — бокс'. Default is true.")] bool createSectionBox = true,
        [Description("Maximum gap between item bounding boxes, in millimeters, for proximity clusters. Default is 10000.")] double clusterMaxDistanceMm = 10000,
        [Description("Extra frame around each markup plan as a fraction of its cluster size. Default is 0.10.")] double fitMarginFactor = 0.10,
        [Description("Optional RGB markup color with three values from 0 to 1. Default is [1, 0, 0].")] List<double> ellipseColor = null,
        [Description("Markup line thickness from 1 to 20. Default is 3.")] int thickness = 3,
        [Description("Extra projected frame size as a fraction of each group box, from 0 to 5. Default is 0.20.")] double paddingFactor = 0.20,
        [Description("Persistent markup shape: rectangle, target, arrow, or hatch. Default is rectangle.")] string markStyle = "rectangle",
        [Description("Add one arrow callout built from supported RedlineLine primitives to every mark, including section-box viewpoints. Default is false.")] bool arrowCallout = false,
        [Description("Arrow length in millimeters. 0 selects 8% of the final camera HeightField, clamped to 5-15%. Default is 0.")] double arrowLengthMm = 0,
        [Description("Draw a crosshair over target ellipses. Default is false.")] bool targetCrosshair = false,
        [Description("Hatch angle in degrees when markStyle=hatch. Default is 45.")] double hatchAngleDeg = 45,
        [Description("Hatch line spacing in millimeters when markStyle=hatch. Default is 500.")] double? hatchSpacingMm = null,
        [Description("Deprecated alias for hatchSpacingMm.")] int? hatchSpacingPx = null,
        [Description("Hatch line thickness from 1 to 20. Defaults to thickness (3). ")] int hatchThickness = 3,
        [Description("Minimum full markup size in millimeters. Default is 500.")] double? minMarkSizeMm = null,
        [Description("Horizontal bbox size in millimeters at which an item receives its own markup frame. Default is 1500.")] double markSoloMinSizeMm = 1500,
        [Description("Maximum horizontal bbox gap in millimeters for merging small items into one markup frame. Default is 1000.")] double markMergeGapMm = 1000,
        [Description("Extra section-box context around every cluster, in millimeters. Default is 1000.")] double boxOffsetMm = 1000,
        [Description("Skip a plan or box when any of its target viewpoint names already exists, avoiding partial duplicate cluster output. Default is true.")] bool skipExisting = true,
        [Description("Maximum concrete sets to process in one call, from 1 to 1000. Default is 500.")] int maxSetCount = 500,
        [Description("Maximum clusters retained per set, from 1 to 100. Default is 10. When capped, the largest clusters are marked with arrows.")] int maxClustersPerSet = 10,
        [Description("Skip distance clustering when a set has more than this many items, from 1 to 10000. Default is 500.")] int maxItemsForDistanceClustering = 500,
        [Description("False previews sets, item counts, clusters, and naming without changing Navisworks. True creates the viewpoints. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.BuildMtrViewpointsAsync(new BuildMtrViewpointsRequest
        {
            FolderPrefix = folderPrefix,
            CreatePlan = createPlan,
            CreateSectionBox = createSectionBox,
            ClusterMaxDistanceMm = clusterMaxDistanceMm,
            FitMarginFactor = fitMarginFactor,
            EllipseColor = ellipseColor,
            Thickness = thickness,
            PaddingFactor = paddingFactor,
            MarkStyle = markStyle,
            ArrowCallout = arrowCallout,
            ArrowLengthMm = arrowLengthMm,
            TargetCrosshair = targetCrosshair,
            HatchAngleDeg = hatchAngleDeg,
            HatchSpacingMm = hatchSpacingMm,
            HatchSpacingPx = hatchSpacingPx,
            HatchThickness = hatchThickness,
            MinMarkSizeMm = minMarkSizeMm,
            MarkSoloMinSizeMm = markSoloMinSizeMm,
            MarkMergeGapMm = markMergeGapMm,
            BoxOffsetMm = boxOffsetMm,
            SkipExisting = skipExisting,
            MaxSetCount = maxSetCount,
            MaxClustersPerSet = maxClustersPerSet,
            MaxItemsForDistanceClustering = maxItemsForDistanceClustering,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Creates one or more saved viewpoints with persistent rectangle, target, arrow, or hatch redline marks around hybrid groups in the current selection. Large items receive individual marks; nearby small items are merged. autoTopView=true creates a top view; false preserves the current orthographic or perspective camera and its section box. Defaults to rectangle and dry-run.")]
    public Task<MarkupSelectionResponse> MarkupSelection(
        [Description("Saved viewpoint name. Slashes are replaced with spaces so the name is safe inside a folder path.")] string name,
        [Description("Optional folder path under Saved Viewpoints, for example MTR/240103-ТХ. Missing folders are created only when apply=true.")] string folderPath = "",
        [Description("Selection source. Only current_selection is currently supported.")] string source = "current_selection",
        [Description("Switch to a strict orthographic top view and fit it to the combined selection box. Default is true.")] bool autoTopView = true,
        [Description("When auto_top_view=false, fit the current view to the combined selection box. Default is true.")] bool fitToSelection = true,
        [Description("Extra frame around the selection when fitting the markup plan, as a fraction of its size. 0 keeps a tight fit; 0.10 adds 10%. Default is 0.10. This does not affect section_box_viewpoint.")] double fitMarginFactor = 0.10,
        [Description("Optional RGB frame color with three values from 0 to 1. The legacy parameter name is retained for compatibility. Default is [1, 0, 0].")] List<double> ellipseColor = null,
        [Description("Rectangular frame line thickness from 1 to 20. Default is 3.")] int thickness = 3,
        [Description("Extra frame half-size as a fraction of each projected group box, from 0 to 5. Default is 0.20.")] double paddingFactor = 0.20,
        [Description("Minimum full markup size in millimeters. Default is 500.")] double? minMarkSizeMm = null,
        [Description("Deprecated alias for minMarkSizeMm.")] int? minRadiusPixels = null,
        [Description("Persistent markup shape: rectangle, target, arrow, or hatch. Default is rectangle.")] string markStyle = "rectangle",
        [Description("Add one arrow callout built from supported RedlineLine primitives to every mark without replacing markStyle. Default is false.")] bool arrowCallout = false,
        [Description("Arrow length in millimeters. 0 selects 8% of the final camera HeightField, clamped to 5-15%. Default is 0.")] double arrowLengthMm = 0,
        [Description("Draw a crosshair over target ellipses. Default is false.")] bool targetCrosshair = false,
        [Description("Hatch angle in degrees when markStyle=hatch. Default is 45.")] double hatchAngleDeg = 45,
        [Description("Hatch line spacing in millimeters when markStyle=hatch. Default is 500.")] double? hatchSpacingMm = null,
        [Description("Deprecated alias for hatchSpacingMm.")] int? hatchSpacingPx = null,
        [Description("Hatch line thickness from 1 to 20. Defaults to thickness.")] int hatchThickness = 3,
        [Description("Horizontal bbox size in millimeters at which an item receives its own frame. Default is 1500.")] double markSoloMinSizeMm = 1500,
        [Description("Maximum horizontal bbox gap in millimeters for merging small items into one frame. Default is 1000.")] double markMergeGapMm = 1000,
        [Description("Maximum gap between item bounding boxes, in millimeters, for proximity clusters. 0 keeps one viewpoint for the full selection; a positive value creates one viewpoint per connected cluster and appends (1), (2), and so on to name. Default is 0.")] double clusterMaxDistanceMm = 0,
        [Description("Clustering strategy: none, distance, count, or grid. Empty preserves legacy inference from clusterMaxDistanceMm.")] string clusterBy = "",
        [Description("Approximate items per cluster for clusterBy=count. Default is 100.")] int clusterTargetSize = 100,
        [Description("Exact number of clusters for clusterBy=count, from 1 to 100. When set, overrides clusterTargetSize.")] int? clusterCount = null,
        [Description("Plan-grid square size in millimeters for clusterBy=grid. Default is 50000.")] double clusterGridSizeMm = 50000,
        [Description("Optional cap from 1 to 100 for created clusters. Largest clusters are retained; droppedClusterCount/uncoveredItemCount report omitted coverage. The arrowCallout setting is never overridden.")] int? maxClusters = null,
        [Description("Distance-clustering safeguard from 1 to 10000. It does not limit count/grid modes. Default is 500.")] int? maxItemsForClustering = null,
        [Description("Deprecated alias for maxItemsForClustering.")] int? maxItemsForDistanceClustering = null,
        [Description("Update an existing viewpoint with the same generated name in place. Duplicate same-name viewpoints remain an error. Default is false.")] bool overwrite = false,
        [Description("False previews the planned viewpoint without changing the view or writing files. True applies the top view/redlines and saves the viewpoint. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.MarkupSelectionAsync(new MarkupSelectionRequest
        {
            Name = name,
            FolderPath = folderPath,
            Source = source,
            AutoTopView = autoTopView,
            FitToSelection = fitToSelection,
            FitMarginFactor = fitMarginFactor,
            EllipseColor = ellipseColor,
            Thickness = thickness,
            PaddingFactor = paddingFactor,
            MinMarkSizeMm = minMarkSizeMm,
            MinRadiusPixels = minRadiusPixels,
            MarkStyle = markStyle,
            ArrowCallout = arrowCallout,
            ArrowLengthMm = arrowLengthMm,
            TargetCrosshair = targetCrosshair,
            HatchAngleDeg = hatchAngleDeg,
            HatchSpacingMm = hatchSpacingMm,
            HatchSpacingPx = hatchSpacingPx,
            HatchThickness = hatchThickness,
            MarkSoloMinSizeMm = markSoloMinSizeMm,
            MarkMergeGapMm = markMergeGapMm,
            ClusterBy = clusterBy,
            ClusterMaxDistanceMm = clusterMaxDistanceMm,
            ClusterTargetSize = clusterTargetSize,
            ClusterCount = clusterCount,
            ClusterGridSizeMm = clusterGridSizeMm,
            MaxClusters = maxClusters,
            MaxItemsForClustering = maxItemsForClustering,
            MaxItemsForDistanceClustering = maxItemsForDistanceClustering,
            Overwrite = overwrite,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Plans or shows runtime-only overlay markers for hybrid groups in the current selection. The markers stay attached while the camera moves but are never stored in saved viewpoints or .nwd/.nwf files. Use markup_selection for persistent deliverables.")]
    public Task<LiveMarkersResponse> LiveMarkers(
        [Description("Overlay shape: rectangle, target, or arrow. Default is target.")] string style = "target",
        [Description("True shows or updates markers; false hides them when apply=true. Default is true.")] bool visible = true,
        [Description("Marker radius or padding in screen pixels, from 5 to 200. Default is 10.")] int markerRadiusPixels = 10,
        [Description("Horizontal bbox size in millimeters at which an item receives its own marker. Default is 1500.")] double markSoloMinSizeMm = 1500,
        [Description("Maximum horizontal bbox gap in millimeters for merging small items into one marker. Default is 1000.")] double markMergeGapMm = 1000,
        [Description("False previews marker groups without changing the active tool. True shows, updates, or hides the overlay. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.LiveMarkersAsync(new LiveMarkersRequest
        {
            Style = style,
            Visible = visible,
            MarkerRadiusPixels = markerRadiusPixels,
            MarkSoloMinSizeMm = markSoloMinSizeMm,
            MarkMergeGapMm = markMergeGapMm,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Creates one or more saved viewpoints with an enabled Navisworks section box around the current selection plus context. Optional persistent markup and line-based arrow callouts are calculated after the final ISO camera and clipping box. It never hides or isolates model items. Defaults to dry-run.")]
    public Task<SectionBoxViewpointResponse> SectionBoxViewpoint(
        [Description("Saved viewpoint base name. Slashes are replaced with spaces. When clustering creates multiple viewpoints, (1), (2), and so on are appended.")] string name,
        [Description("Optional folder path under Saved Viewpoints, for example MTR/240103-ТХ. Missing folders are created only when apply=true.")] string folderPath = "",
        [Description("Selection source. Only current_selection is currently supported.")] string source = "current_selection",
        [Description("Extra context around every cluster bounding box, in millimeters. Default is 1000.")] double boxOffsetMm = 1000,
        [Description("Optional markup style: rectangle, target, arrow, or hatch. Empty keeps the section-box viewpoint unmarked unless arrowCallout=true.")] string markStyle = "",
        [Description("Add one arrow callout built from supported RedlineLine primitives to every mark. Default is false.")] bool arrowCallout = false,
        [Description("Arrow length in millimeters. 0 selects 8% of the final camera HeightField, clamped to 5-15%. Default is 0.")] double arrowLengthMm = 0,
        [Description("Draw a crosshair over target ellipses. Default is false.")] bool targetCrosshair = false,
        [Description("Optional RGB markup color with three values from 0 to 1. Default is [1, 0, 0].")] List<double> ellipseColor = null,
        [Description("Markup line thickness from 1 to 20. Default is 3.")] int thickness = 3,
        [Description("Extra projected frame size as a fraction of each group box, from 0 to 5. Default is 0.20.")] double paddingFactor = 0.20,
        [Description("Minimum full markup size in millimeters. Default is 500.")] double? minMarkSizeMm = null,
        [Description("Hatch angle in degrees when markStyle=hatch. Default is 45.")] double hatchAngleDeg = 45,
        [Description("Hatch line spacing in millimeters when markStyle=hatch. Default is 500.")] double? hatchSpacingMm = null,
        [Description("Hatch line thickness from 1 to 20. Defaults to thickness.")] int hatchThickness = 3,
        [Description("Horizontal bbox size in millimeters at which an item receives its own frame. Default is 1500.")] double markSoloMinSizeMm = 1500,
        [Description("Maximum horizontal bbox gap in millimeters for merging small items into one frame. Default is 1000.")] double markMergeGapMm = 1000,
        [Description("Maximum gap between item bounding boxes, in millimeters, for proximity clusters. 0 keeps one viewpoint for the full selection. Default is 0.")] double clusterMaxDistanceMm = 0,
        [Description("Clustering strategy: none, distance, count, or grid. Empty preserves legacy inference from clusterMaxDistanceMm.")] string clusterBy = "",
        [Description("Approximate items per cluster for clusterBy=count. Default is 100.")] int clusterTargetSize = 100,
        [Description("Exact number of clusters for clusterBy=count, from 1 to 100. When set, overrides clusterTargetSize.")] int? clusterCount = null,
        [Description("Plan-grid square size in millimeters for clusterBy=grid. Default is 50000.")] double clusterGridSizeMm = 50000,
        [Description("Optional cap from 1 to 100 for created clusters. Largest clusters are retained.")] int? maxClusters = null,
        [Description("Distance-clustering safeguard from 1 to 10000. It does not limit count/grid modes. Default is 500.")] int? maxItemsForClustering = null,
        [Description("Deprecated alias for maxItemsForClustering.")] int? maxItemsForDistanceClustering = null,
        [Description("Update an existing viewpoint with the same generated name in place. Duplicate same-name viewpoints remain an error. Default is false.")] bool overwrite = false,
        [Description("False previews the planned viewpoints without changing the view. True creates section-box viewpoints without hiding or isolating items. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SectionBoxViewpointAsync(new SectionBoxViewpointRequest
        {
            Name = name,
            FolderPath = folderPath,
            Source = source,
            BoxOffsetMm = boxOffsetMm,
            MarkStyle = markStyle,
            ArrowCallout = arrowCallout,
            ArrowLengthMm = arrowLengthMm,
            TargetCrosshair = targetCrosshair,
            EllipseColor = ellipseColor,
            Thickness = thickness,
            PaddingFactor = paddingFactor,
            MinMarkSizeMm = minMarkSizeMm,
            HatchAngleDeg = hatchAngleDeg,
            HatchSpacingMm = hatchSpacingMm,
            HatchThickness = hatchThickness,
            MarkSoloMinSizeMm = markSoloMinSizeMm,
            MarkMergeGapMm = markMergeGapMm,
            ClusterBy = clusterBy,
            ClusterMaxDistanceMm = clusterMaxDistanceMm,
            ClusterTargetSize = clusterTargetSize,
            ClusterCount = clusterCount,
            ClusterGridSizeMm = clusterGridSizeMm,
            MaxClusters = maxClusters,
            MaxItemsForClustering = maxItemsForClustering,
            MaxItemsForDistanceClustering = maxItemsForDistanceClustering,
            Overwrite = overwrite,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

}
