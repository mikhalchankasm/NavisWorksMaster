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
}
