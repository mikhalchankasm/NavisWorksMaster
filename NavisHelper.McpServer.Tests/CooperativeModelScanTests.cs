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

    [Fact]
    public void ChunkPolicy_ProgressDoesNotParkNearCompletionOnHugeRoots()
    {
        // Regression for the 94/94 plateau: the old estimator parked above
        // 95% after ~20k items inside the dominant root, so the bar looked
        // finished long before the traversal ended.
        var policy = new CooperativeModelScanChunkPolicy();

        var at100k = policy.CalculateProgress(0, 1, 100_000);
        var at1M = policy.CalculateProgress(0, 1, 1_000_000);

        Assert.InRange(at100k, 0.0, 0.9);
        Assert.InRange(at1M, at100k, 0.95);
        Assert.True(at1M > at100k);
    }

    [Fact]
    public void ChunkPolicy_WithinRootProgressIsMonotonic()
    {
        var policy = new CooperativeModelScanChunkPolicy();
        double previous = 0.0;

        foreach (var processed in new[] { 0, 1, 128, 4096, 65536, 500_000, 5_000_000 })
        {
            var current = policy.CalculateProgress(0, 1, processed);
            Assert.True(current >= previous);
            Assert.InRange(current, 0.0, 1.0);
            previous = current;
        }
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

    [Theory]
    [InlineData(0, 4096, 0)]
    [InlineData(1, 4096, 1)]
    [InlineData(4096, 4096, 1)]
    [InlineData(4097, 4096, 2)]
    [InlineData(10_000, 4096, 3)]
    [InlineData(100_000, 512, 196)]
    public void ApplyChunker_CoversExactlyAndBoundsChunks(
        int totalItems,
        int maxPerChunk,
        int expectedChunks)
    {
        var chunks = CooperativeModelApplyChunker.Plan(totalItems, maxPerChunk);

        Assert.Equal(expectedChunks, chunks.Count);

        int covered = 0;
        int expectedStart = 0;
        foreach (var chunk in chunks)
        {
            Assert.Equal(expectedStart, chunk.Start);
            Assert.InRange(chunk.Count, 1, maxPerChunk);
            expectedStart += chunk.Count;
            covered += chunk.Count;
        }

        Assert.Equal(totalItems, covered);
        Assert.Equal(totalItems, expectedStart);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, 0)]
    [InlineData(10, -5)]
    public void ApplyChunker_RejectsInvalidArguments(
        int totalItems,
        int maxPerChunk)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CooperativeModelApplyChunker.Plan(totalItems, maxPerChunk));
    }

    [Fact]
    public void HiddenStateRollback_RestoresMixedRecordedStates()
    {
        var rollback = new CooperativeHiddenStateRollback<string>();
        rollback.Record("a", true);
        rollback.Record("b", false);
        rollback.Record("c", true);
        rollback.Record(null, true);
        rollback.Record("d", false);

        Assert.Equal(4, rollback.RecordedCount);

        var batches = rollback.BuildRestoreBatches().ToList();

        Assert.Equal(2, batches.Count);
        var hiddenBatch = batches.Single(batch => batch.Hidden);
        var visibleBatch = batches.Single(batch => !batch.Hidden);
        Assert.Equal(new[] { "a", "c" }, hiddenBatch.Items);
        Assert.Equal(new[] { "b", "d" }, visibleBatch.Items);
    }

    [Fact]
    public void HiddenStateRollback_EmptyLogProducesNoBatches()
    {
        var rollback = new CooperativeHiddenStateRollback<string>();

        Assert.Empty(rollback.BuildRestoreBatches());
    }

    [Fact]
    public void HiddenStateRollback_SealsAfterBuildingRestoreBatches()
    {
        var rollback = new CooperativeHiddenStateRollback<string>();
        rollback.Record("a", true);
        Assert.Single(rollback.BuildRestoreBatches());

        Assert.Throws<InvalidOperationException>(
            () => rollback.Record("b", false));
    }

    [Fact]
    public void InvalidationSuppressor_SuppressesOnlySelfMutationKindsWhileActive()
    {
        var suppressor = new CooperativeInvalidationSuppressor();

        Assert.False(suppressor.IsSuppressed(
            CooperativeModelScanInvalidation.Selection));

        suppressor.Begin();
        Assert.True(suppressor.IsSuppressed(
            CooperativeModelScanInvalidation.Selection));
        Assert.True(suppressor.IsSuppressed(
            CooperativeModelScanInvalidation.ModelCollection));
        Assert.True(suppressor.IsSuppressed(
            CooperativeModelScanInvalidation.ModelProperties));
        Assert.False(suppressor.IsSuppressed(
            CooperativeModelScanInvalidation.Document));

        suppressor.End();
        Assert.False(suppressor.IsSuppressed(
            CooperativeModelScanInvalidation.Selection));
    }

    [Fact]
    public void InvalidationSuppressor_DocumentChangeIsNeverSuppressed()
    {
        var suppressor = new CooperativeInvalidationSuppressor();
        suppressor.Begin();
        suppressor.Begin();

        Assert.False(suppressor.IsSuppressed(
            CooperativeModelScanInvalidation.Document |
            CooperativeModelScanInvalidation.Selection));

        suppressor.End();
        suppressor.End();
        suppressor.End();

        Assert.False(suppressor.Active);
    }

    [Fact]
    public void TimingSnapshot_LogStringCarriesPerPhaseEvidence()
    {
        var timings = new CooperativeModelOperationTimingSnapshot
        {
            TotalMs = 5000,
            ScanMs = 3000,
            GateMs = 5,
            ApplyMs = 1990,
            RollbackMs = 0,
            ScanChunks = 900,
            ApplyChunks = 12,
            MaxScanChunkMs = 18,
            MaxApplyChunkMs = 95,
            VisitedItems = 123_456
        };

        var text = timings.ToLogString();

        Assert.Contains("totalMs=5000", text, StringComparison.Ordinal);
        Assert.Contains("scanMs=3000", text, StringComparison.Ordinal);
        Assert.Contains("gateMs=5", text, StringComparison.Ordinal);
        Assert.Contains("applyMs=1990", text, StringComparison.Ordinal);
        Assert.Contains("maxApplyChunkMs=95", text, StringComparison.Ordinal);
        Assert.Contains("visitedItems=123456", text, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidationDecision_UnobservedChangeDoesNotCancelOperation()
    {
        // Regression for the review finding H-1: a selection click during an
        // unhide-all (which observes no selection changes) must not abort
        // the operation and misreport it as a user cancel.
        var unhideObserved = CooperativeModelScanInvalidation.Document |
                             CooperativeModelScanInvalidation.ModelCollection;

        Assert.False(CooperativeInvalidationDecision.ShouldCancelOperation(
            CooperativeModelScanInvalidation.Selection,
            unhideObserved,
            selfMutationInProgress: false));
    }

    [Fact]
    public void InvalidationDecision_ObservedChangeCancelsOperation()
    {
        var invertObserved = CooperativeModelScanInvalidation.Document |
                             CooperativeModelScanInvalidation.ModelCollection |
                             CooperativeModelScanInvalidation.Selection;

        Assert.True(CooperativeInvalidationDecision.ShouldCancelOperation(
            CooperativeModelScanInvalidation.Selection,
            invertObserved,
            selfMutationInProgress: false));
    }

    [Fact]
    public void InvalidationDecision_SelfMutationEventsNeverCancelOperation()
    {
        var observed = CooperativeModelScanInvalidation.Selection |
                       CooperativeModelScanInvalidation.ModelCollection |
                       CooperativeModelScanInvalidation.ModelProperties;

        Assert.False(CooperativeInvalidationDecision.ShouldCancelOperation(
            CooperativeModelScanInvalidation.Selection,
            observed,
            selfMutationInProgress: true));
        Assert.False(CooperativeInvalidationDecision.ShouldCancelOperation(
            CooperativeModelScanInvalidation.ModelCollection,
            observed,
            selfMutationInProgress: true));
    }

    [Fact]
    public void InvalidationDecision_DocumentReplacementAlwaysCancels()
    {
        Assert.True(CooperativeInvalidationDecision.ShouldCancelOperation(
            CooperativeModelScanInvalidation.Document,
            CooperativeModelScanInvalidation.None,
            selfMutationInProgress: true));
    }

    [Fact]
    public void ProgressPhaseMath_ScanNeverFillsTheBar()
    {
        Assert.Equal(0.0, CooperativeProgressPhaseMath.MapScanFraction(0.0));
        Assert.Equal(
            CooperativeProgressPhaseMath.ScanPhaseShare,
            CooperativeProgressPhaseMath.MapScanFraction(1.0));
        Assert.True(
            CooperativeProgressPhaseMath.MapScanFraction(0.999) <
            CooperativeProgressPhaseMath.ScanPhaseShare);
        Assert.True(CooperativeProgressPhaseMath.ScanPhaseShare < 1.0);
    }

    [Fact]
    public void ProgressPhaseMath_ApplyCompletesTheBarOnlyAtTheEnd()
    {
        Assert.Equal(
            CooperativeProgressPhaseMath.ScanPhaseShare,
            CooperativeProgressPhaseMath.MapApplyFraction(0.0));
        Assert.Equal(1.0, CooperativeProgressPhaseMath.MapApplyFraction(1.0));
        Assert.True(
            CooperativeProgressPhaseMath.MapApplyFraction(0.5) >
            CooperativeProgressPhaseMath.ScanPhaseShare);
        Assert.True(
            CooperativeProgressPhaseMath.MapApplyFraction(0.5) < 1.0);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(2.0)]
    [InlineData(double.NaN)]
    public void ProgressPhaseMath_ClampsDegenerateFractions(double fraction)
    {
        var scan = CooperativeProgressPhaseMath.MapScanFraction(fraction);
        var apply = CooperativeProgressPhaseMath.MapApplyFraction(fraction);

        Assert.InRange(scan, 0.0, CooperativeProgressPhaseMath.ScanPhaseShare);
        Assert.InRange(apply, CooperativeProgressPhaseMath.ScanPhaseShare, 1.0);
        Assert.True(apply >= scan);
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
        Assert.Contains("context.Progress.IsCanceled", coordinator, StringComparison.Ordinal);
        Assert.Contains("ActiveDocumentChanging", coordinator, StringComparison.Ordinal);
        Assert.Contains("Models.CollectionChanging", coordinator, StringComparison.Ordinal);
        Assert.Contains("Models.CollectionChanged", coordinator, StringComparison.Ordinal);
        Assert.Contains("Models.ModelItemPropertiesChanged", coordinator, StringComparison.Ordinal);
        Assert.Contains("CurrentSelection.Changing", coordinator, StringComparison.Ordinal);
        Assert.Contains("CurrentSelection.Changed", coordinator, StringComparison.Ordinal);
        var shutdownDispose = Slice(
            coordinator,
            "if (_dispatcher.HasShutdownStarted",
            "_dispatcher.Invoke(new Action(Dispose));");
        Assert.Contains("_disposed = true;", shutdownDispose, StringComparison.Ordinal);
        Assert.Contains(
            "StopObservingDocumentBestEffort()",
            shutdownDispose,
            StringComparison.Ordinal);
        Assert.Contains(
            "StopApplicationEventsBestEffort()",
            shutdownDispose,
            StringComparison.Ordinal);
        Assert.Contains(
            "LogSubscriptionFailures(failures)",
            shutdownDispose,
            StringComparison.Ordinal);
        Assert.True(
            shutdownDispose.IndexOf("_disposed = true;", StringComparison.Ordinal) <
            shutdownDispose.IndexOf("_lifecycle.Dispose();", StringComparison.Ordinal));
        var changingHandler = Slice(
            coordinator,
            "private void OnActiveDocumentChanging",
            "private void OnActiveDocumentChanged");
        Assert.Contains("if (_disposed)", changingHandler, StringComparison.Ordinal);
        var changedHandler = Slice(
            coordinator,
            "private void OnActiveDocumentChanged",
            "private void OnModelCollectionChanging");
        Assert.Contains("if (_disposed)", changedHandler, StringComparison.Ordinal);
        var invalidateCurrent = Slice(
            coordinator,
            "private void InvalidateCurrent",
            "private void ObserveDocument");
        Assert.Contains("if (_disposed)", invalidateCurrent, StringComparison.Ordinal);
        Assert.Contains(
            "CooperativeInvalidationDecision.ShouldCancelOperation(",
            invalidateCurrent,
            StringComparison.Ordinal);
        Assert.Contains("CooperativeDepthFirstTraversal<ModelItem>", coordinator, StringComparison.Ordinal);
        var runOperation = Slice(
            coordinator,
            "internal async Task<UiThreadModelOperationResult> RunOperationAsync",
            "private async Task RunCommitGateAndApplyAsync");
        Assert.DoesNotContain("Attach();", runOperation, StringComparison.Ordinal);
        Assert.Contains("!_attachRequested", runOperation, StringComparison.Ordinal);
        Assert.True(
            runOperation.IndexOf("!_lifecycle.CanStart", StringComparison.Ordinal) <
            runOperation.IndexOf("_operations.TryBegin", StringComparison.Ordinal));
        Assert.True(
            runOperation.IndexOf("!_lifecycle.CanStart", StringComparison.Ordinal) <
            runOperation.IndexOf("BeginProgress", StringComparison.Ordinal));
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
                "await _modelScanCoordinator.RunOperationAsync"));
        Assert.Equal(
            4,
            CountOccurrences(
                colors + selection,
                "ApplyKind = CooperativeModelApplyKind."));
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
        // The four cooperative operations must not mutate the model
        // themselves; the coordinator owns the chunked apply.
        Assert.DoesNotContain(
            "CurrentSelection.CopyFrom(",
            Slice(
                selection,
                "private async Task<TaskAwareCommandOutcome> InvertSelectionAsync()",
                "private async void IsolateSelection()") +
            Slice(
                selection,
                "private async Task<TaskAwareCommandOutcome> IsolateSelectionAsync()",
                "private async void UnhideAll()") +
            Slice(
                selection,
                "private async Task<TaskAwareCommandOutcome> UnhideAllAsync()",
                "private bool ReportIncompleteModelScan(") +
            Slice(
                colors,
                "private async Task<TaskAwareCommandOutcome> SelectByPropertyValueAsync()",
                "private void OnCreateSearchSelectionSet()"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Models.SetHidden(",
            Slice(
                selection,
                "private async Task<TaskAwareCommandOutcome> IsolateSelectionAsync()",
                "private async void UnhideAll()") +
            Slice(
                selection,
                "private async Task<TaskAwareCommandOutcome> UnhideAllAsync()",
                "private bool ReportIncompleteModelScan("),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGuard_ApplyPhaseIsPhasedChunkedAndCancellableBeforeFirstMutation()
    {
        var root = FindRepositoryRoot();
        var coordinator = File.ReadAllText(Path.Combine(
            root,
            "NavisHelper",
            "WPF",
            "UiThreadModelScanCoordinator.cs"));

        // Honest phases: the progress dialog names the current phase and the
        // scan phase owns only a share of the bar.
        Assert.Contains("BeginSubOperation", coordinator, StringComparison.Ordinal);
        Assert.Contains("EndSubOperation", coordinator, StringComparison.Ordinal);
        Assert.Contains(
            "CooperativeProgressPhaseMath.ScanPhaseShare",
            coordinator,
            StringComparison.Ordinal);

        // Chunked apply: bounded Autodesk calls with UI yields between them.
        var applyAsync = Slice(
            coordinator,
            "private async Task ApplyAsync(",
            "private void CancelWithRollback(");
        Assert.Contains("CurrentSelection.Clear()", applyAsync, StringComparison.Ordinal);
        Assert.Contains("CurrentSelection.AddRange(slice)", applyAsync, StringComparison.Ordinal);
        Assert.Contains("Models.SetHidden(", applyAsync, StringComparison.Ordinal);
        Assert.Contains(
            "CooperativeModelApplyChunker.Plan(",
            applyAsync,
            StringComparison.Ordinal);
        Assert.Contains(
            "await Dispatcher.Yield(DispatcherPriority.Background);",
            applyAsync,
            StringComparison.Ordinal);

        // Cancellation is re-checked before every chunk and routes through
        // the rollback path, so Cancel stays live for the whole apply phase
        // and a stop never leaves partial changes behind.
        var chunkLoopIndex = applyAsync.IndexOf(
            "foreach (var chunk in chunks)",
            StringComparison.Ordinal);
        var chunkStopIndex = applyAsync.IndexOf(
            "var stopReason = GetStopReason(context);",
            StringComparison.Ordinal);
        Assert.True(chunkStopIndex > chunkLoopIndex);
        Assert.Contains("CancelWithRollback(", applyAsync, StringComparison.Ordinal);
        Assert.Contains(
            "CooperativeModelApplyStatus.CanceledRolledBack",
            coordinator,
            StringComparison.Ordinal);
        Assert.Contains(
            "CooperativeModelApplyStatus.CanceledRollbackIncomplete",
            coordinator,
            StringComparison.Ordinal);

        // Self-mutation suppression is scoped to the mutation calls only,
        // not to the whole apply phase with its yields.
        Assert.Contains(
            "SuppressSelfMutations(",
            coordinator,
            StringComparison.Ordinal);

        // The bar reaches full completion only after every chunk is applied.
        var completionIndex = applyAsync.IndexOf(
            "UpdateProgressSafely(progress, 1.0);",
            StringComparison.Ordinal);
        Assert.True(completionIndex > chunkLoopIndex);
        Assert.Equal(
            1,
            CountOccurrences(coordinator, "UpdateProgressSafely(progress, 1.0);"));

        // A mid-apply failure rolls back to the captured original state.
        Assert.Contains("RollbackApply(", coordinator, StringComparison.Ordinal);
        Assert.Contains(
            "CooperativeModelApplyStatus.FailedRolledBack",
            coordinator,
            StringComparison.Ordinal);
        Assert.Contains(
            "CooperativeModelApplyStatus.FailedRollbackIncomplete",
            coordinator,
            StringComparison.Ordinal);

        // Document-event invalidation goes through the pure decision table.
        Assert.Contains(
            "CooperativeInvalidationDecision.ShouldCancelOperation(",
            coordinator,
            StringComparison.Ordinal);

        // Timing evidence is logged for every operation outcome and is
        // published per chunk, including on early-return paths.
        Assert.Contains("LogOperationResult(", coordinator, StringComparison.Ordinal);
        Assert.Contains(
            "ToLogString()",
            coordinator,
            StringComparison.Ordinal);
        Assert.Contains(
            "PublishScanTimingSnapshot(",
            coordinator,
            StringComparison.Ordinal);
        Assert.Contains(
            "PublishApplyTimingSnapshot(",
            coordinator,
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
