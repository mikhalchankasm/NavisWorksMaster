using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance for #459. select_by_search with direct_children_of / descendants_of ran
// the whole-model native search (PruneBelowMatch=true) and then kept the matches inside the parent.
// A match at the parent or above pruned everything below it: the scenario-catalog example (children
// wildcard "/100000-XXX1-YY-01*", parent equals "/100000-XXX1-YY-01") selected 0 items, because the
// parent matches its own children's pattern. Now the conditions are evaluated under the resolved
// parent: on its direct children, or by an unpruned walk of its descendants (matchDepth=all), inside
// the scoped traversal budget. The plugin cannot compile in L0, so this pins the source; L3 measures
// the runtime counts.
public sealed class SelectBySearchParentScopeAcceptanceTests
{
    [Fact]
    public void ParentScopesEvaluateTheConditionsUnderTheParentNotOnThePrunedWholeModelResult()
    {
        var select = Member("public SelectBySearchResponse SelectBySearch(");

        Assert.Contains("CollectParentScopeMatches(parent, search, directChildrenOnly)", select, StringComparison.Ordinal);
        Assert.DoesNotContain("IsWithinSelectionScope(", select, StringComparison.Ordinal);
    }

    [Fact]
    public void TheParentScopeWalkIsUnprunedManualAndBudgeted()
    {
        var walk = Member("private static List<ModelItem> CollectParentScopeMatches(ModelItem parent, FindItemsSearch search, bool directChildrenOnly)");

        Assert.Contains("PrepareManualSearch(search)", walk, StringComparison.Ordinal);
        Assert.Contains("MatchesSearchManually(", walk, StringComparison.Ordinal);
        Assert.Contains(".Children", walk, StringComparison.Ordinal);
        Assert.Contains("MaxScopedTraversalMilliseconds", walk, StringComparison.Ordinal);
        Assert.Contains("MaxScopedScannedItems", walk, StringComparison.Ordinal);
        Assert.DoesNotContain(".Descendants", walk, StringComparison.Ordinal);
        Assert.DoesNotContain("PruneBelowMatch", walk, StringComparison.Ordinal);
    }

    // The whole-model search must not run first for a parent scope: its pruned answer is the bug.
    [Fact]
    public void TheWholeModelSearchRunsOnlyForTheWholeModelScope()
    {
        var select = Member("public SelectBySearchResponse SelectBySearch(");
        int search = select.IndexOf("ExecuteSearch(document, search,", StringComparison.Ordinal);
        Assert.True(search > 0, "the whole-model search is still used for scope=whole_model");
        var before = select.Substring(0, search);
        int guard = before.LastIndexOf("SelectBySearchScopes.WholeModel", StringComparison.Ordinal);
        Assert.True(guard > before.LastIndexOf("scope must be whole_model", StringComparison.Ordinal),
            "ExecuteSearch(document, search, ...) sits behind a scope == whole_model check");
    }

    // From the signature to the end of its body (the first closing brace at member indentation).
    private static string Member(string signature)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "NavisHelper", "Agent", "Services", "SearchService.Commands.cs"));
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " is present");
        int end = source.IndexOf("\n        }", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return source.Substring(start, end - start);
    }
}
