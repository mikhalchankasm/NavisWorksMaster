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
    // countOnly no longer excludes itself from the native path: a traversal of
    // ~270k nodes exceeds the 45 second budget every time on this repository's
    // reference model, so the call used to fail rather than answer.
    [InlineData("whole_model", "all", false, true)]
    // matchDepth=first is here for a stronger reason: engine pruning IS first, so
    // the traversal returned the same set and added only the failure. Measured live
    // on 6501.5.nwd, whole_model + first with one equals condition took 45 228 ms
    // and failed on the budget.
    [InlineData("whole_model", "first", false, true)]
    // starts_with/ends_with still force the traversal at either matchDepth.
    [InlineData("whole_model", "all", true, false)]
    [InlineData("whole_model", "first", true, false)]
    // Scoped requests are routed by FindItemsNativeScopedPolicy, not by this one.
    [InlineData("under_handle", "all", false, false)]
    [InlineData("under_handle", "first", false, false)]
    [InlineData("under_named_node", "all", false, false)]
    [InlineData("current_selection", "all", false, false)]
    public void UsesPrunedNativeSearch_IsTheFindItemsRoutingDecision(
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
        //
        // Re-pointed when the condition became a call to the policy instead of a
        // copy of it. The guard now checks more than it used to, not less: the
        // routing must BE the policy call, and the policy must not have grown a
        // countOnly parameter to decide on.
        var source = ReadRepositoryFile("NavisHelper/Agent/Services/SearchService.Commands.cs");

        var at = source.IndexOf("return ExecuteScopedFindItems(", StringComparison.Ordinal);
        Assert.True(at >= 0, "the scoped routing call was renamed; re-point this guard.");

        var conditionStart = source.LastIndexOf(
            "if (!FindItemsNativeSearchPolicy.UsesPrunedNativeSearch(",
            at,
            StringComparison.Ordinal);
        Assert.True(
            conditionStart >= 0,
            "the scoped routing no longer asks FindItemsNativeSearchPolicy; re-point this guard.");

        var condition = source.Substring(conditionStart, at - conditionStart);
        Assert.DoesNotContain("CountOnly", condition, StringComparison.Ordinal);

        // A countOnly parameter on the policy would let the decision come back by
        // the other door, with this guard still green.
        var policy = ReadRepositoryFile("NavisHelper.Contracts/FindItemsNativeSearchPolicy.cs");
        var signatureAt = policy.IndexOf("public static bool UsesPrunedNativeSearch(", StringComparison.Ordinal);
        Assert.True(signatureAt >= 0, "UsesPrunedNativeSearch was renamed; re-point this guard.");

        var signatureEnd = policy.IndexOf(')', signatureAt);
        Assert.True(signatureEnd > signatureAt, "UsesPrunedNativeSearch signature is unreadable; re-point this guard.");

        var signature = policy.Substring(signatureAt, signatureEnd - signatureAt);
        Assert.DoesNotContain("countOnly", signature, StringComparison.OrdinalIgnoreCase);
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
        Assert.Null(FindItemsNativeSearchPolicy.BuildPrunedWholeModelWarning("all", false));

        var warning = FindItemsNativeSearchPolicy.BuildPrunedWholeModelWarning("all", true);

        Assert.Equal(FindItemsNativeSearchPolicy.WholeModelPrunedWarning, warning);
        Assert.Contains("PruneBelowMatch=true", warning, StringComparison.Ordinal);
        Assert.Contains("scope=under_handle", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFirstCallIsNotWarnedThatPruningDroppedDescendants(bool anyMatchHasChildren)
    {
        // Pruning is what matchDepth=first asks for, so the WholeModelPrunedWarning
        // would be describing the request as a defect. Whether matches have children
        // is irrelevant to a caller that asked for the shallowest ones.
        var warning = FindItemsNativeSearchPolicy.BuildPrunedWholeModelWarning("first", anyMatchHasChildren);

        Assert.Equal(FindItemsNativeSearchPolicy.WholeModelFirstWarning, warning);
        Assert.NotEqual(FindItemsNativeSearchPolicy.WholeModelPrunedWarning, warning);
    }

    [Fact]
    public void TheFirstWarning_NamesTheOneThingThatIsMissing()
    {
        var warning = FindItemsNativeSearchPolicy.WholeModelFirstWarning;

        // scannedItemCount is the only thing the engine cannot report, and the
        // warning has to say the match set is exact so a reader does not assume
        // otherwise from the presence of a warning.
        Assert.Contains("scannedItemCount is 0", warning, StringComparison.Ordinal);
        Assert.Contains("exact", warning, StringComparison.Ordinal);
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
            "FindItemsNativeSearchPolicy.BuildPrunedWholeModelWarning(matchDepth, anyMatchHasChildren)",
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
