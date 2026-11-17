using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #523 (L3 of #508, 2026-11-17): a background=true call answered with the tool's own response type and
// only backgroundOperationId set, so the reply read "matchedTestCount: 0, outputWritten: false" and an
// agent could take it for an empty result. The start reply now says in words that a job started and
// where its result comes from. The tool methods need a live host context, so their use is pinned by source.
public sealed class BackgroundStartReplyTests
{
    [Fact]
    public void TheStartMessageNamesTheJobAndTheStatusTool()
    {
        var message = BackgroundStartReply.Message("job-123");
        Assert.Contains("job-123", message, StringComparison.Ordinal);
        Assert.Contains("background_job_status", message, StringComparison.Ordinal);
        Assert.Contains("not filled", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NavisworksClashTransferTools.cs", true)]
    [InlineData("NavisworksClashReportTools.cs", true)]
    [InlineData("NavisworksClashTestTools.cs", false)]
    public void EveryBackgroundStartCarriesTheMessage(string file, bool hasMessageField)
    {
        var source = File.ReadAllText(Path.Combine(Root(), "NavisHelper.McpServer", "Tools", file));
        Assert.Contains("Warnings = { BackgroundStartReply.Message(started.OperationId) }", source, StringComparison.Ordinal);
        Assert.Equal(hasMessageField, source.Contains("Message = BackgroundStartReply.Message(started.OperationId)", StringComparison.Ordinal));
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
