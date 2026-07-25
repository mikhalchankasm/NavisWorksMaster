using NavisHelper.Agent.Contracts;
using System;
using System.Collections.Generic;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashBboxPlanHelperTests
{
    [Fact]
    public void MatchesRootFilters_NoFiltersAcceptsNullValues()
    {
        Assert.True(ClashBboxPlanHelper.MatchesRootFilters(null, null, null, null, null, null));
    }

    [Theory]
    [InlineData("MODEL.NWD", null, null, true)]
    [InlineData(null, "MODEL.NWD", null, true)]
    [InlineData(null, null, "MODEL.NWD", true)]
    [InlineData(null, "Root / MODEL.NWD", null, false)]
    [InlineData(null, null, "C:\\MODEL.NWD", false)]
    [InlineData("other.nwd", "other", "other", false)]
    public void MatchesRootFilters_RootNamesUseCaseInsensitiveExactMatch(string name, string path, string source, bool expected)
    {
        Assert.Equal(expected, ClashBboxPlanHelper.MatchesRootFilters(name, path, source, new[] { "model.nwd" }, null, null));
    }

    [Theory]
    [InlineData("Pump A", null, null, "pump", true)]
    [InlineData(null, "Root / PUMP", null, "pump", true)]
    [InlineData(null, null, "pump.nwd", "PUMP", true)]
    [InlineData("Valve", "Root", "model.nwd", "pump", false)]
    [InlineData("Valve", "Root", "model.nwd", " ", true)]
    public void MatchesRootFilters_NameContainsSearchesAllValues(string name, string path, string source, string query, bool expected)
    {
        Assert.Equal(expected, ClashBboxPlanHelper.MatchesRootFilters(name, path, source, null, query, null));
    }

    [Fact]
    public void MatchesRootFilters_ExclusionWinsAfterPositiveFilters()
    {
        Assert.False(ClashBboxPlanHelper.MatchesRootFilters("Pump A", "Root", "model.nwd", new[] { "Pump A" }, "pump", new[] { "MODEL" }));
    }

    [Fact]
    public void MatchesRootFilters_BlankExclusionPreservesLegacyRejectAllBehavior()
    {
        Assert.False(ClashBboxPlanHelper.MatchesRootFilters("Pump", null, null, null, null, new[] { "" }));
    }

    [Fact]
    public void MatchesRootFilters_BlankRootNameMatchesAnyBlankCandidateValue()
    {
        Assert.True(ClashBboxPlanHelper.MatchesRootFilters("Pump", null, "model.nwd", new[] { "" }, null, null));
        Assert.False(ClashBboxPlanHelper.MatchesRootFilters("Pump", "Root", "model.nwd", new[] { "" }, null, null));
    }

    [Fact]
    public void BuildPreview_NullReturnsNull()
    {
        Assert.Null(ClashBboxPlanHelper.BuildPreview(null, 5, true));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void BuildPreview_TruncatesCollectionsAndControlsRejectedRows(bool includeRejected, bool expectedRejected)
    {
        var full = CreatePlan(3);

        var result = ClashBboxPlanHelper.BuildPreview(full, 2, includeRejected);

        Assert.Equal(2, result.RootItems.Count);
        Assert.Equal(2, result.CandidatePairs.Count);
        Assert.Equal(expectedRejected ? 2 : 0, result.RejectedPairs.Count);
        Assert.True(result.PreviewTruncated);
        Assert.Equal(full.RootMode, result.RootMode);
        Assert.Equal(full.Applied, result.Applied);
        Assert.Equal(full.TargetRootName, result.TargetRootName);
        Assert.Equal(full.TargetRootMatchCount, result.TargetRootMatchCount);
        Assert.Equal(full.TargetFilteredPairCount, result.TargetFilteredPairCount);
        Assert.Equal(full.RefineDepth, result.RefineDepth);
        Assert.Equal(full.BboxToleranceMm, result.BboxToleranceMm);
        Assert.Equal(full.TotalRootItems, result.TotalRootItems);
        Assert.Equal(full.ReturnedRootItems, result.ReturnedRootItems);
        Assert.Equal(full.RootItemsTruncated, result.RootItemsTruncated);
        Assert.Equal(full.RootPairCount, result.RootPairCount);
        Assert.Equal(full.CandidatePairCount, result.CandidatePairCount);
        Assert.Equal(full.SkippedPairCount, result.SkippedPairCount);
        Assert.Equal(full.CandidatePairsTruncated, result.CandidatePairsTruncated);
        Assert.Equal(full.ElapsedMs, result.ElapsedMs);
        Assert.Equal(full.OutputPath, result.OutputPath);
        Assert.Equal(full.Warnings, result.Warnings);
        Assert.NotSame(full.Warnings, result.Warnings);
        Assert.Same(full.RootItems[0], result.RootItems[0]);
    }

    [Fact]
    public void BuildPreview_RejectedOverflowIsIgnoredWhenRejectedRowsExcluded()
    {
        var full = CreatePlan(1);
        full.RejectedPairs.Add(new ClashBboxRejectedPair());

        Assert.False(ClashBboxPlanHelper.BuildPreview(full, 1, false).PreviewTruncated);
        Assert.True(ClashBboxPlanHelper.BuildPreview(full, 1, true).PreviewTruncated);
    }

    [Fact]
    public void BuildPreview_CopiesReasonCountsWithCaseInsensitiveComparer()
    {
        var result = ClashBboxPlanHelper.BuildPreview(CreatePlan(1), 1, false);

        Assert.Equal(2, result.SkippedReasonCounts["OUTSIDE"]);
        result.SkippedReasonCounts["outside"] = 3;
        Assert.Single(result.SkippedReasonCounts);
        Assert.Equal(3, result.SkippedReasonCounts["OUTSIDE"]);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("a\r\nb", "\"a\r\nb\"")]
    public void EscapeCsv_PreservesLegacyQuoting(string value, string expected)
    {
        Assert.Equal(expected, ClashBboxPlanHelper.EscapeCsv(value));
    }

    [Fact]
    public void BuildCsvRow_PreservesColumnOrderNullsAndInvariantNumbers()
    {
        var pair = new ClashBboxCandidatePair
        {
            Index = 7,
            A = new ClashBboxRootItem { Name = "A,1", Path = "Root/A" },
            B = null,
            CheckedChildPairCount = 12,
            ChildIntersectingPairCount = 3,
            Reason = "hit\"reason",
        };

        Assert.Equal("7,\"A,1\",Root/A,,,12,3,\"hit\"\"reason\"", ClashBboxPlanHelper.BuildCsvRow(pair));
    }

    [Fact]
    public void BuildCsvRow_NullPairPreservesLegacyDereferenceFailure()
    {
        Assert.Throws<NullReferenceException>(() => ClashBboxPlanHelper.BuildCsvRow(null));
    }

    private static ClashBboxPairPlanResponse CreatePlan(int count)
    {
        var plan = new ClashBboxPairPlanResponse
        {
            Applied = true,
            RootMode = "top_level_files",
            TargetRootName = "Target",
            TargetRootMatchCount = 1,
            TargetFilteredPairCount = 7,
            RefineDepth = 2,
            BboxToleranceMm = 1.5,
            TotalRootItems = 9,
            ReturnedRootItems = count,
            RootItemsTruncated = true,
            RootPairCount = 12,
            CandidatePairCount = count,
            SkippedPairCount = 4,
            CandidatePairsTruncated = true,
            ElapsedMs = 99,
            OutputPath = "plan.csv",
            Warnings = new List<string> { "warning" },
            SkippedReasonCounts = new Dictionary<string, int> { ["outside"] = 2 },
        };
        for (var i = 0; i < count; i++)
        {
            var root = new ClashBboxRootItem { Index = i, Name = "Root " + i };
            plan.RootItems.Add(root);
            plan.CandidatePairs.Add(new ClashBboxCandidatePair { Index = i, A = root });
            plan.RejectedPairs.Add(new ClashBboxRejectedPair { Index = i, A = root });
        }
        return plan;
    }
}
