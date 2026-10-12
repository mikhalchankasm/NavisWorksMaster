using System.Text.Json;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Protected mode, part 2b-1: the MCP server says which session and client sent a
// request, and the host turns a pending approval into an error the agent can act
// on. Status polls and cancellation are never blocked, so a running job can always
// be watched and stopped. The panel toggle comes in the next part; until then the
// gate stays off and nothing changes for users.
public sealed class ProtectedModeGateTests
{
    [Fact]
    public void RequestEnvelopeCarriesSessionAndClient()
    {
        var json = HostRequestEnvelope.Build("req-1", "inst-1", "save_document", 1234,
            new { path = "x" }, McpSessionIdentity.Id, "Claude Code");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(ProtocolConstants.CurrentProtocolVersion, root.GetProperty("protocol_version").GetString());
        Assert.Equal("req-1", root.GetProperty("request_id").GetString());
        Assert.Equal("inst-1", root.GetProperty("instance_id").GetString());
        Assert.Equal("save_document", root.GetProperty("command").GetString());
        Assert.Equal(1234, root.GetProperty("timeout_ms").GetInt32());
        Assert.Equal("x", root.GetProperty("payload").GetProperty("path").GetString());
        Assert.Equal(McpSessionIdentity.Id, root.GetProperty("session_id").GetString());
        Assert.Equal("Claude Code", root.GetProperty("client_name").GetString());

        Assert.True(Guid.TryParse(McpSessionIdentity.Id, out _));
        Assert.Equal(McpSessionIdentity.Id, McpSessionIdentity.Id);
    }

    [Fact]
    public void UnknownClientIsSentAsNull()
    {
        using var document = JsonDocument.Parse(HostRequestEnvelope.Build("r", "i", "find_items", 10, null, "s", null));
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("client_name").ValueKind);
    }

    private static ProtectedModeApprovals EnabledGate() =>
        new(() => new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc), () => 4821) { Enabled = true };

    [Fact]
    public void DisabledGateAllowsEverything()
    {
        var approvals = new ProtectedModeApprovals(() => DateTime.UtcNow, () => 1);
        var decision = ProtectedModeGate.Evaluate(approvals, "save_document", "C:\\a.nwd", "s1", "Claude Code");
        Assert.True(decision.Allowed);
        Assert.Null(decision.Message);
    }

    [Theory]
    [InlineData("dump_subtree_names_status")]
    [InlineData("clash_run_status")]
    [InlineData("cancel_subtree_names_dump")]
    [InlineData("cancel_clash_run")]
    [InlineData("find_items")]
    public void StatusPollsCancellationAndReadsAreNeverBlocked(string command)
    {
        var approvals = EnabledGate();
        Assert.True(ProtectedModeGate.Evaluate(approvals, command, "C:\\a.nwd", "s1", "Claude Code").Allowed);
        Assert.Empty(approvals.Pending);
    }

    [Fact]
    public void WriteCommandReturnsActionableApprovalError()
    {
        var approvals = EnabledGate();
        var decision = ProtectedModeGate.Evaluate(approvals, "save_document", "C:\\models\\a.nwd", "s1", "Claude Code");

        Assert.False(decision.Allowed);
        Assert.Equal(ErrorCodes.ApprovalRequired, decision.ErrorCode);
        Assert.Contains("4821", decision.Message);
        Assert.Contains("Claude Code", decision.Message);
        Assert.Contains("a.nwd", decision.Message);
        Assert.Contains("NavisHelper panel", decision.Message);
        Assert.Single(approvals.Pending);

        approvals.Approve("4821");
        Assert.True(ProtectedModeGate.Evaluate(approvals, "save_document", "C:\\models\\a.nwd", "s1", "Claude Code").Allowed);
    }

    [Fact]
    public void MissingClientNameStillGetsAnApprovalRequest()
    {
        // A missing session id is refused outright: ProtectedModeExecutionCheckTests.
        var approvals = EnabledGate();
        var decision = ProtectedModeGate.Evaluate(approvals, "save_document", "C:\\a.nwd", "s1", null);
        Assert.False(decision.Allowed);
        Assert.Contains("unknown client", decision.Message);
        var pending = Assert.Single(approvals.Pending);
        // #265: the name is kept as received; the panel localizes a missing one.
        Assert.True(string.IsNullOrEmpty(pending.ClientName));
    }

    // #264: the agent can tell the user which session tag to look for in the panel.
    [Fact]
    public void ApprovalMessageCarriesTheSessionTag()
    {
        var approvals = EnabledGate();
        var decision = ProtectedModeGate.Evaluate(approvals, "save_document", "C:\\models\\a.nwd",
            "3F9A1C77-0B2D-4E5F-8A9B-1C2D3E4F5A6B", "Claude Code");

        Assert.False(decision.Allowed);
        Assert.Equal(ErrorCodes.ApprovalRequired, decision.ErrorCode);
        Assert.Contains("Claude Code [3f9a1c]", decision.Message);
        Assert.Contains("4821", decision.Message);
        Assert.Contains("a.nwd", decision.Message);
        Assert.DoesNotContain("3F9A1C77-0B2D-4E5F-8A9B-1C2D3E4F5A6B", decision.Message);
        Assert.Equal("Claude Code", Assert.Single(approvals.Pending).ClientName);
    }

    [Fact]
    public void ApprovalMessageForAMissingClientStaysEnglishAndTagged()
    {
        var decision = ProtectedModeGate.Evaluate(EnabledGate(), "save_document", "C:\\a.nwd", "ab12cd34", "  ");
        Assert.Contains("unknown client [ab12cd]", decision.Message);
    }
}
