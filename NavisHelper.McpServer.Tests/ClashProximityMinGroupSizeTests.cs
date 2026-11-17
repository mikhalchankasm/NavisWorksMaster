using Xunit;

namespace NavisHelper.McpServer.Tests;

// #505 (Port clash run, 2026-11-17): clash_group_by_proximity raised minGroupSize 1 to 2 without a
// word, so single results were skipped. The plugin cannot compile in L0, so the request handling is
// pinned by source, as FbxLevelExportFollowUpTests does; L3 runs it.
public sealed class ClashProximityMinGroupSizeTests
{
    [Fact]
    public void MinGroupSizeOneReachesThePlannerAndZeroIsRefused()
    {
        var source = Read("NavisHelper", "Agent", "Services", "DocumentCommandService.Clash.Workflow.cs");
        var method = Body(source, "public ClashGroupByProximityResponse ClashGroupByProximity(Document document, ClashGroupByProximityRequest request)");

        Assert.DoesNotContain("Math.Max(2,", method, StringComparison.Ordinal);
        Assert.Contains("var minGroupSize = request.MinGroupSize.GetValueOrDefault(2);", method, StringComparison.Ordinal);
        int check = method.IndexOf("if (minGroupSize < 1)", StringComparison.Ordinal);
        Assert.True(check >= 0, "values below 1 are checked");
        var refusal = method.Substring(check, 200);
        Assert.Contains("ErrorCodes.SchemaViolation", refusal, StringComparison.Ordinal);
        Assert.Contains("\"minGroupSize must be at least 1.\"", refusal, StringComparison.Ordinal);
    }

    private static string Body(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " is present");
        int end = source.IndexOf("\n        }", start, StringComparison.Ordinal);
        Assert.True(end > start, signature + " has a body");
        return source.Substring(start, end - start);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
