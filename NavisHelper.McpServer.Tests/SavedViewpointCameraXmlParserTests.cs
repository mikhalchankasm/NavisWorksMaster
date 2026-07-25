using System.Xml.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SavedViewpointCameraXmlParserTests
{
    [Fact]
    public void Parse_MissingCamera_PreservesWarningAndFails()
    {
        var result = Parse("<view name=\"Named View\"><viewpoint /></view>");

        Assert.False(result.Success);
        Assert.Equal("Skipped viewpoint without camera node: Named View", result.Warning);
    }

    [Theory]
    [InlineData("<position><pos3f x=\"1\" y=\"2\" z=\"3\"/></position>")]
    [InlineData("<rotation><quaternion a=\"1\" b=\"0\" c=\"0\" d=\"0\"/></rotation>")]
    public void Parse_MissingPositionOrRotation_PreservesWarningAndFails(string cameraContents)
    {
        var result = Parse("<view name=\"Incomplete\"><viewpoint><camera>" + cameraContents + "</camera></viewpoint></view>");

        Assert.False(result.Success);
        Assert.Equal("Skipped viewpoint without camera position or rotation: Incomplete", result.Warning);
    }

    [Fact]
    public void Parse_FullCamera_PreservesAllSupportedValues()
    {
        var result = Parse("<view name=\"Full\"><viewpoint render=\"wire\" lighting=\"headlight\" focal=\"12.5\" linear=\"2\" angular=\"3\"><camera projection=\"ortho\" near=\"1\" far=\"100\" aspect=\"1.5\" height=\"0.7\"><position><pos3f x=\"1\" y=\"2\" z=\"3\"/></position><rotation><quaternion a=\"0.1\" b=\"0.2\" c=\"0.3\" d=\"0.4\"/></rotation></camera><up><vec3f x=\"0\" y=\"1\" z=\"0\"/></up><viewer avatar=\" human \" camera_mode=\"third\"/></viewpoint></view>");

        Assert.True(result.Success);
        Assert.Null(result.Warning);
        Assert.Equal((1.0, 2.0, 3.0), (result.PositionX, result.PositionY, result.PositionZ));
        Assert.Equal((0.1, 0.2, 0.3, 0.4), (result.RotationA, result.RotationB, result.RotationC, result.RotationD));
        Assert.Equal(SavedViewpointProjectionKind.Orthographic, result.Projection);
        Assert.Equal(1, result.NearPlaneDistance);
        Assert.Equal(100, result.FarPlaneDistance);
        Assert.Equal(1.5, result.AspectRatio);
        Assert.Equal(0.7, result.HeightField);
        Assert.Equal(12.5, result.FocalDistance);
        Assert.Equal(2, result.LinearSpeed);
        Assert.Equal(3, result.AngularSpeed);
        Assert.True(result.HasWorldUpVector);
        Assert.Equal((0.0, 1.0, 0.0), (result.WorldUpX, result.WorldUpY, result.WorldUpZ));
        Assert.Equal(SavedViewpointRenderStyleKind.Wireframe, result.RenderStyle);
        Assert.Equal(SavedViewpointLightingKind.Headlight, result.Lighting);
        Assert.Equal(" human ", result.ViewerAvatar);
        Assert.Equal(SavedViewpointCameraModeKind.ThirdPerson, result.ViewerCameraMode);
    }

    [Fact]
    public void Parse_InvalidRequiredNumbers_UsesExistingComponentFallbacks()
    {
        var result = Parse("<view><viewpoint><camera><pos3f x=\"bad\" y=\"\"/><quaternion a=\"bad\" b=\"bad\" c=\"bad\" d=\"bad\"/></camera><up><vec3f x=\"bad\" y=\"bad\" z=\"bad\"/></up></viewpoint></view>");

        Assert.True(result.Success);
        Assert.Equal((0.0, 0.0, 0.0), (result.PositionX, result.PositionY, result.PositionZ));
        Assert.Equal((1.0, 0.0, 0.0, 0.0), (result.RotationA, result.RotationB, result.RotationC, result.RotationD));
        Assert.True(result.HasWorldUpVector);
        Assert.Equal((0.0, 0.0, 1.0), (result.WorldUpX, result.WorldUpY, result.WorldUpZ));
    }

    [Fact]
    public void Parse_InvalidOptionalNumbers_AreNotAppliedByAdapter()
    {
        var result = Parse("<view><viewpoint focal=\"bad\" linear=\"bad\" angular=\"bad\"><camera near=\"bad\" far=\"bad\" aspect=\"bad\" height=\"bad\"><pos3f/><quaternion/></camera></viewpoint></view>");

        Assert.True(result.Success);
        Assert.Null(result.NearPlaneDistance);
        Assert.Null(result.FarPlaneDistance);
        Assert.Null(result.AspectRatio);
        Assert.Null(result.HeightField);
        Assert.Null(result.FocalDistance);
        Assert.Null(result.LinearSpeed);
        Assert.Null(result.AngularSpeed);
    }

    [Theory]
    [InlineData("ortho", SavedViewpointProjectionKind.Orthographic)]
    [InlineData(" ORTHOGRAPHIC ", SavedViewpointProjectionKind.Orthographic)]
    [InlineData("perspective", SavedViewpointProjectionKind.Perspective)]
    [InlineData("unknown", SavedViewpointProjectionKind.Perspective)]
    [InlineData("", SavedViewpointProjectionKind.Perspective)]
    public void Parse_ProjectionAliases_PreserveMapping(string value, SavedViewpointProjectionKind expected)
    {
        Assert.Equal(expected, Parse(MinimalXml("projection=\"" + value + "\"")).Projection);
    }

    [Theory]
    [InlineData("full", SavedViewpointRenderStyleKind.FullRender)]
    [InlineData("fullrender", SavedViewpointRenderStyleKind.FullRender)]
    [InlineData(" rendered ", SavedViewpointRenderStyleKind.FullRender)]
    [InlineData("wireframe", SavedViewpointRenderStyleKind.Wireframe)]
    [InlineData("wire", SavedViewpointRenderStyleKind.Wireframe)]
    [InlineData("hiddenline", SavedViewpointRenderStyleKind.HiddenLine)]
    [InlineData("hidden_line", SavedViewpointRenderStyleKind.HiddenLine)]
    [InlineData("unknown", SavedViewpointRenderStyleKind.Shaded)]
    [InlineData("", SavedViewpointRenderStyleKind.Shaded)]
    public void Parse_RenderStyleAliases_PreserveMapping(string value, SavedViewpointRenderStyleKind expected)
    {
        Assert.Equal(expected, Parse(MinimalXml(string.Empty, "render=\"" + value + "\"")).RenderStyle);
    }

    [Theory]
    [InlineData("headlight", SavedViewpointLightingKind.Headlight)]
    [InlineData("fulllights", SavedViewpointLightingKind.FullLights)]
    [InlineData(" full ", SavedViewpointLightingKind.FullLights)]
    [InlineData("none", SavedViewpointLightingKind.None)]
    [InlineData("unknown", SavedViewpointLightingKind.SceneLights)]
    [InlineData("", SavedViewpointLightingKind.SceneLights)]
    public void Parse_LightingAliases_PreserveMapping(string value, SavedViewpointLightingKind expected)
    {
        Assert.Equal(expected, Parse(MinimalXml(string.Empty, "lighting=\"" + value + "\"")).Lighting);
    }

    [Theory]
    [InlineData("third", SavedViewpointCameraModeKind.ThirdPerson)]
    [InlineData(" FIRST ", SavedViewpointCameraModeKind.FirstPerson)]
    [InlineData("unknown", SavedViewpointCameraModeKind.Unspecified)]
    [InlineData("", SavedViewpointCameraModeKind.Unspecified)]
    public void Parse_ViewerCameraMode_PreservesMapping(string value, SavedViewpointCameraModeKind expected)
    {
        var xml = "<view><viewpoint><camera><pos3f/><quaternion/></camera><viewer camera_mode=\"" + value + "\"/></viewpoint></view>";

        Assert.Equal(expected, Parse(xml).ViewerCameraMode);
    }

    [Fact]
    public void Parse_ElementNamesIgnoreNamespaceAndCase()
    {
        var result = Parse("<n:VIEW xmlns:n=\"urn:test\"><n:VIEWPOINT><n:CAMERA projection=\"ORTHO\"><n:wrapper><n:POS3F x=\"1\"/></n:wrapper><n:wrapper><n:QUATERNION a=\"1\"/></n:wrapper></n:CAMERA><n:UP><n:wrapper><n:VEC3F z=\"1\"/></n:wrapper></n:UP><n:VIEWER camera_mode=\"FIRST\"/></n:VIEWPOINT></n:VIEW>");

        Assert.True(result.Success);
        Assert.Equal(1, result.PositionX);
        Assert.Equal(SavedViewpointProjectionKind.Orthographic, result.Projection);
        Assert.True(result.HasWorldUpVector);
        Assert.Equal(SavedViewpointCameraModeKind.FirstPerson, result.ViewerCameraMode);
    }

    [Fact]
    public void Parse_ViewpointAndCameraMustBeDirectChildren()
    {
        var result = Parse("<view name=\"Nested\"><wrapper><viewpoint><camera><pos3f/><quaternion/></camera></viewpoint></wrapper></view>");

        Assert.False(result.Success);
        Assert.Equal("Skipped viewpoint without camera node: Nested", result.Warning);
    }

    [Fact]
    public void Parse_RequiredCameraNodesUseFirstDescendant()
    {
        var result = Parse("<view><viewpoint><camera><first><pos3f x=\"1\"/><quaternion a=\"0.1\"/></first><second><pos3f x=\"9\"/><quaternion a=\"0.9\"/></second></camera></viewpoint></view>");

        Assert.Equal(1, result.PositionX);
        Assert.Equal(0.1, result.RotationA);
    }

    [Fact]
    public void Parse_ViewerWithoutAvatar_PreservesEmptyAvatarAndUnspecifiedMode()
    {
        var result = Parse("<view><viewpoint><camera><pos3f/><quaternion/></camera><viewer/></viewpoint></view>");

        Assert.Equal(string.Empty, result.ViewerAvatar);
        Assert.Equal(SavedViewpointCameraModeKind.Unspecified, result.ViewerCameraMode);
    }

    [Fact]
    public void Parse_UpWithoutVector_DoesNotApplyWorldUpVector()
    {
        var result = Parse("<view><viewpoint><camera><pos3f/><quaternion/></camera><up><wrapper/></up></viewpoint></view>");

        Assert.False(result.HasWorldUpVector);
    }

    [Fact]
    public void Parse_MultipleViewpointChildren_UsesFirstViewpoint()
    {
        var result = Parse("<view><viewpoint><camera projection=\"ortho\"><pos3f x=\"1\"/><quaternion/></camera></viewpoint><viewpoint><camera projection=\"perspective\"><pos3f x=\"9\"/><quaternion/></camera></viewpoint></view>");

        Assert.Equal(1, result.PositionX);
        Assert.Equal(SavedViewpointProjectionKind.Orthographic, result.Projection);
    }

    private static SavedViewpointCameraParseResult Parse(string xml)
    {
        return SavedViewpointCameraXmlParser.Parse(XElement.Parse(xml));
    }

    private static string MinimalXml(string cameraAttributes = "", string viewpointAttributes = "")
    {
        return "<view><viewpoint " + viewpointAttributes + "><camera " + cameraAttributes + "><pos3f/><quaternion/></camera></viewpoint></view>";
    }
}
