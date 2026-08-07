using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SectionBoxGeometryTests
{
    [Fact]
    public void AxisAlignedBox_ClassifiesIntersectionSeparationAndBoundaryTouch()
    {
        var box = AxisAlignedBox(0, 0, 0, 1, 2, 3);

        Assert.True(Intersects(box, -0.5, -0.5, -0.5, 0.5, 0.5, 0.5));
        Assert.False(Intersects(box, 2, 0, 0, 3, 1, 1));
        Assert.True(Intersects(box, 1, -0.25, -0.25, 2, 0.25, 0.25));
    }

    [Fact]
    public void RotatedTranslatedBox_UsesOrientedVolumeNotGlobalAabb()
    {
        var halfAngle = Math.PI / 8.0;
        var box = new SectionBoxGeometry
        {
            FormatVersion = 1,
            CoordinateSpace = "document_global",
            DocumentUnits = "meters",
            Center = V(10, -5, 2),
            HalfExtents = V(2, 0.5, 1),
            Axes = SectionBoxGeometryRules.AxesFromQuaternion(0, 0, Math.Sin(halfAngle), Math.Cos(halfAngle)),
        };

        Assert.True(Intersects(box, 9.8, -5.2, 1.8, 10.2, -4.8, 2.2));
        Assert.False(Intersects(box, 11.55, -3.45, 1.8, 11.75, -3.25, 2.2));
    }

    [Fact]
    public void Validation_RejectsInvalidNumbersExtentsAxesAndRotation()
    {
        var nonFinite = AxisAlignedBox(0, 0, 0, 1, 1, 1);
        nonFinite.Center.X = double.NaN;
        var negativeExtent = AxisAlignedBox(0, 0, 0, 1, 1, 1);
        negativeExtent.HalfExtents.Y = -1;
        var degenerateAxes = AxisAlignedBox(0, 0, 0, 1, 1, 1);
        degenerateAxes.Axes[1] = V(1, 0, 0);

        Assert.Throws<ArgumentException>(() => SectionBoxGeometryRules.Validate(nonFinite));
        Assert.Throws<ArgumentException>(() => SectionBoxGeometryRules.Validate(negativeExtent));
        Assert.Throws<ArgumentException>(() => SectionBoxGeometryRules.Validate(degenerateAxes));
        Assert.Throws<ArgumentException>(() => SectionBoxGeometryRules.AxesFromQuaternion(0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => SectionBoxGeometryRules.AxesFromQuaternion(double.PositiveInfinity, 0, 0, 1));
    }

    [Fact]
    public void Planner_KeepsIntersectingItemsAndAncestors_AndIsIdempotent()
    {
        var items = new List<BoxIsolationPlanItem>
        {
            Item(-1, -10, -10, -10, 10, 10, 10, true),
            Item(0, -0.5, -0.5, -0.5, 0.5, 0.5, 0.5, true),
            Item(0, 5, 5, 5, 6, 6, 6, false),
        };
        var box = AxisAlignedBox(0, 0, 0, 1, 1, 1);

        var first = BoxIsolationPlanner.Build(box, items);
        var second = BoxIsolationPlanner.Build(box, items);
        var afterApply = new List<BoxIsolationPlanItem>
        {
            Item(-1, -10, -10, -10, 10, 10, 10, false),
            Item(0, -0.5, -0.5, -0.5, 0.5, 0.5, 0.5, false),
            Item(0, 5, 5, 5, 6, 6, 6, true),
        };
        var replay = BoxIsolationPlanner.Build(box, afterApply);

        Assert.Equal(new[] { 0, 1 }, first.KeepVisibleIndices);
        Assert.Equal(new[] { 2 }, first.HideIndices);
        Assert.Equal(first.KeepVisibleIndices, second.KeepVisibleIndices);
        Assert.Equal(first.HideIndices, second.HideIndices);
        Assert.Equal(2, first.WouldRevealItemCount);
        Assert.Equal(3, first.WouldChangeVisibilityItemCount);
        Assert.Equal(0, replay.WouldChangeVisibilityItemCount);
    }

    [Fact]
    public void ExecutionRules_DryRunAndPartialApplyNeverAuthorizeMutation()
    {
        Assert.False(BoxIsolationExecutionRules.ShouldApply(false, false));
        Assert.False(BoxIsolationExecutionRules.ShouldApply(false, true));
        Assert.False(BoxIsolationExecutionRules.ShouldApply(true, true));
        Assert.True(BoxIsolationExecutionRules.ShouldApply(true, false));
    }

    [Fact]
    public void Planner_UnclassifiedBoundsAreNotCollapsedIntoWorldOrigin()
    {
        var box = AxisAlignedBox(0, 0, 0, 10, 10, 10);
        var items = new List<BoxIsolationPlanItem>
        {
            new()
            {
                ParentIndex = -1,
                BoundsMin = V(0, 0, 0),
                BoundsMax = V(0, 0, 0),
                Unclassified = true,
            },
        };

        var plan = BoxIsolationPlanner.Build(box, items);

        Assert.Empty(plan.IntersectingIndices);
        Assert.Empty(plan.KeepVisibleIndices);
        Assert.Empty(plan.HideIndices);
        Assert.Equal(new[] { 0 }, plan.UnclassifiedIndices);
    }

    private static bool Intersects(SectionBoxGeometry box, double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        return SectionBoxGeometryRules.IntersectsAabb(box, V(minX, minY, minZ), V(maxX, maxY, maxZ));
    }

    private static SectionBoxGeometry AxisAlignedBox(double x, double y, double z, double ex, double ey, double ez)
    {
        return new SectionBoxGeometry
        {
            FormatVersion = 1,
            CoordinateSpace = "document_global",
            DocumentUnits = "meters",
            Center = V(x, y, z),
            HalfExtents = V(ex, ey, ez),
            Axes = new List<BoxVector3> { V(1, 0, 0), V(0, 1, 0), V(0, 0, 1) },
        };
    }

    private static BoxIsolationPlanItem Item(int parent, double minX, double minY, double minZ, double maxX, double maxY, double maxZ, bool hidden)
    {
        return new BoxIsolationPlanItem
        {
            ParentIndex = parent,
            BoundsMin = V(minX, minY, minZ),
            BoundsMax = V(maxX, maxY, maxZ),
            WasHidden = hidden,
        };
    }

    private static BoxVector3 V(double x, double y, double z)
    {
        return new BoxVector3 { X = x, Y = y, Z = z };
    }
}
