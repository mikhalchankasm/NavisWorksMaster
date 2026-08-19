using System.Text.Json;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashTransferPlanHelperTests
{
    [Fact]
    public void JsonRoundTrip_PreservesPerTestSettingsAndPortableSetPaths()
    {
        var plan = Plan(new ClashTestTransferDefinition
        {
            Name = "Set vs set",
            TestType = "clearance",
            ToleranceMm = 25.4,
            A = SetSide("A", "Folder/Поиск A", "ss:0042", ClashTransferSideKinds.SearchSet),
            B = SetSide("B", "Folder/Set B", "ss:0099", ClashTransferSideKinds.SelectionSet),
            IgnoreRules = new ClashNativeIgnoreRules { SameFile = true },
        });
        ClashTransferPlanHelper.RefreshSupport(plan.Tests[0]);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var json = JsonSerializer.Serialize(plan, options);
        var parsed = JsonSerializer.Deserialize<ClashTestTransferPlan>(json, options);

        var pair = Assert.Single(ClashTransferPlanHelper.ToPairs(parsed, false));
        Assert.Equal("Set vs set", pair.Name);
        Assert.Equal("clearance", pair.TestType);
        Assert.Equal(25.4, pair.ToleranceMm);
        Assert.Equal("Folder/Поиск A", pair.A.Path);
        Assert.Null(pair.A.ItemId);
        Assert.True(pair.IgnoreRules.SameFile);
    }

    [Fact]
    public void ModelRootAndSet_ConvertToExistingPairContract()
    {
        var definition = new ClashTestTransferDefinition
        {
            Name = "Root vs set",
            TestType = "hard",
            A = new ClashTestTransferSide { Side = "A", Kind = ClashTransferSideKinds.ModelRoot, RootName = "Model A", SourceFile = @"C:\Models\A.nwd", Supported = true },
            B = SetSide("B", "Sets/Mechanical", "ss:1", ClashTransferSideKinds.SelectionSet),
        };
        ClashTransferPlanHelper.RefreshSupport(definition);

        var pair = Assert.Single(ClashTransferPlanHelper.ToPairs(Plan(definition), false));
        Assert.Equal("Model A", pair.A.RootName);
        Assert.Equal(@"C:\Models\A.nwd", pair.A.SourceFile);
        Assert.Equal("Sets/Mechanical", pair.B.Path);
    }

    [Fact]
    public void UnsupportedExplicitSnapshotIsNeverConverted()
    {
        var definition = new ClashTestTransferDefinition
        {
            Name = "Snapshot",
            TestType = "hard",
            A = new ClashTestTransferSide { Side = "A", Kind = ClashTransferSideKinds.Unsupported, Supported = false },
            B = SetSide("B", "Sets/B", null, ClashTransferSideKinds.SelectionSet),
        };
        ClashTransferPlanHelper.RefreshSupport(definition);

        Assert.False(definition.Supported);
        Assert.Empty(ClashTransferPlanHelper.ToPairs(Plan(definition), false));
    }

    [Fact]
    public void OmittedToleranceRemainsUnsetForTargetDefault()
    {
        var definition = new ClashTestTransferDefinition
        {
            Name = "Default tolerance",
            TestType = "hard",
            A = SetSide("A", "Sets/A", null, ClashTransferSideKinds.SelectionSet),
            B = SetSide("B", "Sets/B", null, ClashTransferSideKinds.SelectionSet),
        };
        ClashTransferPlanHelper.RefreshSupport(definition);

        var pair = Assert.Single(ClashTransferPlanHelper.ToPairs(Plan(definition), false));
        Assert.Null(pair.ToleranceMm);
    }

    [Fact]
    public void WrongSchemaOrVersionIsRejected()
    {
        Assert.Throws<ClashTransferParseException>(() => ClashTransferPlanHelper.Validate(new ClashTestTransferPlan { Schema = "other" }));
        Assert.Throws<ClashTransferParseException>(() => ClashTransferPlanHelper.Validate(new ClashTestTransferPlan { Version = 2 }));
    }

    private static ClashTestTransferPlan Plan(ClashTestTransferDefinition definition) => new() { Tests = new List<ClashTestTransferDefinition> { definition } };

    private static ClashTestTransferSide SetSide(string side, string path, string itemId, string kind) => new()
    {
        Side = side,
        Kind = kind,
        Path = path,
        Name = Path.GetFileName(path),
        ItemId = itemId,
        Supported = true,
    };
}
