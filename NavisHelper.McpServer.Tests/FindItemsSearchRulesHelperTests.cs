using System;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class FindItemsSearchRulesHelperTests
{
    [Theory]
    [InlineData(null, "contains")]
    [InlineData(" equals ", "equals")]
    [InlineData("NOT_DEFINED", "not_defined")]
    [InlineData("starts_with", "starts_with")]
    [InlineData("endswith", "ends_with")]
    public void NormalizeComparison_UsesWireValues(string value, string expected)
    {
        Assert.Equal(expected, FindItemsSearchRulesHelper.NormalizeComparison(value));
    }

    [Fact]
    public void NormalizeComparison_RejectsUnknownValue()
    {
        var exception = Assert.Throws<ArgumentException>(() => FindItemsSearchRulesHelper.NormalizeComparison("regex"));

        Assert.Equal("Unsupported find_items comparison: regex", exception.Message);
    }

    [Theory]
    [InlineData(null, "all")]
    [InlineData("AND", "all")]
    [InlineData("or", "any")]
    [InlineData(" any ", "any")]
    public void NormalizeCombineOperator_UsesSearchValues(string value, string expected)
    {
        Assert.Equal(expected, FindItemsSearchRulesHelper.NormalizeCombineOperator(value));
    }

    [Theory]
    [InlineData("int", "int32")]
    [InlineData("System.Double", "double")]
    [InlineData("boolean", "bool")]
    [InlineData("date", "datetime")]
    [InlineData("string", "wstring")]
    [InlineData("custom", "custom")]
    [InlineData(null, null)]
    public void NormalizeDataType_UsesExistingAliases(string value, string expected)
    {
        Assert.Equal(expected, FindItemsSearchRulesHelper.NormalizeDataType(value));
    }

    [Fact]
    public void NormalizeConditionString_AppliesRequestedUnicodeRules()
    {
        Assert.Equal("Cafe", FindItemsSearchRulesHelper.NormalizeConditionString("Café", false, true));
        Assert.Equal("A", FindItemsSearchRulesHelper.NormalizeConditionString("Ａ", true, false));
        Assert.Equal("Café", FindItemsSearchRulesHelper.NormalizeConditionString("Café", false, false));
    }

    [Theory]
    [InlineData("contains", "AB-12", 4)]
    [InlineData("wildcard", "A*B?12", 4)]
    [InlineData("wildcard", "***", 0)]
    [InlineData("contains", null, 0)]
    public void CountSearchLiteralCharacters_IgnoresWildcardSyntax(string comparison, string value, int expected)
    {
        Assert.Equal(expected, FindItemsSearchRulesHelper.CountSearchLiteralCharacters(comparison, value));
    }

    [Theory]
    [InlineData("equals", true)]
    [InlineData("contains", true)]
    [InlineData("wildcard", true)]
    [InlineData("starts_with", true)]
    [InlineData("ends_with", true)]
    [InlineData("not_equals", false)]
    [InlineData("defined", false)]
    public void IsPositiveComparison_SeparatesAnchorOperators(string comparison, bool expected)
    {
        Assert.Equal(expected, FindItemsSearchRulesHelper.IsPositiveComparison(comparison));
    }

    [Theory]
    [InlineData("SBFRAMEWORK", "SB", "starts_with", true, false, false, true)]
    [InlineData("SB*FRAMEWORK", "SB*", "starts_with", true, false, false, true)]
    [InlineData("FRAMEWORK?", "WORK?", "ends_with", true, false, false, true)]
    [InlineData("Étage", "etage", "starts_with", true, false, true, true)]
    [InlineData("ＡＢ-frame", "AB-", "starts_with", true, true, false, true)]
    [InlineData("SBFRAMEWORK", "sb", "starts_with", false, false, false, false)]
    public void MatchesAnchoredText_UsesLiteralAnchorsAndTextOptions(
        string value,
        string expected,
        string comparison,
        bool ignoreCase,
        bool ignoreCharWidth,
        bool ignoreDiacritics,
        bool expectedMatch)
    {
        Assert.Equal(
            expectedMatch,
            FindItemsSearchRulesHelper.MatchesAnchoredText(
                value,
                expected,
                comparison,
                ignoreCase,
                ignoreCharWidth,
                ignoreDiacritics));
    }
}
