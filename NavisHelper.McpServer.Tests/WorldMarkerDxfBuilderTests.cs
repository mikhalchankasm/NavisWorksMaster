using System.Globalization;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class WorldMarkerDxfBuilderTests
{
    [Theory]
    [InlineData("Inches", 1)]
    [InlineData("Feet", 2)]
    [InlineData("Miles", 3)]
    [InlineData("Millimeters", 4)]
    [InlineData("Centimeters", 5)]
    [InlineData("Meters", 6)]
    [InlineData("Kilometers", 7)]
    [InlineData("Microinches", 8)]
    [InlineData("Mils", 9)]
    [InlineData("Yards", 10)]
    [InlineData("Micrometers", 13)]
    public void GetInsUnitsCode_MapsSupportedDocumentUnits(string units, int expected)
    {
        Assert.Equal(expected, WorldMarkerDxfBuilder.GetInsUnitsCode(units));
    }

    [Fact]
    public void GetInsUnitsCode_RejectsNullAndInvalidUnitsAndAcceptsAlias()
    {
        Assert.Throws<ArgumentException>(() => WorldMarkerDxfBuilder.GetInsUnitsCode(null));
        Assert.Throws<ArgumentException>(() => WorldMarkerDxfBuilder.GetInsUnitsCode("parsecs"));
        Assert.Equal(13, WorldMarkerDxfBuilder.GetInsUnitsCode("micro_meters"));
        Assert.Equal("Micrometers", WorldMarkerDxfBuilder.NormalizeDocumentUnits("micro meters"));
    }

    [Fact]
    public void Build_WritesCompactHeaderTrueColorAndWorldCoordinates()
    {
        var marker = Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 1234.5,
            Y = -67.25,
            Z = 9.75,
            Size = 2,
            Color = new WorldMarkerColor { R = 1, G = 2, B = 3 },
            Style = "target",
        });

        var dxf = WorldMarkerDxfBuilder.Build(marker, "Meters");

        Assert.Contains("$ACADVER\r\n1\r\nAC1027", dxf);
        Assert.Contains("$INSUNITS\r\n70\r\n6", dxf);
        Assert.Contains("420\r\n66051", dxf);
        Assert.Contains("10\r\n1234.5", dxf);
        Assert.Contains("20\r\n-67.25", dxf);
        Assert.Contains("30\r\n9.75", dxf);
        Assert.EndsWith("0\r\nEOF\r\n", dxf);
    }

    [Fact]
    public void Build_UsesInvariantRoundTripNumbersRegardlessOfCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var dxf = WorldMarkerDxfBuilder.Build(Normalize(new WorldMarkerSpec
            {
                Name = "M",
                X = 1.25,
                Y = 2.5,
                Style = "cross",
            }), "Meters");

            Assert.Contains("1.25", dxf);
            Assert.DoesNotContain("1,25", dxf);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Build_EncodesCyrillicAndFormattingCharactersIntoAsciiText()
    {
        var dxf = WorldMarkerDxfBuilder.Build(Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 0,
            Y = 0,
            Label = "Метка 0А+39,00 \\ 50%",
        }), "Meters");

        Assert.Contains("\\U+041C\\U+0435\\U+0442\\U+043A\\U+0430", dxf);
        Assert.Contains("\\U+005C", dxf);
        Assert.Contains("50\\U+0025", dxf);
        Assert.All(dxf, character => Assert.InRange((int)character, 0, 127));
    }

    [Theory]
    [InlineData("A", 255, true)]
    [InlineData("A", 256, false)]
    [InlineData("\\", 36, true)]
    [InlineData("\\", 37, false)]
    [InlineData("%", 36, true)]
    [InlineData("%", 37, false)]
    [InlineData("Ж", 36, true)]
    [InlineData("Ж", 37, false)]
    public void EncodeText_EnforcesEncodedGroupCodeLength(string value, int count, bool expectedValid)
    {
        var input = string.Concat(Enumerable.Repeat(value, count));

        if (expectedValid)
        {
            Assert.InRange(WorldMarkerDxfBuilder.EncodeText(input).Length, 1, WorldMarkerInputPolicy.MaxEncodedLabelLength);
        }
        else
        {
            var error = Assert.Throws<ArgumentException>(() => WorldMarkerDxfBuilder.EncodeText(input));
            Assert.Contains(WorldMarkerInputPolicy.MaxEncodedLabelLength.ToString(), error.Message);
        }
    }

    [Theory]
    [InlineData("target", 2, 1)]
    [InlineData("cross", 2, 0)]
    [InlineData("circle", 0, 1)]
    [InlineData("pin", 3, 1)]
    [InlineData("pole", 3, 1)]
    [InlineData("box", 12, 0)]
    public void Build_EmitsExpectedV1Geometry(string style, int lineCount, int circleCount)
    {
        var marker = Normalize(new WorldMarkerSpec { Name = style, X = 0, Y = 0, Z = 5, Style = style });
        var dxf = WorldMarkerDxfBuilder.Build(marker, "Meters");

        Assert.Equal(lineCount, CountEntity(dxf, "LINE"));
        Assert.Equal(circleCount, CountEntity(dxf, "CIRCLE"));
    }

    [Fact]
    public void Build_AddsOptionalPoleToNonPoleStyle()
    {
        var marker = Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 0,
            Y = 0,
            Z = 5,
            Style = "circle",
            Pole = new WorldMarkerPole { Enabled = true, BaseZ = 1, TopZ = 9 },
        });

        var dxf = WorldMarkerDxfBuilder.Build(marker, "Meters");

        Assert.Equal(1, CountEntity(dxf, "LINE"));
        Assert.Contains("30\r\n1\r\n11\r\n0\r\n21\r\n0\r\n31\r\n9", dxf);
    }

    [Fact]
    public void EncodeText_RejectsLineBreakInjection()
    {
        Assert.Throws<ArgumentException>(() => WorldMarkerDxfBuilder.EncodeText("safe\n0\nEOF"));
    }

    [Fact]
    public void Build_RejectsNonFiniteDirectPlanCoordinates()
    {
        var marker = Normalize(new WorldMarkerSpec { Name = "M", X = 0, Y = 0 });
        marker.X = double.NaN;

        Assert.Throws<ArgumentException>(() => WorldMarkerDxfBuilder.Build(marker, "Meters"));
    }

    [Fact]
    public void Build_RejectsDirectPlanCoordinateOutsideMagnitudeBound()
    {
        var marker = Normalize(new WorldMarkerSpec { Name = "M", X = 0, Y = 0 });
        marker.X = WorldMarkerInputPolicy.MaxAbsoluteCoordinate + 1;

        var error = Assert.Throws<ArgumentException>(() => WorldMarkerDxfBuilder.Build(marker, "Meters"));
        Assert.Contains(WorldMarkerInputPolicy.MaxAbsoluteCoordinate.ToString("R", CultureInfo.InvariantCulture), error.Message);
    }

    [Fact]
    public void Build_RejectsHandBuiltPoleStyleWithDegenerateEndpointsEvenWhenFlagIsFalse()
    {
        var marker = Normalize(new WorldMarkerSpec { Name = "M", X = 0, Y = 0, Z = 5, Style = "pole" });
        marker.PoleEnabled = false;
        marker.PoleBaseZ = 5;
        marker.PoleTopZ = 5;

        Assert.Throws<ArgumentException>(() => WorldMarkerDxfBuilder.Build(marker, "Meters"));
    }

    [Fact]
    public void Build_RejectsHandBuiltPlanOutsideMagnitudeBounds()
    {
        var marker = Normalize(new WorldMarkerSpec { Name = "M", X = 0, Y = 0 });
        marker.Size = WorldMarkerInputPolicy.MinSize / 10;

        var error = Assert.Throws<ArgumentException>(() => WorldMarkerDxfBuilder.Build(marker, "Meters"));
        Assert.Contains("size must be between", error.Message);
    }

    private static WorldMarkerPlanItem Normalize(WorldMarkerSpec marker)
    {
        return WorldMarkerInputPolicy.NormalizeMarker(marker);
    }

    private static int CountEntity(string dxf, string entity)
    {
        return dxf.Split("0\r\n" + entity + "\r\n", StringSplitOptions.None).Length - 1;
    }
}
