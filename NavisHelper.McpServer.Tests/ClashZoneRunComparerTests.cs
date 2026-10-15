using System;
using System.Collections.Generic;
using System.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance test for comparing two clash-zone runs (docs/CLASH_ZONES_PLAN.md, section 5, PR 5).
public sealed class ClashZoneRunComparerTests
{
    [Fact]
    public void Compare_FirstRun_EveryZoneIsNewWithTracksInZoneOrder()
    {
        var result = ClashZoneRunComparer.Compare(null, new[]
        {
            Zone("Z-0002", 1000, 0, 0, 300),
            Zone("Z-0001", 0, 0, 0, 300),
            Zone("Z-0003", 9000, 0, 0, 300),
        }, null);

        Assert.Equal(new[] { "Z-0001", "Z-0002", "Z-0003" }, result.Current.Select(e => e.ZoneId));
        Assert.Equal(new[] { "T-0001", "T-0002", "T-0003" }, result.Current.Select(e => e.TrackId));
        Assert.All(result.Current, e => Assert.Equal(ClashZoneCompareStates.New, e.CompareState));
        Assert.All(result.Current, e => Assert.Null(e.PreviousZoneId));
        Assert.Empty(result.Disappeared);
        Assert.Equal(4, result.NextTrackNumber);
    }

    [Fact]
    public void Compare_IdenticalRuns_EveryZonePersistsOnItsTrackByItemEvidence()
    {
        var previous = new[] { Zone("Z-0001", 0, 0, 0, 300, "T-0004", items: "a|b"), Zone("Z-0002", 5000, 0, 0, 300, "T-0009", items: "c|d") };
        var current = new[] { Zone("Z-0001", 0, 0, 0, 300, items: "a|b"), Zone("Z-0002", 5000, 0, 0, 300, items: "c|d") };

        var result = ClashZoneRunComparer.Compare(previous, current, null);

        Assert.Equal(new[] { "T-0004", "T-0009" }, result.Current.Select(e => e.TrackId));
        Assert.All(result.Current, e =>
        {
            Assert.Equal(ClashZoneCompareStates.Persisting, e.CompareState);
            Assert.Equal(ClashZoneCompareEvidence.Items, e.Evidence);
            Assert.Equal(0, e.ShiftMm, 9);
            Assert.Equal(1, e.Score, 9);
        });
        Assert.Equal(new[] { "Z-0001", "Z-0002" }, result.Current.Select(e => e.PreviousZoneId));
        Assert.Empty(result.Disappeared);
        Assert.Equal(10, result.NextTrackNumber);
    }

    [Theory]
    [InlineData(120.0, 600.0, "persisting")]
    [InlineData(500.0, 600.0, "moved")]
    [InlineData(90.0, 200.0, "persisting")]
    [InlineData(110.0, 200.0, "moved")]
    public void Compare_StillThresholdIsTheLargerOf100MmAndAQuarterOfThePreviousRadius(double shift, double radius, string state)
    {
        var result = ClashZoneRunComparer.Compare(
            new[] { Zone("Z-0001", 0, 0, 0, radius, "T-0001", items: "a|b") },
            new[] { Zone("Z-0001", shift, 0, 0, radius, items: "a|b") },
            null);

        var entry = Assert.Single(result.Current);
        Assert.Equal(state, entry.CompareState);
        Assert.Equal("T-0001", entry.TrackId);
        Assert.Equal(shift, entry.ShiftMm, 9);
    }

    [Fact]
    public void Compare_WithoutItemsFallsBackToGeometryWithinTheSamePartitionOnly()
    {
        var previous = new[]
        {
            Zone("Z-0001", 0, 0, 0, 600, "T-0001", key: "PIPE|BEAM"),
            Zone("Z-0002", 8000, 0, 0, 600, "T-0002", key: "PIPE|BEAM"),
        };
        var current = new[]
        {
            Zone("Z-0001", 50, 0, 0, 600, key: "PIPE|BEAM"),
            Zone("Z-0002", 8000, 0, 0, 600, key: "DUCT|WALL"),
        };

        var result = ClashZoneRunComparer.Compare(previous, current, null);

        var matched = result.Current[0];
        Assert.Equal(ClashZoneCompareStates.Persisting, matched.CompareState);
        Assert.Equal(ClashZoneCompareEvidence.Geometry, matched.Evidence);
        Assert.Equal(1 - 50.0 / 1200.0, matched.Score, 9);
        Assert.Equal("T-0001", matched.TrackId);

        var other = result.Current[1];
        Assert.Equal(ClashZoneCompareStates.New, other.CompareState);
        Assert.Equal("T-0003", other.TrackId);
        Assert.Null(other.Evidence);

        var gone = Assert.Single(result.Disappeared);
        Assert.Equal("Z-0002", gone.PreviousZoneId);
        Assert.Null(gone.ZoneId);
        Assert.Equal("T-0002", gone.TrackId);
        Assert.Equal(ClashZoneCompareStates.Disappeared, gone.CompareState);
    }

