using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #522 (L3 of #507 on the Port NWD, 2026-11-17): in a single-file NWD the only model root is the
// document itself, so a model_root_child side carried root_name "port-l3.nwd" and source_file = the
// Navisworks temp copy. Both change with every revision, so 1 567 exported sides resolved nowhere else.
// The exporter now writes such a side by its child name alone, and the resolver finds a child by name
// in any model, or a model root of that name (the NWF case), when the recorded model is not there.
// One match or a refusal; never a guess. The plugin cannot compile in L0, so the matching lives in
// ModelRootChildLocator (Contracts) and the plugin's use of it is pinned by source.
public sealed class ClashTransferRootChildRevisionsTests
{
    [Fact]
    public void TheRecordedModelStillWinsWhenItIsThere()
    {
        var match = ModelRootChildLocator.Locate(new[]
        {
            Model("Port.nwd", @"C:\Temp\navis_1.nwd", "A.rvm", "B.rvm"),
            Model("Other.nwd", @"C:\Temp\navis_2.nwd", "A.rvm"),
        }, "Port.nwd", @"C:\Temp\navis_1.nwd", "A.rvm");
        AssertMatch(match, ModelRootChildMatchKind.Child, 0, 0);
    }

    [Fact]
    public void AChildOfTheNextRevisionResolvesByItsName()
    {
        var match = ModelRootChildLocator.Locate(new[] { Model("Port 24-11.nwd", @"C:\Temp\navis_9.nwd", "A.rvm", "B.rvm") },
            "Port 17-11.nwd", @"C:\Temp\navis_1.nwd", "B.rvm");
        AssertMatch(match, ModelRootChildMatchKind.Child, 0, 1);
    }

    [Fact]
    public void InAnNwfTheFileIsAModelRootAndResolvesAsTheRoot()
    {
        var match = ModelRootChildLocator.Locate(new[]
        {
            Model("A.rvm", @"D:\Port\A.rvm", "/A"),
            Model("B.rvm", @"D:\Port\B.rvm", "/B"),
        }, "Port.nwd", @"C:\Temp\navis_1.nwd", "B.rvm");
        AssertMatch(match, ModelRootChildMatchKind.Root, 1, -1);
    }

    [Fact]
    public void AModelRootIsAlsoFoundByItsFileName()
    {
        var models = new List<ModelRootCandidate>
        {
            new ModelRootCandidate { RootName = "Renamed", SourceFile = "", FileName = @"D:\Port\B.rvm", ChildNames = new List<string> { "/B" } },
        };
        AssertMatch(ModelRootChildLocator.Locate(models, "", "", "B.rvm"), ModelRootChildMatchKind.Root, 0, -1);
    }

    [Fact]
    public void AChildOnlyReferenceResolvesAmongManyModels()
    {
        var match = ModelRootChildLocator.Locate(new[]
        {
            Model("A.rvm", @"D:\Port\A.rvm", "/A"),
            Model("Port.nwd", @"C:\Temp\navis_1.nwd", "C.rvm"),
        }, "", "", "C.rvm");
        AssertMatch(match, ModelRootChildMatchKind.Child, 1, 0);
    }

