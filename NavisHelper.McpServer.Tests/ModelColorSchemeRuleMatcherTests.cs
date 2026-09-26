using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ModelColorSchemeRuleMatcherTests
{
    [Fact]
    public void Matches_RequiresAllPopulatedDimensions()
    {
        var rule = new ModelColorSchemeRule
        {
            NameContains = ["panel"],
            SourceFileContains = ["electrical"],
            PropertyContains = ["system"],
            PropertyValueContains = ["power"],
        };
        var item = new ModelColorSchemeItemFacts
        {
            Name = "Panel LP-01",
            SourceFile = @"D:\models\electrical.rvm",
            Properties =
            [
                new ModelColorSchemePropertyFact
                {
                    Category = "Element",
                    Property = "System",
                    Value = "Power distribution",
                },
            ],
        };

        Assert.True(ModelColorSchemeRuleMatcher.Matches(rule, item));

        item.SourceFile = @"D:\models\mechanical.rvm";
        Assert.False(ModelColorSchemeRuleMatcher.Matches(rule, item));
    }

    [Fact]
    public void Matches_PropertyDimensionsMustBelongToSameProperty()
    {
        var rule = new ModelColorSchemeRule
        {
            PropertyContains = ["system"],
            PropertyValueContains = ["power"],
        };
        var item = new ModelColorSchemeItemFacts
        {
            Properties =
            [
                new ModelColorSchemePropertyFact { Property = "System", Value = "HVAC" },
                new ModelColorSchemePropertyFact { Property = "Description", Value = "Power" },
            ],
        };

        Assert.False(ModelColorSchemeRuleMatcher.Matches(rule, item));
    }

    [Fact]
    public void Matches_UsesOrWithinOneDimension()
    {
        var rule = new ModelColorSchemeRule
        {
            NameContains = ["cable", "tray"],
        };

        Assert.True(ModelColorSchemeRuleMatcher.Matches(
            rule,
            new ModelColorSchemeItemFacts { Name = "Main tray" }));
    }

    [Fact]
    public void HasMatchers_RejectsEmptyRule()
    {
        Assert.False(ModelColorSchemeRuleMatcher.HasMatchers(new ModelColorSchemeRule()));
        Assert.True(ModelColorSchemeRuleMatcher.HasMatchers(new ModelColorSchemeRule
        {
            MatchAll = true,
        }));
        Assert.True(ModelColorSchemeRuleMatcher.HasMatchers(new ModelColorSchemeRule
        {
            PathContains = ["Electrical"],
        }));
    }

    [Fact]
    public void Matches_MatchAllAcceptsAnyItem()
    {
        var rule = new ModelColorSchemeRule
        {
            MatchAll = true,
        };

        Assert.True(ModelColorSchemeRuleMatcher.Matches(
            rule,
            new ModelColorSchemeItemFacts()));
    }

    [Fact]
    public void Matches_DoesNotBuildPathWhenRuleHasNoPathMatcher()
    {
        var factoryCalls = 0;
        var item = new ModelColorSchemeItemFacts
        {
            Name = "Panel LP-01",
            PathFactory = () =>
            {
                factoryCalls++;
                return "Store/Level 1/Panel LP-01";
            },
        };
        var rule = new ModelColorSchemeRule
        {
            NameContains = ["panel"],
        };

        Assert.True(ModelColorSchemeRuleMatcher.Matches(rule, item));
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public void PathFactory_RunsOnceHoweverOftenPathIsRead()
    {
        var factoryCalls = 0;
        var item = new ModelColorSchemeItemFacts
        {
            PathFactory = () =>
            {
                factoryCalls++;
                return "Store/Level 1/Cable Tray";
            },
        };
        var rule = new ModelColorSchemeRule
        {
            PathContains = ["cable tray"],
        };

        Assert.True(ModelColorSchemeRuleMatcher.Matches(rule, item));
        Assert.True(ModelColorSchemeRuleMatcher.MatchesPrepared(rule, item));
        Assert.Equal("Store/Level 1/Cable Tray", item.Path);
        Assert.Equal("Store/Level 1/Cable Tray", item.Path);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public void Matches_PathSetDirectlyStillMatches()
    {
        var item = new ModelColorSchemeItemFacts
        {
            Path = "Store/Level 1/Cable Tray",
        };

        Assert.True(ModelColorSchemeRuleMatcher.Matches(
            new ModelColorSchemeRule { PathContains = ["cable tray"] },
            item));
        Assert.False(ModelColorSchemeRuleMatcher.Matches(
            new ModelColorSchemeRule { PathContains = ["panel"] },
            item));
        Assert.Equal("Store/Level 1/Cable Tray", item.Path);
    }
}
