using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class HostRequestLogLinesTests
{
    [Fact]
    public void Completed_KeepsTheLineTheLatencyBaselineReads()
    {
        Assert.Equal(
            "request_id=req-1 command=host_status elapsed_ms=22",
            HostRequestLogLines.Completed("req-1", "host_status", 22));
    }

    [Fact]
    public void Arrival_KeepsTheLineTheTransportUsedToBuildInline()
    {
        Assert.Equal(
            "request_id=req-1 command=find_items received",
            HostRequestLogLines.Arrival("req-1", "find_items"));
    }

    [Fact]
    public void Arrival_RendersAMissingIdentityAsTheNullPlaceholder()
    {
        // A frame whose request_id cannot be read still has to be findable in the log.
        Assert.Equal(
            "request_id=<null> command=<null> received",
            HostRequestLogLines.Arrival(null, null));
    }

    [Fact]
    public void OperationStart_CarriesEveryFactTheDroppedStageLinesCarried()
    {
        Assert.Equal(
            "request_id=req-1 command=find_items operation_start timeout_ms=60000 " +
            "dispatcher=synchronization_context document=\"6501.5.nwd\" selected_item_count=3616 " +
            "parameters={query=pipe} elapsed_ms=7",
            HostRequestLogLines.OperationStart(
                "req-1",
                "find_items",
                60000,
                "synchronization_context",
                "6501.5.nwd",
                3616,
                "{query=pipe}",
                7));
    }

    [Fact]
    public void OperationStart_KeepsTheHostSentinelsForNoDocumentAndAnUnreadableSelection()
    {
        Assert.Equal(
            "request_id=req-1 command=host_status operation_start timeout_ms=1000 dispatcher=none " +
            "document=\"\" selected_item_count=-1 parameters={} elapsed_ms=0",
            HostRequestLogLines.OperationStart(
                "req-1",
                "host_status",
                1000,
                "none",
                string.Empty,
                -1,
                "{}",
                0));
    }

    [Fact]
    public void BypassCompleted_KeepsItsOwnMarker()
    {
        Assert.Equal(
            "request_id=req-2 command=clash_report_status bypass elapsed_ms=4",
            HostRequestLogLines.BypassCompleted("req-2", "clash_report_status", 4));
    }

    [Fact]
    public void TheRequestPathBuildsEachOfItsLinesOnce_AndTheDroppedStagesStayDropped()
    {
        var dispatch = Read("NavisHelper", "Agent", "Host", "AgentHostService.Dispatch.cs");
        var transport = Read("NavisHelper", "Agent", "Host", "AgentHostService.Transport.cs");

        Assert.Equal(1, Count(transport, "HostRequestLogLines.Arrival("));
        Assert.Equal(1, Count(dispatch, "HostRequestLogLines.OperationStart("));
        Assert.Equal(1, Count(dispatch, "HostRequestLogLines.Completed("));
        Assert.Equal(1, Count(dispatch, "HostRequestLogLines.BypassCompleted("));

        foreach (var stage in new[]
                 {
                     "ui_dispatch_start",
                     "ui_callback_start",
                     "active_document_resolved",
                     "discovery_refreshed",
                     "ui_dispatch_done",
                     "find_items_search_start",
                 })
        {
            Assert.DoesNotContain(stage, dispatch, StringComparison.Ordinal);
            Assert.DoesNotContain(stage, transport, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRequestPathWritesThreeInfoLines_AndABypassWritesTwo()
    {
        // Raising either number adds a line to every request, and each line costs a named
        // mutex and a file open inside the host time the baseline measures. Dispatch holds
        // the arrival line's two partners plus the dispatcher-cleared line, which is not
        // request-scoped; transport holds arrival plus the two request-gate rejections.
        var dispatch = Read("NavisHelper", "Agent", "Host", "AgentHostService.Dispatch.cs");
        var transport = Read("NavisHelper", "Agent", "Host", "AgentHostService.Transport.cs");

        Assert.Equal(4, Count(dispatch, "Logger.Info("));
        Assert.Equal(3, Count(transport, "Logger.Info("));
    }

    private static int Count(string source, string token)
    {
        var count = 0;
        var index = source.IndexOf(token, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = source.IndexOf(token, index + token.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate NavisHelper.sln.");
    }
}
