using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashReportTransportCompactionHelperTests
{
    [Fact]
    public void Apply_Compact_RemovesDuplicatedPayloadButKeepsArtifactReferences()
    {
        var response = new ClashGenerateReportResponse
        {
            ManifestPath = @"C:\report\manifest.json",
            ReportPath = @"C:\report\report.html",
            Items =
            {
                new ClashReportItem
                {
                    Description = "Long description",
                    Item1Path = "/root/a/item",
                    Item2Path = "/root/b/item",
                    ScreenshotPath = "images/cluster_000001.jpg",
                    ViewpointPath = "Report/Cluster 1",
                    ClusterId = "cluster-1",
                },
            },
            Clusters =
            {
                new ClashClusterSummary
                {
                    AssociationKeyA = "path:/root/a/item",
                    AssociationKeyB = "path:/root/b/item",
                    ScreenshotPath = "images/cluster_000001.jpg",
                    PreviewRows =
                    {
                        new ClashClusterPreviewRow { Item1Path = "/root/a/item" },
                    },
                },
            },
        };

        ClashReportTransportCompactionHelper.Apply(response, "compact");

        Assert.True(response.ResponseCompacted);
        Assert.Equal("compact", response.Verbosity);
        Assert.Empty(response.Items[0].Description);
        Assert.Empty(response.Items[0].Item1Path);
        Assert.Empty(response.Items[0].Item2Path);
        Assert.Equal("images/cluster_000001.jpg", response.Items[0].ScreenshotPath);
        Assert.Equal("Report/Cluster 1", response.Items[0].ViewpointPath);
        Assert.Equal("cluster-1", response.Items[0].ClusterId);
        Assert.Empty(response.Clusters[0].AssociationKeyA);
        Assert.Empty(response.Clusters[0].AssociationKeyB);
        Assert.Empty(response.Clusters[0].PreviewRows);
        Assert.Equal("images/cluster_000001.jpg", response.Clusters[0].ScreenshotPath);
        Assert.Contains("clusters[].previewRows", response.CompactOmittedFields);
        Assert.Equal(@"C:\report\manifest.json", response.ManifestPath);
    }

    [Fact]
    public void Apply_Full_DoesNotChangePayload()
    {
        var response = new ClashGenerateReportResponse
        {
            Items = { new ClashReportItem { Item1Path = "/root/a/item" } },
        };

        ClashReportTransportCompactionHelper.Apply(response, "full");

        Assert.False(response.ResponseCompacted);
        Assert.Equal("full", response.Verbosity);
        Assert.Equal("/root/a/item", response.Items[0].Item1Path);
        Assert.Empty(response.CompactOmittedFields);
    }
}
