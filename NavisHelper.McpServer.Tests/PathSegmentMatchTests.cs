using System.Collections.Generic;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #358: a caller-authored slash path (no " / ") is split on "/", so names that begin
// with "/" -- usual in RVM exported from E3D -- lost their slash and matched nothing.
// And a single segment such as "X.rvm" matched both the root called X.rvm and its
// child "/X", because every node of that file carries Source File X.rvm. The rule is
// now graded: an exact name beats a name without its leading slash, which beats a
// source-file match, and only the best grade found anywhere is kept.
public sealed class PathSegmentMatchTests
{
    [Theory]
    [InlineData("Pipes", "Pipes", "", "", "", PathSegmentMatch.Name)]
    [InlineData("pipes", "PIPES", "", "", "", PathSegmentMatch.Name)]
    [InlineData("Group", "Something", "Group", "", "", PathSegmentMatch.Name)]
    [InlineData("240101-X", "/240101-X", "", "", "", PathSegmentMatch.NameWithoutLeadingSlash)]
    [InlineData("Pipes", "/Pipes", "", "", "", PathSegmentMatch.NameWithoutLeadingSlash)]
    [InlineData("X.rvm", "X.rvm", "", @"D:\m\X.rvm", "X.rvm", PathSegmentMatch.Name)]
    [InlineData("X.rvm", "/X", "", @"D:\m\X.rvm", "X.rvm", PathSegmentMatch.SourceFile)]
    [InlineData(@"D:\m\X.rvm", "/X", "", @"D:\m\X.rvm", "X.rvm", PathSegmentMatch.SourceFile)]
    [InlineData("Pip", "/Pipes", "", "", "", PathSegmentMatch.None)]
    [InlineData("Pipes", "//Pipes", "", "", "", PathSegmentMatch.None)]
    [InlineData("", "Pipes", "", "", "", PathSegmentMatch.None)]
    public void SegmentsAreGraded(string segment, string displayName, string classDisplayName, string sourceFile, string sourceFileName, PathSegmentMatch expected)
    {
        Assert.Equal(expected, FindItemsPathSegments.MatchSegment(segment, displayName, classDisplayName, sourceFile, sourceFileName));
    }

    [Fact]
    public void APathIsAsWeakAsItsWeakestSegment()
    {
        Assert.Equal(PathSegmentMatch.Name,
            FindItemsPathSegments.CombineSegments(new[] { PathSegmentMatch.Name, PathSegmentMatch.Name }));
        Assert.Equal(PathSegmentMatch.SourceFile,
            FindItemsPathSegments.CombineSegments(new[] { PathSegmentMatch.SourceFile, PathSegmentMatch.Name }));
        Assert.Equal(PathSegmentMatch.NameWithoutLeadingSlash,
            FindItemsPathSegments.CombineSegments(new[] { PathSegmentMatch.Name, PathSegmentMatch.NameWithoutLeadingSlash }));
        Assert.Equal(PathSegmentMatch.None,
            FindItemsPathSegments.CombineSegments(new[] { PathSegmentMatch.Name, PathSegmentMatch.None }));
        Assert.Equal(PathSegmentMatch.None, FindItemsPathSegments.CombineSegments(new PathSegmentMatch[0]));
    }

    [Fact]
    public void OnlyTheBestGradeIsKept()
    {
        // The root X.rvm by name beats its child /X by source file: one node, not two.
        var rootAndChild = FindItemsPathSegments.KeepBestMatches(new[]
        {
            new KeyValuePair<string, PathSegmentMatch>("root X.rvm", PathSegmentMatch.Name),
            new KeyValuePair<string, PathSegmentMatch>("child /X", PathSegmentMatch.SourceFile),
        });
        Assert.Equal(new[] { "root X.rvm" }, rootAndChild);

        // Two nodes at the same best grade stay two, so the caller still refuses an ambiguous path.
        var twins = FindItemsPathSegments.KeepBestMatches(new[]
        {
            new KeyValuePair<string, PathSegmentMatch>("a", PathSegmentMatch.NameWithoutLeadingSlash),
            new KeyValuePair<string, PathSegmentMatch>("b", PathSegmentMatch.NameWithoutLeadingSlash),
            new KeyValuePair<string, PathSegmentMatch>("c", PathSegmentMatch.SourceFile),
            new KeyValuePair<string, PathSegmentMatch>("d", PathSegmentMatch.None),
        });
        Assert.Equal(new[] { "a", "b" }, twins);

        Assert.Empty(FindItemsPathSegments.KeepBestMatches(new[]
        {
            new KeyValuePair<string, PathSegmentMatch>("d", PathSegmentMatch.None),
        }));
    }

    [Fact]
    public void TheHintNamesBothWorkarounds()
    {
        Assert.Contains("parentMatchHandle", FindItemsPathSegments.SlashPathHint, System.StringComparison.Ordinal);
        Assert.Contains("\" / \"", FindItemsPathSegments.SlashPathHint, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ThePluginUsesTheGradedRuleAndTheHint()
    {
        var services = System.IO.Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services");
        var index = System.IO.File.ReadAllText(System.IO.Path.Combine(services, "SearchService.Index.cs"));
        var scoped = System.IO.File.ReadAllText(System.IO.Path.Combine(services, "SearchService.Scoped.cs"));

        Assert.Contains("FindItemsPathSegments.MatchSegment(", index, System.StringComparison.Ordinal);
        Assert.Contains("FindItemsPathSegments.KeepBestMatches(", index, System.StringComparison.Ordinal);
        Assert.Contains("FindItemsPathSegments.SlashPathHint", index, System.StringComparison.Ordinal);
        Assert.Contains("FindItemsPathSegments.SlashPathHint", scoped, System.StringComparison.Ordinal);
    }
}
