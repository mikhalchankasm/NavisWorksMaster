using NavisHelper.Agent.Contracts;
using System;
using System.Collections.Generic;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashGroupDisplayPolicyTests
{
    [Fact]
    public void FormatDistinctNames_ReturnsQuestionMarkForEmptyInput()
    {
        Assert.Equal("?", ClashGroupDisplayPolicy.FormatDistinctNames(Array.Empty<string>()));
    }

    [Fact]
    public void FormatDistinctNames_ReturnsQuestionMarkWithoutUsableNames()
    {
        Assert.Equal("?", ClashGroupDisplayPolicy.FormatDistinctNames(new[] { "", " ", "(нет)" }));
    }

    [Fact]
    public void FormatDistinctNames_PreservesFirstCaseInsensitiveDistinctValues()
    {
        var result = ClashGroupDisplayPolicy.FormatDistinctNames(
            new[] { "Pump", "pump", "Valve", "PIPE", "Pipe" });

        Assert.Equal("Pump, Valve, PIPE", result);
    }

    [Fact]
    public void FormatDistinctNames_AppendsSuffixAfterThreeDistinctNames()
    {
        var result = ClashGroupDisplayPolicy.FormatDistinctNames(
            new[] { "A", "B", "C", "D", "E" });

        Assert.Equal("A, B, C, ...", result);
    }

    [Fact]
    public void FormatDistinctNames_PreservesCaseSensitiveMissingPlaceholderRule()
    {
        Assert.Equal("(НЕТ)", ClashGroupDisplayPolicy.FormatDistinctNames(new[] { "(нет)", "(НЕТ)" }));
    }

    [Fact]
    public void FormatDistinctNames_PreservesLegacySecondEnumerationForSuffix()
    {
        var enumerationCount = 0;

        IEnumerable<string> Names()
        {
            enumerationCount++;
            yield return "A";
            yield return "B";
            yield return "C";
            yield return "D";
        }

        Assert.Equal("A, B, C, ...", ClashGroupDisplayPolicy.FormatDistinctNames(Names()));
        Assert.Equal(2, enumerationCount);
    }

    [Fact]
    public void FormatDistinctNames_EnumeratesOnceWhenPreviewIsEmpty()
    {
        var enumerationCount = 0;

        IEnumerable<string> Names()
        {
            enumerationCount++;
            yield return null;
            yield return " ";
            yield return ClashGroupDisplayPolicy.MissingItemName;
        }

        Assert.Equal("?", ClashGroupDisplayPolicy.FormatDistinctNames(Names()));
        Assert.Equal(1, enumerationCount);
    }

    [Fact]
    public void FormatDistinctNames_DoesNotAppendSuffixForExactlyThreeDistinctNames()
    {
        Assert.Equal("A, B, C", ClashGroupDisplayPolicy.FormatDistinctNames(new[] { "A", "B", "C" }));
    }

    [Fact]
    public void FormatDistinctNames_DuplicatesAfterPreviewDoNotAppendSuffix()
    {
        Assert.Equal("A, B, C", ClashGroupDisplayPolicy.FormatDistinctNames(new[] { "A", "B", "C", "a", "b" }));
    }

    [Fact]
    public void CountDistinctNames_UsesSameFilterAndComparer()
    {
        Assert.Equal(3, ClashGroupDisplayPolicy.CountDistinctNames(
            new[] { null, " ", "(нет)", "Pump", "pump", "Valve", "(НЕТ)" }));
    }

    [Fact]
    public void NullInputs_PreserveLegacyArgumentNullExceptions()
    {
        Assert.Throws<ArgumentNullException>(() => ClashGroupDisplayPolicy.FormatDistinctNames(null));
        Assert.Throws<ArgumentNullException>(() => ClashGroupDisplayPolicy.CountDistinctNames(null));
    }
}