    [Theory]
    [InlineData(2500.0, 2000.0)] // spheres overlap, but the shift exceeds maxShiftMm 2000
    [InlineData(1500.0, 700.0)]  // within maxShiftMm, but the spheres do not touch
    public void Compare_CandidatesNeedTouchingSpheresAndAShiftWithinTheLimit(double shift, double radius)
    {
        var result = ClashZoneRunComparer.Compare(
            new[] { Zone("Z-0001", 0, 0, 0, radius, "T-0001", items: "a|b") },
            new[] { Zone("Z-0001", shift, 0, 0, radius, items: "a|b") },
            null);

        Assert.Equal(ClashZoneCompareStates.New, Assert.Single(result.Current).CompareState);
        Assert.Equal("T-0001", Assert.Single(result.Disappeared).TrackId);
        Assert.Equal("T-0002", result.Current[0].TrackId);
    }

    [Fact]
    public void Compare_ItemEvidenceBeatsACloserGeometricMatchAndTheLoserIsASplit()
    {
        var previous = new[] { Zone("Z-0001", 0, 0, 0, 1000, "T-0001", items: "a|b,a|c") };
        var current = new[]
        {
            Zone("Z-0001", 10, 0, 0, 1000),
            Zone("Z-0002", 800, 0, 0, 1000, items: "a|b"),
        };

        var result = ClashZoneRunComparer.Compare(previous, current, null);

        var byItems = result.Current.Single(e => e.ZoneId == "Z-0002");
        Assert.Equal("T-0001", byItems.TrackId);
        Assert.Equal(ClashZoneCompareEvidence.Items, byItems.Evidence);
        Assert.Equal(0.5, byItems.Score, 9);
        Assert.Equal(ClashZoneCompareStates.Moved, byItems.CompareState);

        var split = result.Current.Single(e => e.ZoneId == "Z-0001");
        Assert.Equal(ClashZoneCompareStates.New, split.CompareState);
        Assert.Equal("Z-0001", split.SplitFrom);
        Assert.Equal("T-0002", split.TrackId);
        Assert.Empty(result.Disappeared);
    }

    [Fact]
    public void Compare_TwoPreviousZonesMergingIntoOneKeepTheBestTrackAndRecordTheMerge()
    {
        var previous = new[]
        {
            Zone("Z-0001", -300, 0, 0, 400, "T-0001"),
            Zone("Z-0002", 100, 0, 0, 400, "T-0002"),
        };
        var current = new[] { Zone("Z-0001", 0, 0, 0, 800) };

        var result = ClashZoneRunComparer.Compare(previous, current, null);

        var merged = Assert.Single(result.Current);
        Assert.Equal("T-0002", merged.TrackId);
        Assert.Equal("Z-0002", merged.PreviousZoneId);
        Assert.Equal(new[] { "Z-0001" }, merged.MergedFrom);
        Assert.Equal("Z-0001", Assert.Single(result.Disappeared).PreviousZoneId);
    }

    [Theory]
    [InlineData(10, 7, 500.0, 500.0, true, false)]
    [InlineData(10, 13, 500.0, 500.0, false, true)]
    [InlineData(10, 10, 500.0, 650.0, false, true)]
    [InlineData(10, 10, 500.0, 390.0, true, false)]
    [InlineData(10, 9, 500.0, 560.0, false, false)]
    [InlineData(10, 5, 500.0, 700.0, true, true)]
    public void Compare_ShrunkAndGrewFlagChangesAbove20Percent(int prevCount, int currCount, double prevRadius, double currRadius, bool shrunk, bool grew)
    {
        var result = ClashZoneRunComparer.Compare(
            new[] { Zone("Z-0001", 0, 0, 0, prevRadius, "T-0001", items: "a|b", count: prevCount) },
            new[] { Zone("Z-0001", 0, 0, 0, currRadius, items: "a|b", count: currCount) },
            null);

        var entry = Assert.Single(result.Current);
        Assert.Equal(shrunk, entry.Shrunk);
        Assert.Equal(grew, entry.Grew);
    }

    [Fact]
    public void Compare_NewTracksStartAboveTheHighWaterMark()
    {
        var options = new ClashZoneCompareOptions { MinimumNextTrackNumber = 50 };

        var result = ClashZoneRunComparer.Compare(
            new[] { Zone("Z-0001", 0, 0, 0, 300, "T-0007", items: "a|b") },
            new[] { Zone("Z-0001", 0, 0, 0, 300, items: "a|b"), Zone("Z-0002", 9000, 0, 0, 300) },
            options);

        Assert.Equal(new[] { "T-0007", "T-0050" }, result.Current.Select(e => e.TrackId));
        Assert.Equal(51, result.NextTrackNumber);
    }

