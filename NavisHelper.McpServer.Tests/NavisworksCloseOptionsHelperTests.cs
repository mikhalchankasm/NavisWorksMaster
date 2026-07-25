using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class NavisworksCloseOptionsHelperTests
{
    [Theory]
    [InlineData(null, "prompt")]
    [InlineData("", "prompt")]
    [InlineData(" PROMPT ", "prompt")]
    [InlineData("SAVE", "save")]
    [InlineData("discard", "discard")]
    public void NormalizeMode_ReturnsStableWireValue(string value, string expected)
    {
        Assert.Equal(expected, NavisworksCloseOptionsHelper.NormalizeMode(value));
    }

    [Fact]
    public void NormalizeMode_RejectsUnknownValue()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => NavisworksCloseOptionsHelper.NormalizeMode("force"));

        Assert.Equal("mode must be prompt, save, or discard.", exception.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ValidateAuthorization_AcceptsSafeCombinations(bool apply, bool confirmClose)
    {
        NavisworksCloseOptionsHelper.ValidateAuthorization(apply, confirmClose);
    }

    [Fact]
    public void ValidateAuthorization_RejectsApplyWithoutConfirmation()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => NavisworksCloseOptionsHelper.ValidateAuthorization(true, false));

        Assert.Equal(
            "Closing Navisworks requires confirmClose=true together with apply=true.",
            exception.Message);
    }
}
