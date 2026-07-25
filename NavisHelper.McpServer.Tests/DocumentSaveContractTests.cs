using System.Text.Json;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class DocumentSaveContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void SaveAsRequest_UsesStableSnakeCasePayloadFields()
    {
        var request = new SaveDocumentAsRequest
        {
            Path = @"D:\Examples\NavisHelper\example-model.nwd",
            Overwrite = false,
        };

        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));
        var root = payload.RootElement;

        Assert.Equal(@"D:\Examples\NavisHelper\example-model.nwd", root.GetProperty("path").GetString());
        Assert.False(root.GetProperty("overwrite").GetBoolean());
    }

    [Fact]
    public void SaveResponse_RoundTripsOutputMetadata()
    {
        var response = new SaveDocumentResponse
        {
            Path = @"D:\Examples\NavisHelper\example-model.nwd",
            Format = "nwd",
            FileSizeBytes = 123456,
            Overwritten = true,
        };

        var roundTripped = JsonSerializer.Deserialize<SaveDocumentResponse>(JsonSerializer.Serialize(response, JsonOptions), JsonOptions);

        Assert.Equal("nwd", roundTripped.Format);
        Assert.Equal(123456, roundTripped.FileSizeBytes);
        Assert.True(roundTripped.Overwritten);
    }

    [Fact]
    public void HostCommandNames_AreStable()
    {
        Assert.Equal("save_document", HostCommandNames.SaveDocument);
        Assert.Equal("save_document_as", HostCommandNames.SaveDocumentAs);
    }
}
