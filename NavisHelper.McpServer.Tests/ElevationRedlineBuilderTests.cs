using System.Text.Json;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ElevationRedlineBuilderTests
{
    [Theory]
    [InlineData(ElevationRedlineBuilder.LabelMode)]
    [InlineData(ElevationRedlineBuilder.DimensionMode)]
    public void Build_EmitsOnlySupportedRedlineLines(string mode)
    {
        var result = ElevationRedlineBuilder.Build(
            mode,
            new[]
            {
                new ElevationProjectedItem
                {
                    TopX = 1,
                    TopY = 2,
                    BaseX = 1,
                    BaseY = -2,
                    Label = "Z=+12.345M",
                },
            },
            0.001,
            0.002);

        using var json = JsonDocument.Parse(result.Json);
        var values = json.RootElement.GetProperty("Values");
        Assert.True(result.PrimitiveCount > 10);
        Assert.Equal(result.PrimitiveCount, values.GetArrayLength());
        Assert.All(
            values.EnumerateArray(),
            value => Assert.Equal("RedlineLine", value.GetProperty("Type").GetString()));
    }

    [Fact]
    public void DimensionMode_ContainsExactProjectedTopToBaseLine()
    {
        var result = ElevationRedlineBuilder.Build(
            ElevationRedlineBuilder.DimensionMode,
            new[]
            {
                new ElevationProjectedItem
                {
                    TopX = 1,
                    TopY = 2,
                    BaseX = 3,
                    BaseY = 4,
                    Label = "Z=+1.000M",
                },
            },
            0.01,
            0.01);

        using var json = JsonDocument.Parse(result.Json);
        var first = json.RootElement.GetProperty("Values")[0];
        Assert.Equal(1, first.GetProperty("Start")[0].GetDouble());
        Assert.Equal(2, first.GetProperty("Start")[1].GetDouble());
        Assert.Equal(3, first.GetProperty("End")[0].GetDouble());
        Assert.Equal(4, first.GetProperty("End")[1].GetDouble());
    }

    [Fact]
    public void LabelMode_DrawsVectorTextDownFromItsVisualTop()
    {
        var result = ElevationRedlineBuilder.Build(
            ElevationRedlineBuilder.LabelMode,
            new[] { Item() },
            0.01,
            0.01);

        using var json = JsonDocument.Parse(result.Json);
        var values = json.RootElement.GetProperty("Values");
        var hasDownwardTextStroke = values.EnumerateArray().Skip(3).Any(line =>
            line.GetProperty("End")[1].GetDouble() <
            line.GetProperty("Start")[1].GetDouble());

        Assert.True(hasDownwardTextStroke);
    }

    [Fact]
    public void Build_RejectsUnsupportedMode()
    {
        Assert.Throws<ArgumentException>(() =>
            ElevationRedlineBuilder.Build(
                "text",
                new[] { Item() },
                0.01,
                0.01));
    }

    [Fact]
    public void Build_RejectsUnsupportedLabelCharacter()
    {
        var item = Item();
        item.Label = "Z:1";

        Assert.Throws<ArgumentException>(() =>
            ElevationRedlineBuilder.Build(
                ElevationRedlineBuilder.LabelMode,
                new[] { item },
                0.01,
                0.01));
    }

    [Fact]
    public void BuildNativeText_EmitsEditableGreenTextAndGreenLines()
    {
        var result = ElevationRedlineBuilder.BuildNativeText(
            ElevationRedlineBuilder.DimensionMode,
            new[] { Item() },
            0.01,
            0.01,
            thickness: 10);

        using var json = JsonDocument.Parse(result.Json);
        var values = json.RootElement.GetProperty("Values");
        var text = Assert.Single(
            values.EnumerateArray(),
            value => value.GetProperty("Type").GetString() == "RedlineText");

        Assert.Equal("Z=+1000 MM", text.GetProperty("Text").GetString());
        Assert.Equal(1, text.GetProperty("Thickness").GetInt32());
        Assert.Equal(1, text.GetProperty("Color")[1].GetInt32());
        Assert.All(
            values.EnumerateArray().Where(value =>
                value.GetProperty("Type").GetString() == "RedlineLine"),
            value => Assert.Equal(10, value.GetProperty("Thickness").GetInt32()));
        Assert.All(
            values.EnumerateArray(),
            value => Assert.Equal(1, value.GetProperty("Color")[1].GetInt32()));
    }

    private static ElevationProjectedItem Item()
    {
        return new ElevationProjectedItem
        {
            TopX = 0,
            TopY = 0,
            BaseX = 0,
            BaseY = -1,
            Label = "Z=+1000 MM",
        };
    }
}
