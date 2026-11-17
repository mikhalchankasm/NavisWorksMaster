using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #511 (Port clash run, 2026-11-17): to leave a few results of a test untouched, clash_set_status
// scope=test needed the whole complement (468 names) in resultNames. excludeResultNames removes
// results by name from any scope before the large-change guard counts them; an unknown name refuses
// the call, so a typo never lets the result it meant to protect be changed. The plugin cannot compile
// in L0, so ClashSetStatus is pinned by source; the selection itself is tested directly.
public sealed class ClashSetStatusExcludeTests
{
    [Fact]
    public void Exclude_DropsEveryMatchAndReportsUnknownNames()
    {
        var exclusion = ClashResultNameSelector.Exclude(
            new[] { "Clash1", "Clash2", "Clash3", "clash2", null },
            new[] { " CLASH2 ", "Clash9", "", "Clash2" });

        Assert.Equal(new[] { 0, 2, 4 }, exclusion.KeptPositions);
        Assert.Equal(2, exclusion.ExcludedCount);
        Assert.Equal(new[] { "Clash9" }, exclusion.MissingNames);
    }

    [Fact]
    public void Exclude_WithNoNamesKeepsEverything()
    {
        var exclusion = ClashResultNameSelector.Exclude(new[] { "Clash1", "Clash2" }, null);

        Assert.Equal(new[] { 0, 1 }, exclusion.KeptPositions);
        Assert.Equal(0, exclusion.ExcludedCount);
        Assert.Empty(exclusion.MissingNames);
    }

    [Fact]
    public void SetStatusExcludesBeforeTheLargeChangeGuardAndRefusesUnknownNames()
    {
        Assert.NotNull(typeof(ClashSetStatusRequest).GetProperty("ExcludeResultNames"));
        Assert.NotNull(typeof(ClashSetStatusResponse).GetProperty("ExcludedResultCount"));

        var source = Read("NavisHelper", "Agent", "Services", "DocumentCommandService.Clash.Workflow.cs");
        var method = Body(source, "public ClashSetStatusResponse ClashSetStatus(Document document, ClashSetStatusRequest request)");
        int exclude = method.IndexOf("ClashResultNameSelector.Exclude(", StringComparison.Ordinal);
        int refuse = method.IndexOf("\"excludeResultNames not found in scope: \"", StringComparison.Ordinal);
        int guard = method.IndexOf("targets.Count > LargeClashStatusChangeThreshold", StringComparison.Ordinal);
        Assert.True(exclude >= 0, "the scope is filtered by excludeResultNames");
        Assert.True(refuse > exclude, "unknown excluded names refuse the call");
        Assert.True(guard > refuse, "the large-change guard counts what is left");
        Assert.Contains("ExcludedResultCount = ", method, StringComparison.Ordinal);

        var tools = Read("NavisHelper.McpServer", "Tools", "NavisworksClashResultTools.cs");
        Assert.Contains("List<string> excludeResultNames = null", tools, StringComparison.Ordinal);
        Assert.Contains("ExcludeResultNames = excludeResultNames ?? new List<string>(),", tools, StringComparison.Ordinal);
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
