using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #347: on a ~770k-item model isolate_by_box took ~3.5 min. The walk prunes only subtrees
// that miss the zone; a subtree fully inside it is still walked to every leaf, because a
// hidden descendant inside must be revealed. With revealHiddenInside=false such a subtree is
// kept visible as a whole and not walked, and the response says how many were skipped. The
// default is unchanged. Items without a readable box stay visible by design; they are now
// listed so a far-away "point" remnant can be found.
public sealed class BoxIsolationPruningTests
{
    private static BoxVector3 V(double x, double y, double z) => new BoxVector3 { X = x, Y = y, Z = z };

    private static SectionBoxGeometry Zone(params BoxVector3[] axes) => new SectionBoxGeometry
    {
        FormatVersion = 1,
        CoordinateSpace = "document_global",
        DocumentUnits = "meters",
        Center = V(0, 0, 0),
        HalfExtents = V(10, 10, 10),
        Axes = axes.Length == 0 ? new List<BoxVector3> { V(1, 0, 0), V(0, 1, 0), V(0, 0, 1) } : axes.ToList(),
    };

    [Theory]
    [InlineData(-1, -1, -1, 1, 1, 1, true)]
    [InlineData(-10, -10, -10, 10, 10, 10, true)]
    [InlineData(5, 5, 5, 11, 6, 6, false)]
    [InlineData(20, 20, 20, 21, 21, 21, false)]
    public void AnAxisAlignedZoneContainsWhatLiesWithinIt(double minX, double minY, double minZ, double maxX, double maxY, double maxZ, bool expected)
    {
        var tester = new SectionBoxGeometryRules.SectionBoxIntersectionTester(Zone());
        Assert.Equal(expected, tester.Contains(V(minX, minY, minZ), V(maxX, maxY, maxZ)));
    }

    [Fact]
    public void ARotatedZoneChecksAllEightCorners()
    {
        var c = Math.Sqrt(0.5);
        var tester = new SectionBoxGeometryRules.SectionBoxIntersectionTester(Zone(V(c, c, 0), V(-c, c, 0), V(0, 0, 1)));
        Assert.True(tester.Contains(V(0, 0, 0), V(1, 1, 1)));
        // The corner (15, 0) lies 10.6 along the rotated x axis: outside, though the box still intersects.
        Assert.False(tester.Contains(V(13, 0, 0), V(15, 1, 1)));
        Assert.True(tester.Intersects(V(13, 0, 0), V(15, 1, 1)));
    }

    [Theory]
    [InlineData(BoxIsolationNodeDisposition.Intersecting, true, true, false, false)]
    [InlineData(BoxIsolationNodeDisposition.Intersecting, true, true, true, true)]
    [InlineData(BoxIsolationNodeDisposition.Intersecting, true, false, false, true)]
    [InlineData(BoxIsolationNodeDisposition.OutsideSubtree, true, false, false, false)]
    [InlineData(BoxIsolationNodeDisposition.Intersecting, false, true, false, false)]
    public void AContainedSubtreeIsSkippedOnlyWhenRevealingIsOff(BoxIsolationNodeDisposition disposition, bool hasChildren, bool fullyInside, bool revealHiddenInside, bool expected)
    {
        Assert.Equal(expected, BoxIsolationTraversalPolicy.ShouldDescend(disposition, hasChildren, fullyInside, revealHiddenInside));
    }

    [Fact]
    public void AccountingCountsSkippedInsideSubtreesApart()
    {
        var accounting = new BoxIsolationTraversalAccounting(100);
        accounting.RecordPrunedInsideSubtree();
        accounting.RecordPrunedInsideSubtree();
        Assert.Equal(2, accounting.PrunedInsideSubtreeCount);
        Assert.Equal(0, accounting.PrunedSubtreeRootCount);
    }

    [Fact]
    public void ContractsCarryTheOptionAndTheReport()
    {
        Assert.False(new IsolateByBoxRequest { RevealHiddenInside = false }.RevealHiddenInside);
        var response = new IsolateByBoxResponse { PrunedInsideSubtreeCount = 3, PreservedEmptyPreviewTruncated = true };
        Assert.Equal(3, response.PrunedInsideSubtreeCount);
        Assert.Empty(response.PreservedEmptyPreview);
    }

    [Fact]
    public void TheToolKeepsRevealingByDefault()
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == "IsolateByBox");
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!;
        Assert.Equal("boolean", schema["properties"]!["revealHiddenInside"]!["type"]!.GetValue<string>());
        Assert.True(schema["properties"]!["revealHiddenInside"]!["default"]!.GetValue<bool>());
    }

    [Fact]
    public void TheWalkUsesContainmentAndTheOption()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "BoxIsolationService.cs"));
        Assert.Contains(".Contains(", service, StringComparison.Ordinal);
        Assert.Contains("RevealHiddenInside", service, StringComparison.Ordinal);
        Assert.Contains("RecordPrunedInsideSubtree(", service, StringComparison.Ordinal);
        Assert.Contains("PreservedEmptyPreview", service, StringComparison.Ordinal);
    }
}
