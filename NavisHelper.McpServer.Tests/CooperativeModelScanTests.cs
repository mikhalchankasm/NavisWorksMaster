using NavisHelper.Core;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class CooperativeModelScanTests
{
    [Fact]
    public void OperationGate_RejectsReentryUntilCanceledOperationCleansUp()
    {
        using var gate = new CooperativeModelScanOperationGate();

        Assert.True(gate.TryBegin(out var first));
        Assert.True(first.IsCurrent);
        Assert.False(gate.TryBegin(out _));

        gate.CancelCurrent();

        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(first.IsCurrent);
        Assert.False(gate.TryBegin(out _));

        first.Dispose();
        Assert.True(gate.TryBegin(out var second));
        Assert.True(second.IsCurrent);
        second.Dispose();
    }

    [Fact]
    public void OperationGate_DisposeCancelsAndPermanentlyRejectsWork()
    {
        var gate = new CooperativeModelScanOperationGate();
        Assert.True(gate.TryBegin(out var operation));

        gate.Dispose();

        Assert.True(operation.Token.IsCancellationRequested);
        Assert.False(operation.IsCurrent);
        Assert.False(gate.TryBegin(out _));
        operation.Dispose();
        gate.Dispose();
    }

    [Theory]
    [InlineData(127, 11, false)]
    [InlineData(128, 1, true)]
    [InlineData(1, 12, true)]
    public void ChunkPolicy_YieldsAtItemOrTimeBoundary(
        int processed,
        int elapsedMilliseconds,
        bool expected)
    {
        var policy = new CooperativeModelScanChunkPolicy(
            128,
            TimeSpan.FromMilliseconds(12));

        Assert.Equal(
            expected,
            policy.ShouldYield(
                processed,
                TimeSpan.FromMilliseconds(elapsedMilliseconds)));
    }

    [Fact]
    public void ChunkPolicy_ProgressIsBoundedAndCompletesExactly()
    {
        var policy = new CooperativeModelScanChunkPolicy();

        var start = policy.CalculateProgress(0, 4, 0);
        var withinRoot = policy.CalculateProgress(0, 4, 4096);
        var nextRoot = policy.CalculateProgress(1, 4, 0);
        var complete = policy.CalculateProgress(4, 4, 0);

        Assert.Equal(0.0, start);
        Assert.InRange(withinRoot, 0.0, 0.25);
        Assert.True(nextRoot >= withinRoot);
        Assert.Equal(1.0, complete);
    }

    [Theory]
    [InlineData((int)CooperativeModelScanStatus.Completed, true, true, true, true)]
    [InlineData((int)CooperativeModelScanStatus.Busy, true, true, true, false)]
    [InlineData((int)CooperativeModelScanStatus.Canceled, true, true, true, false)]
    [InlineData((int)CooperativeModelScanStatus.DocumentChanged, true, true, true, false)]
    [InlineData((int)CooperativeModelScanStatus.Completed, false, true, true, false)]
    [InlineData((int)CooperativeModelScanStatus.Completed, true, false, true, false)]
    [InlineData((int)CooperativeModelScanStatus.Completed, true, true, false, false)]
    public void CommitPolicy_AllowsOnlyCompleteCurrentUnchangedScan(
        int status,
        bool operationIsCurrent,
        bool documentIsCurrent,
        bool sourceStateIsCurrent,
        bool expected)
    {
        Assert.Equal(
            expected,
            CooperativeModelScanCommitPolicy.CanCommit(
                (CooperativeModelScanStatus)status,
                operationIsCurrent,
                documentIsCurrent,
                true,
                sourceStateIsCurrent));
    }

    [Fact]
    public void DepthFirstTraversal_IsCompleteAndPreservesPreOrder()
    {
        var root = new TestNode("root");
        var a = root.Add("a");
        a.Add("a1");
        a.Add("a2");
        var b = root.Add("b");
        b.Add("b1");

        var visited = new List<string>();
        using (var traversal = new CooperativeDepthFirstTraversal<TestNode>(
            root,
            node => node.Children))
        {
            while (traversal.TryTakeNext(out var node))
                visited.Add(node.Name);
        }

        Assert.Equal(
            new[] { "root", "a", "a1", "a2", "b", "b1" },
            visited);
    }

    [Theory]
    [InlineData((int)CooperativeModelScanInvalidation.ModelCollection)]
    [InlineData((int)CooperativeModelScanInvalidation.ModelProperties)]
    [InlineData((int)CooperativeModelScanInvalidation.Selection)]
    public void GenerationTracker_InvalidatesSameDocumentSourceChanges(int change)
    {
        var tracker = new CooperativeModelScanGenerationTracker();
        var observed = CooperativeModelScanInvalidation.Document |
                       CooperativeModelScanInvalidation.ModelCollection |
                       CooperativeModelScanInvalidation.ModelProperties |
                       CooperativeModelScanInvalidation.Selection;
        var snapshot = tracker.Capture(observed);

        tracker.Invalidate((CooperativeModelScanInvalidation)change);

        Assert.False(tracker.IsCurrent(snapshot));
        Assert.Equal(
            CooperativeModelScanStatus.SourceChanged,
            tracker.GetChangeStatus(snapshot));
    }

    [Fact]
    public void GenerationTracker_ClassifiesDocumentReplacementSeparately()
    {
        var tracker = new CooperativeModelScanGenerationTracker();
        var snapshot = tracker.Capture(CooperativeModelScanInvalidation.Document);

        tracker.Invalidate(CooperativeModelScanInvalidation.Document);

        Assert.Equal(
            CooperativeModelScanStatus.DocumentChanged,
            tracker.GetChangeStatus(snapshot));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void CommitPolicy_DeniesDocumentGenerationOrSelectionChanges(
        bool documentCurrent,
        bool generationCurrent,
        bool selectionCurrent)
    {
        Assert.False(CooperativeModelScanCommitPolicy.CanCommit(
            CooperativeModelScanStatus.Completed,
            true,
            documentCurrent,
            generationCurrent,
            selectionCurrent));
    }

    [Fact]
    public void ProgressOwnership_EndsExactlyOnceOnSuccess()
    {
        int ended = 0;
        var ownership = new CooperativeProgressOwnership(() => ended++);

        ownership.Dispose();
        ownership.Dispose();

        Assert.Equal(1, ended);
    }

    [Fact]
    public void ProgressOwnership_EndsExactlyOnceOnCancellationPath()
    {
        int ended = 0;
        using (var ownership = new CooperativeProgressOwnership(() => ended++))
        {
            // A canceled scan exits its using/finally path without transfer.
        }

        Assert.Equal(1, ended);
    }

    [Fact]
    public void ProgressOwnership_EndsExactlyOnceOnExceptionPath()
    {
        int ended = 0;

        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using (var ownership = new CooperativeProgressOwnership(() => ended++))
                throw new InvalidOperationException("probe");
        }));

        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task TaskAwarePaletteCommand_DoesNotReportSuccessBeforeCompletion()
    {
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool successReported = false;

        var execution = TaskAwarePaletteCommandRunner.ExecuteAsync(
            async () =>
            {
                entered.SetResult(true);
                await release.Task;
                return TaskAwareCommandOutcome.Completed;
            },
            () => successReported = true);

        await entered.Task;
        Assert.False(successReported);
        release.SetResult(true);
        Assert.Equal(TaskAwareCommandOutcome.Completed, await execution);
        Assert.True(successReported);
    }

    [Fact]
    public async Task TaskAwarePaletteCommand_DoesNotReportSuccessForCancellation()
    {
        bool successReported = false;

        var outcome = await TaskAwarePaletteCommandRunner.ExecuteAsync(
            () => Task.FromResult(TaskAwareCommandOutcome.NotCompleted),
            () => successReported = true);

        Assert.Equal(TaskAwareCommandOutcome.NotCompleted, outcome);
        Assert.False(successReported);
    }

    [Fact]
    public async Task TaskAwarePaletteCommand_DoesNotReportSuccessForFailure()
    {
        bool successReported = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TaskAwarePaletteCommandRunner.ExecuteAsync(
                () => Task.FromException<TaskAwareCommandOutcome>(
                    new InvalidOperationException("probe")),
                () => successReported = true));

        Assert.False(successReported);
    }

    [Fact]
    public async Task Lifecycle_DetachRejectsQueuedPaletteStartWithoutSideEffects()
    {
        var lifecycle = new CooperativeModelScanLifecycle();
        int subscriptions = 0;
        int progressStarts = 0;
        int commits = 0;
        bool successReported = false;
        lifecycle.Resume();

        Func<Task<TaskAwareCommandOutcome>> queuedStart = () =>
        {
            if (!lifecycle.CanStart)
            {
                return Task.FromResult(TaskAwareCommandOutcome.NotCompleted);
            }

            subscriptions++;
            progressStarts++;
            commits++;
            return Task.FromResult(TaskAwareCommandOutcome.Completed);
        };

        lifecycle.Suspend();
        var outcome = await TaskAwarePaletteCommandRunner.ExecuteAsync(
            queuedStart,
            () => successReported = true);

        Assert.Equal(TaskAwareCommandOutcome.NotCompleted, outcome);
        Assert.Equal(0, subscriptions);
        Assert.Equal(0, progressStarts);
        Assert.Equal(0, commits);
        Assert.False(successReported);
    }

    [Fact]
    public async Task Lifecycle_ReloadAllowsWorkAgain()
    {
        var lifecycle = new CooperativeModelScanLifecycle();
        lifecycle.Resume();
        lifecycle.Suspend();
        lifecycle.Resume();
        bool successReported = false;

        var outcome = await TaskAwarePaletteCommandRunner.ExecuteAsync(
            () => Task.FromResult(
                lifecycle.CanStart
                    ? TaskAwareCommandOutcome.Completed
                    : TaskAwareCommandOutcome.NotCompleted),
            () => successReported = true);

        Assert.Equal(TaskAwareCommandOutcome.Completed, outcome);
        Assert.True(successReported);
    }

    [Fact]
    public async Task Lifecycle_DisposePermanentlyRejectsWorkAndResume()
    {
        var lifecycle = new CooperativeModelScanLifecycle();
        lifecycle.Resume();
        lifecycle.Dispose();
        bool successReported = false;

        Assert.Throws<ObjectDisposedException>(() => lifecycle.Resume());
        var outcome = await TaskAwarePaletteCommandRunner.ExecuteAsync(
            () => Task.FromResult(
                lifecycle.CanStart
                    ? TaskAwareCommandOutcome.Completed
                    : TaskAwareCommandOutcome.NotCompleted),
            () => successReported = true);

        Assert.Equal(TaskAwareCommandOutcome.NotCompleted, outcome);
        Assert.False(successReported);
        Assert.Equal(
            CooperativeModelScanLifecycleState.Disposed,
            lifecycle.State);
    }

    [Fact]
    public void SubscriptionSet_PartialFailureAttemptsAllAndRemainsRetryable()
    {
        var subscriptions = new CooperativeSubscriptionSet();
        var attempts = new[] { 0, 0, 0 };
        bool failSecondOnce = true;
        subscriptions.Add(() => attempts[0]++);
        subscriptions.Add(() =>
        {
            attempts[1]++;
            if (failSecondOnce)
            {
                failSecondOnce = false;
                throw new InvalidOperationException("probe");
            }
        });
        subscriptions.Add(() => attempts[2]++);

        var first = subscriptions.DetachAll();

        Assert.Equal(3, first.Attempted);
        Assert.Single(first.Failures);
        Assert.True(subscriptions.HasActive);
        Assert.Equal(1, subscriptions.ActiveCount);
        Assert.Equal(new[] { 1, 1, 1 }, attempts);

        var retry = subscriptions.DetachAll();

        Assert.Equal(1, retry.Attempted);
        Assert.Empty(retry.Failures);
        Assert.False(subscriptions.HasActive);
        Assert.Equal(0, subscriptions.ActiveCount);
        Assert.Equal(new[] { 1, 2, 1 }, attempts);
    }

    [Fact]
    public void IsolationPolicy_PreservesSelectedAncestorsAndDescendants()
    {
        var root = new TestNode("root");
        var selected = root.Add("selected");
        var descendant = selected.Add("descendant");
        var unrelated = root.Add("unrelated");
        var selectedSet = new HashSet<TestNode> { selected };
        var ancestors = new HashSet<TestNode> { root };

        Assert.False(CooperativeModelIsolationPolicy.ShouldHide(
            root, selectedSet, ancestors, node => node.Parent));
        Assert.False(CooperativeModelIsolationPolicy.ShouldHide(
            selected, selectedSet, ancestors, node => node.Parent));
        Assert.False(CooperativeModelIsolationPolicy.ShouldHide(
            descendant, selectedSet, ancestors, node => node.Parent));
        Assert.True(CooperativeModelIsolationPolicy.ShouldHide(
            unrelated, selectedSet, ancestors, node => node.Parent));
    }

    [Fact]
    public void SourceGuard_ModelApiScanStaysOnCooperativeUiDispatcher()
    {
        var root = FindRepositoryRoot();
        var coordinator = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "UiThreadModelScanCoordinator.cs"));
        var colors = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperPanel.Colors.cs"));
        var selection = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperPanel.Selection.cs"));

        Assert.DoesNotContain("Task.Run", coordinator, StringComparison.Ordinal);
        Assert.Contains(
            "Dispatcher.Yield(DispatcherPriority.Background)",
            coordinator,
            StringComparison.Ordinal);
        Assert.Contains("BeginProgress", coordinator, StringComparison.Ordinal);
        Assert.Contains("progress.IsCanceled", coordinator, StringComparison.Ordinal);
        Assert.Contains("ActiveDocumentChanging", coordinator, StringComparison.Ordinal);
        Assert.Contains("Models.CollectionChanging", coordinator, StringComparison.Ordinal);
        Assert.Contains("Models.CollectionChanged", coordinator, StringComparison.Ordinal);
        Assert.Contains("Models.ModelItemPropertiesChanged", coordinator, StringComparison.Ordinal);
        Assert.Contains("CurrentSelection.Changing", coordinator, StringComparison.Ordinal);
        Assert.Contains("CurrentSelection.Changed", coordinator, StringComparison.Ordinal);
        Assert.Contains("CooperativeDepthFirstTraversal<ModelItem>", coordinator, StringComparison.Ordinal);
        var scanAsync = Slice(
            coordinator,
            "internal async Task<UiThreadModelScanResult> ScanAsync",
            "internal bool CanCommit");
        Assert.DoesNotContain("Attach();", scanAsync, StringComparison.Ordinal);
        Assert.Contains("!_attachRequested", scanAsync, StringComparison.Ordinal);
        Assert.True(
            scanAsync.IndexOf("!_lifecycle.CanStart", StringComparison.Ordinal) <
            scanAsync.IndexOf("_operations.TryBegin", StringComparison.Ordinal));
        Assert.True(
            scanAsync.IndexOf("!_lifecycle.CanStart", StringComparison.Ordinal) <
            scanAsync.IndexOf("BeginProgress", StringComparison.Ordinal));
        Assert.DoesNotContain(
            "foreach (var child in item.Children)",
            coordinator,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CollectModelItems(", colors, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectModelItems(", selection, StringComparison.Ordinal);
        Assert.Equal(
            4,
            CountOccurrences(
                colors + selection,
                "await _modelScanCoordinator.ScanAsync"));
        Assert.Equal(
            4,
            CountOccurrences(
                colors + selection,
                "_modelScanCoordinator.CanCommit"));
        Assert.Equal(
            4,
            CountOccurrences(
                colors + selection,
                "if (!EnsureModelScanAvailable())"));
        Assert.Contains(
            "CooperativeModelScanInvalidation.ModelProperties",
            colors,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CurrentSelection.Clear",
            Slice(
                colors,
                "private async void OnSelectByPropertyValue",
                "private void OnCreateSearchSelectionSet"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CurrentSelection.Clear",
            Slice(
                selection,
                "private async void InvertSelection",
                "private async void IsolateSelection"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGuard_CommandPaletteUsesTaskAwarePathForAllModelScans()
    {
        var root = FindRepositoryRoot();
        var panel = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "NavisHelperPanel.cs"));

        Assert.Contains(
            "RegisterAsyncPaletteCommand(\"SelectByProperty\", SelectByPropertyValueAsync)",
            panel,
            StringComparison.Ordinal);
        Assert.Contains(
            "RegisterAsyncPaletteCommand(\"InvertSelection\", InvertSelectionAsync)",
            panel,
            StringComparison.Ordinal);
        Assert.Contains(
            "RegisterAsyncPaletteCommand(\"Isolate\", IsolateSelectionAsync)",
            panel,
            StringComparison.Ordinal);
        Assert.Contains(
            "RegisterAsyncPaletteCommand(\"UnhideAll\", UnhideAllAsync)",
            panel,
            StringComparison.Ordinal);
        Assert.Contains(
            "await TaskAwarePaletteCommandRunner.ExecuteAsync",
            panel,
            StringComparison.Ordinal);
    }

    private sealed class TestNode
    {
        internal TestNode(string name)
        {
            Name = name;
        }

        internal string Name { get; }
        internal TestNode Parent { get; private set; }
        internal List<TestNode> Children { get; } = new List<TestNode>();

        internal TestNode Add(string name)
        {
            var child = new TestNode(name) { Parent = this };
            Children.Add(child);
            return child;
        }
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source.Substring(start, end - start);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "NavisHelper.sln")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
