using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #362, from real modelling work: duplicated pipelines (copies with suffixes _04, _05, ...)
// were found more cheaply than with a clash test, by comparing bounding boxes on a 1 cm
// grid. find_duplicate_geometry keys every geometry item by its rounded box and primitive
// count and reports the groups; RVM from E3D also pairs most fittings with a same-name
// neighbour of identical geometry, which the groups flag.
public sealed class DuplicateGeometryTests
{
    private static DuplicateGeometryCandidate Item(int index, string root, string parent, string name, double x, int primitives = 12) =>
        new DuplicateGeometryCandidate
        {
            Index = index, RootKey = root, ParentKey = parent, DisplayName = name,
            MinX = x, MinY = 0, MinZ = 0, MaxX = x + 1, MaxY = 0.2, MaxZ = 0.2, PrimitiveCount = primitives,
        };

    [Fact]
    public void BoxesWithinTheGridShareAKey()
    {
        var a = Item(0, "A", "p", "TUBI 1", 10.000);
        var b = Item(1, "A", "p", "TUBI 1", 10.004);
        var c = Item(2, "A", "p", "TUBI 1", 10.020);
        Assert.Equal(DuplicateGeometryRules.Key(a, 0.01), DuplicateGeometryRules.Key(b, 0.01));
        Assert.NotEqual(DuplicateGeometryRules.Key(a, 0.01), DuplicateGeometryRules.Key(c, 0.01));
        Assert.NotEqual(DuplicateGeometryRules.Key(a, 0.01), DuplicateGeometryRules.Key(Item(3, "A", "p", "TUBI 1", 10.0, primitives: 13), 0.01));
    }

    [Fact]
    public void ModesSplitGroupsByRoot()
    {
        var items = new[]
        {
            Item(0, "PIPE_04", "b1", "TUBI 1", 5), Item(1, "PIPE_05", "b9", "TUBI 1", 5),
            Item(2, "PIPE_04", "b2", "ELBO 1", 8), Item(3, "PIPE_04", "b2", "ELBO 1", 8),
            Item(4, "PIPE_04", "b3", "TEE 1", 20),
        };

        var all = DuplicateGeometryRules.Group(items, 0.01, DuplicateGeometryModes.All, 50);
        Assert.Equal(2, all.Count);

        var across = DuplicateGeometryRules.Group(items, 0.01, DuplicateGeometryModes.AcrossRoots, 50);
        Assert.Single(across);
        Assert.Equal(new[] { 0, 1 }, across[0].ItemIndexes);
        Assert.Equal(2, across[0].RootCount);

        var within = DuplicateGeometryRules.Group(items, 0.01, DuplicateGeometryModes.WithinRoot, 50);
        Assert.Single(within);
        Assert.Equal(new[] { 2, 3 }, within[0].ItemIndexes);
        Assert.True(within[0].SameParentAndName);
        Assert.False(all.Single(group => group.ItemIndexes.Contains(0)).SameParentAndName);
    }

    [Fact]
    public void LargestGroupsComeFirstAndTheCountIsCapped()
    {
        var items = new List<DuplicateGeometryCandidate>();
        for (var i = 0; i < 3; i++) items.Add(Item(i, "R", "p", "A", 1));
        for (var i = 3; i < 5; i++) items.Add(Item(i, "R", "p", "B", 2));
        for (var i = 5; i < 7; i++) items.Add(Item(i, "R", "p", "C", 3));

        var groups = DuplicateGeometryRules.Group(items, 0.01, DuplicateGeometryModes.All, 2);
        Assert.Equal(2, groups.Count);
        Assert.Equal(3, groups[0].ItemIndexes.Count);
    }

    [Theory]
    [InlineData("", "all")]
    [InlineData("ACROSS_ROOTS", "across_roots")]
    [InlineData(" within_root ", "within_root")]
    public void ModesNormalise(string value, string expected)
    {
        Assert.True(DuplicateGeometryModes.TryNormalize(value, out var mode));
        Assert.Equal(expected, mode);
        Assert.False(DuplicateGeometryModes.TryNormalize("roots", out _));
    }

    [Theory]
    [InlineData(null, 0.01)]
    [InlineData(0.0, 0.0001)]
    [InlineData(0.05, 0.05)]
    [InlineData(5.0, 1.0)]
    public void GridIsBounded(double? value, double expected)
    {
        Assert.Equal(expected, DuplicateGeometryRules.ClampGrid(value), 10);
    }

    [Fact]
    public void TheToolIsReadOnlyAndNamed()
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == "FindDuplicateGeometry");
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
        Assert.Equal("find_duplicate_geometry", tool.ProtocolTool.Name);
        Assert.Equal(ToolEffects.None, method.GetCustomAttribute<ToolCapabilitiesAttribute>()!.Effects);
        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!;
        Assert.Equal(0.01, schema["properties"]!["gridSize"]!["default"]!.GetValue<double>(), 10);
        Assert.Equal("all", schema["properties"]!["mode"]!["default"]!.GetValue<string>());
        Assert.Equal("find_duplicate_geometry", HostCommandNames.FindDuplicateGeometry);
    }

    [Fact]
    public void ThePluginKeysOnBoxAndPrimitiveCount()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "DuplicateGeometryService.cs"));
        Assert.Contains("DuplicateGeometryRules.Group(", service, StringComparison.Ordinal);
        Assert.Contains("PrimitiveCount", service, StringComparison.Ordinal);
        Assert.Contains("BoundingBox", service, StringComparison.Ordinal);
    }
}
