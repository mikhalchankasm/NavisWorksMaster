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
    // whole_model + all is the only routing that reaches the pruned native search,
    // and countOnly no longer excludes itself from it: a traversal of ~270k nodes
    // exceeds the 45 second budget every time on this repository's reference model,
    // so the call used to fail rather than answer.
    [InlineData("whole_model", "all", false, true)]
    [InlineData("whole_model", "first", false, false)]
    [InlineData("whole_model", "all", true, false)]
    [InlineData("under_handle", "all", false, false)]
    [InlineData("under_named_node", "all", false, false)]
    [InlineData("current_selection", "all", false, false)]
    public void UsesPrunedNativeSearch_MirrorsFindItemsRouting(
        string scope,
        string matchDepth,
        bool requiresLiteralAnchorTraversal,
        bool expected)
    {
        Assert.Equal(
            expected,
            FindItemsNativeSearchPolicy.UsesPrunedNativeSearch(scope, matchDepth, requiresLiteralAnchorTraversal));
    }

    [Fact]
    public void WholeModelCountOnlyWarning_names_what_the_engine_cannot_report()
    {
        var warning = FindItemsNativeSearchPolicy.WholeModelCountOnlyWarning;

        // A zero must not be readable as "nothing was scanned".
        Assert.Contains("scannedItemCount is 0", warning, StringComparison.Ordinal);
        Assert.Contains("matchedItemCount", warning, StringComparison.Ordinal);
        Assert.Contains("scope=under_handle", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Whole_model_count_only_is_not_routed_to_the_traversal()
    {
        // The routing condition in SearchService.FindItems must not send countOnly
        // to ExecuteScopedFindItems for whole_model, or the fix is undone while
        // every policy test still passes.
        var source = ReadRepositoryFile("NavisHelper/Agent/Services/SearchService.Commands.cs");

        var at = source.IndexOf("return ExecuteScopedFindItems(", StringComparison.Ordinal);
        Assert.True(at >= 0, "the scoped routing call was renamed; re-point this guard.");

        var conditionStart = source.LastIndexOf("if (scope != FindItemsScopes.WholeModel", at, StringComparison.Ordinal);
        Assert.True(conditionStart >= 0, "the scoped routing condition was reshaped; re-point this guard.");

        var condition = source.Substring(conditionStart, at - conditionStart);
        Assert.DoesNotContain("CountOnly", condition, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), path + " is missing; re-point this guard.");
        return File.ReadAllText(path);
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
