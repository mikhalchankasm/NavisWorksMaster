using System.Text.RegularExpressions;
using NavisHelper.Agent.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #263, owner decision 2026-10-12: saving under the same name keeps the document
// instance, so protected-mode grants, pending requests and the epoch survive Ctrl+S.
// FileNameChanged with a different key (Save As to another name), every
// ActiveDocumentChanged (even onto the same key) and closing the document still
// reset. ProtectedModeApprovals keeps its #258 contract: every DocumentChanged call
// drops everything, so the host decides here whether to call it at all.
public sealed class DocumentInstanceTrackerTests
{
    private const string Path1 = @"C:\Models\plant.nwd";
    private const string Path2 = @"C:\Models\plant-v2.nwd";

    [Theory]
    [InlineData(Path1, Path1)]
    [InlineData("Untitled", "Untitled")]
    [InlineData("", "")]
    public void FileNameChangedWithTheSameKeyKeepsTheInstance(string previousKey, string newKey)
    {
        Assert.False(DocumentInstanceTracker.ShouldReset(previousKey, newKey, DocumentResetTrigger.FileNameChanged));
    }

    [Theory]
    [InlineData(Path1, Path2)] // Save As to another name
    [InlineData("Untitled", Path1)] // first save of an unsaved document
    [InlineData(Path1, "")] // file name cleared
    [InlineData(Path1, @"C:\Models\PLANT.nwd")] // the gate compares keys ordinally
    public void FileNameChangedWithADifferentKeyResets(string previousKey, string newKey)
    {
        Assert.True(DocumentInstanceTracker.ShouldReset(previousKey, newKey, DocumentResetTrigger.FileNameChanged));
    }

    [Theory]
    [InlineData(Path1, Path1)]
    [InlineData(Path1, Path2)]
    [InlineData("", "")]
    public void ActiveDocumentChangedAlwaysResets(string previousKey, string newKey)
    {
        Assert.True(DocumentInstanceTracker.ShouldReset(previousKey, newKey, DocumentResetTrigger.ActiveDocumentChanged));
    }

    [Theory]
    [InlineData(Path1, Path1)]
    [InlineData(Path1, "")]
    [InlineData("", "")]
    public void ClosedAlwaysResets(string previousKey, string newKey)
    {
        Assert.True(DocumentInstanceTracker.ShouldReset(previousKey, newKey, DocumentResetTrigger.Closed));
    }

    [Fact]
    public void AnUnknownTriggerResets()
    {
        Assert.True(DocumentInstanceTracker.ShouldReset(Path1, Path1, (DocumentResetTrigger)99));
    }

    [Fact]
    public void TheLogLineNamesTheTriggerAndTheDecision()
    {
        Assert.Equal(
            "document_instance trigger=FileNameChanged key_changed=false reset=false",
            DocumentInstanceTracker.LogLine(Path1, Path1, DocumentResetTrigger.FileNameChanged));
        Assert.Equal(
            "document_instance trigger=FileNameChanged key_changed=true reset=true",
            DocumentInstanceTracker.LogLine(Path1, Path2, DocumentResetTrigger.FileNameChanged));
        Assert.Equal(
            "document_instance trigger=ActiveDocumentChanged key_changed=false reset=true",
            DocumentInstanceTracker.LogLine(Path1, Path1, DocumentResetTrigger.ActiveDocumentChanged));
        Assert.Equal(
            "document_instance trigger=Closed key_changed=true reset=true",
            DocumentInstanceTracker.LogLine(Path1, "", DocumentResetTrigger.Closed));
    }

    // A grant made before Ctrl+S is still honoured after it, because the host does
    // not call DocumentChanged when the decision says the instance is kept.
    [Fact]
    public void TheGateKeepsAGrantAndAPendingCodeWhenTheHostSkipsDocumentChanged()
    {
        var codes = new Queue<int>(new[] { 1, 2 });
        var gate = new ProtectedModeApprovals(() => new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc), () => codes.Dequeue());
        gate.Enabled = true;
        gate.DocumentChanged(Path1);
        gate.Check("save_document", Path1, "s1", "Claude Code");
        gate.Approve("0001");
        gate.Check("save_document", Path1, "s2", "Codex");

