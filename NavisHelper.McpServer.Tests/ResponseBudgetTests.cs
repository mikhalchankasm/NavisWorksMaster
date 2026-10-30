using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #427: on a large model selected_items_tree flat with bounding boxes answered 1-3 MB, past
// the MCP client's limit of about 25 000 tokens, which cost more context than the whole
// tool catalogue.
public sealed class ResponseBudgetTests
{
    // The client guide's measured ratio for this server's JSON: 3.375 bytes a token.
    private const int ClientLimitBytes = 25_000 * 3375 / 1000;

    [Fact]
    public void TheBudgetFitsTheClientLimit()
    {
        Assert.True(ResponseBudget.MaxBytes <= ClientLimitBytes,
            $"ResponseBudget.MaxBytes {ResponseBudget.MaxBytes} is past the client limit {ClientLimitBytes}.");
        Assert.True(ResponseBudget.PageBytes + ResponseBudget.EnvelopeBytes <= ResponseBudget.MaxBytes,
            "A page plus its envelope must fit ResponseBudget.MaxBytes.");
        Assert.True(ResponseBudget.EnvelopeBytes >= 2_000, "The envelope reserve must cover the timing block and counters.");
    }

    [Fact]
    public void APageTakesItemsUntilTheNextOneWouldNotFit()
    {
        var budget = new ResponsePageBudget(1_000);

        Assert.True(budget.TryTake(400));
        Assert.True(budget.TryTake(600));
        Assert.False(budget.Exhausted);
        Assert.False(budget.TryTake(1));
        Assert.True(budget.Exhausted);
        Assert.Equal(1_000, budget.Used);
    }

    [Fact]
    public void TheFirstItemAlwaysFitsSoEveryPageMovesForward()
    {
        var budget = new ResponsePageBudget(1_000);

        Assert.True(budget.TryTake(5_000));
        Assert.False(budget.TryTake(1));
        Assert.True(budget.Exhausted);
    }

    [Fact]
    public void ARefusedItemDoesNotCount()
    {
        var budget = new ResponsePageBudget(1_000);

        Assert.True(budget.TryTake(900));
        Assert.False(budget.TryTake(200));
        Assert.Equal(900, budget.Used);
    }

    [Fact]
    public void SelectedItemsTreePagesByTheBudget()
    {
        Assert.False(new SelectedItemsTreeResponse().BudgetTruncated);

        var queries = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "DocumentCommandService.SelectionQueries.cs"));
        Assert.Contains("new ResponsePageBudget(ResponseBudget.PageBytes)", queries, StringComparison.Ordinal);
        Assert.Contains("BudgetTruncated = true", queries, StringComparison.Ordinal);

        var builders = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "DocumentCommandService.cs"));
        Assert.Contains("ResponseSize.Bytes(", builders, StringComparison.Ordinal);
    }
}
