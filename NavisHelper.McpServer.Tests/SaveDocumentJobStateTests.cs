using System;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SaveDocumentJobStateTests
{
    private static readonly DateTime Started = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StartNew_WithoutARunningJob_CreatesRunningJob()
    {
        var job = SaveDocumentJobState.StartNew("save-job-1", @"C:\model\plant.nwd", null, Started);

        Assert.Equal("save-job-1", job.OperationId);
        Assert.Equal(SaveDocumentJobStates.Running, job.State);
        Assert.True(job.IsRunning);
        Assert.Equal(@"C:\model\plant.nwd", job.Path);
        Assert.Equal(Started, job.StartedAtUtc);
        Assert.Null(job.CompletedAtUtc);
        Assert.Null(job.ErrorMessage);
    }

    [Fact]
    public void StartNew_WhileAJobIsRunning_IsRejectedAsASchemaError()
    {
        var running = SaveDocumentJobState.StartNew("save-job-1", @"C:\model\plant.nwd", null, Started);

        var error = Assert.Throws<InvalidOperationException>(
            () => SaveDocumentJobState.StartNew("save-job-2", @"C:\model\plant.nwd", running, Started.AddSeconds(1)));

        Assert.Contains("already running", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("save-job-1", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SaveDocumentJobStates.Completed)]
    [InlineData(SaveDocumentJobStates.Failed)]
    public void StartNew_AfterThePreviousJobFinished_IsAllowed(string finishedState)
    {
        var previous = SaveDocumentJobState.StartNew("save-job-1", @"C:\model\plant.nwd", null, Started);
        previous = finishedState == SaveDocumentJobStates.Completed
            ? previous.Complete(@"C:\model\plant.nwd", "nwd", 4096, Started.AddMinutes(2))
            : previous.Fail("Navisworks did not save the document.", Started.AddMinutes(2));

        var next = SaveDocumentJobState.StartNew("save-job-2", @"C:\model\plant.nwd", previous, Started.AddMinutes(3));

        Assert.Equal("save-job-2", next.OperationId);
        Assert.True(next.IsRunning);
    }

    [Fact]
    public void Complete_RecordsTheSaveOutcomeAndStopsTheClock()
    {
        var completedAt = Started.AddSeconds(95);
        var job = SaveDocumentJobState.StartNew("save-job-1", @"C:\model\plant.nwd", null, Started)
            .Complete(@"C:\model\plant.nwd", "nwd", 419430400, completedAt);

        Assert.Equal(SaveDocumentJobStates.Completed, job.State);
        Assert.False(job.IsRunning);
        Assert.Equal("nwd", job.Format);
        Assert.Equal(419430400, job.FileSizeBytes);
        Assert.Equal(completedAt, job.CompletedAtUtc);
        Assert.Equal(95000, job.ElapsedMs(Started.AddHours(1)));
        Assert.Null(job.ErrorMessage);
    }

    [Fact]
    public void Fail_RecordsTheErrorAndTheFailedState()
    {
        var failedAt = Started.AddSeconds(12);
        var job = SaveDocumentJobState.StartNew("save-job-1", @"C:\model\plant.nwd", null, Started)
            .Fail("Navisworks could not save the document.", failedAt);

        Assert.Equal(SaveDocumentJobStates.Failed, job.State);
        Assert.False(job.IsRunning);
        Assert.Equal("Navisworks could not save the document.", job.ErrorMessage);
        Assert.Equal(failedAt, job.CompletedAtUtc);
        Assert.Equal(12000, job.ElapsedMs(failedAt));
    }

    [Fact]
    public void ElapsedMs_WhileRunning_TracksTheCurrentTime()
    {
        var job = SaveDocumentJobState.StartNew("save-job-1", @"C:\model\plant.nwd", null, Started);

        Assert.Equal(0, job.ElapsedMs(Started));
        Assert.Equal(45000, job.ElapsedMs(Started.AddSeconds(45)));
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("fail")]
    public void FinishedJobs_CannotTransitionAgain(string action)
    {
        var job = SaveDocumentJobState.StartNew("save-job-1", @"C:\model\plant.nwd", null, Started)
            .Complete(@"C:\model\plant.nwd", "nwd", 10, Started.AddSeconds(5));

        Assert.Throws<InvalidOperationException>(
            () => action == "complete" ? job.Complete(@"C:\model\plant.nwd", "nwd", 10, Started.AddSeconds(6)) : job.Fail("late", Started.AddSeconds(6)));
    }

    [Fact]
    public void BuildStatusResponse_MapsEveryFieldAndSpeaksInTheJobState()
    {
        var running = SaveDocumentJobState.StartNew("save-job-1", @"C:\model\plant.nwd", null, Started);
        var runningResponse = running.BuildStatusResponse(Started.AddSeconds(3));

        Assert.Equal("save-job-1", runningResponse.OperationId);
        Assert.Equal(SaveDocumentJobStates.Running, runningResponse.State);
        Assert.True(runningResponse.IsRunning);
        Assert.Equal(@"C:\model\plant.nwd", runningResponse.Path);
        Assert.Equal(3000, runningResponse.ElapsedMs);
        Assert.Equal(string.Empty, runningResponse.ErrorMessage);
        Assert.Null(runningResponse.CompletedAtUtc);
        Assert.Contains("running", runningResponse.Message, StringComparison.OrdinalIgnoreCase);

        var failed = running.Fail("disk full", Started.AddSeconds(9));
        var failedResponse = failed.BuildStatusResponse(Started.AddSeconds(10));

        Assert.Equal(SaveDocumentJobStates.Failed, failedResponse.State);
        Assert.False(failedResponse.IsRunning);
        Assert.Equal("disk full", failedResponse.ErrorMessage);
        Assert.Contains("disk full", failedResponse.Message, StringComparison.Ordinal);
        Assert.Equal(9000, failedResponse.ElapsedMs);
        Assert.Equal(Started.AddSeconds(9), failedResponse.CompletedAtUtc);
    }

    [Fact]
    public void BuildStartResponse_ReturnsAtOnceAndPointsAtTheStatusTool()
    {
        var response = SaveDocumentJobState.StartNew("save-job-1", @"C:\model\plant.nwd", null, Started)
            .BuildStartResponse(Started);

        Assert.Equal("save-job-1", response.OperationId);
        Assert.Equal(SaveDocumentJobStates.Running, response.State);
        Assert.True(response.IsRunning);
        Assert.Equal(@"C:\model\plant.nwd", response.Path);
        Assert.Equal(0, response.ElapsedMs);
        Assert.Contains("save_document_status", response.Message, StringComparison.Ordinal);
        Assert.Contains("save-job-1", response.Message, StringComparison.Ordinal);
    }
}
