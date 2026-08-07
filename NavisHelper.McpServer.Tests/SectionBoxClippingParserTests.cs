using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SectionBoxClippingParserTests
{
    [Fact]
    public void Parse_EnabledOrientedBox_ReturnsTypedValues()
    {
        var parsed = SectionBoxClippingParser.Parse(
            "{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"OrientedBox\":{" +
            "\"Type\":\"OrientedBox3D\",\"Version\":1,\"Box\":[[10,20,30],[14,28,42]]," +
            "\"Rotation\":[0.1,-0.2,0.3]},\"Enabled\":true}");

        Assert.Equal(10, parsed.Minimum.X);
        Assert.Equal(42, parsed.Maximum.Z);
        Assert.Equal(0.3, parsed.EulerRadians.Z, 12);
    }

    [Fact]
    public void Parse_DisabledBox_ReturnsTypedNotEnabledError()
    {
        var error = Assert.Throws<SectionBoxParseException>(() => SectionBoxClippingParser.Parse(
            "{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"OrientedBox\":{" +
            "\"Type\":\"OrientedBox3D\",\"Version\":1,\"Box\":[[0,0,0],[1,1,1]]," +
            "\"Rotation\":[0,0,0]},\"Enabled\":false}"));

        Assert.Equal(ErrorCodes.SectionBoxNotEnabled, error.ErrorCode);
    }

    [Fact]
    public void Parse_PlaneMode_ReturnsTypedModeError()
    {
        var error = Assert.Throws<SectionBoxParseException>(() => SectionBoxClippingParser.Parse(
            "{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"Planes\":[],\"Enabled\":true}"));

        Assert.Equal(ErrorCodes.SectionBoxModeUnsupported, error.ErrorCode);
    }

    [Fact]
    public void Parse_PlaneModeWithRetainedOrientedBox_FailsClosed()
    {
        var error = Assert.Throws<SectionBoxParseException>(() => SectionBoxClippingParser.Parse(
            "{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"Planes\":[{}],\"OrientedBox\":{" +
            "\"Type\":\"OrientedBox3D\",\"Version\":1,\"Box\":[[0,0,0],[1,1,1]]," +
            "\"Rotation\":[0,0,0]},\"Enabled\":true}"));

        Assert.Equal(ErrorCodes.SectionBoxModeUnsupported, error.ErrorCode);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"Type\":\"ClipPlaneSet\",\"Version\":2,\"Enabled\":true}")]
    [InlineData("{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"OrientedBox\":{},\"Enabled\":true}")]
    [InlineData("{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"Enabled\":true,\"Enabled\":false}")]
    public void Parse_MalformedOrUnsupportedPayload_ReturnsTypedPayloadError(string payload)
    {
        var error = Assert.Throws<SectionBoxParseException>(() => SectionBoxClippingParser.Parse(payload));

        Assert.Equal(ErrorCodes.SectionBoxPayloadUnsupported, error.ErrorCode);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void Parse_NonFiniteNumbers_AreRejected(string value)
    {
        var payload = "{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"OrientedBox\":{" +
                      "\"Type\":\"OrientedBox3D\",\"Version\":1,\"Box\":[[0,0,0],[1,1,1]]," +
                      "\"Rotation\":[0,0," + value + "]},\"Enabled\":true}";

        var error = Assert.Throws<SectionBoxParseException>(() => SectionBoxClippingParser.Parse(payload));

        Assert.Equal(ErrorCodes.SectionBoxPayloadUnsupported, error.ErrorCode);
    }
}
