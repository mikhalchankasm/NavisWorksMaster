using System.Text.Json;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class CreateSearchSetContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void Response_ReportsRuntimeResolvedConditionCount()
    {
        var response = new CreateSearchSetResponse
        {
            Apply = true,
            Name = "29019 (ОКШ90-57х6К48-0.А, PN=1,6МПа)",
            Path = "MTR/240100-ТК2/29019",
            ConditionCount = 2,
            MatchedItemCount = 2,
            RuntimeResolvedConditionCount = 1,
        };

        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions));
        Assert.Equal(1, payload.RootElement.GetProperty("runtime_resolved_condition_count").GetInt32());
    }
}
