using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance test for the pure part of clash_zones_markers (docs/CLASH_ZONES_PLAN.md, section 6, PR 7).
public sealed class ClashZoneMarkerModelPlannerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "proj");
    private static readonly string Run1 = Path.Combine(Root, "clash_zones", "run-1", "zones.ifc");
    private static readonly string Run2 = Path.Combine(Root, "clash_zones", "run-2", "zones.ifc");

    [Fact]
    public void Append_AddsTheMarkerFileAndRemovesNothing()
    {
        var plan = ClashZoneMarkerModelPlanner.Plan(Models(Host()), "append", Run1, null);

        Assert.True(plan.Append);
        Assert.Equal(Run1, plan.MarkerFilePath);
        Assert.Empty(plan.RemoveIndices);
        Assert.Empty(plan.StyleIndices);
    }

    [Fact]
    public void Append_AcceptsARunFolderAndResolvesItsMarkerFile()
    {
        var plan = ClashZoneMarkerModelPlanner.Plan(Models(Host()), "APPEND", Path.GetDirectoryName(Run1), null);

        Assert.Equal("append", plan.Operation);
        Assert.Equal(Run1, plan.MarkerFilePath);
    }

    [Fact]
    public void Append_RefusesAFileThatIsAlreadyLoaded()
    {
        var error = Assert.Throws<ArgumentException>(() => ClashZoneMarkerModelPlanner.Plan(Models(Host(), Model(1, Run1.ToUpperInvariant())), "append", Run1, null));

        Assert.Contains("replace", error.Message);
    }

    [Fact]
    public void Replace_RemovesEveryLoadedMarkerModelHighestIndexFirstThenAppends()
    {
        var models = Models(Host(), Model(1, Run1), Model(2, Path.Combine(Root, "steel.nwc")), Model(3, Run2));
        var run3 = Path.Combine(Root, "clash_zones", "run-3", "zones.ifc");

        var plan = ClashZoneMarkerModelPlanner.Plan(models, "replace", run3, null);

        Assert.Equal(new[] { 3, 1 }, plan.RemoveIndices);
        Assert.True(plan.Append);
        Assert.Equal(run3, plan.MarkerFilePath);
    }

    [Fact]
    public void Remove_WithoutAPathRemovesOnlyMarkerModels()
    {
        var models = Models(Host(), Model(1, Run1), Model(2, Path.Combine(Root, "zones-review.nwc")), Model(3, Run2));

        var plan = ClashZoneMarkerModelPlanner.Plan(models, "remove", null, null);

        Assert.Equal(new[] { 3, 1 }, plan.RemoveIndices);
        Assert.False(plan.Append);
    }

    [Fact]
    public void Remove_WithAPathRemovesOnlyThatRun()
    {
        var plan = ClashZoneMarkerModelPlanner.Plan(Models(Host(), Model(1, Run1), Model(2, Run2)), "remove", Path.GetDirectoryName(Run2), null);

        Assert.Equal(new[] { 2 }, plan.RemoveIndices);
    }

    [Fact]
    public void Remove_MatchesTheSourceFileNameWhenTheLoadedNameDiffers()
    {
        var cached = new ClashZoneLoadedModel { Index = 1, FileName = Path.Combine(Root, "cache", "zones.ifc.nwc"), SourceFileName = Run1 };

        var plan = ClashZoneMarkerModelPlanner.Plan(Models(Host(), cached), "remove", Run1, null);

        Assert.Equal(new[] { 1 }, plan.RemoveIndices);
    }

    [Fact]
    public void Remove_NeverRemovesTheFirstModel()
    {
        var plan = ClashZoneMarkerModelPlanner.Plan(Models(Model(0, Run1), Model(1, Run2)), "remove", null, null);

        Assert.Equal(new[] { 1 }, plan.RemoveIndices);
        Assert.Contains(plan.Warnings, w => w.Contains("first model"));
    }

    [Fact]
    public void Remove_NothingLoadedIsAWarningNotAnError()
    {
        var plan = ClashZoneMarkerModelPlanner.Plan(Models(Host()), "remove", null, null);

        Assert.Empty(plan.RemoveIndices);
        Assert.Contains(plan.Warnings, w => w.Contains("No clash-zone marker model"));
    }

    [Fact]
    public void Style_TargetsMarkerModelsWithAValidTransparency()
    {
        var plan = ClashZoneMarkerModelPlanner.Plan(Models(Host(), Model(1, Run1), Model(2, Run2)), "style", null, 0.4);

        Assert.Equal(new[] { 1, 2 }, plan.StyleIndices);
        Assert.Equal(0.4, plan.Transparency);
        Assert.Empty(plan.RemoveIndices);
        Assert.False(plan.Append);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void Style_RequiresATransparencyBetweenZeroAndOne(double? transparency)
    {
        Assert.Throws<ArgumentException>(() => ClashZoneMarkerModelPlanner.Plan(Models(Host(), Model(1, Run1)), "style", null, transparency));
    }

    [Theory]
    [InlineData("append", "")]
    [InlineData("replace", null)]
    [InlineData("append", "C:\\proj\\clash_zones\\run-1\\zones.nwd")]
    [InlineData("hide", "C:\\proj\\clash_zones\\run-1")]
    public void Plan_RejectsMissingPathsWrongExtensionsAndUnknownOperations(string operation, string path)
    {
        Assert.Throws<ArgumentException>(() => ClashZoneMarkerModelPlanner.Plan(Models(Host()), operation, path, null));
    }

    [Fact]
    public void Plan_ListsTheLoadedMarkerModels()
    {
        var plan = ClashZoneMarkerModelPlanner.Plan(Models(Host(), Model(1, Run1), Model(2, Path.Combine(Root, "steel.nwc"))), "remove", null, null);

        var loaded = Assert.Single(plan.LoadedMarkerModels);
        Assert.Equal(1, loaded.Index);
        Assert.Equal(Run1, loaded.FileName);
    }

    private static List<ClashZoneLoadedModel> Models(params ClashZoneLoadedModel[] models)
    {
        return models.ToList();
    }

    private static ClashZoneLoadedModel Host()
    {
        return Model(0, Path.Combine(Root, "site.nwd"));
    }

    private static ClashZoneLoadedModel Model(int index, string fileName)
    {
        return new ClashZoneLoadedModel { Index = index, FileName = fileName, SourceFileName = fileName };
    }
}
