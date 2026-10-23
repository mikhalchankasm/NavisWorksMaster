using System;
using System.IO;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// The service is plugin code that L0 cannot run, so these pin its source. Found by the 2.18.0.0 L3 run
// (#381): each stacked copy's geometry leaf has its own parent node, so keying parents by identity never
// reported sameParentAndName, while the contract promises a comparison of the printed parent path.
public sealed class DuplicateGeometryParentKeyTests
{
    [Fact]
    public void ParentsAreKeyedByPrintedPathAndRootsByIdentity()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "DuplicateGeometryService.cs"));
        Assert.Contains("RootKey = NodeKey(root", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ParentKey = NodeKey(", service, StringComparison.Ordinal);
        Assert.Contains("SearchService.PrintItemPath(parent)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void TheContractNoLongerSaysParentsAreComparedByIdentity()
    {
        var contracts = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "docs", "MCP_TOOL_CONTRACTS.md"));
        Assert.DoesNotContain("Roots and parents are compared by node identity", contracts, StringComparison.Ordinal);
        Assert.Contains("ordinal parent path equality", contracts, StringComparison.Ordinal);
    }
}
