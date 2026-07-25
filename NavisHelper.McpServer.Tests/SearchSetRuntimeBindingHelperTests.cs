using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SearchSetRuntimeBindingHelperTests
{
    [Theory]
    [InlineData("LcOaNode")]
    [InlineData("LcOaNodeSourceFile")]
    [InlineData("lcldrvm_props")]
    public void IsRoundTrippableInternalName_AcceptsCleanNames(string value)
    {
        Assert.True(SearchSetRuntimeBindingHelper.IsRoundTrippableInternalName(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lcldrvm_prop_\uFFFD\uFFFD\uFFFD")]
    public void IsRoundTrippableInternalName_RejectsMissingOrReplacementCharacterNames(string value)
    {
        Assert.False(SearchSetRuntimeBindingHelper.IsRoundTrippableInternalName(value));
    }
}
