using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #360, from real modelling work: no tool showed whether an item's colour or
// transparency was an override or its original material, so copying colours between an
// .nwd and an .nwf was done from screenshots; and nothing reset the appearance of given
// items (model_color_scheme reset only undoes its own scheme), so the user pressed Reset
// Appearance in the ribbon. item_appearance reads; reset_appearance resets in one step.
public sealed class ItemAppearanceTests
{
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.16, 16)]
    [InlineData(0.162, 16)]
    [InlineData(1.0, 100)]
    [InlineData(-0.2, 0)]
    [InlineData(1.7, 100)]
    public void TransparencyIsAWholePercent(double transparency, int expected)
    {
        Assert.Equal(expected, ItemAppearanceRules.ToPercent(transparency));
    }

    [Theory]
    [InlineData(1.0, 0.0, 0.0, "#FF0000")]
    [InlineData(0.0, 0.5, 1.0, "#0080FF")]
    [InlineData(0.6667, 0.7059, 0.7451, "#AAB4BE")]
    [InlineData(1.2, -0.1, 0.0, "#FF0000")]
    public void ColoursAreSrgbHex(double r, double g, double b, string expected)
    {
        Assert.Equal(expected, ItemAppearanceRules.ToHex(r, g, b));
    }

    [Theory]
    [InlineData("#AAB4BE", "#aab4be", false)]
    [InlineData("#AAB4BE", "#AAB4BF", true)]
    [InlineData(null, null, false)]
    [InlineData("#AAB4BE", null, true)]
    public void ColourDifferenceIgnoresCase(string a, string b, bool expected)
    {
        Assert.Equal(expected, ItemAppearanceRules.ColorDiffers(a, b));
    }

    [Fact]
    public void ExactlyOneTargetIsRequired()
    {
        Assert.Contains("useSelection", ItemAppearanceRules.DescribeTargetError(null, null), StringComparison.Ordinal);
        Assert.Contains("not both", ItemAppearanceRules.DescribeTargetError(new List<string> { "h1" }, true), StringComparison.Ordinal);
        Assert.Null(ItemAppearanceRules.DescribeTargetError(new List<string> { "h1" }, null));
        Assert.Null(ItemAppearanceRules.DescribeTargetError(new List<string>(), true));
        Assert.Contains("useSelection", ItemAppearanceRules.DescribeTargetError(new List<string> { " " }, false), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, 50)]
    [InlineData(0, 1)]
    [InlineData(10, 10)]
    [InlineData(5000, 500)]
    public void MaxItemsIsBounded(int? value, int expected)
    {
        Assert.Equal(expected, ItemAppearanceRules.ClampMaxItems(value));
    }

    [Fact]
    public void ContractsCarryOriginalAndCurrent()
    {
        var info = new ItemAppearanceInfo
        {
            OriginalColorHex = "#AAB4BE", OriginalTransparency = 16,
            CurrentColorHex = "#FF0000", CurrentTransparency = 16,
            ColorOverridden = true, TransparencyOverridden = false,
        };
        var response = new ItemAppearanceResponse { ItemCount = 1, OverriddenItemCount = 1, Truncated = false };
        response.Items.Add(info);
        Assert.Single(response.Items);
        Assert.Empty(response.Warnings);

        var reset = new ResetAppearanceResponse { Apply = true, ItemCount = 3, OverriddenItemCount = 2, ResetItemCount = 2 };
        Assert.Equal(2, reset.ResetItemCount);
        Assert.Empty(reset.Warnings);
        Assert.True(new ResetAppearanceRequest { UseSelection = true, Apply = true }.Apply);
        Assert.Equal(20, new ItemAppearanceRequest { MaxItems = 20 }.MaxItems);
    }

    [Fact]
    public void HostCommandsAreNamedAndTagged()
    {
        Assert.Equal("item_appearance", HostCommandNames.ItemAppearance);
        Assert.Equal("reset_appearance", HostCommandNames.ResetAppearance);
    }

    [Fact]
    public void ReadIsReadOnlyAndResetIsADryRunFirstWrite()
    {
        var read = Tool("ItemAppearance", out var readMethod);
        Assert.Equal("item_appearance", read.ProtocolTool.Name);
        Assert.Equal(ToolEffects.None, readMethod.GetCustomAttribute<ToolCapabilitiesAttribute>()!.Effects);
        Assert.Contains("override", read.ProtocolTool.Description ?? "", StringComparison.OrdinalIgnoreCase);

        var reset = Tool("ResetAppearance", out var resetMethod);
        Assert.Equal("reset_appearance", reset.ProtocolTool.Name);
        Assert.Equal(ToolEffects.Document, resetMethod.GetCustomAttribute<ToolCapabilitiesAttribute>()!.Effects);
        var schema = JsonNode.Parse(reset.ProtocolTool.InputSchema.GetRawText())!;
        Assert.False(schema["properties"]!["apply"]!["default"]!.GetValue<bool>());
        Assert.Contains("model_color_scheme", reset.ProtocolTool.Description ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void ThePluginResetsPermanentMaterialsInOneTransaction()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "ItemAppearanceService.cs"));
        Assert.Contains("ResetPermanentMaterials(", service, StringComparison.Ordinal);
        Assert.Contains("BeginTransaction(", service, StringComparison.Ordinal);
        Assert.Contains("ItemAppearanceRules.", service, StringComparison.Ordinal);
    }

    private static McpServerTool Tool(string methodName, out MethodInfo method)
    {
        method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == methodName);
        return McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
    }
}
