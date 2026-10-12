using System.Reflection;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Owner decision 2026-10-12: protected mode is off by default. When on, a host
// command that changes the document, files or the Navisworks process needs the
// user's approval in the panel, granted for one document and one client session.
// View, selection and read commands never need it. This is the pure core (part 1);
// host wiring and the panel come in part 2.
public sealed class ProtectedModeApprovalsTests
{
    private const ToolEffects ApprovalEffects = ToolEffects.Document | ToolEffects.Files | ToolEffects.Host;

    private static IEnumerable<string> HostCommands() => typeof(HostCommandNames)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!);

    [Fact]
    public void ApprovalTableMatchesTheEffectsOfEveryMcpToolWithTheSameName()
    {
        var effects = McpReadOnlyMode.RegisteredToolMethods().ToDictionary(
            McpReadOnlyMode.ToolName,
            method => method.GetCustomAttribute<ToolCapabilitiesAttribute>()!.Effects);
        var mismatches = new List<string>();
        foreach (var command in HostCommands())
        {
            if (!HostCommandEffects.IsKnown(command))
                mismatches.Add(command + " is not classified");
            else if (effects.TryGetValue(command, out var toolEffects) &&
                     HostCommandEffects.RequiresApproval(command) != ((toolEffects & ApprovalEffects) != 0))
                mismatches.Add(command + " disagrees with tool effects " + toolEffects);
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
        Assert.True(HostCommandEffects.RequiresApproval("save_document"));
        Assert.True(HostCommandEffects.RequiresApproval("create_viewpoint"));
        Assert.False(HostCommandEffects.RequiresApproval("find_items"));
        Assert.False(HostCommandEffects.RequiresApproval("hide_selected"));
        Assert.True(HostCommandEffects.RequiresApproval("some_future_command"));
        Assert.False(HostCommandEffects.IsKnown("some_future_command"));
    }

    private sealed class Clock
    {
        public DateTime Now = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);
    }

    private static (ProtectedModeApprovals Gate, Clock Clock) Create(params int[] codes)
    {
        var clock = new Clock();
        var queue = new Queue<int>(codes.Length == 0 ? new[] { 4821, 1234, 5678, 9012 } : codes);
        return (new ProtectedModeApprovals(() => clock.Now, () => queue.Dequeue()) { Enabled = true }, clock);
    }

    [Fact]
    public void DisabledByDefaultAndAllowsEverything()
    {
        var gate = new ProtectedModeApprovals(() => DateTime.UtcNow, () => 1);
        Assert.False(gate.Enabled);
        Assert.True(gate.Check("save_document", "doc-a", "s1", "Claude Code").Allowed);
        Assert.Empty(gate.Pending);
    }

    [Fact]
    public void ReadCommandsNeverNeedApproval()
    {
        var (gate, _) = Create();
        Assert.True(gate.Check("find_items", "doc-a", "s1", "Claude Code").Allowed);
        Assert.True(gate.Check("hide_selected", "doc-a", "s1", "Claude Code").Allowed);
        Assert.Empty(gate.Pending);
    }

    [Fact]
    public void WriteCommandWaitsForApprovalWithOneStableCode()
    {
        var (gate, _) = Create();
        var first = gate.Check("save_document", "doc-a", "s1", "Claude Code");
        var again = gate.Check("create_viewpoint", "doc-a", "s1", "Claude Code");

        Assert.False(first.Allowed);
        Assert.Equal(ErrorCodes.ApprovalRequired, first.ErrorCode);
        Assert.Equal("4821", first.Code);
        Assert.Equal("4821", again.Code);
        var pending = Assert.Single(gate.Pending);
        Assert.Equal(("doc-a", "s1", "Claude Code", "4821"), (pending.DocumentKey, pending.SessionId, pending.ClientName, pending.Code));

        Assert.True(gate.Approve("4821"));
        Assert.Empty(gate.Pending);
        Assert.True(gate.Check("save_document", "doc-a", "s1", "Claude Code").Allowed);
    }

    [Fact]
    public void ApprovalCoversOneDocumentAndOneSession()
    {
        var (gate, _) = Create();
        gate.Check("save_document", "doc-a", "s1", "Claude Code");
        gate.Approve("4821");

        var otherSession = gate.Check("save_document", "doc-a", "s2", "Codex");
        var otherDocument = gate.Check("save_document", "doc-b", "s1", "Claude Code");
        Assert.False(otherSession.Allowed);
        Assert.False(otherDocument.Allowed);
        Assert.Equal(new[] { "1234", "5678" }, gate.Pending.Select(p => p.Code));
    }

    [Fact]
    public void ZeroPaddedCodesStayUniqueAmongPendingRequests()
    {
        var (gate, _) = Create(7, 7, 42);
        Assert.Equal("0007", gate.Check("save_document", "doc-a", "s1", "A").Code);
        Assert.Equal("0042", gate.Check("save_document", "doc-a", "s2", "B").Code);
    }

    [Fact]
    public void DenyRevokeAndDocumentChangeWithdrawAccess()
    {
        var (gate, _) = Create(1, 2, 3, 4, 5);
        Assert.Equal("0001", gate.Check("save_document", "doc-a", "s1", "Claude Code").Code);
        Assert.True(gate.Deny("0001"));
        Assert.Empty(gate.Pending);

        Assert.Equal("0002", gate.Check("save_document", "doc-a", "s1", "Claude Code").Code);
        Assert.True(gate.Approve("0002"));
        Assert.True(gate.Check("save_document", "doc-a", "s1", "Claude Code").Allowed);

        gate.Revoke("doc-a");
        Assert.Equal("0003", gate.Check("save_document", "doc-a", "s1", "Claude Code").Code);
        Assert.True(gate.Approve("0003"));

        Assert.Equal("0004", gate.Check("save_document", "doc-b", "s2", "Codex").Code);
        gate.DocumentChanged("doc-b");
        // The doc-a approval is gone; the doc-b request survives the switch to doc-b.
        Assert.Equal("0005", gate.Check("save_document", "doc-a", "s1", "Claude Code").Code);
        Assert.Equal(new[] { "0004", "0005" }, gate.Pending.Select(p => p.Code));
        Assert.False(gate.Approve("0000"));
        Assert.False(gate.Deny("0000"));
    }

    [Fact]
    public void PendingRequestExpiresAfterTenMinutes()
    {
        var (gate, clock) = Create();
        Assert.Equal("4821", gate.Check("save_document", "doc-a", "s1", "Claude Code").Code);
        clock.Now = clock.Now.AddMinutes(10).AddSeconds(1);
        Assert.False(gate.Approve("4821"));
        Assert.Equal("1234", gate.Check("save_document", "doc-a", "s1", "Claude Code").Code);
    }

    [Fact]
    public void TurningProtectionOffClearsEverything()
    {
        var (gate, _) = Create();
        gate.Check("save_document", "doc-a", "s1", "Claude Code");
        gate.Enabled = false;
        Assert.Empty(gate.Pending);
        gate.Enabled = true;
        Assert.False(gate.Check("save_document", "doc-a", "s1", "Claude Code").Allowed);
    }
}
