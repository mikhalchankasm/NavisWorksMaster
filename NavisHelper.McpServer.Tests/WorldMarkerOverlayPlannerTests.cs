using NavisHelper.Agent.Contracts;
using static NavisHelper.Agent.Contracts.WorldMarkerOverlayPlanner;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class WorldMarkerOverlayPlannerTests
{
    private static WorldMarkerOverlaySpec Spec(
        string name, double x = 0, double y = 0, double z = 0, string style = null, string group = null,
        double? size = null, int? sizePx = null, int? alpha = null, WorldMarkerPole pole = null,
        WorldMarkerColor color = null)
    {
        return new WorldMarkerOverlaySpec
        {
            Name = name, X = x, Y = y, Z = z, Style = style, Group = group, Color = color,
            Size = size, SizePx = sizePx, Alpha = alpha, Pole = pole,
        };
    }

    private static WorldMarkerOverlaySetRequest SetRequest(string mode, params WorldMarkerOverlaySpec[] markers)
    {
        return new WorldMarkerOverlaySetRequest { Mode = mode, Markers = markers.ToList() };
    }

    private static WorldMarkerOverlayManageRequest ManageRequest(
        string operation, string[] names = null, string[] ids = null, string group = null)
    {
        return new WorldMarkerOverlayManageRequest
        {
            Operation = operation,
            Names = (names ?? new string[0]).ToList(),
            Ids = (ids ?? new string[0]).ToList(),
            Group = group,
        };
    }

    private static WorldMarkerOverlaySpec[] Many(int count)
    {
        var markers = new WorldMarkerOverlaySpec[count];
        for (var i = 0; i < count; i++)
            markers[i] = Spec("M" + i, i, i, 0);
        return markers;
    }

    private static WorldMarkerOverlaySnapshot Seed(params WorldMarkerOverlaySpec[] markers)
    {
        return Set(null, SetRequest(null, markers)).Snapshot;
    }

    [Fact]
    public void Set_UpsertStoresNewMarkersAsVisibleWithDefaults()
    {
        var (snapshot, result) = Set(null, SetRequest(null, Spec("Alpha", 10, 20, 5, group: " g1 ", alpha: 200)));

        Assert.True(result.Accepted);
        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Replaced);
        Assert.Equal(1, result.MarkerCount);

        var marker = Assert.Single(snapshot.Markers);
        Assert.Equal(WorldMarkerInputPolicy.CreateMarkerId("Alpha"), marker.MarkerId);
        Assert.Equal("Alpha", marker.Name);
        Assert.Equal("g1", marker.Group);
        Assert.Equal(WorldMarkerStyles.Target, marker.Style);
        Assert.Equal(DefaultSizePx, marker.SizePx);
        Assert.Null(marker.WorldSize);
        Assert.Equal(200, marker.Alpha);
        Assert.True(marker.Visible);
    }

    [Fact]
    public void Set_UpsertReplacesSameNameInPlaceAndKeepsOtherMarkers()
    {
        var seed = Seed(Spec("A", 0, 0, 0), Spec("B", 1, 1, 1));

        var (next, result) = Set(seed, SetRequest(null, Spec("A", 9, 9, 9)));

        Assert.True(result.Accepted);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Replaced);
        Assert.Equal(2, next.Count);
        Assert.Equal("A", next.Markers[0].Name);
        Assert.Equal(9, next.Markers[0].X);
        Assert.NotSame(seed.Markers[0], next.Markers[0]);
        Assert.Same(seed.Markers[1], next.Markers[1]);
    }

    [Fact]
    public void Set_ReplaceAllSwapsTheWholeSnapshot()
    {
        var seed = Seed(Spec("A"), Spec("B"));

        var (next, result) = Set(seed, SetRequest("replace_all", Spec("C", 2, 2, 2), Spec("D", 3, 3, 3), Spec("E", 4, 4, 4)));

        Assert.True(result.Accepted);
        Assert.Equal(3, result.Created);
        Assert.Equal(0, result.Replaced);
        Assert.Equal(3, result.MarkerCount);
        Assert.Equal(new[] { "C", "D", "E" }, next.Markers.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void Set_RefusesToExceedTheCapAndLeavesTheSnapshotUnchanged()
    {
        var seed = Set(null, SetRequest("replace_all", Many(499))).Snapshot;
        var (swapped, swapResult) = Set(seed, SetRequest("replace_all", Many(501)));

        Assert.False(swapResult.Accepted);
        Assert.True(swapResult.CapExceeded);
        Assert.Contains("500", swapResult.RefusalReason);
        Assert.Equal(0, swapResult.Created);
        Assert.Equal(499, swapResult.MarkerCount);
        Assert.Same(seed, swapped);

        var (refused, result) = Set(seed, SetRequest(null, Spec("N1"), Spec("N2")));

        Assert.False(result.Accepted);
        Assert.True(result.CapExceeded);
        Assert.Equal(0, result.Created);
        Assert.Equal(499, result.MarkerCount);
        Assert.Same(seed, refused);

        var (full, accepted) = Set(seed, SetRequest(null, Spec("N1")));
        Assert.True(accepted.Accepted);
        Assert.Equal(500, full.Count);
    }

    [Fact]
    public void Set_ReplacingExistingMarkersDoesNotGrowTheStore()
    {
        var full = Set(null, SetRequest("replace_all", Many(500))).Snapshot;

        var (next, result) = Set(full, SetRequest(null, Spec("M250", 9, 9, 9)));

        Assert.True(result.Accepted);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Replaced);
        Assert.Equal(500, next.Count);
        Assert.Equal(9, next.Markers[250].X);
    }

    [Fact]
    public void Set_RejectsInvalidAndDuplicateMarkers()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => Set(null, SetRequest(null, Spec("A", style: "blob"))));
        Assert.ThrowsAny<ArgumentException>(
            () => Set(null, SetRequest(null, new WorldMarkerOverlaySpec
            {
                Name = "A", Y = 0, Z = 0,
            })));
        var error = Assert.Throws<ArgumentException>(
            () => Set(null, SetRequest(null, Spec("Marker"), Spec(" marker "))));
        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Set_RejectsOutOfRangeSizePxAndAlpha()
    {
        foreach (var invalid in new[]
        {
            Spec("A", sizePx: 4), Spec("A", sizePx: 201), Spec("A", alpha: -1), Spec("A", alpha: 256),
        })
            Assert.ThrowsAny<ArgumentException>(() => Set(null, SetRequest(null, invalid)));

        var (accepted, _) = Set(null, SetRequest(null, Spec("A", sizePx: 5, alpha: 0), Spec("B", sizePx: 200, alpha: 255)));
        Assert.Equal(2, accepted.Count);
    }

    [Fact]
    public void SetAndManage_RejectMalformedRequests()
    {
        Assert.ThrowsAny<ArgumentException>(() => Set(null, new WorldMarkerOverlaySetRequest()));
        Assert.ThrowsAny<ArgumentException>(() => Set(null, SetRequest("merge", Spec("A"))));
        Assert.ThrowsAny<ArgumentException>(() => Manage(Seed(Spec("A")), ManageRequest("frobnicate")));
    }

    [Fact]
    public void Manage_HidesAndShowsByNamesAndReportsMissingOnes()
    {
        var seed = Seed(Spec("Marker A", 0, 0, 0), Spec("Marker B", 1, 1, 1));

        var (hidden, hideResult) = Manage(seed, ManageRequest("hide", names: new[] { "Marker A ", "Marker A", "ghost" }));

        Assert.Equal(1, hideResult.Hidden);
        Assert.Equal(2, hideResult.MarkerCount);
        Assert.Equal(new[] { "ghost" }, hideResult.MissingNames);
        Assert.False(hidden.Markers[0].Visible);
        Assert.True(hidden.Markers[1].Visible);
        Assert.True(seed.Markers[0].Visible);

        var (shown, showResult) = Manage(hidden, ManageRequest("show", names: new[] { "Marker A" }));
        Assert.Equal(1, showResult.Shown);
        Assert.True(shown.Markers[0].Visible);

        var (again, secondHide) = Manage(shown, ManageRequest("hide", names: new[] { "Marker A" }));
        Assert.Equal(1, secondHide.Hidden);

        var (_, thirdHide) = Manage(again, ManageRequest("hide", names: new[] { "Marker A" }));
        Assert.Equal(0, thirdHide.Hidden);
    }

    [Fact]
    public void Manage_SelectsByIdsCaseInsensitivelyAndReportsUnknownOnes()
    {
        var seed = Seed(Spec("A"), Spec("B"));
        var idA = WorldMarkerInputPolicy.CreateMarkerId("A").ToUpperInvariant();

        var (next, result) = Manage(seed,
            ManageRequest("hide", ids: new[] { idA, "wm-0000000000000000" }, names: new[] { "ghost1", "ghost2" }));

        Assert.Equal(1, result.Hidden);
        Assert.Single(result.MissingIds);
        Assert.Equal(new[] { "ghost1", "ghost2" }, result.MissingNames);
        Assert.False(next.Markers[0].Visible);
        Assert.True(next.Markers[1].Visible);

        var (unchanged, noop) = Manage(seed, ManageRequest("hide", names: new[] { "ghost1", "ghost2" }, ids: new[] { "wm-1111111111111111" }));
        Assert.Equal(0, noop.Hidden);
        Assert.Same(seed, unchanged);
    }

    [Fact]
    public void Manage_HideByGroupAffectsOnlyThatGroup()
    {
        var seed = Seed(Spec("A", group: "g1"), Spec("B", group: " g1 "), Spec("C", group: "g2"), Spec("D"));

        var (next, result) = Manage(seed, ManageRequest("hide", group: "g1"));

        Assert.Equal(2, result.Hidden);
        Assert.Empty(result.MissingNames);
        Assert.Equal(new[] { false, false, true, true }, next.Markers.Select(m => m.Visible).ToArray());
    }

    [Fact]
    public void Manage_DeleteByNamesRemovesOnlySelectedMarkers()
    {
        var seed = Seed(Spec("A"), Spec("B"));

        var (next, result) = Manage(seed, ManageRequest("delete", names: new[] { "A", "ghost" }));

        Assert.Equal(1, result.Deleted);
        Assert.Equal(new[] { "ghost" }, result.MissingNames);
        Assert.Equal(new[] { "B" }, next.Markers.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void Manage_HideWithoutSelectorsHidesEveryStoredMarker()
    {
        var seed = Seed(Spec("A"), Spec("B", group: "g"), Spec("C"));

        var (next, result) = Manage(seed, ManageRequest("hide"));

        Assert.True(result.Accepted);
        Assert.Equal(3, result.Hidden);
        Assert.Equal(3, result.MarkerCount);
        Assert.All(next.Markers, marker => Assert.False(marker.Visible));
        Assert.All(seed.Markers, marker => Assert.True(marker.Visible));
    }

    [Fact]
    public void Manage_ShowWithoutSelectorsShowsEveryStoredMarker()
    {
        var seed = Seed(Spec("A"), Spec("B"), Spec("C"));
        var (hidden, hideResult) = Manage(seed, ManageRequest("hide", names: new[] { "A", "B", "C" }));
        Assert.Equal(3, hideResult.Hidden);

        var (shown, result) = Manage(hidden, ManageRequest("show"));

        Assert.True(result.Accepted);
        Assert.Equal(3, result.Shown);
        Assert.Equal(3, result.MarkerCount);
        Assert.All(shown.Markers, marker => Assert.True(marker.Visible));
        Assert.All(hidden.Markers, marker => Assert.False(marker.Visible));
    }

    [Fact]
    public void Manage_RefusesDeleteWithoutSelectorsAndPointsAtClear()
    {
        var seed = Seed(Spec("A"), Spec("B", group: "g"));

        var (next, result) = Manage(seed, ManageRequest("delete"));

        Assert.False(result.Accepted);
        Assert.Equal("delete", result.Operation);
        Assert.Contains("clear", result.RefusalReason, StringComparison.OrdinalIgnoreCase);
        Assert.Same(seed, next);
        Assert.Equal(2, result.MarkerCount);
        Assert.Equal(0, result.Hidden + result.Shown + result.Deleted);
        Assert.Equal(new[] { "A", "B" }, seed.Markers.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void Manage_ClearWithoutSelectorsRemovesEverything()
    {
        var seed = Seed(Spec("A"), Spec("B", group: "g"), Spec("C"));

        var (next, result) = Manage(seed, ManageRequest("clear"));

        Assert.Equal(3, result.Deleted);
        Assert.Equal(0, result.MarkerCount);
        Assert.Empty(next.Markers);
        Assert.True(VisibleBounds(next).IsEmpty);
    }

    [Fact]
    public void Manage_ClearByGroupRemovesOnlyThatGroup()
    {
        var seed = Seed(Spec("A", group: "g1"), Spec("B", group: "g2"), Spec("C", group: "g1"));

        var (next, result) = Manage(seed, ManageRequest("clear", group: "g1"));

        Assert.Equal(2, result.Deleted);
        Assert.Equal(new[] { "B" }, next.Markers.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void Manage_RefusesBlankSelectorsAndLeavesTheSnapshotUnchanged()
    {
        var seed = Seed(Spec("A"), Spec("B", group: "g"));
        foreach (var request in new[]
        {
            ManageRequest("clear", names: new[] { "" }), ManageRequest("hide", ids: new[] { " " }),
            ManageRequest("show", group: " "), ManageRequest("delete", names: new[] { "A", "" }),
        })
        {
            var (next, result) = Manage(seed, request);
            Assert.False(result.Accepted);
            Assert.NotNull(result.RefusalReason);
            Assert.Same(seed, next);
            Assert.Equal(0, result.Hidden + result.Shown + result.Deleted);
        }
    }

    [Fact]
    public void Manage_MatchesNamesUsingTheStoreCaseInsensitiveIdentity()
    {
        var seed = Seed(Spec("Alpha"), Spec("B"));

        var (next, result) = Manage(seed, ManageRequest("hide", names: new[] { " ALPHA " }));

        Assert.True(result.Accepted);
        Assert.Equal(1, result.Hidden);
        Assert.Empty(result.MissingNames);
        Assert.False(next.Markers[0].Visible);
    }

    [Fact]
    public void StoredMarkersAndTheirColoursCannotBeChangedAfterCreation()
    {
        var color = new WorldMarkerColor { R = 1, G = 2, B = 3 };
        var (snapshot, _) = Set(null, SetRequest(null, Spec("A", color: color)));
        var (hidden, _) = Manage(snapshot, ManageRequest("hide", names: new[] { "A" }));

        color.R = 9;
        hidden.Markers[0].Color.G = 9;
        snapshot.Markers[0].Color.B = 9;

        Assert.Equal(1, snapshot.Markers[0].Color.R);
        Assert.Equal(2, snapshot.Markers[0].Color.G);
        Assert.Equal(3, snapshot.Markers[0].Color.B);
    }

    [Fact]
    public void SetAndManage_NeverMutateTheInputSnapshot()
    {
        var seed = Seed(Spec("A", 1, 2, 3, group: "g"), Spec("B", 4, 5, 6));
        var originalA = seed.Markers[0];
        var originalB = seed.Markers[1];

        var (afterReplace, _) = Set(seed, SetRequest(null, Spec("A", 9, 9, 9, group: "g")));
        var (afterHide, _) = Manage(afterReplace, ManageRequest("hide", group: "g"));
        var (afterDelete, _) = Manage(afterHide, ManageRequest("delete", names: new[] { "B" }));

        Assert.Equal(2, seed.Count);
        Assert.Equal(1, originalA.X);
        Assert.True(originalA.Visible);
        Assert.Equal(4, originalB.X);
        Assert.True(originalB.Visible);
        Assert.NotSame(originalA, afterReplace.Markers[0]);
        Assert.Equal(9, afterReplace.Markers[0].X);
        Assert.True(afterReplace.Markers[0].Visible);
        Assert.False(afterHide.Markers[0].Visible);
        Assert.Single(afterDelete.Markers);
        Assert.Equal(originalA.MarkerId, afterDelete.Markers[0].MarkerId);
    }

    [Fact]
    public void VisibleBounds_IsEmptyWhenNothingIsVisible()
    {
        Assert.True(VisibleBounds(WorldMarkerOverlaySnapshot.Empty).IsEmpty);

        var (hidden, _) = Manage(Seed(Spec("A", 1, 1, 1)), ManageRequest("hide", names: new[] { "A" }));
        Assert.True(VisibleBounds(hidden).IsEmpty);
    }

    [Fact]
    public void VisibleBounds_UnionsPointPoleAndWorldSizeOfVisibleMarkers()
    {
        var seed = Set(null, SetRequest(null,
            Spec("P", 10, 20, 5, size: 4, pole: new WorldMarkerPole { BaseZ = 0, TopZ = 8 }),
            Spec("Q", 0, 0, 0), Spec("Far", -100, -100, -100))).Snapshot;
        var (visible, _) = Manage(seed, ManageRequest("hide", names: new[] { "Far" }));

        var bounds = VisibleBounds(visible);

        Assert.False(bounds.IsEmpty);
        Assert.Equal(0, bounds.MinX);
        Assert.Equal(0, bounds.MinY);
        Assert.Equal(0, bounds.MinZ);
        Assert.Equal(14, bounds.MaxX);
        Assert.Equal(24, bounds.MaxY);
        Assert.Equal(9, bounds.MaxZ);
    }
}
