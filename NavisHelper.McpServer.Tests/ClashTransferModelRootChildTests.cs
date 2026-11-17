using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #507 (Port clash run, 2026-11-17): clash_pair_tests_create builds sides from model roots AND their
// direct children; clash_tests_export knew only the model root, so a 1 334-test export of pair tests
// came out with no definitions. A side that is one direct child of a model root is portable as
// model_root_child: the model (sourceFile / rootName) plus the child's display name. The plugin cannot
// compile in L0, so the exporter, resolver and importer are pinned by source; L3 runs the round trip.
public sealed class ClashTransferModelRootChildTests
{
    [Fact]
    public void ARootChildSideIsPortableAndReachesThePairReference()
    {
        Assert.Equal("model_root_child", ClashTransferSideKinds.ModelRootChild);
        var definition = new ClashTestTransferDefinition
        {
            Name = "001 A.rvm-B.rvm",
            TestType = "hard",
            A = ChildSide("A", "A.rvm"),
            B = ChildSide("B", "B.rvm"),
        };
        ClashTransferPlanHelper.RefreshSupport(definition);
        Assert.True(definition.Supported);

        var pair = Assert.Single(ClashTransferPlanHelper.ToPairs(Plan(definition), false));
        Assert.Equal(@"C:\Models\Port.nwd", pair.A.SourceFile);
        Assert.Equal("Port.nwd", pair.A.RootName);
        Assert.Equal("A.rvm", pair.A.ChildName);
        Assert.Equal("B.rvm", pair.B.ChildName);
    }

    [Fact]
    public void ARootChildSideWithoutAChildNameOrModelIsNotPortable()
    {
        var noChild = ChildSide("A", "");
        Assert.False(ClashTransferPlanHelper.IsPortableSide(noChild));
        var noModel = ChildSide("A", "A.rvm");
        noModel.SourceFile = "";
        noModel.RootName = "";
        Assert.False(ClashTransferPlanHelper.IsPortableSide(noModel));
    }

    [Fact]
    public void ExporterResolverAndImporterHandleTheNewKind()
    {
        var exporter = Read("NavisHelper", "Agent", "Services", "ClashTestTransferExporterService.cs");
        Assert.Contains("side.Kind = ClashTransferSideKinds.ModelRootChild;", exporter, StringComparison.Ordinal);
        Assert.Contains("side.ChildName = ", exporter, StringComparison.Ordinal);

        var resolver = Read("NavisHelper", "Agent", "Services", "SelectionSetReferenceResolver.cs");
        Assert.Contains("reference.ChildName", resolver, StringComparison.Ordinal);
        Assert.Contains("Type = \"ModelRootChild\"", resolver, StringComparison.Ordinal);

        var importer = Read("NavisHelper", "Agent", "Services", "ClashBatchtestImportService.cs");
        Assert.Contains("return ClashTransferSideKinds.ModelRootChild", importer, StringComparison.Ordinal);
    }

    private static ClashTestTransferSide ChildSide(string side, string child) => new()
    {
        Side = side,
        Kind = ClashTransferSideKinds.ModelRootChild,
        SourceFile = @"C:\Models\Port.nwd",
        RootName = "Port.nwd",
        ChildName = child,
        Name = child,
        Supported = true,
    };

    private static ClashTestTransferPlan Plan(ClashTestTransferDefinition definition) => new() { Tests = new List<ClashTestTransferDefinition> { definition } };

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
