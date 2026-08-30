using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class WorldMarkerLifecyclePlanHelperTests
{
    [Fact]
    public void PlanReplace_AppendsBeforeDeletingAndOrdersIndicesDescending()
    {
        var fixture = Fixture();

        var newRevision = WorldMarkerArtifactPathPolicy.CreateRevisionId(DateTime.UtcNow, Guid.NewGuid());
        var newArtifact = WorldMarkerArtifactPathPolicy.BuildArtifactPath(fixture.Root, fixture.MarkerA, newRevision);

        var plan = WorldMarkerLifecyclePlanHelper.PlanReplace(fixture.MarkerA, fixture.Models, fixture.Root, newArtifact);

        Assert.Equal(WorldMarkerLifecyclePlanHelper.Replace, plan.Operation);
        Assert.True(plan.AppendNewBeforeDeletingExisting);
        Assert.Equal(newArtifact, plan.NewArtifactPath);
        Assert.Equal(new[] { 8, 2 }, plan.DeleteModelIndices);
        Assert.Equal(2, plan.CleanupArtifactPaths.Count);
    }

    [Fact]
    public void PlanReplace_NeverSchedulesNewArtifactForCleanup()
    {
        var fixture = Fixture();
        var newRevision = WorldMarkerArtifactPathPolicy.CreateRevisionId(DateTime.UtcNow, Guid.NewGuid());
        var newArtifact = WorldMarkerArtifactPathPolicy.BuildArtifactPath(fixture.Root, fixture.MarkerA, newRevision);
        fixture.Models.Add(new WorldMarkerModelDescriptor
        {
            ModelIndex = 12,
            MarkerId = fixture.MarkerA,
            ArtifactPath = Path.Combine(Path.GetDirectoryName(newArtifact)!.ToUpperInvariant(), ".", Path.GetFileName(newArtifact)),
        });

        Assert.True(WorldMarkerArtifactPathPolicy.IsCleanupCandidate(fixture.Root, fixture.Models[^1].ArtifactPath));

        var plan = WorldMarkerLifecyclePlanHelper.PlanReplace(fixture.MarkerA, fixture.Models, fixture.Root, newArtifact);

        Assert.DoesNotContain(plan.CleanupArtifactPaths, path => string.Equals(path, newArtifact, StringComparison.OrdinalIgnoreCase));
        Assert.Null(plan.TargetHidden);
    }

    [Fact]
    public void PlanReplace_WithNoExistingModelsOnlySchedulesAppend()
    {
        var fixture = Fixture();
        var revision = WorldMarkerArtifactPathPolicy.CreateRevisionId(DateTime.UtcNow, Guid.NewGuid());
        var artifact = WorldMarkerArtifactPathPolicy.BuildArtifactPath(fixture.Root, fixture.MarkerA, revision);

        var plan = WorldMarkerLifecyclePlanHelper.PlanReplace(fixture.MarkerA, Array.Empty<WorldMarkerModelDescriptor>(), fixture.Root, artifact);

        Assert.True(plan.AppendNewBeforeDeletingExisting);
        Assert.Equal(artifact, plan.NewArtifactPath);
        Assert.Empty(plan.TargetModels);
        Assert.Empty(plan.DeleteModelIndices);
        Assert.Empty(plan.CleanupArtifactPaths);
    }

    [Fact]
    public void PlanReplace_RejectsOutsideAndWrongExtensionArtifacts()
    {
        var fixture = Fixture();
        var revision = WorldMarkerArtifactPathPolicy.CreateRevisionId(DateTime.UtcNow, Guid.NewGuid());
        var fileName = fixture.MarkerA + "--" + revision;

        Assert.Throws<ArgumentException>(() => WorldMarkerLifecyclePlanHelper.PlanReplace(
            fixture.MarkerA,
            fixture.Models,
            fixture.Root,
            Path.Combine(fixture.Root + "-outside", fileName + ".dxf")));
        Assert.Throws<ArgumentException>(() => WorldMarkerLifecyclePlanHelper.PlanReplace(
            fixture.MarkerA,
            fixture.Models,
            fixture.Root,
            Path.Combine(fixture.Root, fileName + ".txt")));
    }

    [Fact]
    public void PlanDelete_ReportsMissingAndNeverCleansUnsafeArtifact()
    {
        var fixture = Fixture();
        var missing = WorldMarkerInputPolicy.CreateMarkerId("missing");
        fixture.Models.Add(new WorldMarkerModelDescriptor
        {
            ModelIndex = 11,
            MarkerId = fixture.MarkerB,
            ArtifactPath = Path.Combine(Path.GetTempPath(), "outside", "user.dxf"),
        });

        var plan = WorldMarkerLifecyclePlanHelper.PlanDelete(new[] { fixture.MarkerA, fixture.MarkerB, missing }, fixture.Models, fixture.Root);

        Assert.Equal(new[] { 11, 8, 5, 2 }, plan.DeleteModelIndices);
        Assert.Equal(new[] { missing }, plan.MissingMarkerIds);
        Assert.DoesNotContain(plan.CleanupArtifactPaths, path => path.Contains("outside", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlanDelete_DeduplicatesRequestedMarkerIds()
    {
        var fixture = Fixture();

        var plan = WorldMarkerLifecyclePlanHelper.PlanDelete(
            new[] { fixture.MarkerA, fixture.MarkerA, fixture.MarkerB },
            fixture.Models,
            fixture.Root);

        Assert.Equal(new[] { 8, 5, 2 }, plan.DeleteModelIndices);
        Assert.Empty(plan.MissingMarkerIds);
    }

    [Fact]
    public void PlanVisibilityReturnsAscendingTargetsWithoutDeletePlan()
    {
        var fixture = Fixture();

        var plan = WorldMarkerLifecyclePlanHelper.PlanVisibility(WorldMarkerLifecyclePlanHelper.Hide, new[] { fixture.MarkerA }, fixture.Models);

        Assert.Equal(new[] { 2, 8 }, plan.TargetModels.Select(model => model.ModelIndex));
        Assert.True(plan.TargetHidden);
        Assert.Empty(plan.DeleteModelIndices);
        Assert.Empty(plan.CleanupArtifactPaths);
    }

    [Fact]
    public void PlanVisibility_ExplicitlySetsShowTargetHiddenFalse()
    {
        var fixture = Fixture();

        var plan = WorldMarkerLifecyclePlanHelper.PlanVisibility(WorldMarkerLifecyclePlanHelper.Show, new[] { fixture.MarkerA }, fixture.Models);

        Assert.False(plan.TargetHidden);
    }

    [Fact]
    public void PlanVisibility_ReportsMissingMarkers()
    {
        var fixture = Fixture();
        var missing = WorldMarkerInputPolicy.CreateMarkerId("missing");

        var plan = WorldMarkerLifecyclePlanHelper.PlanVisibility(
            WorldMarkerLifecyclePlanHelper.Hide,
            new[] { missing },
            fixture.Models);

        Assert.True(plan.TargetHidden);
        Assert.Empty(plan.TargetModels);
        Assert.Equal(new[] { missing }, plan.MissingMarkerIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("delete")]
    public void PlanVisibility_RejectsMissingOrInvalidOperation(string operation)
    {
        var fixture = Fixture();

        Assert.Throws<ArgumentException>(() => WorldMarkerLifecyclePlanHelper.PlanVisibility(
            operation,
            new[] { fixture.MarkerA },
            fixture.Models));
    }

    [Fact]
    public void PlanningRejectsDuplicateModelIndices()
    {
        var fixture = Fixture();
        fixture.Models.Add(new WorldMarkerModelDescriptor
        {
            ModelIndex = 2,
            MarkerId = fixture.MarkerB,
            ArtifactPath = fixture.Models[0].ArtifactPath,
        });

        Assert.Throws<ArgumentException>(() => WorldMarkerLifecyclePlanHelper.PlanDelete(new[] { fixture.MarkerA }, fixture.Models, fixture.Root));
    }

    [Fact]
    public void PlanReplace_RejectsNewArtifactForAnotherMarker()
    {
        var fixture = Fixture();
        var revision = WorldMarkerArtifactPathPolicy.CreateRevisionId(DateTime.UtcNow, Guid.NewGuid());
        var wrongArtifact = WorldMarkerArtifactPathPolicy.BuildArtifactPath(fixture.Root, fixture.MarkerB, revision);

        Assert.Throws<ArgumentException>(() =>
            WorldMarkerLifecyclePlanHelper.PlanReplace(fixture.MarkerA, fixture.Models, fixture.Root, wrongArtifact));
    }

    private static LifecycleFixture Fixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "world-marker-lifecycle");
        var markerA = WorldMarkerInputPolicy.CreateMarkerId("A");
        var markerB = WorldMarkerInputPolicy.CreateMarkerId("B");
        var revision1 = WorldMarkerArtifactPathPolicy.CreateRevisionId(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var revision2 = WorldMarkerArtifactPathPolicy.CreateRevisionId(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), Guid.Parse("22222222-2222-2222-2222-222222222222"));
        return new LifecycleFixture
        {
            Root = root,
            MarkerA = markerA,
            MarkerB = markerB,
            Models =
            {
                new WorldMarkerModelDescriptor { ModelIndex = 2, MarkerId = markerA, ArtifactPath = WorldMarkerArtifactPathPolicy.BuildArtifactPath(root, markerA, revision1) },
                new WorldMarkerModelDescriptor { ModelIndex = 8, MarkerId = markerA, ArtifactPath = WorldMarkerArtifactPathPolicy.BuildArtifactPath(root, markerA, revision2) },
                new WorldMarkerModelDescriptor { ModelIndex = 5, MarkerId = markerB, ArtifactPath = string.Empty },
            },
        };
    }

    private sealed class LifecycleFixture
    {
        public string Root { get; set; }
        public string MarkerA { get; set; }
        public string MarkerB { get; set; }
        public List<WorldMarkerModelDescriptor> Models { get; } = new();
    }
}
