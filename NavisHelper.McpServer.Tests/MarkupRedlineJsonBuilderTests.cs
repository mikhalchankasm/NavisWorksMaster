using System.Text.Json;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class MarkupRedlineJsonBuilderTests
{
    [Fact]
    public void OrthographicProjection_TopViewPreservesWorldDelta()
    {
        double x;
        double y;
        var projected = OrthographicRedlineProjectionHelper.TryProject(
            10, 5, 0,
            0, 0, 100,
            0, 0, 0, -1,
            20, 2,
            out x,
            out y);

        Assert.True(projected);
        Assert.Equal(10, x, 8);
        Assert.Equal(5, y, 8);
        Assert.Equal(0.25, OrthographicRedlineProjectionHelper.GetMinimumRadiusFromSize(0.5), 8);
    }

    [Fact]
    public void OrthographicProjection_IsometricFixtureUsesNavisworksQuaternionLayout()
    {
        double x;
        double y;
        var projected = OrthographicRedlineProjectionHelper.TryProject(
            1132.73,
            41.24,
            34.0,
            1306.3411,
            -52.2049,
            202.7685,
            -0.4247082003,
            -0.1759198966,
            -0.3398511430,
            -0.8204732386,
            85.61,
            1.4473,
            out x,
            out y);

        Assert.Equal(-56.7, x, 1);
        Assert.Equal(-28.8, y, 1);
        Assert.InRange(x, -61.95, 61.95);
        Assert.InRange(y, -42.81, 42.81);
    }

    [Fact]
    public void OrthographicProjection_MatchesNavisworksExportCameraSpaceFixture()
    {
        double x;
        double y;
        var projected = OrthographicRedlineProjectionHelper.TryProject(
            868.6177406347,
            228.8183533858,
            195.0804373241,
            868.0548126347,
            171.2121343858,
            195.0804373241,
            0,
            0,
            0,
            -1,
            130.0361893650,
            1.4473304473,
            out x,
            out y);

        Assert.True(projected);
        Assert.Equal(0.562928, x, 6);
        Assert.Equal(57.606219, y, 6);
    }

    [Theory]
    [InlineData("rectangle", 4, 0, 4)]
    [InlineData("target", 1, 1, 0)]
    [InlineData("arrow", 3, 0, 3)]
    [InlineData("hatch", 0, 0, 0)]
    public void Build_EmitsSupportedPersistentPrimitives(string style, int expectedCount, int expectedEllipses, int expectedLines)
    {
        var json = MarkupRedlineJsonBuilder.Build(
            style,
            new[] { new MarkupRedlineRect { Left = -1, Top = 2, Right = 3, Bottom = -4 } },
            1,
            0,
            0,
            3,
            0.08,
            0.08);

        using var document = JsonDocument.Parse(json);
        var values = document.RootElement.GetProperty("Values").EnumerateArray().ToArray();

        if (style == "hatch")
            Assert.True(values.Length > 4);
        else
            Assert.Equal(expectedCount, values.Length);
        Assert.Equal(expectedEllipses, values.Count(value => value.GetProperty("Type").GetString() == "RedlineEllipse"));
        if (style == "hatch")
            Assert.True(values.Count(value => value.GetProperty("Type").GetString() == "RedlineLine") > 4);
        else
            Assert.Equal(expectedLines, values.Count(value => value.GetProperty("Type").GetString() == "RedlineLine"));
        Assert.All(values.Where(value => value.GetProperty("Type").GetString() == "RedlineLine"), value =>
        {
            Assert.True(value.TryGetProperty("Start", out _));
            Assert.True(value.TryGetProperty("End", out _));
        });
        Assert.All(values, value => Assert.True(RedlineTypeWhitelistHelper.IsSupportedForSetRedlines(value.GetProperty("Type").GetString())));
    }

    [Fact]
    public void Target_CrossExtendsBeyondEllipseBounds()
    {
        var json = MarkupRedlineJsonBuilder.Build(
            "target",
            new[] { new MarkupRedlineRect { Left = -1, Top = 1, Right = 1, Bottom = -1 } },
            1,
            0,
            0,
            3,
            0.08,
            0.08,
            new MarkupRedlineBuildOptions { TargetCrosshair = true });

        using var document = JsonDocument.Parse(json);
        var horizontal = document.RootElement.GetProperty("Values")[1];
        var ellipse = document.RootElement.GetProperty("Values")[0];
        Assert.Equal(-1, ellipse.GetProperty("MinPoint")[0].GetDouble());
        Assert.Equal(-1, ellipse.GetProperty("MinPoint")[1].GetDouble());
        Assert.Equal(1, ellipse.GetProperty("MaxPoint")[0].GetDouble());
        Assert.Equal(1, ellipse.GetProperty("MaxPoint")[1].GetDouble());
        Assert.Equal(-1.7, horizontal.GetProperty("Start")[0].GetDouble(), 8);
        Assert.Equal(1.7, horizontal.GetProperty("End")[0].GetDouble(), 8);
    }

    [Fact]
    public void Arrow_UsesDocumentUnitCameraBounds()
    {
        var json = MarkupRedlineJsonBuilder.Build(
            "arrow",
            new[] { new MarkupRedlineRect { Left = -60, Top = 20, Right = -55, Bottom = 15 } },
            1,
            0,
            0,
            3,
            2,
            2,
            new MarkupRedlineBuildOptions { CameraHalfWidth = 70, CameraHalfHeight = 50 });

        using var document = JsonDocument.Parse(json);
        var shaft = document.RootElement.GetProperty("Values")[0];
        Assert.Equal("RedlineLine", shaft.GetProperty("Type").GetString());
        Assert.Equal(-55, shaft.GetProperty("End")[0].GetDouble(), 8);
        Assert.Equal(17.5, shaft.GetProperty("End")[1].GetDouble(), 8);
        Assert.Equal(-53, shaft.GetProperty("Start")[0].GetDouble(), 8);
        Assert.InRange(shaft.GetProperty("Start")[0].GetDouble(), -70, 70);
    }

    [Fact]
    public void TargetWithArrowCallout_EmitsEllipseAndThreeSupportedLinesWithoutCrosshair()
    {
        var json = MarkupRedlineJsonBuilder.Build(
            "target",
            new[] { new MarkupRedlineRect { Left = -2, Top = 2, Right = 2, Bottom = -2 } },
            1, 0, 0, 3, 1, 1,
            new MarkupRedlineBuildOptions
            {
                ArrowCallout = true,
                ArrowLength = 8,
                CameraHalfWidth = 60,
                CameraHalfHeight = 40,
            });

        using var document = JsonDocument.Parse(json);
        var values = document.RootElement.GetProperty("Values").EnumerateArray().ToArray();
        Assert.Equal(4, values.Length);
        Assert.Equal("RedlineEllipse", values[0].GetProperty("Type").GetString());
        Assert.All(values.Skip(1), value => Assert.Equal("RedlineLine", value.GetProperty("Type").GetString()));
        Assert.Equal(8, Distance(values[1].GetProperty("Start"), values[1].GetProperty("End")), 8);
    }

    [Fact]
    public void ArrowLength_AutoUsesEightPercentAndClampsExplicitValues()
    {
        Assert.Equal(6.8488, MarkupRedlineJsonBuilder.ResolveArrowLength(85.61, 0), 8);
        Assert.Equal(4.2805, MarkupRedlineJsonBuilder.ResolveArrowLength(85.61, 1), 8);
        Assert.Equal(12.8415, MarkupRedlineJsonBuilder.ResolveArrowLength(85.61, 20), 8);
    }

    [Fact]
    public void ArrowCallouts_ChooseNonParallelDirectionsAroundNeighbouringMarks()
    {
        var json = MarkupRedlineJsonBuilder.Build(
            "target",
            new[]
            {
                new MarkupRedlineRect { Left = -3, Top = 2, Right = -1, Bottom = 0 },
                new MarkupRedlineRect { Left = 1, Top = 2, Right = 3, Bottom = 0 },
                new MarkupRedlineRect { Left = -1, Top = -1, Right = 1, Bottom = -3 },
            },
            1, 0, 0, 3, 1, 1,
            new MarkupRedlineBuildOptions
            {
                ArrowCallout = true,
                ArrowLength = 6,
                CameraHalfWidth = 30,
                CameraHalfHeight = 20,
            });

        using var document = JsonDocument.Parse(json);
        var values = document.RootElement.GetProperty("Values").EnumerateArray().ToArray();
        Assert.Equal(12, values.Length);
        var lines = values.Where(value => value.GetProperty("Type").GetString() == "RedlineLine").ToArray();
        Assert.Equal(9, lines.Length);
        var shafts = lines
            .Where(value => Math.Abs(Distance(value.GetProperty("Start"), value.GetProperty("End")) - 6) < 1e-8)
            .ToArray();
        Assert.Equal(3, shafts.Length);
        var directions = shafts.Select(value => DirectionKey(value.GetProperty("Start"), value.GetProperty("End"))).Distinct().ToArray();
        Assert.True(directions.Length > 1);
    }

    [Fact]
    public void Arrow_FallbackNeverEmitsADegenerateStub()
    {
        var json = MarkupRedlineJsonBuilder.Build(
            "arrow",
            new[] { new MarkupRedlineRect { Left = -10, Top = 10, Right = 10, Bottom = -10 } },
            1, 0, 0, 3, 6, 6,
            new MarkupRedlineBuildOptions
            {
                ArrowLength = 6,
                CameraHalfWidth = 10,
                CameraHalfHeight = 10,
            },
            out var arrowCount);

        using var document = JsonDocument.Parse(json);
        var lines = document.RootElement.GetProperty("Values").EnumerateArray().ToArray();
        Assert.Equal(3, lines.Length);
        Assert.Equal(1, arrowCount);
        Assert.InRange(Distance(lines[0].GetProperty("Start"), lines[0].GetProperty("End")), 6 * 0.35, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("rectangle")]
    [InlineData("TARGET")]
    [InlineData(" arrow ")]
    public void NormalizeStyle_AcceptsSupportedValues(string style)
    {
        Assert.Contains(MarkupRedlineJsonBuilder.NormalizeStyle(style), new[] { "rectangle", "target", "arrow", "hatch" });
    }

    [Fact]
    public void Hatch_EmitsFrameAndClippedParallelLinesWithFloatColor()
    {
        var json = MarkupRedlineJsonBuilder.Build(
            "hatch",
            new[] { new MarkupRedlineRect { Left = -0.5, Top = 0.5, Right = 0.5, Bottom = -0.5 } },
            0.25, 0.5, 0.75, 3, 0.08, 0.08,
            new MarkupRedlineBuildOptions { HatchAngleDeg = 45, HatchSpacing = 0.25, HatchThickness = 2 });

        using var document = JsonDocument.Parse(json);
        var values = document.RootElement.GetProperty("Values").EnumerateArray().ToArray();
        Assert.True(values.Length > 4);
        Assert.All(values, value => Assert.Equal("RedlineLine", value.GetProperty("Type").GetString()));
        Assert.Equal(0.25, values[0].GetProperty("Color")[0].GetDouble(), 8);
        Assert.Equal(0.5, values[0].GetProperty("Color")[1].GetDouble(), 8);
        Assert.Equal(0.75, values[0].GetProperty("Color")[2].GetDouble(), 8);
    }

    [Fact]
    public void Hatch_CapsLineCountForLargeDocumentUnitFrames()
    {
        var json = MarkupRedlineJsonBuilder.Build(
            "hatch",
            new[] { new MarkupRedlineRect { Left = -100, Top = 100, Right = 100, Bottom = -100 } },
            1, 0, 0, 3, 1, 1,
            new MarkupRedlineBuildOptions { HatchSpacing = 0.001, MaxHatchLines = 100 });

        using var document = JsonDocument.Parse(json);
        var values = document.RootElement.GetProperty("Values").EnumerateArray().ToArray();
        Assert.InRange(values.Length, 5, 106);
    }

    [Fact]
    public void NormalizeStyle_RejectsUnsupportedValue()
    {
        Assert.Throws<ArgumentException>(() => MarkupRedlineJsonBuilder.NormalizeStyle("cloud"));
    }

    private static double Distance(JsonElement start, JsonElement end)
    {
        var dx = end[0].GetDouble() - start[0].GetDouble();
        var dy = end[1].GetDouble() - start[1].GetDouble();
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string DirectionKey(JsonElement tail, JsonElement tip)
    {
        var dx = tail[0].GetDouble() - tip[0].GetDouble();
        var dy = tail[1].GetDouble() - tip[1].GetDouble();
        return Math.Round(Math.Atan2(dy, dx), 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
