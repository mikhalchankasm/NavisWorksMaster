using System.Reflection;
using System.Text.RegularExpressions;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #439 (#427 slice): on 10 000 selected items in the 908 MB model selection_copy_names
// answered 1 534 801 bytes and selection_property_report 1 349 190 with default arguments,
// past ResponseBudget.MaxBytes. Both now page by bytes the way selected_items_tree does since
// #434, through one pure helper so the paging rule is tested here rather than only in L3.
public sealed class ResponsePagingTests
{
    private sealed class Sized
    {
        public Sized(int bytes) => Bytes = bytes;
        public int Bytes { get; }
    }

    private static ResponsePage<Sized> Take(int offset, int selectedCount, int pageBytes, params int?[] sizes)
    {
        var candidates = sizes.Select(size => size.HasValue ? new Sized(size.Value) : null);
        return ResponsePaging.Take(candidates, offset, selectedCount, item => item.Bytes, pageBytes);
    }

    [Fact]
    public void EverythingThatFitsIsOnePageWithNoNextOffset()
    {
        var page = Take(0, 2, 1_000, 100, 200);

        Assert.Equal(new[] { 100, 200 }, page.Items.Select(item => item.Bytes));
        Assert.Equal(2, page.Consumed);
        Assert.Null(page.NextOffset);
        Assert.False(page.BudgetTruncated);
        Assert.Null(page.OversizedPosition);
    }

    [Fact]
    public void AnItemThatExactlyFillsThePageIsTaken()
    {
        var page = Take(0, 2, 1_000, 600, 400);

        Assert.Equal(2, page.Items.Count);
        Assert.False(page.BudgetTruncated);
    }

    [Fact]
    public void ThePageStopsBeforeTheItemThatWouldPassTheBudgetAndThatItemOpensTheNextPage()
    {
        var page = Take(10, 50, 1_000, 400, 500, 300);

        Assert.Equal(new[] { 400, 500 }, page.Items.Select(item => item.Bytes));
        Assert.Equal(2, page.Consumed);
        Assert.Equal(12, page.NextOffset);
        Assert.True(page.BudgetTruncated);
        Assert.Null(page.OversizedPosition);
    }

    [Fact]
    public void NothingPastTheRefusedItemIsBuilt()
    {
        var built = 0;
        var candidates = new[] { 400, 500, 300, 100, 100 }.Select(size =>
        {
            built++;
            return new Sized(size);
        });

        ResponsePaging.Take(candidates, 0, 5, item => item.Bytes, 1_000);

        Assert.Equal(3, built);
    }

    [Fact]
    public void AFirstItemLargerThanAPageIsReportedByItsPositionAndNotReturned()
    {
        var page = Take(7, 50, 1_000, 5_000, 100);

        Assert.Empty(page.Items);
        Assert.Equal(7, page.OversizedPosition);
        Assert.False(page.BudgetTruncated);
    }

    [Fact]
    public void ALargeItemAfterTheFirstOnlyEndsThePage()
    {
        var page = Take(3, 50, 1_000, 100, 5_000);

        Assert.Single(page.Items);
        Assert.Null(page.OversizedPosition);
        Assert.True(page.BudgetTruncated);
        Assert.Equal(4, page.NextOffset);
    }

    [Fact]
    public void ASkippedPositionIsConsumedSoTheNextPageDoesNotRepeatIt()
    {
        var page = Take(0, 10, 1_000, null, 100, null);

        Assert.Single(page.Items);
        Assert.Equal(3, page.Consumed);
        Assert.Equal(3, page.NextOffset);
    }

    [Fact]
    public void ReachingTheItemLimitLeavesANextOffsetWithoutBudgetTruncation()
    {
        // The caller passes source.Skip(offset).Take(limit): three of ten positions here.
        var page = Take(0, 10, 1_000, 100, 100, 100);

        Assert.Equal(3, page.NextOffset);
        Assert.False(page.BudgetTruncated);
    }

