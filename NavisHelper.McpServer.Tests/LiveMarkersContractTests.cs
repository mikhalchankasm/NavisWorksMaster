using System.Text.Json;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class LiveMarkersContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void Request_UsesStableSnakeCasePayloadFields()
    {
        var request = new LiveMarkersRequest
        {
            Style = "arrow",
            Visible = true,
            MarkerRadiusPixels = 12,
            MarkSoloMinSizeMm = 1500,
            MarkMergeGapMm = 1000,
            Apply = true,
        };

        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));
        var root = payload.RootElement;
        Assert.Equal("arrow", root.GetProperty("style").GetString());
        Assert.True(root.GetProperty("visible").GetBoolean());
        Assert.Equal(12, root.GetProperty("marker_radius_pixels").GetInt32());
        Assert.Equal(1500, root.GetProperty("mark_solo_min_size_mm").GetDouble());
        Assert.Equal(1000, root.GetProperty("mark_merge_gap_mm").GetDouble());
        Assert.True(root.GetProperty("apply").GetBoolean());
    }

    [Fact]
    public void Response_ExplicitlyReportsRuntimeOnlyState()
    {
        var response = new LiveMarkersResponse
        {
            Apply = true,
            Visible = true,
            Active = true,
            Persistent = false,
            Style = "target",
            SelectedItemCount = 8,
            MarkerCount = 1,
            MergedMarkerCount = 1,
        };

        var roundTripped = JsonSerializer.Deserialize<LiveMarkersResponse>(JsonSerializer.Serialize(response, JsonOptions), JsonOptions);
        Assert.True(roundTripped.Active);
        Assert.False(roundTripped.Persistent);
        Assert.Equal(1, roundTripped.MarkerCount);
        Assert.Equal(1, roundTripped.MergedMarkerCount);
    }

    [Fact]
    public void HostCommandName_IsStable()
    {
        Assert.Equal("live_markers", HostCommandNames.LiveMarkers);
    }
}
