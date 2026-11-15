using Xunit;

namespace NavisHelper.McpServer.Tests;

// #444 slice delta, correction pass. L3 on Navisworks 2027 (live 625947b): during a long export the
// panel showed no progress and Cancel had no effect; all 10 items of a 908 MB model were exported.
// ProcessNext posted the next item at once through SynchronizationContext.Post/BeginInvoke, and
// Windows hands out posted messages before input and paint, so the UI never ran between items.
// The fix: every item after the first waits for a one-shot WinForms timer (WM_TIMER comes after
// posted, input and paint messages), and the service raises Changed after each item so the panel
// repaints its progress in that gap. The message order itself is only observable at L3.
public sealed class FbxLevelExportYieldTests
{
    [Fact]
    public void ItemsAfterTheFirstWaitForInputAndPaint()
    {
        var service = Read("NavisHelper", "Agent", "Services", "FbxLevelExportJobService.cs");
        var processNext = Between(service, "private void ProcessNext(", "private void Fail(");

        Assert.Contains("ScheduleAfterInput(job, host, documentKey)", processNext, StringComparison.Ordinal);
        Assert.DoesNotContain("Schedule(job, host, documentKey)", processNext.Replace("ScheduleAfterInput(job, host, documentKey)", string.Empty), StringComparison.Ordinal);
        Assert.DoesNotContain("_postToUi(", processNext, StringComparison.Ordinal);

        var yield = Between(service, "private void ScheduleAfterInput(", "private void ProcessNext(");
        Assert.Contains("new System.Windows.Forms.Timer", yield, StringComparison.Ordinal);
        Assert.Contains("Interval = 1", yield, StringComparison.Ordinal);
        Assert.Contains(".Stop()", yield, StringComparison.Ordinal);
        Assert.Contains(".Dispose()", yield, StringComparison.Ordinal);

        // Only the first item is posted from the host thread.
        Assert.Equal(1, Count(service, "_postToUi("));
    }

    [Fact]
    public void TheServiceReportsEveryItemAndThePanelListens()
    {
        var service = Read("NavisHelper", "Agent", "Services", "FbxLevelExportJobService.cs");
        Assert.Contains("public event EventHandler Changed;", service, StringComparison.Ordinal);
        Assert.Contains("Changed", Between(service, "private void ProcessNext(", "private void Fail("), StringComparison.Ordinal);

        var section = Read("NavisHelper", "WPF", "FbxLevelExportSection.cs");
        Assert.Contains(".Changed += OnJobChanged", section, StringComparison.Ordinal);
        Assert.Contains(".Changed -= OnJobChanged", section, StringComparison.Ordinal);
        // The panel unsubscribes when the job stops and when it is disposed; Start's own
        // "-= then +=" guard against a double subscription does not count for either.
        var update = Between(section, "private void Update(", "private void SetRunning(");
        Assert.Contains(".Changed -= OnJobChanged", update, StringComparison.Ordinal);
        var dispose = section.Substring(section.IndexOf("public void Dispose()", StringComparison.Ordinal));
        Assert.Contains(".Changed -= OnJobChanged", dispose, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryStartBeginsWithAnEmptyProgressBar()
    {
        var section = Read("NavisHelper", "WPF", "FbxLevelExportSection.cs");
        var onStart = Between(section, "private void OnStart(", "private void OnCancel(");
        Assert.Contains("_progress.Value = 0", onStart, StringComparison.Ordinal);
    }

    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "missing " + start);
        int to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, "missing " + end + " after " + start);
        return text.Substring(from, to - from);
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryPaths.Root }.Concat(parts).ToArray()));
}