    [Fact]
    public void ContractsCarryThePage()
    {
        Assert.Equal(300, new SelectionCopyNamesRequest { Offset = 300 }.Offset);
        var names = new SelectionCopyNamesResponse { Offset = 300, NextOffset = 600, BudgetTruncated = true };
        Assert.Equal(300, names.Offset);
        Assert.Equal(600, names.NextOffset);
        Assert.True(names.BudgetTruncated);
        Assert.Null(new SelectionCopyNamesResponse().NextOffset);

        Assert.Equal(40, new SelectionPropertyReportRequest { Offset = 40 }.Offset);
        var report = new SelectionPropertyReportResponse { Offset = 40, NextOffset = 52, BudgetTruncated = true };
        Assert.Equal(40, report.Offset);
        Assert.Equal(52, report.NextOffset);
        Assert.True(report.BudgetTruncated);
        Assert.Null(new SelectionPropertyReportResponse().NextOffset);
    }

    [Theory]
    [InlineData(typeof(NavisworksSelectionTools), "SelectionCopyNames")]
    [InlineData(typeof(NavisworksSelectionReportTools), "SelectionPropertyReport")]
    public void BothToolsTakeAnOffset(Type tools, string method)
    {
        var tool = tools.GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(tool);
        var offset = Assert.Single(tool!.GetParameters(), parameter => parameter.Name == "offset");
        Assert.Equal(typeof(int), offset.ParameterType);
        Assert.Equal(0, offset.DefaultValue);
    }

    [Fact]
    public void CopyNamesPagesThroughTheHelper()
    {
        var body = MethodBody(
            Path.Combine("NavisHelper", "Agent", "Services", "DocumentCommandService.SelectionQueries.cs"),
            "public SelectionCopyNamesResponse SelectionCopyNames(");

        Assert.Contains("ResponsePaging.Take(", body, StringComparison.Ordinal);
        Assert.Contains("ResponseBudget.PageBytes", body, StringComparison.Ordinal);
        Assert.Contains("ErrorCodes.ResponseTooLarge", body, StringComparison.Ordinal);
        Assert.Contains("ResponseSize.Bytes(", body, StringComparison.Ordinal);
        Assert.Contains("SelectedItemsTreePaging.DescribeOffsetError(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePropertyReportToolPagesButTheExportDoesNot()
    {
        var file = Path.Combine("NavisHelper", "Agent", "Services", "DocumentCommandService.PropertyReport.cs");

        // The router calls SelectionPropertyReport for the MCP tool: it pages by bytes.
        var tool = MethodBody(file, "public SelectionPropertyReportResponse SelectionPropertyReport(");
        Assert.Contains("ResponseBudget.PageBytes", tool, StringComparison.Ordinal);

        var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, file));
        Assert.Contains("ResponsePaging.Take(", source, StringComparison.Ordinal);
        Assert.Contains("ErrorCodes.ResponseTooLarge", source, StringComparison.Ordinal);
        Assert.Contains("BuildSelectionPropertyReport(", source, StringComparison.Ordinal);

        // selection_export_properties writes a file: it must keep every row, so it reads the
        // report through the unbudgeted builder, never through the paged tool method.
        var export = MethodBody(file, "public SelectionExportPropertiesResponse SelectionExportProperties(");
        Assert.Contains("BuildSelectionPropertyReport(", export, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?<!Build)SelectionPropertyReport\(document", export);
        Assert.DoesNotContain("ResponseBudget.PageBytes", export, StringComparison.Ordinal);
    }

    // One method's text: from its signature to the next member declared at the same indent.
    private static string MethodBody(string relativePath, string signature)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, relativePath)).Replace("\r\n", "\n");
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found in {relativePath}");
        var ends = new[] { "\n        public ", "\n        internal ", "\n        private " }
            .Select(marker => source.IndexOf(marker, start + signature.Length, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(source.Length);
        return source.Substring(start, ends.Min() - start);
    }
}
