using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #339, quick part. A rule may change transparency alone and leave the colour as it
// is ("isolation -> 50 %"), and pathContains accepts the " / " separator that
// find_items previews show, not only the "/" the matcher builds paths with.
public sealed class ModelColorSchemeAppearanceRuleTests
{
    [Fact]
    public void RuleNeedsColorOrTransparency()
    {
        Assert.Null(ModelColorSchemeRuleMatcher.DescribeAppearanceError(new ModelColorSchemeRule { ColorHex = "#FF0000" }));
        Assert.Null(ModelColorSchemeRuleMatcher.DescribeAppearanceError(new ModelColorSchemeRule { Transparency = 0.5f }));
        Assert.Null(ModelColorSchemeRuleMatcher.DescribeAppearanceError(new ModelColorSchemeRule { ColorHex = "#FF0000", Transparency = 0f }));

        var neither = ModelColorSchemeRuleMatcher.DescribeAppearanceError(new ModelColorSchemeRule { ColorHex = "  " });
        Assert.NotNull(neither);
        Assert.Contains("colorHex", neither, StringComparison.Ordinal);
        Assert.Contains("transparency", neither, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void TransparencyMustBeBetweenZeroAndOne(float transparency)
    {
        var error = ModelColorSchemeRuleMatcher.DescribeAppearanceError(new ModelColorSchemeRule { Transparency = transparency });
        Assert.NotNull(error);
        Assert.Contains("transparency", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HVAC")]
    [InlineData("/HVAC/")]
    [InlineData("/ HVAC /")]
    [InlineData("demo.nwd / HVAC / Insert")]
    [InlineData("demo.nwd/HVAC/Insert")]
    public void PathContainsAcceptsBothSeparators(string filter)
    {
        var rule = new ModelColorSchemeRule { PathContains = [filter] };
        var item = new ModelColorSchemeItemFacts { PathFactory = () => "demo.nwd/HVAC/Insert/HVAC-01" };

        Assert.True(ModelColorSchemeRuleMatcher.Matches(rule, item));
        Assert.True(ModelColorSchemeRuleMatcher.MatchesPrepared(ModelColorSchemeRuleMatcher.NormalizeRule(rule), item));
    }

    // Review finding: RVM node names may contain " / " themselves; a filter copied from
    // such a name must keep matching as written.
    [Theory]
    [InlineData("Supply / Return")]
    [InlineData("/Supply / Return/")]
    public void PathContainsKeepsNodeNamesWithSpacedSlashes(string filter)
    {
        var rule = new ModelColorSchemeRule { PathContains = [filter] };
        var item = new ModelColorSchemeItemFacts { PathFactory = () => "demo.nwd/Supply / Return/Part" };

        Assert.True(ModelColorSchemeRuleMatcher.Matches(rule, item));
        Assert.True(ModelColorSchemeRuleMatcher.MatchesPrepared(ModelColorSchemeRuleMatcher.NormalizeRule(rule), item));
    }

    [Fact]
    public void PathContainsStillRejectsOtherNodes()
    {
        var rule = new ModelColorSchemeRule { PathContains = ["/ Electrical /"] };
        var item = new ModelColorSchemeItemFacts { PathFactory = () => "demo.nwd/HVAC/Insert/HVAC-01" };

        Assert.False(ModelColorSchemeRuleMatcher.Matches(rule, item));
    }

    [Fact]
    public void ToolDescribesTransparencyOnlyRulesAndThePathFormat()
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == "ModelColorScheme");
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
        var rules = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!["properties"]!["rules"]!["description"]!.GetValue<string>();

        Assert.Contains("transparency", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("Each rule requires colorHex", rules, StringComparison.Ordinal);
        Assert.Contains("pathContains", rules, StringComparison.Ordinal);
        Assert.Contains(" / ", rules, StringComparison.Ordinal);
    }
}
