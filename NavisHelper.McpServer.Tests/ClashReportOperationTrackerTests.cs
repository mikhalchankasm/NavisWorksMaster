using System;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashReportOperationTrackerTests
{
    [Fact]
    public void GetStatus_ReturnsEmptyStatusBeforeOperation()
    {
        var tracker = CreateTracker();

        var status = tracker.GetStatus("");

        Assert.False(status.IsRunning);
        Assert.False(status.CancelRequested);
        Assert.Equal(string.Empty, status.State);
        Assert.Equal("No clash report operation was found.", status.Message);
    }

    [Fact]
    public void Start_PublishesRunningOperationAndRejectsSecondRunningOperation()
    {
        var tracker = CreateTracker();

        var operation = tracker.Start(@"C:\out", @"C:\out\report.html", @"C:\out\manifest.json", 10, 25);
        var status = tracker.GetStatus(operation.OperationId);

        Assert.StartsWith("clash-report-", operation.OperationId);
        Assert.True(status.IsRunning);
        Assert.Equal(ClashReportOperationTracker.Running, status.State);
        Assert.Equal(@"C:\out", status.OutputDirectory);
        Assert.Equal(@"C:\out\report.html", status.ReportPath);
        Assert.Equal(@"C:\out\manifest.json", status.ManifestPath);
        Assert.Equal(10, status.ResultOffset);
        Assert.Equal(25, status.TotalBatchCount);
        Assert.Throws<InvalidOperationException>(() => tracker.Start("", "", "", 0, 0));
    }

    [Fact]
    public void UpdateMethods_PublishCurrentItemAndProgress()
    {
        var tracker = CreateTracker();
        var operation = tracker.Start("", "", "", 0, 0);

        tracker.UpdateState(operation, ClashReportOperationTracker.RunningTests, "Running tests");
        tracker.UpdateBatchScope(operation, 3);
        tracker.UpdateCurrent(operation, "240101", "Clash 001", 42);
        tracker.UpdateProgress(operation, 2, 1, 4);
        var status = tracker.GetStatus("");

        Assert.True(status.IsRunning);
        Assert.Equal(ClashReportOperationTracker.RunningTests, status.State);
        Assert.Equal("Running tests", status.Message);
        Assert.Equal(3, status.TotalBatchCount);
        Assert.Equal("240101", status.CurrentTestName);
        Assert.Equal("Clash 001", status.CurrentResultName);
        Assert.Equal(2, status.ProcessedResultCount);
        Assert.Equal(1, status.CreatedViewpointCount);
        Assert.Equal(4, status.ScreenshotCount);
    }

    [Fact]
    public void Cancel_SetsRequestFlagForRunningOperation()
    {
        var tracker = CreateTracker();
        var operation = tracker.Start("", "", "", 0, 0);

        var cancelled = tracker.Cancel(operation.OperationId);

        Assert.True(cancelled.IsRunning);
        Assert.True(cancelled.CancelRequested);
        Assert.True(cancelled.CancelAccepted);
        Assert.True(tracker.IsCancellationRequested(operation));
    }

    [Fact]
    public void Complete_ClearsRunningOperationButKeepsLastStatus()
    {
        var now = new DateTime(2026, 7, 7, 12, 0, 0, DateTimeKind.Utc);
        var tracker = new ClashReportOperationTracker(() => now);
        var operation = tracker.Start("", "", "", 0, 0);
        now = now.AddMilliseconds(1250);

        tracker.Complete(operation, false, "Done");
        var status = tracker.GetStatus("");

        Assert.False(status.IsRunning);
        Assert.False(status.CancelRequested);
        Assert.Equal(ClashReportOperationTracker.Done, status.State);
        Assert.Equal("Done", status.Message);
        Assert.Equal(1250, status.ElapsedMs);
        Assert.NotNull(status.CompletedAtUtc);
    }

    [Fact]
    public void FailRunning_MarksActiveOperationFailedAndCancelled()
    {
        var tracker = CreateTracker();
        tracker.Start("", "", "", 0, 0);

        tracker.FailRunning("Host shutdown");
        var status = tracker.GetStatus("");

        Assert.False(status.IsRunning);
        Assert.True(status.CancelRequested);
        Assert.Equal(ClashReportOperationTracker.Failed, status.State);
        Assert.Equal("Host shutdown", status.Message);
    }

    [Fact]
    public void Cancel_ReturnsNotAcceptedForCompletedOperation()
    {
        var tracker = CreateTracker();
        var operation = tracker.Start("", "", "", 0, 0);
        tracker.Complete(operation, false, "Done");

        var status = tracker.Cancel(operation.OperationId);

        Assert.False(status.IsRunning);
        Assert.False(status.CancelRequested);
        Assert.False(status.CancelAccepted);
        Assert.Equal(ClashReportOperationTracker.Done, status.State);
    }

    private static ClashReportOperationTracker CreateTracker()
    {
        var now = new DateTime(2026, 7, 7, 12, 0, 0, DateTimeKind.Utc);
        return new ClashReportOperationTracker(() => now);
    }
}
