using NavisHelper.Core;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class CooperativeModelApplyRecoveryTests
{
    [Fact]
    public async Task PartialSelectionMutationThenThrow_RestoresOriginalSelection()
    {
        var selected = new List<int> { 1, 2 };
        var original = selected.ToArray();
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            selected.Clear();
            selected.Add(9);
            throw new InvalidOperationException("Partial native apply failure");
        }));

        var result = await CooperativeModelApplyRecovery.RecoverAsync(
            new Action[] { () => { selected.Clear(); selected.AddRange(original); } },
            () => { },
            () => Task.FromResult(selected.SequenceEqual(original)));

        Assert.Equal(CooperativeModelRecoveryStatus.Restored, result.Status);
        Assert.Empty(result.Errors);
        Assert.Equal(original, selected);
    }

    [Fact]
    public async Task RestoreStepThrows_StillAttemptsIndependentStepsAndReadback()
    {
        var failure = new InvalidOperationException("Visibility recovery failed");
        bool selectionRestored = false;
        bool readbackPerformed = false;

        var result = await CooperativeModelApplyRecovery.RecoverAsync(
            new Action[] { () => throw failure, () => selectionRestored = true },
            () => { },
            () => { readbackPerformed = true; return Task.FromResult(true); });

        Assert.True(selectionRestored);
        Assert.True(readbackPerformed);
        Assert.Equal(CooperativeModelRecoveryStatus.Unknown, result.Status);
        Assert.Same(failure, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task MultipleRecoveryFailures_PreservesAllIndependentErrors()
    {
        var visibleFailure = new InvalidOperationException("Visible partition failed");
        var selectionFailure = new InvalidOperationException("Selection recovery failed");
        var readbackFailure = new InvalidOperationException("Readback failed");
        var result = await CooperativeModelApplyRecovery.RecoverAsync(
            new Action[] { () => throw visibleFailure, () => throw selectionFailure },
            () => { },
            () => Task.FromException<bool>(readbackFailure));

        Assert.Equal(CooperativeModelRecoveryStatus.Unknown, result.Status);
        Assert.Equal(new[] { visibleFailure, selectionFailure, readbackFailure }, result.Errors);
    }

    [Fact]
    public async Task UserCancelsAfterMutation_RecoveryStillFinishes()
    {
        using var userCancellation = new CancellationTokenSource();
        int selected = 9;
        userCancellation.Cancel();

        var result = await CooperativeModelApplyRecovery.RecoverAsync(
            new Action[] { () => selected = 1 },
            () => { },
            () => Task.FromResult(selected == 1));

        Assert.True(userCancellation.IsCancellationRequested);
        Assert.Equal(1, selected);
        Assert.Equal(CooperativeModelRecoveryStatus.Restored, result.Status);
    }

    [Fact]
    public async Task DocumentChangesDuringRestore_NoFurtherMutationOrReadback()
    {
        bool documentCurrent = true;
        bool secondStepCalled = false;
        bool readbackCalled = false;
        var result = await CooperativeModelApplyRecovery.RecoverAsync(
            new Action[]
            {
                () => documentCurrent = false,
                () => secondStepCalled = true
            },
            () => { if (!documentCurrent) throw new InvalidOperationException("Document changed"); },
            () => { readbackCalled = true; return Task.FromResult(true); });

        Assert.False(secondStepCalled);
        Assert.False(readbackCalled);
        Assert.Equal(CooperativeModelRecoveryStatus.Unknown, result.Status);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task DocumentAlreadyChanged_DoesNotAttemptAnyRestore()
    {
        bool mutationCalled = false;
        var result = await CooperativeModelApplyRecovery.RecoverAsync(
            new Action[] { () => mutationCalled = true },
            () => throw new InvalidOperationException("Document changed"),
            () => Task.FromResult(true));

        Assert.False(mutationCalled);
        Assert.Equal(CooperativeModelRecoveryStatus.Unknown, result.Status);
    }

    [Fact]
    public async Task DocumentChangesWhileAwaitingReadback_DoesNotClaimRestored()
    {
        bool documentCurrent = true;
        var readback = new TaskCompletionSource<bool>();
        var recovery = CooperativeModelApplyRecovery.RecoverAsync(
            Array.Empty<Action>(),
            () => { if (!documentCurrent) throw new InvalidOperationException("Document changed"); },
            () => readback.Task);

        Assert.False(recovery.IsCompleted);
        documentCurrent = false;
        readback.SetResult(true);
        var result = await recovery;

        Assert.Equal(CooperativeModelRecoveryStatus.Unknown, result.Status);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task ReadbackMismatch_IsUnknownEvenWhenRestoreCallsReturnNormally()
    {
        var result = await CooperativeModelApplyRecovery.RecoverAsync(
            new Action[] { () => { } }, () => { }, () => Task.FromResult(false));

        Assert.Equal(CooperativeModelRecoveryStatus.Unknown, result.Status);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ReadbackThrows_PreservesFailureAndReturnsUnknown()
    {
        var failure = new InvalidOperationException("Unreadable original item");
        var result = await CooperativeModelApplyRecovery.RecoverAsync(
            Array.Empty<Action>(), () => { }, () => Task.FromException<bool>(failure));

        Assert.Equal(CooperativeModelRecoveryStatus.Unknown, result.Status);
        Assert.Same(failure, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task FullModelSnapshot_RestoresAliasesOutsideApplySelection()
    {
        var selected = new Item("selected", 1, false);
        var unselectedAlias = new Item("unselected alias", 1, false);
        var hidden = new Item("previously hidden", 2, true);
        var model = new[] { selected, unselectedAlias, hidden };
        var snapshot = new VisibilitySnapshot<Item>(new InstanceComparer());
        foreach (var item in model)
            snapshot.Record(item, item.Hidden);

        void SetHidden(IEnumerable<Item> items, bool value)
        {
            var instanceIds = items.Select(item => item.InstanceId).ToHashSet();
            foreach (var item in model.Where(item => instanceIds.Contains(item.InstanceId)))
                item.Hidden = value;
        }

        // Native mutation expands to all instances before it fails.
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            SetHidden(new[] { selected }, true);
            throw new InvalidOperationException("Partial visibility apply failure");
        }));
        Assert.True(unselectedAlias.Hidden);

        var result = await CooperativeModelApplyRecovery.RecoverAsync(
            new Action[]
            {
                () => SetHidden(snapshot.Visible, false),
                () => SetHidden(snapshot.Hidden, true)
            },
            () => { },
            () => Task.FromResult(snapshot.Entries.All(record => record.Key.Hidden == record.Value)));

        Assert.Equal(CooperativeModelRecoveryStatus.Restored, result.Status);
        Assert.Equal(3, snapshot.Entries.Count);
        Assert.Contains(unselectedAlias, snapshot.Visible);
        Assert.False(selected.Hidden);
        Assert.False(unselectedAlias.Hidden);
        Assert.True(hidden.Hidden);
    }

    [Fact]
    public void ConflictingAliasFlags_RejectsSnapshotBeforeMutation()
    {
        var snapshot = new VisibilitySnapshot<Item>(new InstanceComparer());
        snapshot.Record(new Item("first", 1, false), false);

        Assert.Throws<InvalidOperationException>(() =>
            snapshot.Record(new Item("alias", 1, true), true));

        Assert.Single(snapshot.Entries);
        Assert.Single(snapshot.Visible);
        Assert.Empty(snapshot.Hidden);
    }

    [Fact]
    public void InstanceHashCollision_DoesNotMergeDifferentInstances()
    {
        var snapshot = new VisibilitySnapshot<Item>(new InstanceComparer());
        snapshot.Record(new Item("visible", 1, false), false);
        snapshot.Record(new Item("hidden", 2, true), true);

        Assert.Equal(2, snapshot.Entries.Count);
        Assert.Single(snapshot.Visible);
        Assert.Single(snapshot.Hidden);
    }

    private sealed class Item(string name, int instanceId, bool hidden)
    {
        internal string Name { get; } = name;
        internal int InstanceId { get; } = instanceId;
        internal bool Hidden { get; set; } = hidden;
    }

    private sealed class InstanceComparer : IEqualityComparer<Item>
    {
        public bool Equals(Item x, Item y) => x?.InstanceId == y?.InstanceId;
        public int GetHashCode(Item item) => 0; // Exercise real identity despite collisions.
    }
}
