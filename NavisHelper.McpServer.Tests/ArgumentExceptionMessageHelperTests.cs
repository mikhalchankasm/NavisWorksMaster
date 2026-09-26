using System;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ArgumentExceptionMessageHelperTests
{
    [Theory]
    [InlineData("up must be non-zero.")]
    [InlineData("cameraPosition must differ from cameraTarget.")]
    [InlineData("vector (degenerate)")]
    public void StripsTheSuffixTheRunningRuntimeAppends(string message)
    {
        var raw = new ArgumentException(message, "request").Message;

        Assert.NotEqual(message, raw);
        Assert.Equal(message, ArgumentExceptionMessageHelper.StripParameterNameSuffix(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("up must be non-zero.")]
    [InlineData("Multi\r\nline message that carries no appended identification text.")]
    public void ReturnsMessagesWithoutTheSuffixUnchanged(string message)
    {
        Assert.Equal(message, ArgumentExceptionMessageHelper.StripParameterNameSuffix(message));
    }

    [Fact]
    public void StripsOnlyTheSuffixWhenTheMessageAlreadyContainsParentheses()
    {
        var raw = new ArgumentException("camera (orthographic).", "up").Message;

        Assert.Equal("camera (orthographic).", ArgumentExceptionMessageHelper.StripParameterNameSuffix(raw));
    }

    [Fact]
    public void LeavesTextUnchangedWhenTheRuntimeSuffixAppearsOnlyMidMessage()
    {
        var embedded = new ArgumentException("inner", "request").Message;
        var message = "context: " + embedded + " followed by more words";

        Assert.Equal(message, ArgumentExceptionMessageHelper.StripParameterNameSuffix(message));
    }

    [Fact]
    public void HandlesParameterIdentifiersOfDifferentLengths()
    {
        foreach (var parameterName in new[] { "a", "request", "veryLongParameterIdentifier" })
        {
            var raw = new ArgumentException("value is invalid.", parameterName).Message;

            Assert.Equal("value is invalid.", ArgumentExceptionMessageHelper.StripParameterNameSuffix(raw));
        }
    }

    [Fact]
    public void LeavesACompositeMessageIntactWhenOnlyAnEarlierSegmentCarriesTheSuffix()
    {
        var applyRaw = new ArgumentException("cannot move the camera.", "cameraState").Message;
        var message = "camera_state_restore_failed. Apply error: " + applyRaw
            + " Restore error: viewpoint lookup failed (name 'Main')";

        Assert.Equal(message, ArgumentExceptionMessageHelper.StripParameterNameSuffix(message));
    }

    [Fact]
    public void StillStripsTheSuffixWhenACompositeMessageEndsWithIt()
    {
        var restoreRaw = new ArgumentException("cannot move the camera.", "cameraState").Message;
        var message = "camera_state_restore_failed. Apply error: view is degenerate Restore error: " + restoreRaw;

        Assert.Equal(
            "camera_state_restore_failed. Apply error: view is degenerate Restore error: cannot move the camera.",
            ArgumentExceptionMessageHelper.StripParameterNameSuffix(message));
    }
}
