using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashRootReferenceResolverTests
{
    private static readonly List<ClashBboxRootItem> Roots = new()
    {
        new ClashBboxRootItem { Name = "Unique.rvm", Path = "Doc/Unique.rvm", SourceFile = @"C:\Models\Unique.rvm" },
        new ClashBboxRootItem { Name = "Duplicate.rvm", Path = "Doc/A/Duplicate.rvm", SourceFile = @"C:\Models\A\Duplicate.rvm" },
        new ClashBboxRootItem { Name = "Duplicate.rvm", Path = "Doc/B/Duplicate.rvm", SourceFile = @"C:\Models\B\Duplicate.rvm" },
    };

    [Fact]
    public void Resolve_NameOnlyUniqueRootSucceeds()
    {
        var result = ClashRootReferenceResolver.Resolve(Roots, new ClashBboxRootItem { Name = "Unique.rvm" }, "A", 5);
        Assert.True(result.Resolved);
        Assert.Equal("name", result.Diagnostic.MatchStrategy);
        Assert.Equal("A", result.Diagnostic.Side);
    }

    [Fact]
    public void Resolve_DuplicateNameIsAmbiguousAndDoesNotSelectFirst()
    {
        var result = ClashRootReferenceResolver.Resolve(Roots, new ClashBboxRootItem { Name = "Duplicate.rvm" }, "B", 5);
        Assert.False(result.Resolved);
        Assert.Null(result.Root);
        Assert.Equal("ambiguous", result.Diagnostic.Status);
        Assert.Equal(2, result.Diagnostic.MatchCount);
        Assert.Equal("B", result.Diagnostic.Side);
    }

    [Fact]
    public void Resolve_ExactPathPrecedesAmbiguousName()
    {
        var result = ClashRootReferenceResolver.Resolve(Roots, new ClashBboxRootItem { Path = "Doc/B/Duplicate.rvm", Name = "Duplicate.rvm" }, "A", 5);
        Assert.True(result.Resolved);
        Assert.Equal("path", result.Diagnostic.MatchStrategy);
        Assert.Equal("Doc/B/Duplicate.rvm", result.Root.Path);
    }

    [Fact]
    public void Resolve_SourceFileBasenameRequiresUniqueness()
    {
        var result = ClashRootReferenceResolver.Resolve(Roots, new ClashBboxRootItem { SourceFile = "Duplicate.rvm" }, "A", 5);
        Assert.False(result.Resolved);
        Assert.Equal("ambiguous", result.Diagnostic.Status);
    }

    [Fact]
    public void Resolve_SourceFileDisambiguatesDuplicateExactName()
    {
        var result = ClashRootReferenceResolver.Resolve(Roots, new ClashBboxRootItem
        {
            Name = "Duplicate.rvm",
            SourceFile = @"C:\Models\B\Duplicate.rvm",
        }, "B", 5);
        Assert.True(result.Resolved);
        Assert.Equal("name+source_file", result.Diagnostic.MatchStrategy);
        Assert.Equal(@"C:\Models\B\Duplicate.rvm", result.Root.SourceFile);
    }

    [Fact]
    public void RootNameOutcomes_ReportMatchedUnmatchedAndNotEvaluatedSeparately()
    {
        var complete = ClashBboxPlanHelper.BuildRootNameOutcomes(Roots, new[] { "Unique.rvm", "Missing.nwd" }, false);
        Assert.Equal(new[] { "Unique.rvm" }, complete.Matched);
        Assert.Equal(new[] { "Missing.nwd" }, complete.Unmatched);
        Assert.Empty(complete.NotEvaluatedDueToLimit);

        var truncated = ClashBboxPlanHelper.BuildRootNameOutcomes(Roots, new[] { "Missing.nwd" }, true);
        Assert.Empty(truncated.Unmatched);
        Assert.Equal(new[] { "Missing.nwd" }, truncated.NotEvaluatedDueToLimit);
    }
}