    [Fact]
    public void Compare_DefaultOptionsAreTheOwnerAcceptedNumbers()
    {
        var options = new ClashZoneCompareOptions();

        Assert.Equal(2000, options.MaxShiftMm);
        Assert.Equal(100, options.StillMinMm);
        Assert.Equal(0.25, options.StillRadiusFraction);
        Assert.Equal(0.2, options.ChangeFraction);
        Assert.Equal(1, options.MinimumNextTrackNumber);
    }

    [Fact]
    public void Compare_DoesNotDependOnInputOrder()
    {
        var random = new Random(3);
        var previous = Enumerable.Range(1, 30)
            .Select(i => Zone("Z-" + i.ToString("D4"), random.Next(0, 20000), random.Next(0, 3000), 0, 400 + random.Next(0, 600), "T-" + (i * 2).ToString("D4"),
                key: "k" + (i % 3), items: random.Next(2) == 0 ? "i" + (i % 7) + "|j" : null))
            .ToList();
        var current = Enumerable.Range(1, 30)
            .Select(i => Zone("Z-" + i.ToString("D4"), random.Next(0, 20000), random.Next(0, 3000), 0, 400 + random.Next(0, 600),
                key: "k" + (i % 3), items: random.Next(2) == 0 ? "i" + (i % 7) + "|j" : null))
            .ToList();

        var a = ClashZoneRunComparer.Compare(previous, current, null);
        var b = ClashZoneRunComparer.Compare(Enumerable.Reverse(previous).ToList(), Enumerable.Reverse(current).ToList(), null);

        Assert.Equal(Describe(a), Describe(b));
        Assert.Equal(30, a.Current.Count);
        Assert.Equal(30, a.Current.Count(e => e.CompareState == ClashZoneCompareStates.New) + a.Current.Count(e => e.PreviousZoneId != null));
        Assert.Equal(a.Current.Count(e => e.PreviousZoneId != null) + a.Disappeared.Count, 30);
    }

    [Fact]
    public void Compare_RejectsInvalidInput()
    {
        var current = new[] { Zone("Z-0001", 0, 0, 0, 300) };

        Assert.Throws<ArgumentNullException>(() => ClashZoneRunComparer.Compare(null, null, null));
        Assert.Throws<ArgumentException>(() => ClashZoneRunComparer.Compare(new[] { Zone("Z-0001", 0, 0, 0, 300) }, current, null));
        Assert.Throws<ArgumentException>(() => ClashZoneRunComparer.Compare(new[] { Zone("Z-0001", 0, 0, 0, 300, "track-1") }, current, null));
        Assert.Throws<ArgumentException>(() => ClashZoneRunComparer.Compare(null, new[] { Zone("Z-0001", 0, 0, 0, 300), Zone("Z-0001", 9, 0, 0, 300) }, null));
        Assert.Throws<ArgumentException>(() => ClashZoneRunComparer.Compare(null, new[] { Zone("Z-0001", 0, 0, 0, -1) }, null));
    }

    [Fact]
    public void Compare_RejectsSharedPreviousTracksAndNonFiniteThresholds()
    {
        var current = new[] { Zone("Z-0001", 0, 0, 0, 300) };

        Assert.Throws<ArgumentException>(() => ClashZoneRunComparer.Compare(
            new[] { Zone("Z-0001", 0, 0, 0, 300, "T-0001"), Zone("Z-0002", 900, 0, 0, 300, "T-0001") }, current, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClashZoneRunComparer.Compare(null, current, new ClashZoneCompareOptions { MaxShiftMm = double.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClashZoneRunComparer.Compare(null, current, new ClashZoneCompareOptions { StillMinMm = double.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClashZoneRunComparer.Compare(null, current, new ClashZoneCompareOptions { StillRadiusFraction = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClashZoneRunComparer.Compare(null, current, new ClashZoneCompareOptions { ChangeFraction = 1 }));
    }

    private static string Describe(ClashZoneComparison comparison)
    {
        return string.Join(";", comparison.Current.Concat(comparison.Disappeared).Select(e => string.Join(",",
            e.ZoneId, e.PreviousZoneId, e.TrackId, e.CompareState, e.Evidence, e.ShiftMm.ToString("R"), e.Score.ToString("R"),
            e.Shrunk, e.Grew, e.SplitFrom, string.Join("+", e.MergedFrom ?? new List<string>()))))
            + "|" + comparison.NextTrackNumber;
    }

    private static ClashZoneRunZone Zone(string id, double x, double y, double z, double radius, string track = null,
        string key = "A|B", string items = null, int count = 1)
    {
        return new ClashZoneRunZone
        {
            ZoneId = id,
            TrackId = track,
            PartitionKey = key,
            X = x,
            Y = y,
            Z = z,
            RadiusMm = radius,
            ClashCount = count,
            ItemPairs = items == null ? new List<string>() : items.Split(',').ToList(),
        };
    }
}
