using System;
using System.IO;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// Regression cover for the whole-model pruning contract.
///
/// Search.PruneBelowMatch defaults to true, and the native whole-model path has
/// always run with that default, so scope=whole_model + matchDepth=all returns
/// the shallowest match on each branch while the manual scoped traversal with
/// matchDepth=all does not prune. Measured on 6501.5.nwd, Item/Name contains
/// "Copy-of-" returned 57 whole-model against 1626 under /STORE alone.
///
/// The asymmetry is kept by decision. These tests pin that the flag is assigned
/// deliberately and that the pruned path announces itself instead of differing
/// from the scoped path in silence.
/// </summary>
public sealed class FindItemsNativeSearchPolicyTests
{
    [Fact]
    public void WholeModelSearchStaysPruned()
    {
        Assert.True(FindItemsNativeSearchPolicy.PruneBelowMatch);
    }

    [Theory]
    // whole_model + all is the only routing that reaches the pruned native search.
    [InlineData("whole_model", "all", false, false, true)]
    [InlineData("whole_model", "first", false, false, false)]
    [InlineData("whole_model", "all", true, false, false)]
    [InlineData("whole_model", "all", false, true, false)]
    [InlineData("under_handle", "all", false, false, false)]
    [InlineData("under_named_node", "all", false, false, false)]
    [InlineData("current_selection", "all", false, false, false)]
    public void UsesPrunedNativeSearch_MirrorsFindItemsRouting(
        string scope,
        string matchDepth,
        bool countOnly,
        bool requiresLiteralAnchorTraversal,
        bool expected)
    {
        Assert.Equal(
            expected,
            FindItemsNativeSearchPolicy.UsesPrunedNativeSearch(scope, matchDepth, countOnly, requiresLiteralAnchorTraversal));
    }

    [Fact]
    public void PrunedWholeModelWarning_IsRaisedOnlyWhenPruningCouldHaveDroppedSomething()
    {
        Assert.Null(FindItemsNativeSearchPolicy.BuildPrunedWholeModelWarning(false));

        var warning = FindItemsNativeSearchPolicy.BuildPrunedWholeModelWarning(true);

        Assert.Equal(FindItemsNativeSearchPolicy.WholeModelPrunedWarning, warning);
        Assert.Contains("PruneBelowMatch=true", warning, StringComparison.Ordinal);
        Assert.Contains("scope=under_handle", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchService_AssignsPruningExplicitly_AndDedupsByItemIdentity()
    {
        var root = FindRepositoryRoot();
        var execution = File.ReadAllText(
            Path.Combine(root, "NavisHelper", "Agent", "Services", "SearchService.Execution.cs"));
        var commands = File.ReadAllText(
            Path.Combine(root, "NavisHelper", "Agent", "Services", "SearchService.Commands.cs"));

        Assert.Contains(
            "search.PruneBelowMatch = FindItemsNativeSearchPolicy.PruneBelowMatch;",
            execution,
            StringComparison.Ordinal);
        Assert.Contains(
            "FindItemsNativeSearchPolicy.BuildPrunedWholeModelWarning(anyMatchHasChildren)",
            commands,
            StringComparison.Ordinal);

        // The native accumulators must key by item identity. Any reintroduction
        // of a path-keyed match map brings back the collapsed-sibling bug.
        Assert.Contains("new FindItemsMatchSet<ModelItem>()", execution, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<string, ModelItem>", execution, StringComparison.Ordinal);
        Assert.DoesNotContain("HashSet<string>(StringComparer.OrdinalIgnoreCase)", execution, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
