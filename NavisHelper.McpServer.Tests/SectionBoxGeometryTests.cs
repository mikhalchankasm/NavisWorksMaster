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
    public void FullyRotatedBox_UsesEdgeCrossEdgeSeparatingAxes()
    {
        var box = new SectionBoxGeometry
        {
            FormatVersion = 1,
            CoordinateSpace = "document_global",
            DocumentUnits = "meters",
            Center = V(-3.212657030784329, -0.7790297431818498, -1.2855791568402948),
            HalfExtents = V(1.737177819070303, 0.5724470344485427, 0.4613969260375419),
            Axes = new List<BoxVector3>
            {
                V(0.7657870946255331, -0.5997049058476767, 0.23221574366785322),
                V(0.6373199346267104, 0.7559876003631396, -0.14935209748974818),
                V(-0.08598503725875983, 0.2623676313865928, 0.9611294394451761),
            },
        };

        Assert.False(Intersects(
            box,
            -1.4852441139737163,
            -1.379392639857273,
            -1.9699139015953357,
            1.4852441139737163,
            1.379392639857273,
            1.9699139015953357));
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
    public void QuaternionNormalization_IsStableForLargeFiniteComponents()
    {
        var axes = SectionBoxGeometryRules.AxesFromQuaternion(1e308, 0, 0, 0);

        Assert.Equal(1, axes[0].X, 12);
        Assert.Equal(-1, axes[1].Y, 12);
        Assert.Equal(-1, axes[2].Z, 12);
        Assert.Equal(0, axes[0].Y, 12);
        SectionBoxGeometryRules.Validate(new SectionBoxGeometry
        {
            FormatVersion = 1,
            CoordinateSpace = "document_global",
            DocumentUnits = "meters",
            Center = V(0, 0, 0),
            HalfExtents = V(1, 1, 1),
            Axes = axes,
        });
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
        Assert.Equal(new[] { 2 }, first.OutsideIndices);
        Assert.Equal(first.KeepVisibleIndices, second.KeepVisibleIndices);
        Assert.Equal(first.HideIndices, second.HideIndices);
        Assert.Equal(2, first.WouldRevealItemCount);
        Assert.Equal(3, first.WouldChangeVisibilityItemCount);
        Assert.Equal(new[] { 0, 1 }, first.RevealIndices);
        Assert.Equal(new[] { 2 }, first.NewlyHiddenIndices);
        Assert.Equal(0, replay.WouldChangeVisibilityItemCount);
    }

    [Fact]
    public void ExecutionRules_DryRunAndPartialApplyNeverAuthorizeMutation()
    {
        Assert.False(BoxIsolationExecutionRules.ShouldApply(false, false, 0));
        Assert.False(BoxIsolationExecutionRules.ShouldApply(false, true, 0));
        Assert.False(BoxIsolationExecutionRules.ShouldApply(true, true, 0));
        Assert.True(BoxIsolationExecutionRules.ShouldApply(true, false, 0));
        Assert.False(BoxIsolationExecutionRules.IsPartial(false, false));
        Assert.True(BoxIsolationExecutionRules.IsPartial(true, false));
        Assert.True(BoxIsolationExecutionRules.IsPartial(false, true));
        Assert.False(BoxIsolationExecutionRules.HasTimedOut(9999, 10000));
        Assert.True(BoxIsolationExecutionRules.HasTimedOut(10000, 10000));
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
        Assert.Equal(new[] { 0 }, plan.KeepVisibleIndices);
        Assert.Empty(plan.HideIndices);
        Assert.Equal(new[] { 0 }, plan.UnclassifiedIndices);
    }

    [Fact]
    public void Planner_InvalidMinGreaterThanMaxLeaf_IsConservativelyKeptVisible()
    {
        var item = Item(-1, 5, 5, 5, 4, 4, 4, false);
        item.Unclassified = true;

        var plan = BoxIsolationPlanner.Build(
            AxisAlignedBox(0, 0, 0, 1, 1, 1),
            new List<BoxIsolationPlanItem> { item });

        Assert.Equal(new[] { 0 }, plan.UnclassifiedIndices);
        Assert.Equal(new[] { 0 }, plan.KeepVisibleIndices);
        Assert.Empty(plan.HideIndices);
        Assert.Empty(plan.NewlyHiddenIndices);
    }

    [Fact]
    public void Planner_UnclassifiedDescendantPreventsOutsideAncestorFromBeingHidden()
    {
        var invalidChild = Item(0, 5, 5, 5, 4, 4, 4, false);
        invalidChild.Unclassified = true;
        var plan = BoxIsolationPlanner.Build(
            AxisAlignedBox(0, 0, 0, 1, 1, 1),
            new List<BoxIsolationPlanItem>
            {
                Item(-1, 10, 10, 10, 11, 11, 11, false),
                invalidChild,
            });

        Assert.Equal(new[] { 0 }, plan.OutsideIndices);
        Assert.Equal(new[] { 1 }, plan.UnclassifiedIndices);
        Assert.Equal(new[] { 0, 1 }, plan.KeepVisibleIndices);
        Assert.Empty(plan.HideIndices);
        Assert.Equal(0, plan.WouldChangeVisibilityItemCount);
    }

    [Fact]
    public void Planner_AlreadyHiddenUnclassifiedItemAndAncestor_AreRevealed()
    {
        var invalidChild = Item(0, 5, 5, 5, 4, 4, 4, true);
        invalidChild.Unclassified = true;
        var plan = BoxIsolationPlanner.Build(
            AxisAlignedBox(0, 0, 0, 1, 1, 1),
            new List<BoxIsolationPlanItem>
            {
                Item(-1, 10, 10, 10, 11, 11, 11, true),
                invalidChild,
            });

        Assert.Equal(new[] { 0, 1 }, plan.RevealIndices);
        Assert.Equal(2, plan.WouldRevealItemCount);
        Assert.Equal(2, plan.WouldChangeVisibilityItemCount);
    }

    [Fact]
    public void Planner_OneUnclassifiedAmongMany_RemainsVisibleWithoutMakingPlanIncomplete()
    {
        var invalid = Item(-1, 3, 3, 3, 2, 2, 2, false);
        invalid.Unclassified = true;
        var items = new List<BoxIsolationPlanItem>
        {
            Item(-1, -0.5, -0.5, -0.5, 0.5, 0.5, 0.5, false),
            Item(-1, 5, 5, 5, 6, 6, 6, false),
            invalid,
            Item(-1, 7, 7, 7, 8, 8, 8, true),
        };

        var plan = BoxIsolationPlanner.Build(AxisAlignedBox(0, 0, 0, 1, 1, 1), items);

        Assert.Equal(items.Count, plan.IntersectingIndices.Count + plan.OutsideIndices.Count + plan.UnclassifiedIndices.Count);
        Assert.Contains(2, plan.KeepVisibleIndices);
        Assert.DoesNotContain(2, plan.HideIndices);
        Assert.Equal(new[] { 1, 3 }, plan.OutsideIndices);
        Assert.Equal(new[] { 1 }, plan.NewlyHiddenIndices);
        Assert.False(BoxIsolationExecutionRules.ShouldApply(true, partial: false, classificationErrorCount: 1));
    }

    [Fact]
    public void TraversalPolicy_ReadableOutsideParentPrunesWholeSubtree()
    {
        var disposition = BoxIsolationTraversalPolicy.Classify(
            boundsReadable: true,
            intersects: false,
            geometryStatusKnown: true,
            hasGeometry: false,
            hasChildren: true);

        Assert.Equal(BoxIsolationNodeDisposition.OutsideSubtree, disposition);
        Assert.False(BoxIsolationTraversalPolicy.ShouldDescend(disposition, hasChildren: true));
        Assert.False(BoxIsolationTraversalPolicy.ShouldPreserveCurrentVisibility(disposition));
        Assert.False(BoxIsolationTraversalPolicy.IsRealClassificationError(disposition));
    }

    [Fact]
    public void TraversalPolicy_OutsideParentPrunesIntersectingLookingChildByHierarchyContract()
    {
        var box = AxisAlignedBox(0, 0, 0, 1, 1, 1);
        Assert.True(Intersects(box, -0.5, -0.5, -0.5, 0.5, 0.5, 0.5));

        var parentDisposition = BoxIsolationTraversalPolicy.Classify(
            boundsReadable: true,
            intersects: false,
            geometryStatusKnown: true,
            hasGeometry: false,
            hasChildren: true);

        // The child fixture would intersect if evaluated independently, but the Autodesk API
        // contract says a ModelItem bounding box includes all children. Such a fixture cannot
        // exist below a readable outside parent at runtime, so descent is safely pruned.
        Assert.Equal(BoxIsolationNodeDisposition.OutsideSubtree, parentDisposition);
        Assert.False(BoxIsolationTraversalPolicy.ShouldDescend(parentDisposition, true));
    }

    [Fact]
    public void TraversalPolicy_IntersectingAndUnreadableParentsDescendSafely()
    {
        var intersecting = BoxIsolationTraversalPolicy.Classify(true, true, true, false, true);
        var structural = BoxIsolationTraversalPolicy.Classify(false, false, true, false, true);
        var realError = BoxIsolationTraversalPolicy.Classify(false, false, false, false, true);

        Assert.True(BoxIsolationTraversalPolicy.ShouldDescend(intersecting, true));
        Assert.True(BoxIsolationTraversalPolicy.ShouldDescend(structural, true));
        Assert.True(BoxIsolationTraversalPolicy.ShouldDescend(realError, true));
        Assert.True(BoxIsolationTraversalPolicy.ShouldPreserveCurrentVisibility(structural));
        Assert.False(BoxIsolationTraversalPolicy.ShouldPreserveCurrentVisibility(realError));
        Assert.False(BoxIsolationTraversalPolicy.IsRealClassificationError(structural));
        Assert.True(BoxIsolationTraversalPolicy.IsRealClassificationError(realError));
    }

    [Fact]
    public void TraversalPolicy_EmptyLeafIsPreservedWithoutClassificationError()
    {
        var empty = BoxIsolationTraversalPolicy.Classify(
            boundsReadable: false,
            intersects: false,
            geometryStatusKnown: true,
            hasGeometry: false,
            hasChildren: false);

        Assert.Equal(BoxIsolationNodeDisposition.EmptyLeaf, empty);
        Assert.False(BoxIsolationTraversalPolicy.ShouldDescend(empty, false));
        Assert.True(BoxIsolationTraversalPolicy.ShouldPreserveCurrentVisibility(empty));
        Assert.False(BoxIsolationTraversalPolicy.IsRealClassificationError(empty));
    }

    [Fact]
    public void TraversalAccounting_SeparatesScannedNodesFromPrunedBranchesAndHonorsLimit()
    {
        var accounting = new BoxIsolationTraversalAccounting(2);

        Assert.True(accounting.TryRegisterScannedItem());
        accounting.RecordPrunedSubtree(7);
        Assert.True(accounting.TryRegisterScannedItem());
        Assert.False(accounting.TryRegisterScannedItem());
        accounting.RecordPrunedSubtree(0);

        Assert.Equal(2, accounting.ScannedItemCount);
        Assert.Equal(1, accounting.PrunedSubtreeRootCount);
        Assert.Equal(7, accounting.PrunedDirectChildBranchCount);
    }

    [Fact]
    public void Planner_PrunedOutsideParentHidesOnlyParentAndIsIdempotent()
    {
        var box = AxisAlignedBox(0, 0, 0, 1, 1, 1);
        var first = BoxIsolationPlanner.Build(
            box,
            new List<BoxIsolationPlanItem> { PreclassifiedItem(-1, intersects: false) });
        var afterApply = PreclassifiedItem(-1, intersects: false);
        afterApply.WasHidden = true;
        var replay = BoxIsolationPlanner.Build(box, new List<BoxIsolationPlanItem> { afterApply });

        Assert.Equal(new[] { 0 }, first.HideIndices);
        Assert.Equal(new[] { 0 }, first.NewlyHiddenIndices);
        Assert.Equal(1, first.WouldChangeVisibilityItemCount);
        Assert.Equal(0, replay.WouldChangeVisibilityItemCount);
    }

    [Fact]
    public void Planner_StructuralContainerIsPreservedWithoutBecomingUnclassified()
    {
        var structural = PreclassifiedItem(-1, intersects: false);
        structural.PreserveCurrentVisibility = true;

        var plan = BoxIsolationPlanner.Build(
            AxisAlignedBox(0, 0, 0, 1, 1, 1),
            new List<BoxIsolationPlanItem> { structural });

        Assert.Equal(new[] { 0 }, plan.KeepVisibleIndices);
        Assert.Empty(plan.UnclassifiedIndices);
        Assert.Empty(plan.HideIndices);
    }

    [Fact]
    public void Planner_HiddenStructuralContainerStaysHiddenUnlessKeptDescendantRequiresReveal()
    {
        var hiddenStructural = PreclassifiedItem(-1, intersects: false);
        hiddenStructural.PreserveCurrentVisibility = true;
        hiddenStructural.WasHidden = true;
        var outsideChild = PreclassifiedItem(0, intersects: false);
        var keptChild = PreclassifiedItem(0, intersects: true);
        var box = AxisAlignedBox(0, 0, 0, 1, 1, 1);

        var withoutKeptDescendant = BoxIsolationPlanner.Build(
            box,
            new List<BoxIsolationPlanItem> { hiddenStructural, outsideChild });
        var withKeptDescendant = BoxIsolationPlanner.Build(
            box,
            new List<BoxIsolationPlanItem> { hiddenStructural, keptChild });

        Assert.Equal(new[] { 0, 1 }, withoutKeptDescendant.HideIndices);
        Assert.Empty(withoutKeptDescendant.RevealIndices);
        Assert.Equal(new[] { 0 }, withKeptDescendant.RevealIndices);
        Assert.Equal(new[] { 0, 1 }, withKeptDescendant.KeepVisibleIndices);
    }

    [Fact]
    public void TraversalPolicy_OutsideClassificationDoesNotDependOnReadableHierarchy()
    {
        var outsideWithoutKnownChildren = BoxIsolationTraversalPolicy.Classify(
            boundsReadable: true,
            intersects: false,
            geometryStatusKnown: false,
            hasGeometry: false,
            hasChildren: false);

        Assert.Equal(BoxIsolationNodeDisposition.OutsideSubtree, outsideWithoutKnownChildren);
        Assert.False(BoxIsolationTraversalPolicy.IsRealClassificationError(outsideWithoutKnownChildren));
    }

    [Fact]
    public void ExecutionRules_RealClassificationErrorsRejectApply()
    {
        Assert.True(BoxIsolationExecutionRules.ShouldApply(true, partial: false, classificationErrorCount: 0));
        Assert.False(BoxIsolationExecutionRules.ShouldApply(true, partial: false, classificationErrorCount: 1));
        Assert.False(BoxIsolationExecutionRules.ShouldApply(true, partial: true, classificationErrorCount: 0));
    }

    [Fact]
    public void IsolationLimits_DefaultToHardMaximum_AndTruncationStillRejectsApply()
    {
        Assert.Equal(500000, SectionBoxIsolationLimits.DefaultMaxScannedItems);
        Assert.Equal(500000, SectionBoxIsolationLimits.MaximumMaxScannedItems);
        Assert.False(BoxIsolationExecutionRules.ShouldApply(true, partial: true, classificationErrorCount: 0));
    }

    [Fact]
    public void DurationPolicy_ValidatesDefaultBoundsAndCoherentUpstreamBudget()
    {
        Assert.Equal(60, SectionBoxIsolationLimits.ValidateMaxDurationSeconds(null));
        Assert.Equal(1, SectionBoxIsolationLimits.ValidateMaxDurationSeconds(1));
        Assert.Equal(480, SectionBoxIsolationLimits.ValidateMaxDurationSeconds(480));
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionBoxIsolationLimits.ValidateMaxDurationSeconds(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionBoxIsolationLimits.ValidateMaxDurationSeconds(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionBoxIsolationLimits.ValidateMaxDurationSeconds(481));

        Assert.Equal(160000, SectionBoxIsolationLimits.GetBridgeRequestTimeoutMilliseconds(60));
        Assert.Equal(580000, SectionBoxIsolationLimits.GetBridgeRequestTimeoutMilliseconds(480));
        Assert.Equal(165000, SectionBoxIsolationLimits.GetRecommendedClientTimeoutMilliseconds(60));
        Assert.Equal(585000, SectionBoxIsolationLimits.GetRecommendedClientTimeoutMilliseconds(480));
        Assert.True(
            SectionBoxIsolationLimits.GetBridgeRequestTimeoutMilliseconds(480) -
            ProtocolConstants.HostTransportResponseMarginMilliseconds <=
            ProtocolConstants.MaximumHostRequestTimeoutMilliseconds);
    }

    [Fact]
    public void DurationGuard_CompletionWinsAtBoundaryButRemainingWorkTimesOut()
    {
        var clock = new FakeClock { ElapsedMilliseconds = 1000 };
        var guard = new SectionBoxIsolationDurationGuard(1, clock);

        Assert.False(guard.ShouldStop(hasRemainingWork: false));
        Assert.True(guard.ShouldStop(hasRemainingWork: true));
    }

    [Fact]
    public void BoundedPlanner_TimesOutBeforeRemainingWorkWithoutAuthorizingApply()
    {
        var clock = new SequenceClock(999, 1000);
        var guard = new SectionBoxIsolationDurationGuard(1, clock);
        var items = new List<BoxIsolationPlanItem>
        {
            PreclassifiedItem(-1, intersects: true),
            PreclassifiedItem(-1, intersects: false),
        };

        var result = BoxIsolationPlanner.BuildBounded(AxisAlignedBox(0, 0, 0, 1, 1, 1), items, guard);

        Assert.True(result.TimedOut);
        Assert.Equal(1, result.ClassificationProcessedItemCount);
        Assert.Equal(0, result.VisibilityProcessedItemCount);
        Assert.False(BoxIsolationExecutionRules.ShouldApply(true, partial: result.TimedOut, classificationErrorCount: 0));
    }

    [Theory]
    [InlineData(73579)]
    [InlineData(111599)]
    public void BoundedPlanner_OwnerModelSizesHonorFakeClockPolicyBelowDefaultLimit(int itemCount)
    {
        var clock = new FakeClock { ElapsedMilliseconds = 59999 };
        var guard = new SectionBoxIsolationDurationGuard(
            SectionBoxIsolationLimits.DefaultMaxDurationSeconds,
            clock);
        var items = Enumerable.Range(0, itemCount)
            .Select(_ => PreclassifiedItem(-1, intersects: false))
            .ToList();

        var result = BoxIsolationPlanner.BuildBounded(
            AxisAlignedBox(0, 0, 0, 1, 1, 1),
            items,
            guard);

        Assert.False(result.TimedOut);
        Assert.Equal(itemCount, result.ClassificationProcessedItemCount);
        Assert.Equal(itemCount, result.VisibilityProcessedItemCount);
        Assert.Equal(itemCount, result.Plan.OutsideIndices.Count);
        Assert.Equal(itemCount, result.Plan.HideIndices.Count);
        clock.ElapsedMilliseconds = 60000;
        Assert.False(guard.ShouldStop(hasRemainingWork: false));
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

    private static BoxIsolationPlanItem PreclassifiedItem(int parent, bool intersects)
    {
        return new BoxIsolationPlanItem
        {
            ParentIndex = parent,
            BoundsMin = V(0, 0, 0),
            BoundsMax = V(0, 0, 0),
            HasPrecomputedIntersection = true,
            PrecomputedIntersects = intersects,
        };
    }

    private sealed class FakeClock : ISectionBoxIsolationClock
    {
        public long ElapsedMilliseconds { get; set; }
    }

    private sealed class SequenceClock : ISectionBoxIsolationClock
    {
        private readonly Queue<long> _values;
        private long _last;

        public SequenceClock(params long[] values)
        {
            _values = new Queue<long>(values);
            _last = values.Length == 0 ? 0 : values[values.Length - 1];
        }

        public long ElapsedMilliseconds
        {
            get
            {
                if (_values.Count > 0)
                    _last = _values.Dequeue();
                return _last;
            }
        }
    }

    private static BoxVector3 V(double x, double y, double z)
    {
        return new BoxVector3 { X = x, Y = y, Z = z };
    }
}
