using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #377, from the #361 spike: one panel had planar texture mapping and its neighbours box, so a
// single pixel row was stretched over it, and nothing could read the mapping to show that.
// get_item_texture_mapping reads it per item through the texture-space internals, which every
// supported version (2024-2027) carries with the same members; the reader works on the current
// selection, so the tool selects one item at a time and restores the user's selection.
public sealed class ItemTextureMappingTests
{
    [Theory]
    [InlineData("eMAPPING_BOX", "box")]
    [InlineData("eMAPPING_PLANE", "plane")]
    [InlineData("eMAPPING_CYLINDER", "cylinder")]
    [InlineData("eMAPPING_SPHERE", "sphere")]
    [InlineData("eMAPPING_EXPLICIT", "explicit")]
    [InlineData("eMAPPING_NUM", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NativeMappingNamesBecomeContractNames(string native, string expected)
    {
        Assert.Equal(expected, TextureMappingRules.MappingName(native));
    }

    [Theory]
    [InlineData("eDIRECTION_PLUSX", "+x")]
    [InlineData("eDIRECTION_MINUSX", "-x")]
    [InlineData("eDIRECTION_PLUSY", "+y")]
    [InlineData("eDIRECTION_MINUSY", "-y")]
    [InlineData("eDIRECTION_PLUSZ", "+z")]
    [InlineData("eDIRECTION_MINUSZ", "-z")]
    [InlineData("eDIRECTION_SIDEWAYS", null)]
    [InlineData(null, null)]
    public void NativeAxisNamesBecomeSignedAxes(string native, string expected)
    {
        Assert.Equal(expected, TextureMappingRules.AxisName(native));
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(0, 1)]
    [InlineData(7, 7)]
    [InlineData(500, 100)]
    public void TheItemCountIsBounded(int? requested, int expected)
    {
        Assert.Equal(expected, TextureMappingRules.ClampMaxItems(requested));
        Assert.Equal(20, TextureMappingRules.DefaultMaxItems);
        Assert.Equal(100, TextureMappingRules.MaxItems);
    }

    [Fact]
    public void ContractsCarryEveryReadAndItsGaps()
    {
        var item = new ItemTextureMapping
        {
            Path = "Model / Panel-01",
            Mapping = "plane",
            HasTextureCoordinates = true,
            Translation = new TextureVector { X = 1, Y = 2, Z = 3 },
            Scale = new TextureVector { X = 1, Y = 1, Z = 1 },
            Rotation = new TextureRotation { A = 0, B = 0, C = 0, D = 1 },
            DomainMin = new TextureVector(),
            DomainMax = new TextureVector { X = 2, Y = 3, Z = 0 },
        };
        item.FaceDirections.Add(new TextureFaceDirection { Face = "+z", U = "+x", V = "+y" });
        var response = new GetItemTextureMappingResponse { ItemCount = 2, ReadCount = 1, SelectionRestored = true };
        response.Items.Add(item);
        response.Items.Add(new ItemTextureMapping { Path = "Model / Panel-02", Error = "no texture space" });
        Assert.Null(response.Items[1].Mapping);
        Assert.Null(response.Items[1].HasTextureCoordinates);
        Assert.Null(response.Items[1].CapEnabled);
        Assert.Empty(response.Warnings);
        Assert.Empty(new GetItemTextureMappingRequest().MatchHandles);
        Assert.Equal("get_item_texture_mapping", HostCommandNames.GetItemTextureMapping);
        Assert.True(HostCommandEffects.IsKnown(HostCommandNames.GetItemTextureMapping));
        Assert.False(HostCommandEffects.RequiresApproval(HostCommandNames.GetItemTextureMapping));
    }

    [Fact]
    public void TheToolIsReadOnlyAndTakesHandles()
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == "GetItemTextureMapping");
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
        Assert.Equal("get_item_texture_mapping", tool.ProtocolTool.Name);
        Assert.Equal(ToolEffects.None, method.GetCustomAttribute<ToolCapabilitiesAttribute>()!.Effects);
        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!;
        Assert.Contains("matchHandles", schema["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Contains("selection", tool.ProtocolTool.Description ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThePluginReadsTheSelectedTextureSpaceAndRestoresTheSelection()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "ItemTextureMappingService.cs"));
        Assert.Contains("LcOpState.GetActiveInstance(", service, StringComparison.Ordinal);
        Assert.Contains("LcOpTextureSpaceElement.GetSelectedTextureSpaceMapping(", service, StringComparison.Ordinal);
        Assert.Contains("LcOpTextureSpaceElement.CheckSelectedHasTextureCoordinates(", service, StringComparison.Ordinal);
        Assert.Contains("LcOpTextureSpaceElement.GetSelectedTextureSpace(", service, StringComparison.Ordinal);
        Assert.Contains("CurrentSelection", service, StringComparison.Ordinal);
        Assert.Contains("finally", service, StringComparison.Ordinal);
        Assert.Contains("TextureMappingRules.", service, StringComparison.Ordinal);
    }
}
