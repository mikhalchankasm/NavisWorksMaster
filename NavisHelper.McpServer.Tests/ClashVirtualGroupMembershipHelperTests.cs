using NavisHelper.Agent.Contracts;
using System.Collections.Generic;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashVirtualGroupMembershipHelperTests
{
    [Fact]
    public void CreateReferenceSet_UsesIdentityAndIgnoresNulls()
    {
        var first = new EqualByValue(1);
        var equalButDifferent = new EqualByValue(1);

        var result = ClashVirtualGroupMembershipHelper.CreateReferenceSet(
            new[] { first, first, equalButDifferent, null });

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => ReferenceEquals(item, first));
        Assert.Contains(result, item => ReferenceEquals(item, equalButDifferent));
        Assert.DoesNotContain(result, item => item == null);
    }

    [Fact]
    public void CollectReferenceSet_FlattensGroupsAndSkipsNulls()
    {
        var first = new object();
        var second = new object();
        IEnumerable<object>[] groups = { new[] { first, null }, null, new[] { first, second } };

        var result = ClashVirtualGroupMembershipHelper.CollectReferenceSet(groups);

        Assert.Equal(2, result.Count);
        Assert.Contains(first, result);
        Assert.Contains(second, result);
    }

    [Fact]
    public void ContainsReference_DoesNotUseValueEquality()
    {
        var stored = new EqualByValue(1);

        Assert.True(ClashVirtualGroupMembershipHelper.ContainsReference(new[] { stored }, stored));
        Assert.False(ClashVirtualGroupMembershipHelper.ContainsReference(new[] { stored }, new EqualByValue(1)));
        Assert.False(ClashVirtualGroupMembershipHelper.ContainsReference<EqualByValue>(null, stored));
        Assert.False(ClashVirtualGroupMembershipHelper.ContainsReference(new[] { stored }, null));
    }

    [Fact]
    public void ExceptReferences_PreservesOrderAndDuplicatesOfRemainingReferences()
    {
        var keep = new EqualByValue(1);
        var remove = new EqualByValue(1);
        var excluded = ClashVirtualGroupMembershipHelper.CreateReferenceSet(new[] { remove });

        var result = ClashVirtualGroupMembershipHelper.ExceptReferences(
            new[] { keep, remove, keep, null },
            excluded);

        Assert.Equal(3, result.Count);
        Assert.Same(keep, result[0]);
        Assert.Same(keep, result[1]);
        Assert.Null(result[2]);
    }

    [Fact]
    public void IntersectReferences_PreservesOrderAndDuplicatesOfIncludedReferences()
    {
        var include = new EqualByValue(1);
        var equalButDifferent = new EqualByValue(1);
        var included = ClashVirtualGroupMembershipHelper.CreateReferenceSet(new[] { include });

        var result = ClashVirtualGroupMembershipHelper.IntersectReferences(
            new[] { equalButDifferent, include, include, null },
            included);

        Assert.Equal(2, result.Count);
        Assert.Same(include, result[0]);
        Assert.Same(include, result[1]);
    }

    [Fact]
    public void ExceptReferences_WithNullSetKeepsNonNullItemsAndNullSlots()
    {
        var item = new object();

        var result = ClashVirtualGroupMembershipHelper.ExceptReferences(
            new[] { item, null },
            null);

        Assert.Equal(2, result.Count);
        Assert.Same(item, result[0]);
        Assert.Null(result[1]);
    }

    [Fact]
    public void IntersectReferences_WithEmptySetReturnsEmptyCollection()
    {
        Assert.Empty(ClashVirtualGroupMembershipHelper.IntersectReferences(
            new[] { new object() },
            ClashVirtualGroupMembershipHelper.CreateReferenceSet<object>(null)));
    }

    [Fact]
    public void EmptyInputs_ReturnEmptyCollections()
    {
        Assert.Empty(ClashVirtualGroupMembershipHelper.CreateReferenceSet<object>(null));
        Assert.Empty(ClashVirtualGroupMembershipHelper.CollectReferenceSet<object>(null));
        Assert.Empty(ClashVirtualGroupMembershipHelper.ExceptReferences<object>(null, null));
        Assert.Empty(ClashVirtualGroupMembershipHelper.IntersectReferences<object>(null, null));
    }

    private sealed class EqualByValue
    {
        private readonly int _value;

        public EqualByValue(int value)
        {
            _value = value;
        }

        public override bool Equals(object obj)
        {
            return obj is EqualByValue other && other._value == _value;
        }

        public override int GetHashCode()
        {
            return _value;
        }
    }
}