        if (DocumentInstanceTracker.ShouldReset(Path1, Path1, DocumentResetTrigger.FileNameChanged))
            gate.DocumentChanged(Path1);

        Assert.Single(gate.Approved);
        Assert.Single(gate.Pending);
        Assert.True(gate.Check("save_document", Path1, "s1", "Claude Code").Allowed);
    }

    [Fact]
    public void FileNameChangedGoesThroughTheTracker()
    {
        var body = MethodBody(DocumentSource(), "OnTrackedDocumentFileNameChanged");
        Assert.Contains("DocumentResetTrigger.FileNameChanged", body, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentResetTrigger.ActiveDocumentChanged", body, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentResetTrigger.Closed", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveDocumentChangedAlwaysUsesTheResettingTrigger()
    {
        var source = DocumentSource();
        var changed = MethodBody(source, "OnActiveDocumentChanged");
        Assert.Contains("DocumentResetTrigger.ActiveDocumentChanged", changed, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentResetTrigger.FileNameChanged", changed, StringComparison.Ordinal);

        var changing = MethodBody(source, "OnActiveDocumentChanging");
        Assert.Contains("DocumentResetTrigger.Closed", changing, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentResetTrigger.FileNameChanged", changing, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryAttachNamesItsTrigger()
    {
        var lines = (DocumentSource() + "\n" + Read("NavisHelper", "Agent", "Host", "AgentHostService.Lifecycle.cs"))
            .Split('\n')
            .Where(line => line.Contains("AttachTrackedDocument(", StringComparison.Ordinal)
                           && !line.Contains("private void AttachTrackedDocument(", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(4, lines.Count);
        Assert.All(lines, line => Assert.Contains("DocumentResetTrigger.", line, StringComparison.Ordinal));
    }

    [Fact]
    public void DocumentChangedIsCalledOnlyWhenTheTrackerSaysReset()
    {
        var body = MethodBody(DocumentSource(), "UpdateTrackedDocumentMetadata");

        var decision = Regex.Match(body, @"var\s+(\w+)\s*=\s*DocumentInstanceTracker\.ShouldReset\(\s*AgentRuntime\.CurrentDocumentKey\s*,\s*documentKey\s*,\s*trigger\s*\)\s*;");
        Assert.True(decision.Success, "UpdateTrackedDocumentMetadata must decide with ShouldReset(AgentRuntime.CurrentDocumentKey, documentKey, trigger).");
        var assignment = body.IndexOf("AgentRuntime.CurrentDocumentKey = documentKey", StringComparison.Ordinal);
        Assert.True(assignment > decision.Index, "The decision must read the previous key before the new key is stored.");

        var guarded = Regex.IsMatch(body, @"if\s*\(\s*" + decision.Groups[1].Value + @"\s*\)\s*\{?\s*AgentRuntime\.ProtectedModeApprovals\.DocumentChanged\(\s*documentKey\s*\)\s*;");
        Assert.True(guarded, "DocumentChanged must be guarded by the ShouldReset decision.");
        Assert.Contains("Logger.Info(DocumentInstanceTracker.LogLine(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePluginCallsDocumentChangedFromOnePlaceOnly()
    {
        var calls = Directory.EnumerateFiles(Path.Combine(Root(), "NavisHelper"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Sum(file => Regex.Matches(File.ReadAllText(file), @"ProtectedModeApprovals\.DocumentChanged\(").Count);

        Assert.Equal(1, calls);
    }

    private static string DocumentSource() => Read("NavisHelper", "Agent", "Host", "AgentHostService.Document.cs");

    private static string MethodBody(string source, string name)
    {
        var signature = Regex.Match(source, @"private\s+void\s+" + name + @"\s*\(");
        Assert.True(signature.Success, "Method not found: " + name);
        var open = source.IndexOf('{', signature.Index);
        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0)
                return source.Substring(open, index - open + 1);
        }

        throw new InvalidOperationException("Unbalanced braces in " + name);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate NavisHelper.sln.");
    }
}
