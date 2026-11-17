using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #506 (Port clash run, 2026-11-17): a rename_batch dry-run over 825 tests showed only the old names,
// so the new names could not be checked before apply=true. The plugin cannot compile in L0, so the
// service is pinned by source, as FbxLevelExportFollowUpTests does; L3 runs it.
public sealed class ClashRenameBatchPreviewTests
{
    [Fact]
    public void EveryRenameRowCarriesItsNewName()
    {
        Assert.NotNull(typeof(ClashTestOperationResult).GetProperty("NewName"));

        var source = Read("NavisHelper", "Agent", "Services", "DocumentCommandService.Clash.Workflow.cs");
        var method = Body(source, "private static ClashManageTestsResponse ClashManageRenameBatch(");
        int row = method.IndexOf("var item = new ClashTestOperationResult", StringComparison.Ordinal);
        int added = method.IndexOf("response.Tests.Add(item);", row, StringComparison.Ordinal);
        Assert.True(row >= 0 && added > row, "the rename row is built before it is added");
        Assert.Contains("NewName = rename.NewName.Trim(),", method.Substring(row, added - row), StringComparison.Ordinal);
        Assert.True(added < method.IndexOf("if (!apply)", StringComparison.Ordinal), "the dry-run returns rows that already hold the new name");
    }

    [Fact]
    public void ThePatternDescriptionSaysWherePairNamesComeFrom()
    {
        var tools = Read("NavisHelper.McpServer", "Tools", "NavisworksClashTestTools.cs");
        int at = tools.IndexOf("string namePattern = \"\"", StringComparison.Ordinal);
        Assert.True(at > 0, "namePattern parameter is present");
        var line = tools.Substring(tools.LastIndexOf('\n', at), at - tools.LastIndexOf('\n', at));
        Assert.Contains("current test name", line, StringComparison.Ordinal);
        Assert.Contains("' vs '", line, StringComparison.Ordinal);
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
