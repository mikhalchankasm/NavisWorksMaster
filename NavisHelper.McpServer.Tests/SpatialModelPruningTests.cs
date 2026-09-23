using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// `find_items_by_bbox` had one lever on a large federated model: raise `maxScannedItems`
/// to its 500 000 ceiling and hope to finish inside the host's ten second budget. Narrowing
/// the zone did not help, because the zone was read after an item had been scanned, and
/// neither did `sourceFileContains`, because the counter increments before every filter.
///
/// Pruning a whole model was the first lever that could reduce `scannedItemCount` at all;
/// skipping a subtree whose own box misses the zone, decided by this same test, is the
/// second. These pin when the prune is allowed to fire -- the plugin side, which needs a
/// live document, is verified on the rig by comparing `matchedItemCount` against a run
/// with pruning disabled.
/// </summary>
public sealed class SpatialModelPruningTests
{
    [Fact]
    public void AModelWhoseExtentsMissTheZoneIsPruned()
    {
        Assert.False(SpatialModelPruning.ModelExtentsCanHoldAMatch(
            Point(0, 0, 0), Point(10, 10, 10),
            Point(100, 100, 100), Point(110, 110, 110)));
    }

    [Fact]
    public void AModelWhoseExtentsOverlapTheZoneIsScanned()
    {
        Assert.True(SpatialModelPruning.ModelExtentsCanHoldAMatch(
            Point(0, 0, 0), Point(10, 10, 10),
            Point(5, 5, 5), Point(15, 15, 15)));
    }

    [Fact]
    public void TouchingAtAFaceCounts()
    {
        // Overlap is inclusive, matching how the per-item test treats `intersects`: a box
        // whose face lies exactly on the zone boundary is a match there, so the model
        // holding it must not be pruned here.
        Assert.True(SpatialModelPruning.ModelExtentsCanHoldAMatch(
            Point(0, 0, 0), Point(10, 10, 10),
            Point(10, 0, 0), Point(20, 10, 10)));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    public void OneAxisFailingToOverlapIsEnoughToPrune(double xShift, double yShift, double zShift)
    {
        // A separating axis is sufficient. Two of the three overlapping is not a partial
        // match, and reading the test as "mostly overlaps" would scan a model that provably
        // holds nothing.
        Assert.False(SpatialModelPruning.ModelExtentsCanHoldAMatch(
            Point(0, 0, 0), Point(10, 10, 10),
            Point(20 * xShift, 20 * yShift, 20 * zShift),
            Point(10 + 20 * xShift, 10 + 20 * yShift, 10 + 20 * zShift)));
    }

    [Fact]
    public void UnknownExtentsAreScannedRatherThanPruned()
    {
        // "We could not tell" must not be recorded as "nothing here". A prune is only worth
        // having because it is provably empty, and an unreadable model is not.
        Assert.True(SpatialModelPruning.ModelExtentsCanHoldAMatch(
            null, Point(10, 10, 10), Point(100, 100, 100), Point(110, 110, 110)));
        Assert.True(SpatialModelPruning.ModelExtentsCanHoldAMatch(
            Point(0, 0, 0), null, Point(100, 100, 100), Point(110, 110, 110)));
    }

    [Fact]
    public void NonFiniteExtentsAreScannedRatherThanPruned()
    {
        // An infinite or NaN coordinate makes every comparison meaningless rather than
        // false, so a naive overlap test would silently prune a model it knows nothing about.
        Assert.True(SpatialModelPruning.ModelExtentsCanHoldAMatch(
            Point(double.NaN, 0, 0), Point(10, 10, 10),
            Point(100, 100, 100), Point(110, 110, 110)));
        Assert.True(SpatialModelPruning.ModelExtentsCanHoldAMatch(
            Point(0, 0, 0), Point(double.PositiveInfinity, 10, 10),
            Point(100, 100, 100), Point(110, 110, 110)));
    }

    [Fact]
    public void AFileFilterPrunesAModelThatCannotSatisfyIt()
    {
        Assert.False(SpatialModelPruning.ModelFileCanSatisfyFilter(
            "D:\\models\\structure.nwc", "piping"));
        Assert.True(SpatialModelPruning.ModelFileCanSatisfyFilter(
            "D:\\models\\piping-east.nwc", "piping"));
    }

    [Fact]
    public void AFileFilterIsMatchedTheSameWayTheItemFilterMatchesIt()
    {
        // Case-insensitive substring, exactly as the per-item filter does it. A prune that
        // matched more strictly than the filter it stands in for would drop real matches.
        Assert.True(SpatialModelPruning.ModelFileCanSatisfyFilter(
            "D:\\models\\PIPING-East.nwc", "piping"));
        Assert.True(SpatialModelPruning.ModelFileCanSatisfyFilter(
            "D:\\models\\piping-east.nwc", "  PIPING  "));
    }

    [Fact]
    public void NoFilterPrunesNothing()
    {
        Assert.True(SpatialModelPruning.ModelFileCanSatisfyFilter("D:\\models\\structure.nwc", ""));
        Assert.True(SpatialModelPruning.ModelFileCanSatisfyFilter("D:\\models\\structure.nwc", null));
        Assert.True(SpatialModelPruning.ModelFileCanSatisfyFilter("D:\\models\\structure.nwc", "   "));
    }

    [Fact]
    public void AnUnknownFileNameIsScannedRatherThanPruned()
    {
        Assert.True(SpatialModelPruning.ModelFileCanSatisfyFilter(null, "piping"));
        Assert.True(SpatialModelPruning.ModelFileCanSatisfyFilter("", "piping"));
        Assert.True(SpatialModelPruning.ModelFileCanSatisfyFilter("   ", "piping"));
    }

    private static SpatialPoint Point(double x, double y, double z) =>
        new SpatialPoint { X = x, Y = y, Z = z };
}
