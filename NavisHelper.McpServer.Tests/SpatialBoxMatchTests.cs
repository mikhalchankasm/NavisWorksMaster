using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// The zone match of `find_items_by_bbox`, now a pure function on the points the
/// walk read once. These pin the arithmetic the walk used to do straight against
/// the Navisworks box: one test method per mode for the accepting case, edges
/// inclusive on every axis, and one axis enough to reject.
/// </summary>
public sealed class SpatialBoxMatchTests
{
    [Fact]
    public void ABoxOverlappingTheZoneIntersects()
    {
        Assert.True(MatchesZone(
            SpatialSearchOptionsHelper.Intersects, Point(2, 2, 2), Point(8, 8, 8), null));
    }

    [Theory]
    [InlineData(10, 2, 2, 20, 8, 8)]
    [InlineData(2, 10, 2, 8, 20, 8)]
    [InlineData(2, 2, 10, 8, 8, 20)]
    [InlineData(-10, 2, 2, 0, 8, 8)]
    [InlineData(2, -10, 2, 8, 0, 8)]
    [InlineData(2, 2, -10, 8, 8, 0)]
    public void ABoxTouchingTheZoneOnAFaceStillIntersects(
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        Assert.True(MatchesZone(
            SpatialSearchOptionsHelper.Intersects, Point(minX, minY, minZ), Point(maxX, maxY, maxZ), null));
    }

    [Theory]
    [InlineData(11, 2, 2, 20, 8, 8)]
    [InlineData(2, 11, 2, 8, 20, 8)]
    [InlineData(2, 2, 11, 8, 8, 20)]
    [InlineData(-20, 2, 2, -1, 8, 8)]
    [InlineData(2, -20, 2, 8, -1, 8)]
    [InlineData(2, 2, -20, 8, 8, -1)]
    public void ABoxOutsideTheZoneOnOneAxisDoesNotIntersect(
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        Assert.False(MatchesZone(
            SpatialSearchOptionsHelper.Intersects, Point(minX, minY, minZ), Point(maxX, maxY, maxZ), null));
    }

    [Fact]
    public void ABoxInsideTheZoneIsContained()
    {
        Assert.True(MatchesZone(
            SpatialSearchOptionsHelper.Contains, Point(2, 2, 2), Point(8, 8, 8), null));
    }

    [Fact]
    public void ABoxExactlyFillingTheZoneIsContained()
    {
        Assert.True(MatchesZone(
            SpatialSearchOptionsHelper.Contains, Point(0, 0, 0), Point(10, 10, 10), null));
    }

    [Theory]
    [InlineData(2, 2, 2, 11, 8, 8)]
    [InlineData(2, 2, 2, 8, 11, 8)]
    [InlineData(2, 2, 2, 8, 8, 11)]
    [InlineData(-1, 2, 2, 8, 8, 8)]
    [InlineData(2, -1, 2, 8, 8, 8)]
    [InlineData(2, 2, -1, 8, 8, 8)]
    public void ABoxStickingOutOnOneAxisIsNotContained(
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        Assert.False(MatchesZone(
            SpatialSearchOptionsHelper.Contains, Point(minX, minY, minZ), Point(maxX, maxY, maxZ), null));
    }

    [Fact]
    public void ACenterInsideTheZoneMatches()
    {
        Assert.True(MatchesZone(
            SpatialSearchOptionsHelper.Center, Point(2, 2, 2), Point(8, 8, 8), Point(5, 5, 5)));
    }

    [Theory]
    [InlineData(0, 5, 5)]
    [InlineData(10, 5, 5)]
    [InlineData(5, 0, 5)]
    [InlineData(5, 10, 5)]
    [InlineData(5, 5, 0)]
    [InlineData(5, 5, 10)]
    public void ACenterOnTheZoneEdgeMatches(double x, double y, double z)
    {
        Assert.True(MatchesZone(
            SpatialSearchOptionsHelper.Center, Point(2, 2, 2), Point(8, 8, 8), Point(x, y, z)));
    }

    [Theory]
    [InlineData(11, 5, 5)]
    [InlineData(-1, 5, 5)]
    [InlineData(5, 11, 5)]
    [InlineData(5, -1, 5)]
    [InlineData(5, 5, 11)]
    [InlineData(5, 5, -1)]
    public void ACenterOutsideTheZoneOnOneAxisDoesNotMatch(double x, double y, double z)
    {
        Assert.False(MatchesZone(
            SpatialSearchOptionsHelper.Center, Point(2, 2, 2), Point(8, 8, 8), Point(x, y, z)));
    }

    [Fact]
    public void AnUnknownModeFallsBackToIntersects()
    {
        Assert.True(MatchesZone(
            "not-a-mode", Point(10, 2, 2), Point(20, 8, 8), null));
        Assert.True(MatchesZone(
            null, Point(10, 2, 2), Point(20, 8, 8), null));
    }

    [Fact]
    public void CornersThatWereNeverReadNeverMatch()
    {
        Assert.False(MatchesZone(SpatialSearchOptionsHelper.Intersects, null, null, null));
        Assert.False(MatchesZone(SpatialSearchOptionsHelper.Contains, null, null, null));
        Assert.False(MatchesZone(SpatialSearchOptionsHelper.Center, Point(2, 2, 2), Point(8, 8, 8), null));
    }

    private static bool MatchesZone(string matchMode, SpatialPoint itemMin, SpatialPoint itemMax, SpatialPoint itemCenter)
    {
        return SpatialBoxMatch.Matches(itemMin, itemMax, itemCenter, Point(0, 0, 0), Point(10, 10, 10), matchMode);
    }

    private static SpatialPoint Point(double x, double y, double z) =>
        new SpatialPoint { X = x, Y = y, Z = z };
}
