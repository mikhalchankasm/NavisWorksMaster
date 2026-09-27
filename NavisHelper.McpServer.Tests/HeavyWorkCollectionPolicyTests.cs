using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class HeavyWorkCollectionPolicyTests
{
    [Fact]
    public void DoesNotCollectBelowThreshold()
    {
        var policy = new HeavyWorkCollectionPolicy(0);

        Assert.False(policy.ShouldCollect(HeavyWorkCollectionPolicy.Threshold - 1));
    }

    [Fact]
    public void CollectsAtThreshold()
    {
        var policy = new HeavyWorkCollectionPolicy(0);

        Assert.True(policy.ShouldCollect(HeavyWorkCollectionPolicy.Threshold));
    }

    [Fact]
    public void SlowCommandWithOneGen0CollectionCollects()
    {
        var policy = new HeavyWorkCollectionPolicy(0);

        Assert.True(policy.ShouldCollect(1, HeavyWorkCollectionPolicy.SlowCommandMilliseconds));
    }

    [Fact]
    public void SlowCommandWithoutGen0CollectionCollects()
    {
        var policy = new HeavyWorkCollectionPolicy(0);

        Assert.True(policy.ShouldCollect(0, HeavyWorkCollectionPolicy.SlowCommandMilliseconds));
    }

    [Fact]
    public void FastCommandWithOneGen0CollectionDoesNotCollect()
    {
        var policy = new HeavyWorkCollectionPolicy(0);

        Assert.False(policy.ShouldCollect(1, HeavyWorkCollectionPolicy.SlowCommandMilliseconds - 1));
    }

    [Fact]
    public void FastCommandAtThresholdCollects()
    {
        var policy = new HeavyWorkCollectionPolicy(0);

        Assert.True(policy.ShouldCollect(HeavyWorkCollectionPolicy.Threshold, HeavyWorkCollectionPolicy.SlowCommandMilliseconds - 1));
    }

    [Fact]
    public void RecordCollectionResetsFastPathWithoutSuppressingSlowCommand()
    {
        var policy = new HeavyWorkCollectionPolicy(0);
        policy.RecordCollection(HeavyWorkCollectionPolicy.Threshold);

        Assert.True(policy.ShouldCollect(HeavyWorkCollectionPolicy.Threshold, HeavyWorkCollectionPolicy.SlowCommandMilliseconds));
        Assert.False(policy.ShouldCollect(HeavyWorkCollectionPolicy.Threshold + 1, HeavyWorkCollectionPolicy.SlowCommandMilliseconds - 1));
        Assert.True(policy.ShouldCollect(HeavyWorkCollectionPolicy.Threshold + 1, HeavyWorkCollectionPolicy.SlowCommandMilliseconds));
        Assert.True(policy.ShouldCollect(HeavyWorkCollectionPolicy.Threshold * 2, HeavyWorkCollectionPolicy.SlowCommandMilliseconds - 1));
    }

    [Fact]
    public void RecordCollectionResetsTheBaseline()
    {
        var policy = new HeavyWorkCollectionPolicy(0);
        policy.RecordCollection(HeavyWorkCollectionPolicy.Threshold);

        Assert.False(policy.ShouldCollect(HeavyWorkCollectionPolicy.Threshold * 2 - 1));
        Assert.True(policy.ShouldCollect(HeavyWorkCollectionPolicy.Threshold * 2));
    }

    [Fact]
    public void CollectionsCausedByTheForcedCollectionDoNotRetriggerIt()
    {
        var policy = new HeavyWorkCollectionPolicy(0);
        var beforeForcedCollection = HeavyWorkCollectionPolicy.Threshold;
        var afterForcedCollection = beforeForcedCollection + 2;

        Assert.True(policy.ShouldCollect(beforeForcedCollection));
        policy.RecordCollection(afterForcedCollection);

        Assert.False(policy.ShouldCollect(afterForcedCollection));
        Assert.False(policy.ShouldCollect(afterForcedCollection + 2));
    }

    [Fact]
    public void StartingCountIsUsedAsTheInitialBaseline()
    {
        var policy = new HeavyWorkCollectionPolicy(37);

        Assert.False(policy.ShouldCollect(40));
        Assert.True(policy.ShouldCollect(41));
    }

    [Fact]
    public void CollectionsBetweenRequestsDoNotCount()
    {
        var policy = new HeavyWorkCollectionPolicy(0);
        policy.BeginRequest(0);

        Assert.False(policy.ShouldCollect(1, 10));
        policy.EndRequest(1);

        // The host idles and Navisworks collects three times on its own.
        policy.BeginRequest(4);

        Assert.False(policy.ShouldCollect(4, 10));
    }

    [Fact]
    public void CollectionsAccumulateAcrossRequestsUntilTheThreshold()
    {
        var policy = new HeavyWorkCollectionPolicy(0);

        policy.BeginRequest(0);
        Assert.False(policy.ShouldCollect(2, 10));
        policy.EndRequest(2);

        policy.BeginRequest(20);
        Assert.False(policy.ShouldCollect(21, 10));
        policy.EndRequest(21);

        policy.BeginRequest(40);
        Assert.True(policy.ShouldCollect(42, 10));
    }

    [Fact]
    public void ForcedCollectionResetsTheAccumulatedRequestCount()
    {
        var policy = new HeavyWorkCollectionPolicy(0);

        policy.BeginRequest(0);
        Assert.False(policy.ShouldCollect(HeavyWorkCollectionPolicy.Threshold - 1, 10));
        policy.EndRequest(HeavyWorkCollectionPolicy.Threshold - 1);

        policy.BeginRequest(50);
        Assert.True(policy.ShouldCollect(51, 10));
        policy.RecordCollection(53);

        policy.BeginRequest(80);
        Assert.False(policy.ShouldCollect(80 + HeavyWorkCollectionPolicy.Threshold - 1, 10));
    }

    [Fact]
    public void TheCurrentRequestCountsTowardsTheThreshold()
    {
        var policy = new HeavyWorkCollectionPolicy(0);
        policy.BeginRequest(100);

        Assert.True(policy.ShouldCollect(100 + HeavyWorkCollectionPolicy.Threshold, 10));
    }

    [Fact]
    public void AskingDoesNotCloseTheRequest()
    {
        var policy = new HeavyWorkCollectionPolicy(0);
        policy.BeginRequest(0);

        Assert.False(policy.ShouldCollect(2, 10));
        Assert.False(policy.ShouldCollect(2, 10));
        Assert.Equal(2, policy.CollectionsSinceLast(2));
    }
}
