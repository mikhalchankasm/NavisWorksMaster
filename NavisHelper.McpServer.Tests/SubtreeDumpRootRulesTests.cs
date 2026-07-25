using NavisHelper.Agent.Contracts;
using System;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SubtreeDumpRootRulesTests
{
    [Theory]
    [InlineData("model.rvm", true)]
    [InlineData(" MODEL.DWG ", true)]
    [InlineData("model.NwD", true)]
    [InlineData("model.nwf", true)]
    [InlineData("model.nwc", false)]
    [InlineData("model.rvm.bak", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsModelFileName_PreservesLegacyExtensions(string value, bool expected)
    {
        Assert.Equal(expected, SubtreeDumpRootRules.IsModelFileName(value));
    }

    [Theory]
    [InlineData("source.ifc", "node", true)]
    [InlineData(" ", "model.nwd", true)]
    [InlineData("", "node", false)]
    [InlineData(null, null, false)]
    public void IsCandidate_UsesOwnSourceOrModelDisplayName(string source, string display, bool expected)
    {
        Assert.Equal(expected, SubtreeDumpRootRules.IsCandidate(source, display));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData(" model.nwd ", "model.nwd")]
    [InlineData("C:\\Models\\model.nwd", "model.nwd")]
    [InlineData("/models/model.nwd", "model.nwd")]
    [InlineData("C:\\Models\\", "C:\\Models\\")]
    [InlineData("/", "/")]
    public void GetContentFileName_PreservesSeparatorAndTrailingSeparatorRules(string value, string expected)
    {
        Assert.Equal(expected, SubtreeDumpRootRules.GetContentFileName(value));
    }

    [Fact]
    public void BuildAliases_TrimsDeduplicatesAndAddsBothFileNames()
    {
        var aliases = SubtreeDumpRootRules.BuildAliases(" C:\\Models\\MODEL.NWD ", "/imports/model.nwd");

        Assert.Equal(3, aliases.Count);
        Assert.Equal("C:\\Models\\MODEL.NWD", aliases[0]);
        Assert.Equal("/imports/model.nwd", aliases[1]);
        Assert.Equal("MODEL.NWD", aliases[2]);
    }

    [Fact]
    public void BuildAliases_IgnoresBlankValues()
    {
        Assert.Empty(SubtreeDumpRootRules.BuildAliases(" ", null));
    }

    [Fact]
    public void BuildAliases_DeduplicatesSamePathIgnoringCaseBeforeAddingFileName()
    {
        var aliases = SubtreeDumpRootRules.BuildAliases("C:\\Models\\MODEL.NWD", "c:\\models\\model.nwd");

        Assert.Equal(2, aliases.Count);
        Assert.Equal("C:\\Models\\MODEL.NWD", aliases[0]);
        Assert.Equal("MODEL.NWD", aliases[1]);
    }

    [Theory]
    [InlineData("MODEL.NWD", true)]
    [InlineData(" model.nwd ", false)]
    [InlineData("other.nwd", false)]
    [InlineData("", false)]
    public void Matches_IsCaseInsensitiveButDoesNotTrimQuery(string value, bool expected)
    {
        var aliases = new[] { "model.nwd" };

        Assert.Equal(expected, SubtreeDumpRootRules.Matches(aliases, value));
    }

    [Fact]
    public void Matches_NullAliasesReturnsFalse()
    {
        Assert.False(SubtreeDumpRootRules.Matches(null, "model.nwd"));
    }
}
