using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class WorldMarkerInputPolicyTests
{
    [Fact]
    public void NormalizeBatch_AppliesDocumentUnitAndMarkerDefaults()
    {
        var plan = WorldMarkerInputPolicy.NormalizeBatch(new WorldMarkerCreateRequest
        {
            DocumentUnits = "meters",
            Markers =
            {
                new WorldMarkerSpec { Name = " Marker A ", X = 10, Y = 20 },
            },
        });

        var marker = Assert.Single(plan.Markers);
        Assert.Equal("Meters", plan.DocumentUnits);
        Assert.Equal("Marker A", marker.Name);
        Assert.Equal(0, marker.Z);
        Assert.Equal(WorldMarkerStyles.Target, marker.Style);
        Assert.Equal(1, marker.Size);
        Assert.Equal((255, 0, 0), (marker.Color.R, marker.Color.G, marker.Color.B));
        Assert.False(marker.PoleEnabled);
        Assert.Matches("^wm-[0-9a-f]{16}$", marker.MarkerId);
    }

    [Fact]
    public void CreateMarkerId_IsStableAcrossCaseAndUnicodeNormalization()
    {
        var composed = WorldMarkerInputPolicy.CreateMarkerId("Метка é");
        var decomposed = WorldMarkerInputPolicy.CreateMarkerId("метка e\u0301");

        Assert.Equal(composed, decomposed);
    }

    [Fact]
    public void NormalizeBatch_RejectsDuplicateNormalizedNamesBeforeReturningPlan()
    {
        var request = Request(
            new WorldMarkerSpec { Name = "Marker", X = 0, Y = 0 },
            new WorldMarkerSpec { Name = " marker ", X = 1, Y = 1 });

        var error = Assert.Throws<ArgumentException>(() => WorldMarkerInputPolicy.NormalizeBatch(request));

        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeBatch_RejectsMoreThanOneHundredMarkers()
    {
        var request = new WorldMarkerCreateRequest { DocumentUnits = "Meters" };
        for (var i = 0; i < 101; i++)
            request.Markers.Add(new WorldMarkerSpec { Name = "M" + i, X = i, Y = i });

        Assert.Throws<ArgumentException>(() => WorldMarkerInputPolicy.NormalizeBatch(request));
    }

    [Fact]
    public void NormalizeBatch_AcceptsExactlyOneHundredMarkers()
    {
        var request = new WorldMarkerCreateRequest { DocumentUnits = "Meters" };
        for (var i = 0; i < 100; i++)
            request.Markers.Add(new WorldMarkerSpec { Name = "M" + i, X = i, Y = i });

        Assert.Equal(100, WorldMarkerInputPolicy.NormalizeBatch(request).Markers.Count);
    }

    [Fact]
    public void NormalizeBatch_RejectsNullAndEmptyMarkerCollections()
    {
        Assert.Throws<ArgumentException>(() => WorldMarkerInputPolicy.NormalizeBatch(new WorldMarkerCreateRequest
        {
            DocumentUnits = "Meters",
            Markers = null,
        }));
        Assert.Throws<ArgumentException>(() => WorldMarkerInputPolicy.NormalizeBatch(new WorldMarkerCreateRequest
        {
            DocumentUnits = "Meters",
        }));
    }

    [Fact]
    public void NormalizeMarker_EnforcesNameLengthBoundary()
    {
        var accepted = WorldMarkerInputPolicy.NormalizeMarker(new WorldMarkerSpec
        {
            Name = new string('A', WorldMarkerInputPolicy.MaxNameLength),
            X = 0,
            Y = 0,
        });
        Assert.Equal(WorldMarkerInputPolicy.MaxNameLength, accepted.Name.Length);

        Assert.Throws<ArgumentException>(() => WorldMarkerInputPolicy.NormalizeMarker(new WorldMarkerSpec
        {
            Name = new string('A', WorldMarkerInputPolicy.MaxNameLength + 1),
            X = 0,
            Y = 0,
        }));
    }

    [Theory]
    [InlineData("bad\nlabel")]
    [InlineData("bad\rlabel")]
    [InlineData("bad\tlabel")]
    [InlineData("emoji \ud83d\ude80")]
    public void NormalizeMarker_RejectsUnsafeOrUnsupportedLabel(string label)
    {
        var marker = new WorldMarkerSpec { Name = "M", X = 0, Y = 0, Label = label };

        Assert.Throws<ArgumentException>(() => WorldMarkerInputPolicy.NormalizeMarker(marker));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NormalizeMarker_RejectsNonFiniteCoordinates(double value)
    {
        var marker = new WorldMarkerSpec { Name = "M", X = value, Y = 0 };

        Assert.Throws<ArgumentException>(() => WorldMarkerInputPolicy.NormalizeMarker(marker));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.0000001)]
    [InlineData(1000000001)]
    public void NormalizeMarker_RejectsSizeOutsideSupportedBounds(double size)
    {
        var marker = new WorldMarkerSpec { Name = "M", X = 0, Y = 0, Size = size };

        Assert.Throws<ArgumentException>(() => WorldMarkerInputPolicy.NormalizeMarker(marker));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 256, 0)]
    [InlineData(0, 0, 999)]
    public void NormalizeMarker_RejectsInvalidColor(int r, int g, int b)
    {
        var marker = new WorldMarkerSpec
        {
            Name = "M",
            X = 0,
            Y = 0,
            Color = new WorldMarkerColor { R = r, G = g, B = b },
        };

        Assert.Throws<ArgumentException>(() => WorldMarkerInputPolicy.NormalizeMarker(marker));
    }

    [Theory]
    [InlineData("target")]
    [InlineData("cross")]
    [InlineData("circle")]
    [InlineData("pin")]
    [InlineData("pole")]
    [InlineData("box")]
    public void NormalizeMarker_AcceptsEveryV1Style(string style)
    {
        var marker = new WorldMarkerSpec
        {
            Name = style,
            X = 0,
            Y = 0,
            Z = 5,
            Style = style,
        };

        Assert.Equal(style, WorldMarkerInputPolicy.NormalizeMarker(marker).Style);
    }

    [Fact]
    public void NormalizeMarker_UsesExplicitPoleEndpoints()
    {
        var marker = WorldMarkerInputPolicy.NormalizeMarker(new WorldMarkerSpec
        {
            Name = "Pole marker",
            X = 1,
            Y = 2,
            Z = 7,
            Style = "circle",
            Pole = new WorldMarkerPole { Enabled = true, BaseZ = -2, TopZ = 8 },
        });

        Assert.True(marker.PoleEnabled);
        Assert.Equal(-2, marker.PoleBaseZ);
        Assert.Equal(8, marker.PoleTopZ);
    }

    [Fact]
    public void NormalizeMarker_DefaultPoleRunsFromZeroToAnchor()
    {
        var marker = WorldMarkerInputPolicy.NormalizeMarker(new WorldMarkerSpec
        {
            Name = "Pole marker",
            X = 1,
            Y = 2,
            Z = 7,
            Style = "pole",
        });

        Assert.True(marker.PoleEnabled);
        Assert.Equal(0, marker.PoleBaseZ);
        Assert.Equal(7, marker.PoleTopZ);
    }

    [Fact]
    public void NormalizeMarker_DefaultGroundPoleDerivesNonzeroSpanFromSize()
    {
        var marker = WorldMarkerInputPolicy.NormalizeMarker(new WorldMarkerSpec
        {
            Name = "Ground pole",
            X = 1,
            Y = 2,
            Style = "pole",
            Size = 3,
        });

        Assert.True(marker.PoleEnabled);
        Assert.Equal(0, marker.PoleBaseZ);
        Assert.Equal(3, marker.PoleTopZ);
    }

    [Fact]
    public void NormalizeMarker_RejectsExplicitlyEqualPoleEndpoints()
    {
        var spec = new WorldMarkerSpec
        {
            Name = "Bad pole",
            X = 1,
            Y = 2,
            Z = 5,
            Style = "pole",
            Pole = new WorldMarkerPole { BaseZ = 5, TopZ = 5 },
        };

        var error = Assert.Throws<ArgumentException>(() => WorldMarkerInputPolicy.NormalizeMarker(spec));
        Assert.Contains("Explicit pole", error.Message);
    }

    [Fact]
    public void NormalizeMarker_RejectsExplicitPoleSpanBelowMinimumSize()
    {
        var spec = new WorldMarkerSpec
        {
            Name = "Tiny pole",
            X = 1,
            Y = 2,
            Style = "circle",
            Pole = new WorldMarkerPole { Enabled = true, BaseZ = 0, TopZ = WorldMarkerInputPolicy.MinSize / 2 },
        };

        var error = Assert.Throws<ArgumentException>(() => WorldMarkerInputPolicy.NormalizeMarker(spec));
        Assert.Contains(WorldMarkerInputPolicy.MinSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture), error.Message);
    }

    [Fact]
    public void NormalizeMarker_EnforcesCoordinateAndDerivedCoordinateBounds()
    {
        var valid = new WorldMarkerSpec
        {
            Name = "Bounded",
            X = WorldMarkerInputPolicy.MaxAbsoluteCoordinate - 1,
            Y = 0,
            Size = 1,
        };
        Assert.Equal(valid.X, WorldMarkerInputPolicy.NormalizeMarker(valid).X);

        valid.X = WorldMarkerInputPolicy.MaxAbsoluteCoordinate;
        var error = Assert.Throws<ArgumentException>(() => WorldMarkerInputPolicy.NormalizeMarker(valid));
        Assert.Contains(WorldMarkerInputPolicy.MaxAbsoluteCoordinate.ToString("R", System.Globalization.CultureInfo.InvariantCulture), error.Message);
    }

    [Fact]
    public void NormalizeMarker_ExplicitEndpointsEnableOptionalPole()
    {
        var marker = WorldMarkerInputPolicy.NormalizeMarker(new WorldMarkerSpec
        {
            Name = "Circle with pole",
            X = 1,
            Y = 2,
            Z = 7,
            Style = "circle",
            Pole = new WorldMarkerPole { BaseZ = 1, TopZ = 8 },
        });

        Assert.True(marker.PoleEnabled);
        Assert.Equal(1, marker.PoleBaseZ);
        Assert.Equal(8, marker.PoleTopZ);
    }

    [Fact]
    public void NormalizeMarker_EnabledPoleWithoutEndpointsDerivesSpanFromSize()
    {
        var marker = WorldMarkerInputPolicy.NormalizeMarker(new WorldMarkerSpec
        {
            Name = "Optional pole",
            X = 1,
            Y = 2,
            Size = 4,
            Style = "circle",
            Pole = new WorldMarkerPole { Enabled = true },
        });

        Assert.True(marker.PoleEnabled);
        Assert.Equal(0, marker.PoleBaseZ);
        Assert.Equal(4, marker.PoleTopZ);
    }

    [Fact]
    public void NormalizeMarker_DisabledPolePreservesExplicitEndpointsWithoutEnablingGeometry()
    {
        var marker = WorldMarkerInputPolicy.NormalizeMarker(new WorldMarkerSpec
        {
            Name = "Disabled pole",
            X = 1,
            Y = 2,
            Style = "circle",
            Pole = new WorldMarkerPole { Enabled = false, BaseZ = 1, TopZ = 8 },
        });

        Assert.False(marker.PoleEnabled);
        Assert.Equal(1, marker.PoleBaseZ);
        Assert.Equal(8, marker.PoleTopZ);
    }

    [Fact]
    public void NormalizeMarker_RejectsPoleEndpointOutsideMagnitudeBound()
    {
        var error = Assert.Throws<ArgumentException>(() => WorldMarkerInputPolicy.NormalizeMarker(new WorldMarkerSpec
        {
            Name = "Unbounded pole",
            X = 0,
            Y = 0,
            Style = "circle",
            Pole = new WorldMarkerPole
            {
                Enabled = true,
                BaseZ = WorldMarkerInputPolicy.MaxAbsoluteCoordinate + 1,
                TopZ = 0,
            },
        }));

        Assert.Contains(WorldMarkerInputPolicy.MaxAbsoluteCoordinate.ToString("R", System.Globalization.CultureInfo.InvariantCulture), error.Message);
    }

    private static WorldMarkerCreateRequest Request(params WorldMarkerSpec[] markers)
    {
        var request = new WorldMarkerCreateRequest { DocumentUnits = "Meters" };
        request.Markers.AddRange(markers);
        return request;
    }
}
