using System;
using System.Collections.Generic;
using System.IO;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// Eligibility cover for answering a scoped find_items with the native search.
///
/// Engine pruning means matchDepth=first and nothing else. Measured on
/// 6501.5.nwd over the nine discipline roots (~88k nodes), the manual traversal
/// cannot answer Item/Name contains "насос" at all — command_failed after
/// 45 018 ms — while the native path returns the single match in 253 ms. The
/// earlier attempt at this disabled pruning so it could also serve
/// matchDepth=all and took 37 614 ms for a query manual answered in 18 ms, so
/// every rule below exists to keep pruning the only semantics this path serves.
/// </summary>
public sealed class FindItemsNativeScopedPolicyTests
{
    private static FindItemsSearch Search(params FindItemsCondition[] conditions)
    {
        return new FindItemsSearch
        {
            CombineOperator = FindItemsCombineOperators.All,
            Conditions = new List<FindItemsCondition>(conditions),
        };
    }

    private static FindItemsCondition Condition(string comparison = FindItemsComparisons.Contains)
    {
        return new FindItemsCondition
        {
            Category = "Item",
            Property = "Name",
            Operator = comparison,
            Value = "насос",
            LogicalOperator = FindItemsConditionOptionsHelper.And,
        };
    }

    [Theory]
    [InlineData(FindItemsComparisons.Contains, true)]
    [InlineData(FindItemsComparisons.Equal, true)]
    [InlineData(FindItemsComparisons.Wildcard, true)]
    // starts_with/ends_with keep their existing literal-anchor routing.
    [InlineData(FindItemsComparisons.StartsWith, false)]
    [InlineData(FindItemsComparisons.EndsWith, false)]
    // Complement semantics stay on one path so both agree.
    [InlineData(FindItemsComparisons.NotEquals, false)]
    [InlineData(FindItemsComparisons.Defined, false)]
    [InlineData(FindItemsComparisons.NotDefined, false)]
    public void IsEligible_AcceptsOnlyNativelyExpressibleComparisons(string comparison, bool expected)
    {
        var eligible = FindItemsNativeScopedPolicy.IsEligible(
            Search(Condition(comparison)),
            FindItemsScopes.UnderHandle,
            FindItemsMatchDepths.First,
            countOnly: false);

        Assert.Equal(expected, eligible);
    }

    [Theory]
    [InlineData(FindItemsScopes.UnderHandle, true)]
    [InlineData(FindItemsScopes.UnderNamedNode, true)]
    [InlineData(FindItemsScopes.CurrentSelection, true)]
    // whole_model has its own native routing and must not be taken over here.
    [InlineData(FindItemsScopes.WholeModel, false)]
    public void IsEligible_CoversEveryScopeThatResolvesExplicitRoots(string scope, bool expected)
    {
        Assert.Equal(
            expected,
            FindItemsNativeScopedPolicy.IsEligible(Search(Condition()), scope, FindItemsMatchDepths.First, countOnly: false));
    }

    [Fact]
    public void IsEligible_RejectsMatchDepthAll_BecausePruningIsNotAll()
    {
        Assert.False(FindItemsNativeScopedPolicy.IsEligible(
            Search(Condition()),
            FindItemsScopes.UnderHandle,
            FindItemsMatchDepths.All,
            countOnly: false));
    }

    [Fact]
    public void IsEligible_RejectsCountOnly_BecauseTheEngineDoesNotReportScannedItems()
    {
        Assert.False(FindItemsNativeScopedPolicy.IsEligible(
            Search(Condition()),
            FindItemsScopes.UnderHandle,
            FindItemsMatchDepths.First,
            countOnly: true));
    }

    [Fact]
    public void IsEligible_RejectsNegatedAndInheritedConditions()
    {
        var negated = Condition();
        negated.Negate = true;
        Assert.False(FindItemsNativeScopedPolicy.IsEligible(
            Search(negated), FindItemsScopes.UnderHandle, FindItemsMatchDepths.First, false));

        var inherited = Condition();
        inherited.InheritFromAncestor = true;
        Assert.False(FindItemsNativeScopedPolicy.IsEligible(
            Search(inherited), FindItemsScopes.UnderHandle, FindItemsMatchDepths.First, false));
    }

    [Fact]
    public void IsEligible_RejectsOrSemantics_AtSearchLevelAndBetweenConditions()
    {
        var anySearch = Search(Condition());
        anySearch.CombineOperator = FindItemsCombineOperators.Any;
        Assert.False(FindItemsNativeScopedPolicy.IsEligible(
            anySearch, FindItemsScopes.UnderHandle, FindItemsMatchDepths.First, false));

        var second = Condition();
        second.LogicalOperator = FindItemsConditionOptionsHelper.Or;
        Assert.False(FindItemsNativeScopedPolicy.IsEligible(
            Search(Condition(), second), FindItemsScopes.UnderHandle, FindItemsMatchDepths.First, false));
    }

    [Fact]
    public void IsEligible_AcceptsSeveralAndedConditions()
    {
        Assert.True(FindItemsNativeScopedPolicy.IsEligible(
            Search(Condition(), Condition(FindItemsComparisons.Equal)),
            FindItemsScopes.UnderHandle,
            FindItemsMatchDepths.First,
            false));
    }

    [Fact]
    public void IsEligible_RejectsEmptyOrNullSearch()
    {
        Assert.False(FindItemsNativeScopedPolicy.IsEligible(
            null, FindItemsScopes.UnderHandle, FindItemsMatchDepths.First, false));
        Assert.False(FindItemsNativeScopedPolicy.IsEligible(
            Search(), FindItemsScopes.UnderHandle, FindItemsMatchDepths.First, false));
    }

