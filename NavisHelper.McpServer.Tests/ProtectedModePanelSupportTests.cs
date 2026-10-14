using System.Text.RegularExpressions;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.Core;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Protected mode, part 2b-2: what the panel needs from the core, on the #248 contract.
// The panel lists pending requests and granted approvals and refreshes on Changed,
// which fires only when Enabled, the pending list or the granted list really changed.
// Revoking one client drops exactly that grant id; a write without a session is
// refused and never listed. The panel only decides (Approve, Deny, Revoke, Enabled);
// it never runs Check or Confirm itself. The toggle is persisted per user and stays
// off unless the user turned it on.
public sealed class ProtectedModePanelSupportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NavisHelper-Protected-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static ProtectedModeApprovals Gate(params int[] codes)
    {
        var queue = new Queue<int>(codes);
        return new ProtectedModeApprovals(() => new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc), () => queue.Dequeue());
    }

    [Fact]
    public void ChangedFiresOnlyOnRealChanges()
    {
        var gate = Gate(1, 2);
        var changes = 0;
        gate.Changed += (_, _) => changes++;

        gate.Enabled = true; Assert.Equal(1, changes);
        gate.Enabled = true; Assert.Equal(1, changes);
        gate.Check("save_document", "doc-a", "s1", "Claude Code"); Assert.Equal(2, changes);
        gate.Check("save_document", "doc-a", "s1", "Claude Code"); Assert.Equal(2, changes);
        gate.Check("find_items", "doc-a", "s1", "Claude Code"); Assert.Equal(2, changes);
        gate.Approve("0001"); Assert.Equal(3, changes);
        gate.Check("save_document", "doc-a", "s1", "Claude Code"); Assert.Equal(3, changes);
        gate.Check("save_document", "doc-a", "s2", "Codex"); Assert.Equal(4, changes);
        gate.Deny("0002"); Assert.Equal(5, changes);
        Assert.False(gate.Deny("0002")); Assert.Equal(5, changes);
        Assert.False(gate.Approve("9999")); Assert.Equal(5, changes);
        gate.Revoke("doc-a", "s9"); Assert.Equal(5, changes);
        gate.Revoke("doc-a", "s1"); Assert.Equal(6, changes);
        gate.Revoke("doc-a", "s1"); Assert.Equal(6, changes);
        gate.DocumentChanged("doc-b"); Assert.Equal(6, changes);
        gate.Enabled = false; Assert.Equal(7, changes);
        gate.Enabled = false; Assert.Equal(7, changes);
    }

    [Fact]
    public void DocumentChangeThatDropsApprovalsNotifies()
    {
        var gate = Gate(1);
        gate.Enabled = true;
        gate.Check("save_document", "doc-a", "s1", "Claude Code");
        gate.Approve("0001");
        var changes = 0;
        gate.Changed += (_, _) => changes++;

        gate.DocumentChanged("doc-a");
        Assert.Equal(1, changes);
        Assert.Empty(gate.Approved);
    }

    // #258: dropping a pending request on a reopen of the same document is a real change.
    [Fact]
    public void DocumentChangeThatDropsAPendingRequestNotifiesOnce()
    {
        var gate = Gate(1);
        gate.Enabled = true;
        gate.DocumentChanged("doc-a");
        gate.Check("save_document", "doc-a", "s1", "Claude Code");
        var changes = 0;
        gate.Changed += (_, _) => changes++;

        gate.DocumentChanged("doc-a");
        Assert.Equal(1, changes);
        Assert.Empty(gate.Pending);
    }

    [Fact]
    public void ApprovedListsGrantsAndRevokeDropsExactlyOneGrantId()
    {
        var gate = Gate(1, 2, 3);
        gate.Enabled = true;
        gate.DocumentChanged("doc-a");
        gate.Check("save_document", "doc-a", "s1", "Claude Code");
        gate.Check("save_document", "doc-a", "s2", "Codex");
        gate.Approve("0001");
        gate.Approve("0002");

        Assert.Equal(new[] { ("doc-a", "s1", "Claude Code"), ("doc-a", "s2", "Codex") },
            gate.Approved.Select(grant => (grant.DocumentKey, grant.SessionId, grant.ClientName)));
        var grants = gate.Approved.ToDictionary(grant => grant.SessionId, grant => grant.GrantId);
        Assert.NotEqual(grants["s1"], grants["s2"]);

        var first = gate.Check("save_document", "doc-a", "s1", "Claude Code").Ticket!;
        var second = gate.Check("save_document", "doc-a", "s2", "Codex").Ticket!;
        Assert.Equal(grants["s1"], first.GrantId);
        Assert.Equal(grants["s2"], second.GrantId);

        gate.Revoke("doc-a", "s1");

        var remaining = Assert.Single(gate.Approved);
        Assert.Equal(grants["s2"], remaining.GrantId);
        Assert.False(gate.Confirm(first, "doc-a"));
        Assert.True(gate.Confirm(second, "doc-a"));
        Assert.False(gate.Check("save_document", "doc-a", "s1", "Claude Code").Allowed);
        Assert.Equal(grants["s2"], gate.Check("save_document", "doc-a", "s2", "Codex").Ticket!.GrantId);
    }

    [Fact]
    public void WritesWithoutASessionNeverAppearInThePanel()
    {
        var gate = Gate(1);
        gate.Enabled = true;
        var changes = 0;
        gate.Changed += (_, _) => changes++;

        foreach (var session in new[] { null, "", "  " })
            Assert.Equal(ErrorCodes.SessionRequired, gate.Check("save_document", "doc-a", session!, "Claude Code").ErrorCode);

        Assert.Empty(gate.Pending);
        Assert.Empty(gate.Approved);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void PanelSectionOnlyDecidesAndNeverRunsTheAccessCheck()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "WPF", "ProtectedModeSection.cs"));
        Assert.DoesNotMatch(new Regex(@"\.\s*(Check|Confirm)\s*\("), source);
        Assert.DoesNotContain("ProtectedModeGate", source);
        Assert.Contains(".Approve(", source);
        Assert.Contains(".Deny(", source);
        Assert.Contains(".Revoke(", source);
        Assert.Contains(".Changed", source);
    }

    // #276: a long client name wrapped Deny onto its own line, away from Allow. A row
    // now takes all of its buttons at once and keeps them in one horizontal container
    // docked right; only the text beside them wraps.
    [Fact]
    public void RowButtonsShareOneContainerSeparateFromTheWrappingText()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "WPF", "ProtectedModeSection.cs"));
        Assert.DoesNotContain("WrapPanel", source);
        Assert.DoesNotMatch(new Regex(@"\.Children\.Add\(\s*ActionButton\("), source);
        Assert.Matches(new Regex(
            @"Row\([^;]*ActionButton\(""Settings_ProtectedMode_Allow""[^;]*ActionButton\(""Settings_ProtectedMode_Deny""[^;]*\);"),
            source);
        Assert.Matches(new Regex(@"Row\([^;]*ActionButton\(""Settings_ProtectedMode_Revoke""[^;]*\);"), source);
        Assert.Matches(new Regex(@"new\s+DockPanel\b"), source);
        Assert.Matches(new Regex(@"Orientation\s*=\s*Orientation\.Horizontal"), source);
        Assert.Matches(new Regex(@"DockPanel\.SetDock\([^;]*Dock\.Right\s*\)"), source);
        Assert.Matches(new Regex(@"TextWrapping\s*=\s*TextWrapping\.Wrap"), source);
    }

    [Fact]
    public void SettingDefaultsToOffAndRoundTrips()
    {
        var path = Path.Combine(_directory, "protected_mode.ini");
        var store = new ProtectedModeSettingsStore(path);
        Assert.False(store.ReadEnabled());

        Assert.True(store.TryWriteEnabled(true));
        Assert.True(new ProtectedModeSettingsStore(path).ReadEnabled());
        Assert.True(store.TryWriteEnabled(false));
        Assert.False(store.ReadEnabled());

        File.WriteAllText(path, "garbage");
        Assert.False(store.ReadEnabled());
        Assert.EndsWith(Path.Combine("NavisHelper", "protected_mode.ini"), ProtectedModeSettingsStore.CreateDefault().FilePath);
    }
}
