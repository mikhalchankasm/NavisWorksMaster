using NavisHelper.Agent.Contracts;
using System.Reflection;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class HostRequestPolicyTests
{
    public static TheoryData<string, HostRequestGateBypassKind, bool> KnownCommands => new()
    {
        { HostCommandNames.ClashReportStatus, HostRequestGateBypassKind.ClashReportStatus, true },
        { HostCommandNames.LastOperationStatus, HostRequestGateBypassKind.LastOperationStatus, true },
        { HostCommandNames.CancelClashReport, HostRequestGateBypassKind.CancelClashReport, false },
        { HostCommandNames.CancelSubtreeNamesDump, HostRequestGateBypassKind.CancelSubtreeNamesDump, false },
        { HostCommandNames.ClashRunStatus, HostRequestGateBypassKind.ClashRunStatus, true },
        { HostCommandNames.CancelClashRun, HostRequestGateBypassKind.CancelClashRun, false },
    };

    [Theory]
    [MemberData(nameof(KnownCommands))]
    public void KnownBypassCommands_HaveSingleClassification(string command, HostRequestGateBypassKind expectedKind, bool statusPoll)
    {
        Assert.Equal(expectedKind, HostRequestPolicy.GetRequestGateBypassKind(command));
        Assert.True(HostRequestPolicy.IsRequestGateBypassCommand(command));
        Assert.Equal(statusPoll, HostRequestPolicy.IsOperationStatusPollCommand(command));
    }

    [Theory]
    [MemberData(nameof(KnownCommands))]
    public void Classification_IsOrdinalIgnoreCase(string command, HostRequestGateBypassKind expectedKind, bool statusPoll)
    {
        var upper = command.ToUpperInvariant();

        Assert.Equal(expectedKind, HostRequestPolicy.GetRequestGateBypassKind(upper));
        Assert.True(HostRequestPolicy.IsRequestGateBypassCommand(upper));
        Assert.Equal(statusPoll, HostRequestPolicy.IsOperationStatusPollCommand(upper));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData(HostCommandNames.HostStatus)]
    public void NonBypassCommands_AreNotClassified(string command)
    {
        Assert.Equal(HostRequestGateBypassKind.None, HostRequestPolicy.GetRequestGateBypassKind(command));
        Assert.False(HostRequestPolicy.IsRequestGateBypassCommand(command));
        Assert.False(HostRequestPolicy.IsOperationStatusPollCommand(command));
    }

    [Fact]
    public void CompleteHostCommandSurface_HasExactBypassAndStatusPollSets()
    {
        var commands = typeof(HostCommandNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(87, commands.Length);
        Assert.Equal(
            new[]
            {
                HostCommandNames.CancelClashReport,
                HostCommandNames.CancelClashRun,
                HostCommandNames.CancelSubtreeNamesDump,
                HostCommandNames.ClashReportStatus,
                HostCommandNames.ClashRunStatus,
                HostCommandNames.LastOperationStatus,
            },
            commands.Where(HostRequestPolicy.IsRequestGateBypassCommand).OrderBy(command => command));
        Assert.Equal(
            new[]
            {
                HostCommandNames.ClashReportStatus,
                HostCommandNames.ClashRunStatus,
                HostCommandNames.LastOperationStatus,
            },
            commands.Where(HostRequestPolicy.IsOperationStatusPollCommand).OrderBy(command => command));
    }

    [Theory]
    [InlineData("completed", true, true)]
    [InlineData("COMPLETED", true, true)]
    [InlineData("completed", false, false)]
    [InlineData("failed", true, false)]
    [InlineData("running", null, false)]
    public void OperationHistory_SuccessfulCompletionIsAuthoritative(
        string state,
        bool? ok,
        bool expected)
    {
        Assert.Equal(expected, OperationHistoryPolicy.IsAuthoritativeSuccessfulCompletion(state, ok));
    }

    /// <summary>
    /// `last_operation_status` answers "what did I just lose?". It is recorded in the
    /// history like every other command, so without this exclusion a caller who asks
    /// without a request_id is told about its own question.
    ///
    /// Observed live: a find_items call timed out at the MCP client while the host log
    /// recorded that same call completing ok in 118 ms. The reply was produced and lost
    /// in transport, and the caller had no request_id to ask about, because a request_id
    /// arrives with the reply that never came.
    /// </summary>
    [Fact]
    public void AskingAboutTheAnswerIsNotItselfAnOperation()
    {
        Assert.False(OperationHistoryPolicy.CountsAsLastOperation(HostCommandNames.LastOperationStatus));
    }

    [Theory]
    [InlineData("find_items")]
    [InlineData("select_items")]
    [InlineData("cancel_subtree_names_dump")]
    [InlineData("clash_report_status")]
    public void EveryOtherCommandCanBeTheOneThatWasLost(string command)
    {
        // A cancel is a real operation, and a status poll the caller issued
        // deliberately is a fact about the session worth reporting. Only the question
        // about the answer is excluded.
        Assert.True(OperationHistoryPolicy.CountsAsLastOperation(command));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnnamedCommandIsNotAnAnswer(string command)
    {
        Assert.False(OperationHistoryPolicy.CountsAsLastOperation(command));
    }

    [Fact]
    public void TheExclusionIgnoresCaseAndSurroundingSpace()
    {
        Assert.False(OperationHistoryPolicy.CountsAsLastOperation("  Last_Operation_Status  "));
    }
}
