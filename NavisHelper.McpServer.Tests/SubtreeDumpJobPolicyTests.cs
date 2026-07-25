using NavisHelper.Agent.Contracts;
using System;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SubtreeDumpJobPolicyTests
{
    [Theory]
    [InlineData(null, 1000)]
    [InlineData(-1, 1000)]
    [InlineData(0, 1000)]
    [InlineData(1, 1)]
    [InlineData(9999, 9999)]
    [InlineData(10000, 10000)]
    [InlineData(10001, 10000)]
    public void NormalizeMaxItemsPerPoll_PreservesLegacyClamp(int? value, int expected)
    {
        Assert.Equal(expected, SubtreeDumpJobPolicy.NormalizeMaxItemsPerPoll(value));
    }

    [Theory]
    [InlineData(null, 500)]
    [InlineData(-1, 500)]
    [InlineData(0, 500)]
    [InlineData(1, 1)]
    [InlineData(2999, 2999)]
    [InlineData(3000, 3000)]
    [InlineData(3001, 3000)]
    public void NormalizeMaxElapsedMs_PreservesLegacyClamp(int? value, int expected)
    {
        Assert.Equal(expected, SubtreeDumpJobPolicy.NormalizeMaxElapsedMs(value));
    }

    [Theory]
    [InlineData("running", true)]
    [InlineData("RUNNING", true)]
    [InlineData("done", false)]
    [InlineData(null, false)]
    public void IsRunning_IsCaseInsensitive(string state, bool expected)
    {
        Assert.Equal(expected, SubtreeDumpJobPolicy.IsRunning(state));
    }

    [Fact]
    public void IsCompletedJobExpired_UsesStrictTwoHourCutoff()
    {
        var now = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(SubtreeDumpJobPolicy.IsCompletedJobExpired(null, now));
        Assert.False(SubtreeDumpJobPolicy.IsCompletedJobExpired(now.AddHours(-2), now));
        Assert.True(SubtreeDumpJobPolicy.IsCompletedJobExpired(now.AddHours(-2).AddTicks(-1), now));
    }

    [Fact]
    public void IsRunningJobExpired_UsesStrictThirtyMinuteCutoffAndRunningState()
    {
        var now = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(SubtreeDumpJobPolicy.IsRunningJobExpired(null, now.AddHours(-1), now));
        Assert.False(SubtreeDumpJobPolicy.IsRunningJobExpired("done", now.AddHours(-1), now));
        Assert.False(SubtreeDumpJobPolicy.IsRunningJobExpired("running", now.AddMinutes(-30), now));
        Assert.True(SubtreeDumpJobPolicy.IsRunningJobExpired("RUNNING", now.AddMinutes(-30).AddTicks(-1), now));
    }

    [Fact]
    public void BuildStatus_MapsRunningJobAndUsesCurrentTime()
    {
        var started = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
        var values = CreateValues(started);

        var result = SubtreeDumpJobPolicy.BuildStatus(values, started.AddTicks(TimeSpan.TicksPerMillisecond * 1250 + 9000));

        Assert.Equal("job-1", result.JobId);
        Assert.Equal("running", result.State);
        Assert.Equal("C:\\out.csv", result.OutputPath);
        Assert.Equal("C:\\out.csv.partial", result.PartialOutputPath);
        Assert.Equal("csv", result.Format);
        Assert.Equal("Root", result.RootName);
        Assert.Equal("Root / Child", result.RootPath);
        Assert.Equal("model.nwd", result.RootSourceFile);
        Assert.Equal(10, result.ItemCount);
        Assert.Equal(2, result.SkippedHiddenItemCount);
        Assert.Equal(12, result.ProcessedItemCount);
        Assert.Equal(4, result.PendingItemCount);
        Assert.Equal(1234, result.FileSizeBytes);
        Assert.Equal(started, result.StartedAtUtc);
        Assert.Equal(started.AddSeconds(1), result.UpdatedAtUtc);
        Assert.Null(result.CompletedAtUtc);
        Assert.Equal(1250, result.ElapsedMs);
        Assert.Equal(string.Empty, result.ErrorMessage);
        Assert.False(result.IsDone);
        Assert.Null(result.InstanceId);
    }

    [Fact]
    public void BuildStatus_CompletedJobUsesCompletionTimeAndPreservesError()
    {
        var started = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
        var values = CreateValues(started);
        values.State = "FAILED";
        values.CompletedAtUtc = started.AddTicks(TimeSpan.TicksPerMillisecond * 2000 + 9000);
        values.ErrorMessage = "failure";

        var result = SubtreeDumpJobPolicy.BuildStatus(values, started.AddHours(1));

        Assert.Equal(2000, result.ElapsedMs);
        Assert.Equal(values.CompletedAtUtc, result.CompletedAtUtc);
        Assert.Equal("failure", result.ErrorMessage);
        Assert.True(result.IsDone);
    }

    [Fact]
    public void BuildStatus_NullValuesAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => SubtreeDumpJobPolicy.BuildStatus(null, DateTime.UtcNow));
    }

    private static SubtreeDumpJobStatusValues CreateValues(DateTime started)
    {
        return new SubtreeDumpJobStatusValues
        {
            JobId = "job-1",
            State = "running",
            OutputPath = "C:\\out.csv",
            PartialOutputPath = "C:\\out.csv.partial",
            Format = "csv",
            RootName = "Root",
            RootPath = "Root / Child",
            RootSourceFile = "model.nwd",
            ItemCount = 10,
            SkippedHiddenItemCount = 2,
            ProcessedItemCount = 12,
            PendingItemCount = 4,
            FileSizeBytes = 1234,
            StartedAtUtc = started,
            UpdatedAtUtc = started.AddSeconds(1),
        };
    }
}
