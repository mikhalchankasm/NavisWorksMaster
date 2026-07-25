using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SavedItemNamePolicyTests
{
    [Theory]
    [InlineData(null, "Fallback", "Fallback")]
    [InlineData("", "Fallback", "Fallback")]
    [InlineData("   ", "Fallback", "Fallback")]
    [InlineData("  Pumps  ", "Fallback", "Pumps")]
    [InlineData(" A\r\nB\t ", "Fallback", "AB")]
    public void Normalize_PreservesFallbackTrimAndControlCharacterRules(string value, string fallback, string expected)
    {
        Assert.Equal(expected, SavedItemNamePolicy.Normalize(value, fallback));
    }

    [Fact]
    public void Normalize_UsesFallbackAfterControlCharactersRemoveEntireValue()
    {
        Assert.Equal("Fallback", SavedItemNamePolicy.Normalize("\r\n\t", "Fallback"));
    }

    [Fact]
    public void Normalize_PreservesLegacyUnnormalizedSecondFallbackAssignment()
    {
        Assert.Equal("\r", SavedItemNamePolicy.Normalize("\n", "\r"));
    }

    [Theory]
    [InlineData(119, 119)]
    [InlineData(120, 120)]
    [InlineData(121, 120)]
    public void Normalize_AppliesLegacy120CharacterLimit(int inputLength, int expectedLength)
    {
        Assert.Equal(expectedLength, SavedItemNamePolicy.Normalize(new string('x', inputLength), "Fallback").Length);
    }

    [Fact]
    public void Normalize_TrimsWhitespaceExposedByTruncation()
    {
        var value = new string('x', 117) + "   " + new string('y', 10);

        Assert.Equal(new string('x', 117), SavedItemNamePolicy.Normalize(value, "Fallback"));
    }

    [Fact]
    public void Normalize_DoesNotSplitSurrogatePairAtBoundary()
    {
        var value = new string('x', 119) + "\uD83D\uDE00";

        var result = SavedItemNamePolicy.Normalize(value, "Fallback");

        Assert.Equal(new string('x', 119), result);
    }

    [Fact]
    public void Normalize_KeepsCompleteSurrogatePairWhenItFitsExactly()
    {
        var value = new string('x', 118) + "\uD83D\uDE00";

        var result = SavedItemNamePolicy.Normalize(value, "Fallback");

        Assert.Equal(value, result);
        Assert.Equal(120, result.Length);
    }

    [Fact]
    public void Normalize_KeepsCompletePairEndingAtLimitDuringTruncation()
    {
        var expected = new string('x', 118) + "\uD83D\uDE00";

        var result = SavedItemNamePolicy.Normalize(expected + new string('y', 5), "Fallback");

        Assert.Equal(expected, result);
        Assert.Equal(SavedItemNamePolicy.DefaultMaxLength, result.Length);
    }

    [Fact]
    public void Normalize_PreservesLegacyLoneHighSurrogateAtLimit()
    {
        var value = new string('x', 119) + '\uD83D' + 'y';

        var result = SavedItemNamePolicy.Normalize(value, "Fallback");

        Assert.Equal(SavedItemNamePolicy.DefaultMaxLength, result.Length);
        Assert.True(char.IsHighSurrogate(result[result.Length - 1]));
    }

    [Fact]
    public void Normalize_UsesNormalLimitWhenLowSurrogateFollowsNonHighCharacter()
    {
        var expected = new string('x', 120);

        Assert.Equal(expected, SavedItemNamePolicy.Normalize(expected + '\uDE00' + 'y', "Fallback"));
    }

    [Fact]
    public void Normalize_TrimsWhitespaceExposedBySurrogateSafeCut()
    {
        var value = new string('x', 117) + "  " + "\uD83D\uDE00" + 'y';

        Assert.Equal(new string('x', 117), SavedItemNamePolicy.Normalize(value, "Fallback"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\u0001")]
    public void Normalize_CharacterizesLegacyNullFallbackException(string value)
    {
        Assert.Throws<System.NullReferenceException>(() => SavedItemNamePolicy.Normalize(value, null));
    }

    [Fact]
    public void MakeUnique_ReturnsNormalizedBaseWhenAvailable()
    {
        var overflowCalled = false;

        var result = SavedItemNamePolicy.MakeUnique(
            "  Pumps  ",
            "Fallback",
            _ => false,
            () => { overflowCalled = true; return "token"; });

        Assert.Equal("Pumps", result);
        Assert.False(overflowCalled);
    }

    [Fact]
    public void MakeUnique_StartsNumericSuffixAtTwoAndPreservesProbeOrder()
    {
        var probes = new System.Collections.Generic.List<string>();

        var result = SavedItemNamePolicy.MakeUnique(
            "Conflict",
            "Fallback",
            name =>
            {
                probes.Add(name);
                return name == "Conflict" || name == "Conflict (2)";
            },
            () => "token");

        Assert.Equal("Conflict (3)", result);
        Assert.Equal(new[] { "Conflict", "Conflict (2)", "Conflict (3)" }, probes);
    }

    [Fact]
    public void MakeUnique_UsesOverflowTokenOnlyAfterAllNumericCandidatesExist()
    {
        var probeCount = 0;
        var tokenCalls = 0;

        var result = SavedItemNamePolicy.MakeUnique(
            "Conflict",
            "Fallback",
            _ => { probeCount++; return true; },
            () => { tokenCalls++; return "deadbeef"; });

        Assert.Equal("Conflict deadbeef", result);
        Assert.Equal(9999, probeCount);
        Assert.Equal(1, tokenCalls);
    }
}
