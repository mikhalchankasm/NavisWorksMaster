using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Review of #248 (owner, 2026-10-12):
// 1. The pipe-thread check is only an early answer. The final check runs on the UI
//    thread right before the command changes the document, against a ticket that binds
//    the request to the document instance it was accepted for. A document switch or a
//    revoked approval in between refuses the command.
// 2. With protection on, a write without a non-empty session id is refused outright:
//    one client's approval must never cover clients that do not identify themselves.
// 3. Only an explicit list of known status and cancel commands skips approval; an
//    unknown command, whatever its name, still needs it.
public sealed class ProtectedModeExecutionCheckTests
{
    private static ProtectedModeApprovals Gate(bool enabled = true, params int[] codes)
    {
        var queue = new Queue<int>(codes.Length == 0 ? new[] { 1, 2, 3, 4 } : codes);
        return new ProtectedModeApprovals(
            () => new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc), () => queue.Dequeue()) { Enabled = enabled };
    }

    private static ProtectedModeGateDecision Approved(ProtectedModeApprovals gate, string document, string session)
    {
        var first = ProtectedModeGate.Evaluate(gate, "save_document", document, session, "Claude Code");
        Assert.False(first.Allowed);
        Assert.True(gate.Approve(Assert.Single(gate.Pending, p => p.SessionId == session).Code));
        var accepted = ProtectedModeGate.Evaluate(gate, "save_document", document, session, "Claude Code");
        Assert.True(accepted.Allowed);
        Assert.NotNull(accepted.Ticket);
        return accepted;
    }

    [Fact]
    public void AcceptedWriteStillRunsWhenNothingChanged()
    {
        var gate = Gate();
        gate.DocumentChanged("C:\\a.nwd");
        var accepted = Approved(gate, "C:\\a.nwd", "s1");
        Assert.True(gate.Confirm(accepted.Ticket));
    }

    [Fact]
    public void DocumentSwitchBetweenAcceptanceAndExecutionRefusesTheWrite()
    {
        var gate = Gate();
        gate.DocumentChanged("C:\\a.nwd");
        var accepted = Approved(gate, "C:\\a.nwd", "s1");

        // Reopening the same file is a new document instance.
        gate.DocumentChanged("C:\\a.nwd");
        Assert.False(gate.Confirm(accepted.Ticket));
    }

    [Fact]
    public void RevokeBetweenAcceptanceAndExecutionRefusesTheWrite()
    {
        var gate = Gate();
        gate.DocumentChanged("C:\\a.nwd");
        var accepted = Approved(gate, "C:\\a.nwd", "s1");

        gate.Revoke("C:\\a.nwd");
        Assert.False(gate.Confirm(accepted.Ticket));

        // A fresh approval does not revive the old ticket.
        Assert.False(ProtectedModeGate.Evaluate(gate, "save_document", "C:\\a.nwd", "s1", "Claude Code").Allowed);
        gate.Approve(Assert.Single(gate.Pending).Code);
        Assert.False(gate.Confirm(accepted.Ticket));
    }

    [Fact]
    public void TurningProtectionOnBetweenAcceptanceAndExecutionRefusesAnUnapprovedWrite()
    {
        var gate = Gate(enabled: false);
        var accepted = ProtectedModeGate.Evaluate(gate, "save_document", "C:\\a.nwd", "s1", "Claude Code");
        Assert.True(accepted.Allowed);
        gate.Enabled = true;
        Assert.False(gate.Confirm(accepted.Ticket));
    }

    [Fact]
    public void WithProtectionOffADocumentSwitchChangesNothing()
    {
        var gate = Gate(enabled: false);
        gate.DocumentChanged("C:\\a.nwd");
        var accepted = ProtectedModeGate.Evaluate(gate, "save_document", "C:\\a.nwd", null, null);
        Assert.True(accepted.Allowed);
        gate.DocumentChanged("C:\\b.nwd");
        Assert.True(gate.Confirm(accepted.Ticket));
        Assert.Null(accepted.Ticket.GrantId);
    }

    // External review of #248 (P2): turning protection off after an approved request was
    // accepted must not refuse it.
    [Fact]
    public void TurningProtectionOffBetweenAcceptanceAndExecutionLetsTheWriteRun()
    {
        var gate = Gate();
        gate.DocumentChanged("C:\\a.nwd");
        var accepted = Approved(gate, "C:\\a.nwd", "s1");
        gate.Enabled = false;
        Assert.True(gate.Confirm(accepted.Ticket));
        Assert.True(gate.Confirm(accepted.Ticket, "C:\\b.nwd"));
    }

    [Fact]
    public void ActiveDocumentMustMatchTheAcceptedOneWhenProtected()
    {
        var gate = Gate();
        gate.DocumentChanged("C:\\a.nwd");
        var accepted = Approved(gate, "C:\\a.nwd", "s1");
        Assert.True(gate.Confirm(accepted.Ticket, "C:\\a.nwd"));
        Assert.False(gate.Confirm(accepted.Ticket, "C:\\b.nwd"));
    }

    [Theory]
    [InlineData("find_items")]
    [InlineData("clash_run_status")]
    [InlineData("cancel_clash_run")]
    public void ReadsStatusPollsAndCancellationConfirmAcrossDocumentSwitches(string command)
    {
        var gate = Gate();
        gate.DocumentChanged("C:\\a.nwd");
        var accepted = ProtectedModeGate.Evaluate(gate, command, "C:\\a.nwd", "s1", "Claude Code");
        Assert.True(accepted.Allowed);
        gate.DocumentChanged("C:\\b.nwd");
        Assert.True(gate.Confirm(accepted.Ticket));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WriteWithoutSessionIdIsRefusedAndNeverQueued(string sessionId)
    {
        var gate = Gate();
        var decision = ProtectedModeGate.Evaluate(gate, "save_document", "C:\\a.nwd", sessionId, "Claude Code");
        Assert.False(decision.Allowed);
        Assert.Equal(ErrorCodes.SessionRequired, decision.ErrorCode);
        Assert.Null(decision.Ticket);
        Assert.Empty(gate.Pending);
    }

    [Fact]
    public void TwoClientsWithoutSessionIdCannotRideOnAnApproval()
    {
        var gate = Gate();
        gate.DocumentChanged("C:\\a.nwd");
        Approved(gate, "C:\\a.nwd", "s1");

        Assert.False(ProtectedModeGate.Evaluate(gate, "save_document", "C:\\a.nwd", null, "Codex").Allowed);
        Assert.False(ProtectedModeGate.Evaluate(gate, "save_document", "C:\\a.nwd", "", "Cursor").Allowed);
        Assert.Empty(gate.Pending);
        Assert.True(ProtectedModeGate.Evaluate(gate, "save_document", "C:\\a.nwd", "s1", "Claude Code").Allowed);
    }

    [Fact]
    public void ReadWithoutSessionIdStillWorks()
    {
        var gate = Gate();
        Assert.True(ProtectedModeGate.Evaluate(gate, "find_items", "C:\\a.nwd", null, null).Allowed);
    }

    [Theory]
    [InlineData("future_status")]
    [InlineData("cancel_future_job")]
    [InlineData("totally_new_command")]
    public void UnknownCommandsNeedApprovalWhateverTheirName(string command)
    {
        var gate = Gate();
        var decision = ProtectedModeGate.Evaluate(gate, command, "C:\\a.nwd", "s1", "Claude Code");
        Assert.False(decision.Allowed);
        Assert.Equal(ErrorCodes.ApprovalRequired, decision.ErrorCode);
    }

    [Fact]
    public void ExemptCommandsAreExactlyTheKnownStatusAndCancelCommands()
    {
        Assert.Equal(
            new[]
            {
                HostCommandNames.CancelClashReport, HostCommandNames.CancelClashRun, HostCommandNames.CancelSubtreeNamesDump,
                HostCommandNames.ClashReportStatus, HostCommandNames.ClashRunStatus, HostCommandNames.DumpSubtreeNamesStatus,
                HostCommandNames.LastOperationStatus, HostCommandNames.SaveDocumentStatus,
            }.OrderBy(name => name, StringComparer.Ordinal),
            ProtectedModeGate.ExemptCommands.OrderBy(name => name, StringComparer.Ordinal));
    }
}