    [Fact]
    public void IsEligible_DoesNotThrowOnAnUnsupportedComparison()
    {
        var condition = Condition();
        condition.Operator = "regex";

        Assert.False(FindItemsNativeScopedPolicy.IsEligible(
            Search(condition), FindItemsScopes.UnderHandle, FindItemsMatchDepths.First, false));
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("false", true)]
    [InlineData("OFF", true)]
    [InlineData(" manual ", true)]
    [InlineData("1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDisabledByEnvironment_ReadsTheEscapeHatch(string value, bool expected)
    {
        Assert.Equal(expected, FindItemsNativeScopedPolicy.IsDisabledByEnvironment(value));
        Assert.Equal("NAVISHELPER_FIND_ITEMS_NATIVE_SCOPE", FindItemsNativeScopedPolicy.DisableEnvironmentVariable);
    }

    [Fact]
    public void ScopedExecutor_PrunesInTheEngine_AndRestoresFirstAcrossVariants()
    {
        var root = FindRepositoryRoot();
        var nativeScoped = File.ReadAllText(
            Path.Combine(root, "NavisHelper", "Agent", "Services", "SearchService.NativeScoped.cs"));
        var scoped = File.ReadAllText(
            Path.Combine(root, "NavisHelper", "Agent", "Services", "SearchService.Scoped.cs"));

        Assert.Contains(
            "nativeSearch.PruneBelowMatch = FindItemsNativeSearchPolicy.PruneBelowMatch;",
            nativeScoped,
            StringComparison.Ordinal);
        Assert.Contains("nativeSearch.Selection.CopyFrom(scopeSelection);", nativeScoped, StringComparison.Ordinal);

        // Each variant prunes on its own, so the union has to be reduced back to
        // the shallowest match per branch or matchDepth=first leaks nested hits.
        Assert.Contains("KeepShallowestMatches(found)", nativeScoped, StringComparison.Ordinal);

        Assert.Contains("ShouldTryNativeScopedSearch(search, scope, matchDepth, countOnly)", scoped, StringComparison.Ordinal);
        Assert.Contains("FindItemsNativeScopedPolicy.IsEligible", scoped, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupedConditions_HonourTheComparisonField()
    {
        var root = FindRepositoryRoot();
        var rules = File.ReadAllText(
            Path.Combine(root, "NavisHelper", "Agent", "Services", "SearchService.Rules.cs"));

        // Reading condition.Operator alone made `comparison: "equals"` inside a
        // grouped search silently fall back to the contains default. Only the
        // entry point matters: every later site runs on a condition this method
        // already normalized, where Operator and Comparison agree.
        var entryPoint = rules.IndexOf(
            "private static FindItemsCondition NormalizeCondition(FindItemsCondition condition)",
            StringComparison.Ordinal);
        Assert.True(entryPoint >= 0, "NormalizeCondition was renamed; re-point this guard.");

        var body = rules.Substring(entryPoint, Math.Min(1200, rules.Length - entryPoint));
        Assert.Contains("NormalizeComparison(GetConditionComparison(condition))", body, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeComparison(condition.Operator)", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0L, false)]
    [InlineData(44999L, false)]
    [InlineData(45000L, false)]
    [InlineData(45001L, true)]
    public void ExceedsTraversalBudget_matches_the_manual_traversal_comparison(long elapsed, bool expected)
    {
        // The manual traversal throws on `elapsed > 45000`. An off-by-one here
        // would let one path accept a millisecond the other rejects.
        Assert.Equal(expected, FindItemsNativeScopedPolicy.ExceedsTraversalBudget(elapsed));
    }

    [Fact]
    public void BuildTraversalBudgetMessage_names_the_progress_and_the_remedy()
    {
        var message = FindItemsNativeScopedPolicy.BuildTraversalBudgetMessage(2, 5);

        Assert.Contains("2 of 5 native searches", message, StringComparison.Ordinal);
        Assert.Contains("Narrow the scope", message, StringComparison.Ordinal);
        // matchDepth is already first on this path, so suggesting it would be
        // advice the caller has already taken.
        Assert.DoesNotContain("matchDepth=first", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_scoped_path_enforces_the_budget_between_variants()
    {
        // Up to MaxNativeAndFastPathVariants engine searches run per request and
        // a single FindAll cannot be interrupted. Without a check inside the loop
        // the fast path silently drops the guard the manual traversal has always
        // had. The rethrow matters as much as the check: the surrounding
        // catch-all turns engine rejections into a manual fallback, which would
        // spend the budget a second time.
        var source = ReadRepositoryFile("NavisHelper/Agent/Services/SearchService.NativeScoped.cs");

        var loop = source.IndexOf("foreach (var conditions in variants)", StringComparison.Ordinal);
        Assert.True(loop >= 0, "The variant loop was reshaped; re-point this guard.");

        var body = source.Substring(loop);
        Assert.Contains("FindItemsNativeScopedPolicy.ExceedsTraversalBudget", body, StringComparison.Ordinal);
        Assert.Contains("catch (AgentCommandException)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_scoped_paths_read_the_budget_from_one_place()
    {
        // Two copies of 45000 drift, and the drift is invisible: each path keeps
        // enforcing a budget, just not the same one.
        var source = ReadRepositoryFile("NavisHelper/Agent/Services/SearchService.Scoped.cs");

        var declaration = source.IndexOf("MaxScopedTraversalMilliseconds =", StringComparison.Ordinal);
        Assert.True(declaration >= 0, "MaxScopedTraversalMilliseconds was renamed; re-point this guard.");

        var body = source.Substring(declaration, Math.Min(200, source.Length - declaration));
        Assert.Contains("FindItemsNativeScopedPolicy.TraversalBudgetMilliseconds", body, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var path = Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), path + " is missing; re-point this guard.");
        return File.ReadAllText(path);
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