    [Fact]
    public void TwoCandidatesRefuseAndNoneIsNotFound()
    {
        var models = new[]
        {
            Model("One.nwd", @"C:\Temp\navis_1.nwd", "A.rvm"),
            Model("Two.nwd", @"C:\Temp\navis_2.nwd", "A.rvm"),
        };
        Assert.Equal(ModelRootChildMatchKind.Ambiguous, ModelRootChildLocator.Locate(models, "Gone.nwd", "", "A.rvm").Kind);
        Assert.Equal(ModelRootChildMatchKind.Ambiguous, ModelRootChildLocator.Locate(models, "", "", "A.rvm").Kind);
        Assert.Equal(ModelRootChildMatchKind.NotFound, ModelRootChildLocator.Locate(models, "Gone.nwd", "", "Z.rvm").Kind);

        // The recorded model is here but lacks the child: a miss, not a same-named child of another model.
        Assert.Equal(ModelRootChildMatchKind.NotFound, ModelRootChildLocator.Locate(models, "One.nwd", "", "Z.rvm").Kind);
        var recordedWithoutChild = new[] { Model("One.nwd", @"C:\Temp\navis_1.nwd", "B.rvm"), Model("Two.nwd", @"C:\Temp\navis_2.nwd", "A.rvm") };
        Assert.Equal(ModelRootChildMatchKind.NotFound, ModelRootChildLocator.Locate(recordedWithoutChild, "One.nwd", "", "A.rvm").Kind);

        // A child-only reference where the file is both a model root and another model's child.
        var rootAndChild = new[] { Model("A.rvm", @"D:\Port\A.rvm", "/A"), Model("Port.nwd", @"C:\Temp\navis_1.nwd", "A.rvm") };
        Assert.Equal(ModelRootChildMatchKind.Ambiguous, ModelRootChildLocator.Locate(rootAndChild, "", "", "A.rvm").Kind);

        var siblings = new[] { Model("Port.nwd", @"C:\Temp\navis_1.nwd", "A.rvm", "a.rvm") };
        Assert.Equal(ModelRootChildMatchKind.Ambiguous, ModelRootChildLocator.Locate(siblings, "Port.nwd", "", "A.rvm").Kind);
    }

    [Fact]
    public void TheDocumentItselfIsRecognisedAsTheRoot()
    {
        Assert.True(ModelRootChildLocator.IsDocumentRoot("port-l3.nwd", @"D:\exec\l3-505\port-l3.nwd"));
        Assert.True(ModelRootChildLocator.IsDocumentRoot("PORT-L3.NWD", @"D:\exec\l3-505\port-l3.nwd"));
        Assert.False(ModelRootChildLocator.IsDocumentRoot("A.rvm", @"D:\Port\Port.nwf"));
        Assert.False(ModelRootChildLocator.IsDocumentRoot("port-l3.nwd", ""));
    }

    [Fact]
    public void AChildOnlySideIsPortable()
    {
        var side = new ClashTestTransferSide { Side = "A", Kind = ClashTransferSideKinds.ModelRootChild, ChildName = "A.rvm", Supported = true };
        Assert.True(ClashTransferPlanHelper.IsPortableSide(side));
        side.ChildName = "";
        Assert.False(ClashTransferPlanHelper.IsPortableSide(side));
    }

    [Fact]
    public void TheExporterAndTheResolverUseTheLocator()
    {
        var exporter = Read("NavisHelper", "Agent", "Services", "ClashTestTransferExporterService.cs");
        Assert.Contains("ModelRootChildLocator.IsDocumentRoot(", exporter, StringComparison.Ordinal);
        // The document root is dropped only when the child name alone is unique (Codex review, #522).
        Assert.Contains("string.Empty, string.Empty, childName).Kind == ModelRootChildMatchKind.Child", exporter, StringComparison.Ordinal);
        // ...only when exactly one model carries the document name, and from one snapshot per export.
        Assert.Contains("ModelRootChildLocator.IsDocumentRoot(candidate.RootName, document.FileName)) == 1", exporter, StringComparison.Ordinal);
        Assert.Contains("new Lazy<List<ModelRootCandidate>>(", exporter, StringComparison.Ordinal);
        var resolver = Read("NavisHelper", "Agent", "Services", "SelectionSetReferenceResolver.cs");
        Assert.Contains("ModelRootChildLocator.Locate(", resolver, StringComparison.Ordinal);
    }

    private static ModelRootCandidate Model(string rootName, string sourceFile, params string[] children) =>
        new ModelRootCandidate { RootName = rootName, SourceFile = sourceFile, ChildNames = children.ToList() };

    private static void AssertMatch(ModelRootChildMatch match, ModelRootChildMatchKind kind, int model, int child)
    {
        Assert.Equal(kind, match.Kind);
        Assert.Equal(model, match.ModelIndex);
        Assert.Equal(child, match.ChildIndex);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
