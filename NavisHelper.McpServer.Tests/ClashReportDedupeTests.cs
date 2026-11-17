using System.Reflection;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #509 slice b: clash_generate_report gets the same dedupe as clash_list_results (#517): repeated rows
// (same test, items, point and distance) fold to the first before the large-report count, sorting and
// paging, keyed by ClashResultDuplicateKey so both tools agree. The plugin cannot compile in L0, so the
// scope service and the report are pinned by source; the key itself is tested in ClashRepeatedResultsTests.
public sealed class ClashReportDedupeTests
{
    [Fact]
    public void TheReportFoldsRepeatedRowsBeforeItCountsAndPages()
    {
        Assert.NotNull(typeof(ClashGenerateReportRequest).GetProperty("Dedupe"));
        Assert.NotNull(typeof(ClashGenerateReportResponse).GetProperty("DedupedResultCount"));

        var scope = Read("NavisHelper", "Agent", "Services", "ClashReportScopePageService.cs");
        var core = scope.Substring(scope.IndexOf("private static ClashReportScopePage<TWorkItem> BuildCore<TWorkItem>(", StringComparison.Ordinal));
        int fold = core.IndexOf("duplicateKey(", StringComparison.Ordinal);
        int confirm = core.IndexOf("var confirmationRequired", StringComparison.Ordinal);
        int sort = core.IndexOf("sortRows(matchedRows)", StringComparison.Ordinal);
        Assert.True(fold > 0, "BuildCore folds rows by duplicateKey");
        Assert.True(confirm > fold && sort > fold, "rows are folded before the large-report count, sorting and paging");

        var report = Read("NavisHelper", "Agent", "Services", "DocumentCommandService.Clash.Report.cs");
        Assert.Contains("request.Dedupe == true", report, StringComparison.Ordinal);
        Assert.Contains("ClashResultDuplicateKey.For(", report, StringComparison.Ordinal);

        var tool = typeof(NavisworksClashReportTools).GetMethod(nameof(NavisworksClashReportTools.ClashGenerateReport))!;
        var dedupe = tool.GetParameters().Single(parameter => parameter.Name == "dedupe");
        Assert.Equal(false, dedupe.DefaultValue);
        Assert.Contains("Dedupe = dedupe,", Read("NavisHelper.McpServer", "Tools", "NavisworksClashReportTools.cs"), StringComparison.Ordinal);
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
