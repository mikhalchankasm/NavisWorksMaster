using System.Text.Json;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class MarkupSelectionContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void Request_UsesStableSnakeCasePayloadFields()
    {
        var request = new MarkupSelectionRequest
        {
            Name = "28245 (КП820)",
            FolderPath = "MTR/240103-ТХ",
            Source = "current_selection",
            AutoTopView = true,
            FitToSelection = true,
            FitMarginFactor = 0.10,
            EllipseColor = new List<double> { 1, 0, 0 },
            Thickness = 3,
            PaddingFactor = 0.20,
            MinMarkSizeMm = 500,
            MarkStyle = "target",
            ArrowCallout = true,
            ArrowLengthMm = 0,
            TargetCrosshair = false,
            HatchSpacingMm = 500,
            MarkSoloMinSizeMm = 1500,
            MarkMergeGapMm = 1000,
            ClusterBy = SelectionClusterModes.Count,
            ClusterMaxDistanceMm = 10000,
            ClusterTargetSize = 300,
            ClusterCount = 5,
            MaxClusters = 10,
            MaxItemsForClustering = 750,
            Overwrite = true,
            Apply = true,
        };

        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));
        var root = payload.RootElement;

        Assert.Equal("28245 (КП820)", root.GetProperty("name").GetString());
        Assert.Equal("MTR/240103-ТХ", root.GetProperty("folder_path").GetString());
        Assert.True(root.GetProperty("auto_top_view").GetBoolean());
        Assert.Equal(0.10, root.GetProperty("fit_margin_factor").GetDouble());
        Assert.Equal(500, root.GetProperty("min_mark_size_mm").GetDouble());
        Assert.Equal("target", root.GetProperty("mark_style").GetString());
        Assert.True(root.GetProperty("arrow_callout").GetBoolean());
        Assert.Equal(0, root.GetProperty("arrow_length_mm").GetDouble());
        Assert.False(root.GetProperty("target_crosshair").GetBoolean());
        Assert.Equal(500, root.GetProperty("hatch_spacing_mm").GetDouble());
        Assert.Equal(1500, root.GetProperty("mark_solo_min_size_mm").GetDouble());
        Assert.Equal(1000, root.GetProperty("mark_merge_gap_mm").GetDouble());
        Assert.Equal(10000, root.GetProperty("cluster_max_distance_mm").GetDouble());
        Assert.Equal("count", root.GetProperty("cluster_by").GetString());
        Assert.Equal(300, root.GetProperty("cluster_target_size").GetInt32());
        Assert.Equal(5, root.GetProperty("cluster_count").GetInt32());
        Assert.Equal(10, root.GetProperty("max_clusters").GetInt32());
        Assert.Equal(750, root.GetProperty("max_items_for_clustering").GetInt32());
        Assert.True(root.GetProperty("overwrite").GetBoolean());
        Assert.True(root.GetProperty("apply").GetBoolean());
    }

    [Fact]
    public void Response_RoundTripsMarkupCountsAndWarnings()
    {
        var response = new MarkupSelectionResponse
        {
            Apply = true,
            Name = "28245 (КП820)",
            FolderPath = "MTR/240103-ТХ",
            Path = "MTR/240103-ТХ/28245 (КП820)",
            SelectedItemCount = 2,
            MarkStyle = "target",
            ArrowCallout = true,
            ArrowLengthMm = 0,
            TargetCrosshair = false,
            MarkSoloMinSizeMm = 1500,
            MarkMergeGapMm = 1000,
            MarkCount = 1,
            SoloMarkCount = 0,
            MergedMarkCount = 1,
            EllipseCount = 1,
            ArrowCount = 1,
            DroppedClusterCount = 2,
            UncoveredItemCount = 17,
            SkippedItemCount = 0,
            Created = true,
            Warnings = new List<string> { "example" },
        };

        var roundTripped = JsonSerializer.Deserialize<MarkupSelectionResponse>(JsonSerializer.Serialize(response, JsonOptions), JsonOptions);

        Assert.Equal(1, roundTripped.MarkCount);
        Assert.Equal("target", roundTripped.MarkStyle);
        Assert.Equal(1, roundTripped.MergedMarkCount);
        Assert.Equal(1, roundTripped.EllipseCount);
        Assert.Equal(1, roundTripped.ArrowCount);
        Assert.Equal(2, roundTripped.DroppedClusterCount);
        Assert.Equal(17, roundTripped.UncoveredItemCount);
        Assert.True(roundTripped.ArrowCallout);
        Assert.Equal(0, roundTripped.SkippedItemCount);
        Assert.True(roundTripped.Created);
        Assert.Single(roundTripped.Warnings);
    }

    [Fact]
    public void SectionBoxRequestAndClusterResponse_RoundTripWithStableFields()
    {
        var request = new SectionBoxViewpointRequest
        {
            Name = "28245 — бокс",
            FolderPath = "MTR/240103-ТХ",
            Source = "current_selection",
            BoxOffsetMm = 1500,
            MarkStyle = "target",
            ArrowCallout = true,
            ArrowLengthMm = 0,
            TargetCrosshair = false,
            ClusterBy = SelectionClusterModes.Grid,
            ClusterGridSizeMm = 50000,
            MaxItemsForClustering = 1000,
            Overwrite = true,
            Apply = true,
        };
        var response = new SectionBoxViewpointResponse
        {
            Apply = true,
            ClusterCount = 2,
            MarkStyle = "target",
            ArrowCallout = true,
            MarkCount = 3,
            EllipseCount = 3,
            ArrowCount = 3,
            Clusters = new List<SelectionViewpointClusterInfo>
            {
                new()
                {
                    Index = 1,
                    ItemCount = 3,
                    ViewpointName = "28245 — бокс (1)",
                    ViewpointPath = "MTR/240103-ТХ/28245 — бокс (1)",
                    PreviewItemNames = new List<string> { "A", "B" },
                },
            },
        };

        using var requestPayload = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));
        Assert.Equal(1500, requestPayload.RootElement.GetProperty("box_offset_mm").GetDouble());
        Assert.Equal("grid", requestPayload.RootElement.GetProperty("cluster_by").GetString());
        Assert.Equal(50000, requestPayload.RootElement.GetProperty("cluster_grid_size_mm").GetDouble());
        Assert.Equal(1000, requestPayload.RootElement.GetProperty("max_items_for_clustering").GetInt32());
        Assert.Equal("target", requestPayload.RootElement.GetProperty("mark_style").GetString());
        Assert.True(requestPayload.RootElement.GetProperty("arrow_callout").GetBoolean());
        Assert.True(requestPayload.RootElement.GetProperty("overwrite").GetBoolean());

        var roundTripped = JsonSerializer.Deserialize<SectionBoxViewpointResponse>(JsonSerializer.Serialize(response, JsonOptions), JsonOptions);
        Assert.Equal(2, roundTripped.ClusterCount);
        Assert.Equal(3, roundTripped.MarkCount);
        Assert.Equal(3, roundTripped.ArrowCount);
        Assert.Single(roundTripped.Clusters);
        Assert.Equal("28245 — бокс (1)", roundTripped.Clusters[0].ViewpointName);
    }

    [Fact]
    public void HostCommandName_IsStable()
    {
        Assert.Equal("markup_selection", HostCommandNames.MarkupSelection);
        Assert.Equal("section_box_viewpoint", HostCommandNames.SectionBoxViewpoint);
    }
}
