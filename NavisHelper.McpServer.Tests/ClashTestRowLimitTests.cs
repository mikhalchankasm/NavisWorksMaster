using System.Reflection;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #530 (#427 slice): on the Port clash run (825 pair tests, 2026-11-17) clash_pair_tests_create answered
// 1.77 M characters and clash_run_status 130-137 k. The counters already cover every test; the size is the
// per-test rows. The MCP server now caps those rows with maxTestRows (default 25, 0 = counters only),
// problems first, and says so with testsTruncated and totalTestRowCount. The cap is applied in the server
// after the host answers, so the plugin is unchanged.
public sealed class ClashTestRowLimitTests
{
    private static ClashRunBatchResponse Run(int count, params int[] failed)
    {
        var response = new ClashRunBatchResponse { TotalTestCount = count, ProcessedTestCount = count, FailedTestCount = failed.Length };
        for (var i = 1; i <= count; i++)
            response.Tests.Add(new ClashRunTestResult
            {
                Index = i,
                TestName = "T" + i,
                Status = failed.Contains(i) ? "failed" : "completed",
            });
        return response;
    }

    [Fact]
    public void TheDefaultKeepsTwentyFiveRowsAndEveryCounter()
    {
        var response = Run(825, 300, 700);
        ClashTestRowLimit.Apply(response, null);

        Assert.Equal(25, ClashTestRowLimit.DefaultMaxRows);
        Assert.Equal(25, response.Tests.Count);
        Assert.True(response.TestsTruncated);
        Assert.Equal(825, response.TotalTestRowCount);
        Assert.Equal(825, response.TotalTestCount);
        Assert.Equal(2, response.FailedTestCount);
        // Problems first, then the rest, each in test order.
        Assert.Equal(new[] { 300, 700, 1, 2, 3 }, response.Tests.Take(5).Select(test => test.Index));
    }

    [Fact]
    public void ZeroGivesCountersOnlyAndAFittingListIsUntouched()
    {
        var none = Run(10, 4);
        ClashTestRowLimit.Apply(none, 0);
        Assert.Empty(none.Tests);
        Assert.True(none.TestsTruncated);
        Assert.Equal(10, none.TotalTestRowCount);

        var fits = Run(3, 2);
        ClashTestRowLimit.Apply(fits, null);
        Assert.Equal(new[] { 1, 2, 3 }, fits.Tests.Select(test => test.Index));
        Assert.False(fits.TestsTruncated);
        Assert.Equal(3, fits.TotalTestRowCount);
    }

    [Fact]
    public void TheLimitIsClamped()
    {
        Assert.Equal(25, ClashTestRowLimit.Clamp(null));
        Assert.Equal(0, ClashTestRowLimit.Clamp(-5));
        Assert.Equal(5000, ClashTestRowLimit.Clamp(9000));
        Assert.Equal(5000, ClashTestRowLimit.MaxRows);
        Assert.Equal(40, ClashTestRowLimit.Clamp(40));
    }

    [Theory]
    [InlineData("failed", "", false, true)]
    [InlineData("run_failed", "", false, true)]
    [InlineData("conflict", "", false, true)]
    [InlineData("skipped", "", false, true)]
    [InlineData("timed_out", "", false, true)]
    [InlineData("selection_set_reference_not_supported", "", false, true)]
    [InlineData("completed", "boom", false, true)]
    [InlineData("completed", "", true, true)]
    [InlineData("completed", "", false, false)]
    [InlineData("created", "", false, false)]
    [InlineData("applied", null, false, false)]
    [InlineData("", "", false, false)]
    public void AProblemIsAnErrorATimeoutOrAProblemStatus(string status, string? error, bool exceeded, bool problem)
    {
        Assert.Equal(problem, ClashTestRowLimit.IsProblem(status, error, exceeded));
    }

    [Fact]
    public void PairTestCreationAndTestManagementAreCappedTheSameWay()
    {
        var create = new ClashPairTestsCreateResponse { PlannedTestCount = 30, ConflictTestCount = 1 };
        for (var i = 1; i <= 30; i++)
            create.Tests.Add(new ClashPairTestPlanItem { PairIndex = i, TestName = "P" + i, Status = i == 17 ? "conflict" : "created" });
        ClashTestRowLimit.Apply(create, 5);
        Assert.Equal(new[] { 17, 1, 2, 3, 4 }, create.Tests.Select(test => test.PairIndex));
        Assert.True(create.TestsTruncated);
        Assert.Equal(30, create.TotalTestRowCount);
        Assert.Equal(30, create.PlannedTestCount);

        var manage = new ClashManageTestsResponse { MatchedTestCount = 4 };
        for (var i = 1; i <= 4; i++)
            manage.Tests.Add(new ClashTestOperationResult { TestIndex = i, Name = "M" + i, Status = "applied", ErrorMessage = i == 3 ? "locked" : "" });
        ClashTestRowLimit.Apply(manage, 2);
        Assert.Equal(new[] { 3, 1 }, manage.Tests.Select(test => test.TestIndex));
        Assert.True(manage.TestsTruncated);
        Assert.Equal(4, manage.TotalTestRowCount);
    }

    [Theory]
    [InlineData(typeof(NavisworksClashSetTools), "ClashRunBatch")]
    [InlineData(typeof(NavisworksClashSetTools), "ClashRunResume")]
    [InlineData(typeof(NavisworksClashSetTools), "ClashRunStatus")]
    [InlineData(typeof(NavisworksClashSetTools), "CancelClashRun")]
    [InlineData(typeof(NavisworksClashTestTools), "ClashManageTests")]
    [InlineData(typeof(NavisworksClashTestTools), "ClashPairTestsCreate")]
    public void EachToolTakesMaxTestRows(Type tools, string method)
    {
        var tool = tools.GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(tool);
        var limit = Assert.Single(tool!.GetParameters(), parameter => parameter.Name == "maxTestRows");
        Assert.Equal(typeof(int), limit.ParameterType);
        Assert.Equal(25, limit.DefaultValue);
    }

    [Fact]
    public void TheToolsApplyTheLimitAndTheDocNamesIt()
    {
        foreach (var file in new[] { "NavisworksClashSetTools.cs", "NavisworksClashTestTools.cs" })
        {
            var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper.McpServer", "Tools", file));
            Assert.Contains("ClashTestRowLimit.Apply(", source, StringComparison.Ordinal);
        }
        var doc = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "docs", "MCP_TOOL_CONTRACTS.md"));
        Assert.Contains("maxTestRows", doc, StringComparison.Ordinal);
        Assert.Contains("testsTruncated", doc, StringComparison.Ordinal);
    }
}
