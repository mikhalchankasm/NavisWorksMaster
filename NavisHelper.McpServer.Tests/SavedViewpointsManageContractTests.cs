using System.Text.Json;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SavedViewpointsManageContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void DeleteManyRequest_RoundTripsStructuredTargets()
    {
        var request = new SavedViewpointsManageRequest
        {
            Operation = "delete_many",
            Apply = true,
            Items = new List<SavedViewpointsManageTarget>
            {
                new() { PathOrName = "MTR/A — план" },
                new() { ItemId = "sv:0/2", Occurrence = 1 },
            },
        };

        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));

        Assert.Equal("delete_many", payload.RootElement.GetProperty("operation").GetString());
        Assert.Equal(2, payload.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal("MTR/A — план", payload.RootElement.GetProperty("items")[0].GetProperty("path_or_name").GetString());
        Assert.Equal("sv:0/2", payload.RootElement.GetProperty("items")[1].GetProperty("item_id").GetString());
    }

    [Fact]
    public void DeleteManyResponse_RoundTripsPerItemResults()
    {
        var response = new SavedViewpointsManageResponse
        {
            Apply = true,
            Operation = "delete_many",
            MatchedItemCount = 2,
            DeletedItemCount = 2,
            Changed = true,
            Items = new List<SavedViewpointsManageItemResponse>
            {
                new() { ItemId = "sv:0/1", Path = "MTR/A", Name = "A", Type = "SavedViewpoint", Deleted = true },
                new() { ItemId = "sv:0/2", Path = "MTR/B", Name = "B", Type = "SavedViewpoint", Deleted = true },
            },
        };

        var roundTripped = JsonSerializer.Deserialize<SavedViewpointsManageResponse>(JsonSerializer.Serialize(response, JsonOptions), JsonOptions);

        Assert.Equal(2, roundTripped.DeletedItemCount);
        Assert.All(roundTripped.Items, item => Assert.True(item.Deleted));
    }
}
