using System;
using System.Collections.Generic;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashReportHtmlRendererTests
{
    [Fact]
    public void Render_ThrowsForNullResponse()
    {
        Assert.Throws<ArgumentNullException>(() => ClashReportHtmlRenderer.Render(null));
    }

    [Fact]
    public void Render_IncludesSummaryWarningsStatusSectionsAndEscapedRows()
    {
        var response = new ClashGenerateReportResponse
        {
            ReturnedResultCount = 2,
            CreatedViewpointCount = 1,
            ScreenshotCount = 2,
            FullBoxTransparencyItemCount = 3,
            ExcludedByItemNameCount = 4,
            Truncated = true,
            TotalStatusCounts = new Dictionary<string, int> { ["Resolved"] = 5, ["New"] = 1 },
            ReturnedStatusCounts = new Dictionary<string, int> { ["Active"] = 2 },
            ExcludedByItemNameCounts = new Dictionary<string, int> { ["Pipe"] = 4 },
            Warnings = { "A&B <warning>" },
            Items =
            {
                new ClashReportItem
                {
                    Index = 1,
                    ResultName = "R<1>&\"",
                    TestName = "T&1",
                    GroupPath = "G<1>",
                    Status = "New",
                    AssignedTo = "User",
                    Item1Name = "Pipe A",
                    Item2Name = "Duct B",
                    ViewpointPath = "Folder/View",
                    ScreenshotCaptured = true,
                    ScreenshotPath = @"images\clash_000001&x.jpg",
                    TopViewScreenshotCaptured = true,
                    TopViewScreenshotPath = @"images\clash_000001_top.jpg",
                    Distance = 12.3456789,
                    BoxMode = "items",
                    BoxOffsetMm = 25,
                    Description = "Desc <unsafe>",
                    ErrorMessage = "Err & unsafe",
                    FullBoxTransparencyItemCount = 3,
                },
                new ClashReportItem
                {
                    Index = 2,
                    ResultName = "No shot",
                },
            },
        };

        var html = ClashReportHtmlRenderer.Render(response);

        Assert.Contains("2 clashes, 1 viewpoints, 2 screenshots, 3 context transparency applications, 4 excluded by item-name filter", html);
        Assert.Contains("<span class=\"warn\">(truncated)</span>", html);
        Assert.Contains("<p class=\"warn\">A&amp;B &lt;warning&gt;</p>", html);
        Assert.Contains("<h2>Status counts in selected test scope</h2>", html);
        Assert.Contains("<tr><th>New</th><td>1</td></tr>", html);
        Assert.Contains("<tr><th>Resolved</th><td>5</td></tr>", html);
        Assert.Contains("<h2>0001 R&lt;1&gt;&amp;&quot;</h2>", html);
        Assert.Contains("src=\"images/clash_000001&amp;x.jpg\"", html);
        Assert.Contains("alt=\"R&lt;1&gt;&amp;&quot;\"", html);
        Assert.Contains("<tr><th>Description</th><td>Desc &lt;unsafe&gt;</td></tr>", html);
        Assert.Contains("<tr><th>Error</th><td>Err &amp; unsafe</td></tr>", html);
        Assert.Contains("<div class=\"placeholder\">Screenshot unavailable</div>", html);
    }

    [Fact]
    public void Render_IncludesClusterSummaryPreviewRowsAndTruncationMarker()
    {
        var response = new ClashGenerateReportResponse
        {
            ClusterCount = 1,
            ReturnedResultCount = 1,
            Clusters =
            {
                new ClashClusterSummary
                {
                    Index = 1,
                    ClusterId = "cluster-1",
                    GroupMode = "hybrid",
                    ClashCount = 3,
                    WeakAssociation = true,
                    DisplayNameA = "Pipe A",
                    DisplayNameB = "Duct B",
                    AssociationLevelA = "source",
                    AssociationLevelB = "path",
                    AssociationKeyA = "src:a",
                    AssociationKeyB = "path:b",
                    SourceFileA = "a.nwc",
                    SourceFileB = "b.nwc",
                    StatusCounts = new Dictionary<string, int> { ["Resolved"] = 2, ["New"] = 1 },
                    Tags = { "tag-a", "tag-b" },
                    PreviewRows =
                    {
                        new ClashClusterPreviewRow
                        {
                            ResultHandle = "clash-result:0:1",
                            TestName = "Test",
                            GroupPath = "Group",
                            ResultName = "Result",
                        },
                    },
                    PreviewRowsTruncated = true,
                },
            },
        };

        var html = ClashReportHtmlRenderer.Render(response);

        Assert.Contains("1 clusters", html);
        Assert.Contains("<h2>Clusters</h2>", html);
        Assert.Contains("<summary><strong>#1 Pipe A / Duct B</strong> - 3 clashes <span class=\"warn\">weak association</span></summary>", html);
        Assert.Contains("<tr><th>Statuses</th><td>New: 1, Resolved: 2</td></tr>", html);
        Assert.Contains("<tr><th>clash-result:0:1</th><td>Test / Group / Result</td></tr>", html);
        Assert.Contains("<tr><th>More</th><td>Member list truncated in HTML/manifest preview.</td></tr>", html);
    }

    [Fact]
    public void Render_ClusterArtifacts_AreShownOnceInClusterSummary()
    {
        var response = new ClashGenerateReportResponse
        {
            ArtifactGranularity = "cluster",
            ClusterCount = 1,
            ReturnedResultCount = 2,
            Clusters =
            {
                new ClashClusterSummary
                {
                    Index = 1,
                    ClashCount = 2,
                    DisplayNameA = "Pipe",
                    DisplayNameB = "Duct",
                    ViewpointPath = "Report/Cluster 1",
                    ScreenshotCaptured = true,
                    ScreenshotPath = @"images\clash_000001.jpg",
                },
            },
            Items =
            {
                new ClashReportItem { Index = 1, ResultName = "Raw 1", ScreenshotCaptured = true, ScreenshotPath = @"images\clash_000001.jpg" },
                new ClashReportItem { Index = 2, ResultName = "Raw 2", ScreenshotCaptured = true, ScreenshotPath = @"images\clash_000001.jpg" },
            },
        };

        var html = ClashReportHtmlRenderer.Render(response);

        Assert.Contains("<tr><th>Viewpoint</th><td>Report/Cluster 1</td></tr>", html);
        Assert.Equal(1, CountOccurrences(html, "src=\"images/clash_000001.jpg\""));
        Assert.DoesNotContain("<div class=\"placeholder\">Screenshot unavailable</div>", html);
    }

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(fragment, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }
        return count;
    }
}
