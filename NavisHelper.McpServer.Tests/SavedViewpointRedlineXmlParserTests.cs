using System.Linq;
using System.Xml.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SavedViewpointRedlineXmlParserTests
{
    [Fact]
    public void Parse_NoRedlines_ReturnsEmptyResult()
    {
        var result = SavedViewpointRedlineXmlParser.Parse(XElement.Parse("<view><viewpoint /></view>"));

        Assert.Empty(result.Redlines);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_Ellipse_PreservesFloatingColorAndCoordinates()
    {
        var result = Parse("<view><redlines><rlellipse thickness=\"5\"><colour red=\"0.25\" green=\"0.5\" blue=\"0.75\"/><start><pos2f x=\"-1.5\" y=\"2\"/></start><end><pos2f x=\"3.25\" y=\"-4\"/></end></rlellipse></redlines></view>");

        var ellipse = Assert.Single(result.Redlines);
        Assert.Equal("RedlineEllipse", ellipse.Type);
        Assert.Equal(5, ellipse.Thickness);
        Assert.False(ellipse.IntegerColor);
        Assert.Equal(0.25, ellipse.Red);
        Assert.Equal(0.5, ellipse.Green);
        Assert.Equal(0.75, ellipse.Blue);
        Assert.Equal(-1.5, ellipse.StartX);
        Assert.Equal(2, ellipse.StartY);
        Assert.Equal(3.25, ellipse.EndX);
        Assert.Equal(-4, ellipse.EndY);
    }

    [Fact]
    public void Parse_Line_UsesExistingIntegerColorThreshold()
    {
        var result = Parse("<view><redlines><rlline thickness=\"4\"><color red=\"0.49\" green=\"0.5\" blue=\"1\"/><start><pos2f x=\"1\" y=\"2.5\"/></start><end><pos2f x=\"-3\" y=\"4\"/></end></rlline></redlines></view>");

        var line = Assert.Single(result.Redlines);
        Assert.Equal("RedlineLine", line.Type);
        Assert.True(line.IntegerColor);
        Assert.Equal(0, line.Red);
        Assert.Equal(1, line.Green);
        Assert.Equal(1, line.Blue);
    }

    [Fact]
    public void Parse_Arrow_ConvertsToThreeWritableLines()
    {
        var result = Parse("<view><redlines><rlarrow thickness=\"2\"><start><pos2f x=\"0\" y=\"0\"/></start><end><pos2f x=\"10\" y=\"0\"/></end></rlarrow></redlines></view>");

        Assert.Equal(3, result.Redlines.Count);
        Assert.All(result.Redlines, line =>
        {
            Assert.Equal("RedlineLine", line.Type);
            Assert.Equal(2, line.Thickness);
            Assert.True(line.IntegerColor);
        });
        Assert.Equal((0.0, 0.0, 10.0, 0.0), Coordinates(result.Redlines[0]));
        Assert.Equal((10.0, 0.0, 7.0, -1.6), Coordinates(result.Redlines[1]));
        Assert.Equal((10.0, 0.0, 7.0, 1.6), Coordinates(result.Redlines[2]));
    }

    [Fact]
    public void Parse_MissingCoordinates_PreservesWarningsAndSkipsValues()
    {
        var result = Parse("<view><redlines><rlellipse/><rlline><start><pos2f x=\"1\" y=\"2\"/></start></rlline><rlarrow><end><pos2f x=\"3\" y=\"4\"/></end></rlarrow></redlines></view>");

        Assert.Empty(result.Redlines);
        Assert.Equal(new[]
        {
            "Skipped ellipse redline without start/end coordinates.",
            "Skipped line redline without start/end coordinates.",
            "Skipped arrow redline without start/end coordinates.",
        }, result.Warnings);
    }

    [Fact]
    public void Parse_UnsupportedNode_PreservesNodeNameInWarning()
    {
        var result = Parse("<view><redlines><rltext value=\"x\"/></redlines></view>");

        Assert.Empty(result.Redlines);
        Assert.Equal("Skipped unsupported redline node: rltext", Assert.Single(result.Warnings));
    }

    [Fact]
    public void Parse_NamespacesAndCase_AreIgnoredAndInvalidNumbersUseFallbacks()
    {
        var result = Parse("<n:view xmlns:n=\"urn:test\"><n:wrapper><n:REDLINES><n:RLLINE thickness=\"bad\"><n:start><n:pos2f x=\"bad\" y=\"2\"/></n:start><n:end><n:pos2f x=\"3\" y=\"bad\"/></n:end></n:RLLINE></n:REDLINES></n:wrapper></n:view>");

        var line = Assert.Single(result.Redlines);
        Assert.Equal(3, line.Thickness);
        Assert.Equal((0.0, 2.0, 3.0, 0.0), Coordinates(line));
        Assert.Equal((1.0, 0.0, 0.0), (line.Red, line.Green, line.Blue));
    }

    [Fact]
    public void Parse_MixedNodes_PreservesSourceOrder()
    {
        var result = Parse("<view><redlines><rlline><start><pos2f x=\"1\" y=\"2\"/></start><end><pos2f x=\"3\" y=\"4\"/></end></rlline><rlellipse><start><pos2f x=\"5\" y=\"6\"/></start><end><pos2f x=\"7\" y=\"8\"/></end></rlellipse></redlines></view>");

        Assert.Equal(new[] { "RedlineLine", "RedlineEllipse" }, result.Redlines.Select(item => item.Type));
    }

    [Fact]
    public void Parse_EllipseWithoutColor_UsesFloatingRedDefault()
    {
        var result = Parse("<view><redlines><rlellipse><start><pos2f x=\"1\" y=\"2\"/></start><end><pos2f x=\"3\" y=\"4\"/></end></rlellipse></redlines></view>");

        var ellipse = Assert.Single(result.Redlines);
        Assert.False(ellipse.IntegerColor);
        Assert.Equal((1.0, 0.0, 0.0), (ellipse.Red, ellipse.Green, ellipse.Blue));
    }

    [Fact]
    public void Parse_ArrowWithColor_UsesExistingIntegerColorThreshold()
    {
        var result = Parse("<view><redlines><rlarrow><colour red=\"0.49\" green=\"0.5\" blue=\"0.9\"/><start><pos2f x=\"0\" y=\"0\"/></start><end><pos2f x=\"10\" y=\"0\"/></end></rlarrow></redlines></view>");

        Assert.Equal(3, result.Redlines.Count);
        Assert.All(result.Redlines, line =>
            Assert.Equal((0.0, 1.0, 1.0), (line.Red, line.Green, line.Blue)));
    }

    [Fact]
    public void Parse_PositionsMayContainNestedPos2f()
    {
        var result = Parse("<view><redlines><rlline><start><wrapper><pos2f x=\"1\" y=\"2\"/></wrapper></start><end><wrapper><pos2f x=\"3\" y=\"4\"/></wrapper></end></rlline></redlines></view>");

        Assert.Equal((1.0, 2.0, 3.0, 4.0), Coordinates(Assert.Single(result.Redlines)));
    }

    [Fact]
    public void Parse_MultipleRedlineCollections_UsesFirstCollection()
    {
        var result = Parse("<view><redlines><rlline><start><pos2f x=\"1\" y=\"2\"/></start><end><pos2f x=\"3\" y=\"4\"/></end></rlline></redlines><redlines><rlellipse><start><pos2f x=\"5\" y=\"6\"/></start><end><pos2f x=\"7\" y=\"8\"/></end></rlellipse></redlines></view>");

        Assert.Equal("RedlineLine", Assert.Single(result.Redlines).Type);
    }

    private static SavedViewpointRedlineParseResult Parse(string xml)
    {
        return SavedViewpointRedlineXmlParser.Parse(XElement.Parse(xml));
    }

    private static (double StartX, double StartY, double EndX, double EndY) Coordinates(SavedViewpointRedlineDefinition value)
    {
        return (value.StartX, value.StartY, value.EndX, value.EndY);
    }
}
