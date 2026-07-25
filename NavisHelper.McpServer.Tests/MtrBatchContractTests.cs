using System.Text.Json;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class MtrBatchContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void Request_UsesStableSnakeCasePayloadFields()
    {
        var request = new BuildMtrViewpointsRequest
        {
            FolderPrefix = "MTR",
            CreatePlan = true,
            CreateSectionBox = true,
            ClusterMaxDistanceMm = 10000,
            FitMarginFactor = 0.10,
            MarkStyle = "arrow",
            ArrowCallout = true,
            ArrowLengthMm = 0,
            TargetCrosshair = false,
            HatchSpacingMm = 500,
            MarkSoloMinSizeMm = 1500,
            MarkMergeGapMm = 1000,
            BoxOffsetMm = 1000,
            SkipExisting = true,
            MaxSetCount = 500,
            Apply = false,
        };

        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));
        var root = payload.RootElement;

        Assert.Equal("MTR", root.GetProperty("folder_prefix").GetString());
        Assert.Equal(10000, root.GetProperty("cluster_max_distance_mm").GetDouble());
        Assert.Equal(0.10, root.GetProperty("fit_margin_factor").GetDouble());
        Assert.Equal("arrow", root.GetProperty("mark_style").GetString());
        Assert.True(root.GetProperty("arrow_callout").GetBoolean());
        Assert.Equal(0, root.GetProperty("arrow_length_mm").GetDouble());
        Assert.Equal(500, root.GetProperty("hatch_spacing_mm").GetDouble());
        Assert.Equal(1500, root.GetProperty("mark_solo_min_size_mm").GetDouble());
        Assert.Equal(1000, root.GetProperty("mark_merge_gap_mm").GetDouble());
        Assert.Equal(1000, root.GetProperty("box_offset_mm").GetDouble());
        Assert.True(root.GetProperty("skip_existing").GetBoolean());
    }

    [Fact]
    public void Response_RoundTripsBatchSummary()
    {
        var response = new BuildMtrViewpointsResponse
        {
            Apply = true,
            FolderPrefix = "MTR",
            SelectionSetCount = 180,
            ProcessedSetCount = 180,
            NonEmptySetCount = 174,
            SkippedEmptySetCount = 6,
            CreatedPlanViewpointCount = 212,
            CreatedSectionBoxViewpointCount = 212,
            Items = new List<BuildMtrViewpointsItemResponse>
            {
                new()
                {
                    SelectionSetPath = "MTR/240103-ТХ/28245",
                    FolderPath = "MTR/240103-ТХ",
                    SelectedItemCount = 2,
                    ClusterCount = 1,
                    CreatedPlanViewpointCount = 1,
                    CreatedSectionBoxViewpointCount = 1,
                },
            },
        };

        var roundTripped = JsonSerializer.Deserialize<BuildMtrViewpointsResponse>(JsonSerializer.Serialize(response, JsonOptions), JsonOptions);

        Assert.Equal(180, roundTripped.ProcessedSetCount);
        Assert.Equal(212, roundTripped.CreatedPlanViewpointCount);
        Assert.Single(roundTripped.Items);
        Assert.Equal("MTR/240103-ТХ/28245", roundTripped.Items[0].SelectionSetPath);
    }

    [Fact]
    public void HostCommandName_IsStable()
    {
        Assert.Equal("build_mtr_viewpoints", HostCommandNames.BuildMtrViewpoints);
        Assert.Equal("selection_sets_build_viewpoints", HostCommandNames.SelectionSetsBuildViewpoints);
    }

    [Fact]
    public void NeutralBatchRequest_RoundTripsIndependentSteps()
    {
        var request = new SelectionSetsBuildViewpointsRequest
        {
            FolderPrefix = "Equipment",
            NameTemplate = "{set} - {step}",
            Steps = new List<SelectionSetViewpointStep>
            {
                new() { Step = SelectionSetViewpointStepKinds.Overview, Label = "overview", MarkStyle = "hatch", ClusterBy = SelectionClusterModes.None },
                new() { Step = SelectionSetViewpointStepKinds.Markup, Label = "groups", MarkStyle = "arrow", MinMarkSizeMm = 500, HatchSpacingMm = 500, ClusterBy = SelectionClusterModes.Count, ClusterTargetSize = 300, ClusterCount = 5, WhenItemCountMin = 1000, Overwrite = true },
                new() { Step = SelectionSetViewpointStepKinds.SectionBox, Label = "zones", MarkStyle = "target", ArrowCallout = true, ArrowLengthMm = 0, TargetCrosshair = false, ClusterBy = SelectionClusterModes.Grid, ClusterGridSizeMm = 50000 },
            },
            Apply = false,
            Verbosity = "summary",
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var payload = JsonDocument.Parse(json);
        Assert.Equal("Equipment", payload.RootElement.GetProperty("folder_prefix").GetString());
        Assert.Equal(3, payload.RootElement.GetProperty("steps").GetArrayLength());
        Assert.Equal("count", payload.RootElement.GetProperty("steps")[1].GetProperty("cluster_by").GetString());
        Assert.Equal(500, payload.RootElement.GetProperty("steps")[1].GetProperty("min_mark_size_mm").GetDouble());
        Assert.Equal(500, payload.RootElement.GetProperty("steps")[1].GetProperty("hatch_spacing_mm").GetDouble());
        Assert.Equal(300, payload.RootElement.GetProperty("steps")[1].GetProperty("cluster_target_size").GetInt32());
        Assert.Equal(5, payload.RootElement.GetProperty("steps")[1].GetProperty("cluster_count").GetInt32());
        Assert.Equal(1000, payload.RootElement.GetProperty("steps")[1].GetProperty("when_item_count_min").GetInt32());
        Assert.True(payload.RootElement.GetProperty("steps")[1].GetProperty("overwrite").GetBoolean());
        Assert.Equal("summary", payload.RootElement.GetProperty("verbosity").GetString());
        Assert.Equal(50000, payload.RootElement.GetProperty("steps")[2].GetProperty("cluster_grid_size_mm").GetDouble());
        Assert.Equal("target", payload.RootElement.GetProperty("steps")[2].GetProperty("mark_style").GetString());
        Assert.True(payload.RootElement.GetProperty("steps")[2].GetProperty("arrow_callout").GetBoolean());
    }
}
