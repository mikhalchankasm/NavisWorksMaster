using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// 2.15.0.0 picks from the blog and forum review (docs/FEATURE_BACKLOG.md):
// - clash_tests_from_sets sets the primitive types each side of a new test checks;
// - clash_list_results can return the comments of each result;
// - model_color_scheme applies and restores inside one Navisworks transaction each.
public sealed class Release215FeatureTests
{
    private static McpServerTool Tool(string methodName)
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == methodName);
        return McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
    }

    private static JsonObject Properties(McpServerTool tool) =>
        JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!["properties"]!.AsObject();

    // --- primitive types -------------------------------------------------------------

    [Fact]
    public void PrimitiveTypesParseCaseInsensitivelyIntoFlags()
    {
        Assert.True(ClashPrimitiveTypesParser.TryParse(["Triangles", "LINES"], out var kinds, out var error));
        Assert.Null(error);
        Assert.Equal(ClashPrimitiveKinds.Triangles | ClashPrimitiveKinds.Lines, kinds);

        Assert.True(ClashPrimitiveTypesParser.TryParse([" points ", "points"], out kinds, out _));
        Assert.Equal(ClashPrimitiveKinds.Points, kinds);
    }

    [Fact]
    public void EmptyPrimitiveTypesMeanNavisworksDefault()
    {
        Assert.True(ClashPrimitiveTypesParser.TryParse(null, out var kinds, out _));
        Assert.Equal(ClashPrimitiveKinds.None, kinds);
        Assert.True(ClashPrimitiveTypesParser.TryParse([], out kinds, out _));
        Assert.Equal(ClashPrimitiveKinds.None, kinds);
        Assert.True(ClashPrimitiveTypesParser.TryParse(["", "  "], out kinds, out _));
        Assert.Equal(ClashPrimitiveKinds.None, kinds);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("snappoints")]
    [InlineData("surfaces")]
    public void UnknownPrimitiveTypeIsRejectedByName(string value)
    {
        Assert.False(ClashPrimitiveTypesParser.TryParse(["triangles", value], out _, out var error));
        Assert.Contains(value, error, StringComparison.Ordinal);
        Assert.Contains("triangles, lines, points", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FromSetsRequestAndToolCarryPrimitiveTypes()
    {
        var request = new ClashTestsFromSetsRequest { PrimitiveTypesA = ["triangles"], PrimitiveTypesB = ["lines", "points"] };
        Assert.Equal(["lines", "points"], request.PrimitiveTypesB);

        var properties = Properties(Tool("ClashTestsFromSets"));
        foreach (var name in new[] { "primitiveTypesA", "primitiveTypesB" })
        {
            Assert.Equal("array", properties[name]!["type"]!.GetValue<string>());
            var text = properties[name]!["description"]!.GetValue<string>();
            Assert.Contains("lines", text, StringComparison.Ordinal);
            Assert.Contains("points", text, StringComparison.Ordinal);
        }
    }

    // --- clash comments --------------------------------------------------------------

    private static ClashCommentInfo Comment(string body, string author = "user") =>
        new() { Body = body, Author = author, Status = "New", CreatedUtc = "2026-10-21T08:00:00Z" };

    [Fact]
    public void InternalMarkerCommentsAreLeftOut()
    {
        var selection = ClashCommentSelection.Select(
            [Comment("[NavisHelper Ignore] rule 3"), Comment("Move the duct up 200 mm"), Comment("Agreed")],
            "[NavisHelper Ignore] ",
            maxComments: 20,
            maxBodyChars: 1000);

        Assert.Equal(["Move the duct up 200 mm", "Agreed"], selection.Comments.Select(comment => comment.Body));
        Assert.Equal(2, selection.TotalCount);
        Assert.False(selection.Truncated);
    }

    [Fact]
    public void CommentsKeepTheNewestWithinTheCapAndShortenLongBodies()
    {
        var comments = Enumerable.Range(1, 5).Select(index => Comment("comment " + index)).ToList();
        comments.Add(Comment(new string('x', 50)));

        var selection = ClashCommentSelection.Select(comments, "[NavisHelper Ignore] ", maxComments: 3, maxBodyChars: 10);

        Assert.Equal(6, selection.TotalCount);
        Assert.True(selection.Truncated);
        Assert.Equal(3, selection.Comments.Count);
        Assert.Equal("comment 4", selection.Comments[0].Body);
        Assert.Equal("comment 5", selection.Comments[1].Body);
        Assert.Equal(10, selection.Comments[2].Body.Length);
        Assert.EndsWith("…", selection.Comments[2].Body, StringComparison.Ordinal);
    }

    [Fact]
    public void NullCommentsSelectNothing()
    {
        var selection = ClashCommentSelection.Select(null, "[NavisHelper Ignore] ", 20, 1000);
        Assert.Empty(selection.Comments);
        Assert.Equal(0, selection.TotalCount);
        Assert.False(selection.Truncated);
    }

    [Fact]
    public void ListResultsCarriesCommentsOnRequest()
    {
        var request = new ClashListResultsRequest { IncludeComments = true };
        Assert.True(request.IncludeComments);
        var summary = new ClashResultSummary { CommentCount = 2, CommentsTruncated = false, Comments = [Comment("a"), Comment("b")] };
        Assert.Equal(2, summary.Comments.Count);

        var property = Properties(Tool("ClashListResults"))["includeComments"]!;
        Assert.Equal("boolean", property["type"]!.GetValue<string>());
        Assert.False(property["default"]!.GetValue<bool>());
    }

    // --- one transaction per colour-scheme change ------------------------------------

    [Fact]
    public void ColorSchemeApplyAndRestoreEachCommitOneTransaction()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "ModelColorSchemeService.Apply.cs"));

        Assert.Equal(2, Regex.Matches(source, @"\.BeginTransaction\s*\(").Count);
        Assert.Equal(2, Regex.Matches(source, @"\.Commit\s*\(\s*\)").Count);
        foreach (var method in new[] { "private void Apply(", "private void RestoreOriginalStates(" })
        {
            var start = source.IndexOf(method, StringComparison.Ordinal);
            Assert.True(start >= 0, method + " not found");
            var next = source.IndexOf("\n        private ", start + method.Length, StringComparison.Ordinal);
            var body = source.Substring(start, (next < 0 ? source.Length : next) - start);
            Assert.Matches(@"using\s*\(\s*var\s+\w+\s*=\s*document\.BeginTransaction\s*\(", body);
            Assert.Contains(".Commit()", body, StringComparison.Ordinal);
        }
    }
}
