using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashStatusFilterHelperTests
{
    [Fact]
    public void Normalize_WithoutDefault_KeepsEmptyFiltersAsAllStatusesByCallerConvention()
    {
        var plan = ClashStatusFilterHelper.Normalize(null, includeAllStatuses: false, defaultToNewActive: false);

        Assert.False(plan.IncludeAllStatuses);
        Assert.Empty(plan.StatusFilters);
        Assert.False(plan.ShouldWarnIncludeAllOverrides);
        Assert.True(ClashStatusFilterHelper.Matches(plan.StatusFilters, "Resolved"));
    }

    [Fact]
    public void Normalize_WithDefault_AddsNewAndActive()
    {
        var plan = ClashStatusFilterHelper.Normalize(null, includeAllStatuses: false, defaultToNewActive: true);

        Assert.False(plan.IncludeAllStatuses);
        Assert.Equal(new[] { "New", "Active" }, plan.StatusFilters);
        Assert.True(ClashStatusFilterHelper.Matches(plan.StatusFilters, "New"));
        Assert.True(ClashStatusFilterHelper.Matches(plan.StatusFilters, "active"));
        Assert.False(ClashStatusFilterHelper.Matches(plan.StatusFilters, "Resolved"));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("All")]
    [InlineData("any")]
    public void Normalize_AllMarkers_EnableAllStatusesAndRemoveMarkerFromEffectiveFilters(string marker)
    {
        var plan = ClashStatusFilterHelper.Normalize(new[] { marker }, includeAllStatuses: false, defaultToNewActive: true);

        Assert.True(plan.IncludeAllStatuses);
        Assert.Empty(plan.StatusFilters);
        Assert.False(plan.ShouldWarnIncludeAllOverrides);
    }

    [Fact]
    public void Normalize_IncludeAllWithExplicitFilters_KeepsWarningSignal()
    {
        var plan = ClashStatusFilterHelper.Normalize(new[] { "New", "Resolved" }, includeAllStatuses: true, defaultToNewActive: true);

        Assert.True(plan.IncludeAllStatuses);
        Assert.Equal(new[] { "New", "Resolved" }, plan.StatusFilters);
        Assert.Equal(2, plan.ExplicitStatusFilterCount);
        Assert.True(plan.ShouldWarnIncludeAllOverrides);
    }

    [Fact]
    public void Normalize_TrimsAndDeduplicatesStatusFilters()
    {
        var plan = ClashStatusFilterHelper.Normalize(new[] { " New ", "new", "", " Active" }, includeAllStatuses: false, defaultToNewActive: true);

        Assert.Equal(new[] { "New", "Active" }, plan.StatusFilters);
    }

    [Fact]
    public void Normalize_AllMarkerWithExplicitFilter_KeepsExplicitFilterForWarningOnly()
    {
        var plan = ClashStatusFilterHelper.Normalize(new[] { "All", "Reviewed" }, includeAllStatuses: false, defaultToNewActive: true);

        Assert.True(plan.IncludeAllStatuses);
        Assert.Equal(new[] { "Reviewed" }, plan.StatusFilters);
        Assert.True(plan.ShouldWarnIncludeAllOverrides);
    }

    [Theory]
    [InlineData("*", true)]
    [InlineData("All", true)]
    [InlineData("Any", true)]
    [InlineData("New", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAllStatusesFilterValue_RecognizesOnlyAllMarkers(string value, bool expected)
    {
        Assert.Equal(expected, ClashStatusFilterHelper.IsAllStatusesFilterValue(value));
    }
}
