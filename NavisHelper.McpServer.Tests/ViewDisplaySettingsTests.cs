using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

[Collection("MCP stdio")]
public sealed class ViewDisplaySettingsTests
{
    [Theory]
    [InlineData("plain", 1)]
    [InlineData("graduated", 2)]
    [InlineData("horizon", 4)]
    public void BackgroundRequiresExactOpaqueRgbColorsAndCopiesInput(string mode, int count)
    {
        var colors = Enumerable.Repeat("#aBc123", count).ToArray();
        var request = new ViewDisplaySettingsRequest { Background = new() { Mode = mode, Colors = colors } };
        var plan = ViewDisplaySettingsValidation.Validate(request, true);
        Assert.False(plan.Apply);
        Assert.All(plan.Background.Colors, color => Assert.Equal("#ABC123", color));
        Assert.All(colors, color => Assert.Equal("#aBc123", color));
        plan.Background.Colors[0] = "#FFFFFF";
        Assert.Equal("#aBc123", colors[0]);
        Assert.Null(plan.Lighting);
        Assert.Null(plan.RenderStyle);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("white")]
    [InlineData("#fff")]
    [InlineData("#FFFFFFFF")]
    [InlineData("#12345G")]
    [InlineData("# 12345")]
    [InlineData("#123456 ")]
    public void InvalidColorsAreRejected(string color) => Assert.Throws<ArgumentException>(() =>
        ViewDisplaySettingsValidation.Validate(new() { Background = new() { Mode = "plain", Colors = new[] { color } } }, true));

    [Theory]
    [InlineData("plain", 0)]
    [InlineData("plain", 2)]
    [InlineData("graduated", 1)]
    [InlineData("horizon", 3)]
    [InlineData("image", 1)]
    [InlineData(null, 1)]
    public void InvalidBackgroundShapeIsRejected(string mode, int count) => Assert.Throws<ArgumentException>(() =>
        ViewDisplaySettingsValidation.Validate(new() { Background = new() { Mode = mode, Colors = Enumerable.Repeat("#000000", count).ToArray() } }, true));

    [Fact]
    public void HorizonRejectedWhenContextDoesNotSupportIt() => Assert.Throws<ArgumentException>(() =>
        ViewDisplaySettingsValidation.Validate(new() { Background = new() { Mode = "horizon", Colors = Enumerable.Repeat("#000000", 4).ToArray() } }, false));

    [Theory]
    [InlineData("none")]
    [InlineData("scene_lights")]
    [InlineData("HEADLIGHT")]
    [InlineData("full_lights")]
    public void LightingCanBeChangedIndependently(string lighting)
    {
        var plan = ViewDisplaySettingsValidation.Validate(new() { Lighting = lighting, Apply = true }, false);
        Assert.True(plan.Apply);
        Assert.Equal(lighting.ToLowerInvariant(), plan.Lighting);
        Assert.Null(plan.RenderStyle);
        Assert.Null(plan.Background);
    }

    [Theory]
    [InlineData("full_render")]
    [InlineData("preview")]
    [InlineData("shaded")]
    [InlineData("wireframe")]
    [InlineData("hidden_line")]
    public void RenderStyleCanBeChangedIndependently(string style)
    {
        var plan = ViewDisplaySettingsValidation.Validate(new() { RenderStyle = style }, false);
        Assert.Equal(style, plan.RenderStyle);
        Assert.Null(plan.Lighting);
        Assert.Null(plan.Background);
    }

    [Fact]
    public void EmptyAndUnknownModesAreNotSilentNoOps()
    {
        Assert.Throws<ArgumentException>(() => ViewDisplaySettingsValidation.Validate(new(), true));
        Assert.Throws<ArgumentException>(() => ViewDisplaySettingsValidation.Validate(new() { Lighting = "" }, true));
        Assert.Throws<ArgumentException>(() => ViewDisplaySettingsValidation.Validate(new() { RenderStyle = "realistic" }, true));
        Assert.Throws<ArgumentException>(() => ViewDisplaySettingsValidation.Validate(new() { Lighting = "2" }, true));
    }

    [Fact]
    public void CombinedPlanValidatesAllFields()
    {
        var request = new ViewDisplaySettingsRequest { Lighting = "headlight", RenderStyle = "wireframe",
            Background = new() { Mode = "graduated", Colors = new[] { "#FFFFFF", "#000000" } } };
        var plan = ViewDisplaySettingsValidation.Validate(request, true);
        Assert.Equal("headlight", plan.Lighting);
        Assert.Equal("wireframe", plan.RenderStyle);
        request.Background.Colors[1] = "invalid";
        Assert.Throws<ArgumentException>(() => ViewDisplaySettingsValidation.Validate(request, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewProfileAndReadOnlyModeExposeTheCorrectTools(bool readOnly)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpCatalogResourceTests.ConnectAsync("view", readOnly, timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Contains(tools, tool => tool.Name == "get_view_display_settings");
        Assert.Equal(!readOnly, tools.Any(tool => tool.Name == "set_view_display_settings"));
        if (!readOnly)
        {
            var schema = tools.Single(tool => tool.Name == "set_view_display_settings").ProtocolTool.InputSchema;
            Assert.False(schema.GetProperty("properties").GetProperty("apply").GetProperty("default").GetBoolean());
            var background = schema.GetProperty("properties").GetProperty("background").GetProperty("properties");
            Assert.Contains(background.GetProperty("mode").GetProperty("type").EnumerateArray(), type => type.GetString() == "string");
            var colors = background.GetProperty("colors");
            Assert.Contains(colors.GetProperty("type").EnumerateArray(), type => type.GetString() == "array");
            Assert.Equal("string", colors.GetProperty("items").GetProperty("type").GetString());
        }
        var mode = new McpReadOnlyMode(readOnly, McpReadOnlyMode.RegisteredToolMethods());
        Assert.True(mode.IsAllowed("get_view_display_settings"));
        Assert.Equal(!readOnly, mode.IsAllowed("set_view_display_settings"));
    }
}
