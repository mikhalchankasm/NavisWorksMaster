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

        Assert.Equal(88, commands.Length);
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
    /// `last_operation_status` answers "what did I just lose?", so it must never be
    /// the answer. It is a status poll and is therefore never recorded in the first
    /// place, which is what makes repeated asks idempotent: the second ask still
    /// names the lost call.
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
    // A cancel is not a poll: it changes something, it is recorded, and it can be
    // the lost call.
    [InlineData("cancel_subtree_names_dump")]
    [InlineData("cancel_clash_report")]
    public void ARecordedCommandCanBeTheOneThatWasLost(string command)
    {
        Assert.True(OperationHistoryPolicy.CountsAsLastOperation(command));
    }

    [Theory]
    [InlineData("clash_report_status")]
    [InlineData("clash_run_status")]
    public void TheOtherStatusPollsAreExcludedToo_BecauseTheyAreNeverRecorded(string command)
    {
        // An earlier version of CountsAsLastOperation excluded only
        // last_operation_status and asserted here that clash_report_status "can be
        // the one that was lost". It cannot: RecordOperationStarted,
        // RecordOperationCompleted and RecordOperationFailed all return early for
        // every status poll, so the history never holds one. An external review
        // caught the contradiction.
        Assert.False(OperationHistoryPolicy.CountsAsLastOperation(command));
    }

    [Fact]
    public void TheExclusionIsTheStatusPollRuleInverted_NotASecondList()
    {
        // Two lists would drift. If a fourth status poll is added to
        // IsOperationStatusPollCommand, this predicate has to follow it without
        // being edited.
        var commands = new[]
        {
            HostCommandNames.LastOperationStatus,
            HostCommandNames.ClashReportStatus,
            HostCommandNames.ClashRunStatus,
            HostCommandNames.CancelClashReport,
            "find_items",
            "select_items",
        };

        foreach (var command in commands)
        {
            Assert.Equal(
                !HostRequestPolicy.IsOperationStatusPollCommand(command),
                OperationHistoryPolicy.CountsAsLastOperation(command));
        }
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
    public void TheExclusionIgnoresCase()
    {
        Assert.False(OperationHistoryPolicy.CountsAsLastOperation("Last_Operation_Status"));
    }

    [Fact]
    public void ThePaddedNameIsNotSpecialCasedHere()
    {
        // A first draft of this file asserted that "  Last_Operation_Status  " is
        // excluded, which would have required trimming in this one predicate while
        // GetRequestGateBypassKind -- the rule that actually dispatches a command --
        // does not trim. A padded name is not a bypass command anywhere else, so
        // tolerating it only here would be an inconsistency dressed as robustness.
        // Command names arrive from the protocol, not from a text box.
        Assert.False(HostRequestPolicy.IsOperationStatusPollCommand("  last_operation_status  "));
        Assert.True(OperationHistoryPolicy.CountsAsLastOperation("  last_operation_status  "));
    }
}
