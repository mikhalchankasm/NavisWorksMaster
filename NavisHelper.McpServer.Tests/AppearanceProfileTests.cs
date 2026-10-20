using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #339: apply a saved Appearance Profiler profile in one call. Navisworks' own loader
// (AutoAppearanceLoader) does the search and colouring natively; NavisHelper reads the
// profile first so a dry-run can say what it will do. The sample below is synthetic
// and has the shape Navisworks 2027 writes (SingleFilePersistedItemAppearanceProfileV3).
public sealed class AppearanceProfileTests
{
    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <Root>
          <SingleFilePersistedItemVersion xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <Version>3</Version>
          </SingleFilePersistedItemVersion>
          <SingleFilePersistedItemAppearanceProfileV3 xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <Profile>
              <PersistedItemAppearanceProfileRuleV3 xsi:type="PersistedItemAppearanceProfilePropertyRuleV3">
                <Transparency>0</Transparency>
                <Color><A>255</A><R>170</R><G>180</G><B>190</B></Color>
                <ForceVisibility>Unchanged</ForceVisibility>
                <Category Internal="LcOaNode">Item</Category>
                <Property Internal="LcOaSceneBaseUserName">Name</Property>
                <Value><data type="wstring">*/PIPE</data></Value>
                <Condition>DisplayStringWildcard</Condition>
              </PersistedItemAppearanceProfileRuleV3>
              <PersistedItemAppearanceProfileRuleV3 xsi:type="PersistedItemAppearanceProfilePropertyRuleV3">
                <Transparency>50</Transparency>
                <Color><A>255</A><R>0</R><G>128</G><B>255</B></Color>
                <ForceVisibility>Hidden</ForceVisibility>
                <Category Internal="LcOaNode">Item</Category>
                <Property Internal="LcOaNodeLayer">Layer</Property>
                <Value><data type="wstring">TEMP</data></Value>
                <Condition>DisplayStringContains</Condition>
              </PersistedItemAppearanceProfileRuleV3>
              <PersistedItemAppearanceProfileRuleV3 xsi:type="PersistedItemAppearanceProfileSetsRuleV3">
                <Transparency>0</Transparency>
                <Color><A>255</A><R>18</R><G>52</G><B>86</B></Color>
                <ForceVisibility>Unchanged</ForceVisibility>
                <Name>TRAY 01</Name>
                <SavedItemId>Profiles
        TRAY 01</SavedItemId>
              </PersistedItemAppearanceProfileRuleV3>
            </Profile>
            <Name>Demo profile</Name>
          </SingleFilePersistedItemAppearanceProfileV3>
        </Root>
        """;

    [Fact]
    public void ParsesPropertyAndSetRulesInOrder()
    {
        var profile = AppearanceProfileFile.Parse(Sample);

        Assert.Equal("Demo profile", profile.Name);
        Assert.Equal(3, profile.Version);
        Assert.Equal(3, profile.Rules.Count);
        Assert.Equal(1, profile.SetRuleCount);
        Assert.Equal(1, profile.HiddenRuleCount);

        var first = profile.Rules[0];
        Assert.Equal("property", first.Kind);
        Assert.Equal("Item", first.Category);
        Assert.Equal("Name", first.Property);
        Assert.Equal("DisplayStringWildcard", first.Condition);
        Assert.Equal("*/PIPE", first.Value);
        Assert.Equal("#AAB4BE", first.ColorHex);
        Assert.Equal(0, first.Transparency);
        Assert.Equal("Unchanged", first.ForceVisibility);

        var hidden = profile.Rules[1];
        Assert.Equal("Hidden", hidden.ForceVisibility);
        Assert.Equal(50, hidden.Transparency);
        Assert.Equal("#0080FF", hidden.ColorHex);

        var set = profile.Rules[2];
        Assert.Equal("set", set.Kind);
        Assert.Equal("TRAY 01", set.SetName);
        Assert.Equal("Profiles/TRAY 01", set.SetPath);
        Assert.Equal("#123456", set.ColorHex);
        Assert.Empty(profile.Errors);
    }

    // Live finding (#339): Navisworks' own reader drops the whole profile, silently, when one
    // rule cannot be deserialised, and its scRGB fields overwrite R/G/B. The dry-run must say so
    // before apply, because the native loader reports nothing.
    [Theory]
    [InlineData("<Transparency>0.3</Transparency>", "transparency")]
    [InlineData("<Transparency>101</Transparency>", "transparency")]
    [InlineData("<Transparency>-1</Transparency>", "transparency")]
    public void FractionalOrOutOfRangeTransparencyIsABlockingError(string transparency, string expected)
    {
        var text = ReplaceFirst(Sample, "<Transparency>0</Transparency>", transparency);
        var profile = AppearanceProfileFile.Parse(text);
        Assert.Single(profile.Errors);
        Assert.Contains(expected, profile.Errors[0], StringComparison.Ordinal);
        Assert.Contains("rule 1", profile.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ScRgbThatDisagreesWithRgbIsABlockingError()
    {
        // R=170 G=180 B=190 is about scRGB 0.402, 0.456, 0.515; zeros would paint the rule black.
        var zeros = ReplaceFirst(Sample, "<B>190</B></Color>", "<B>190</B><ScA>1</ScA><ScR>0</ScR><ScG>0</ScG><ScB>0</ScB></Color>");
        var profile = AppearanceProfileFile.Parse(zeros);
        Assert.Single(profile.Errors);
        Assert.Contains("scRGB", profile.Errors[0], StringComparison.Ordinal);

        var consistent = ReplaceFirst(Sample, "<B>190</B></Color>",
            "<B>190</B><ScA>1</ScA><ScR>0.4019778</ScR><ScG>0.456411034</ScG><ScB>0.5149177</ScB></Color>");
        Assert.Empty(AppearanceProfileFile.Parse(consistent).Errors);

        // Review finding: near zero a linear tolerance hid a visible difference (ScR 0.002 is byte 07).
        var nearZero = ReplaceFirst(Sample, "<R>0</R><G>128</G><B>255</B></Color>",
            "<R>0</R><G>128</G><B>255</B><ScA>1</ScA><ScR>0.002</ScR><ScG>0.2158605</ScG><ScB>1</ScB></Color>");
        Assert.Single(AppearanceProfileFile.Parse(nearZero).Errors);
    }

    private static string ReplaceFirst(string text, string oldValue, string newValue)
    {
        var index = text.IndexOf(oldValue, StringComparison.Ordinal);
        Assert.True(index >= 0, oldValue + " not found");
        return text.Substring(0, index) + newValue + text.Substring(index + oldValue.Length);
    }

    [Theory]
    [InlineData("not xml at all")]
    [InlineData("<Root><Other/></Root>")]
    [InlineData("<Root><SingleFilePersistedItemAppearanceProfileV3><Name>x</Name></SingleFilePersistedItemAppearanceProfileV3></Root>")]
    public void RejectsWhatIsNotAProfile(string text)
    {
        var error = Assert.Throws<FormatException>(() => AppearanceProfileFile.Parse(text));
        Assert.Contains("Appearance Profiler", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownRuleTypeIsKeptAndWarned()
    {
        var text = Sample.Replace("PersistedItemAppearanceProfileSetsRuleV3", "PersistedItemAppearanceProfileFutureRuleV9");
        var profile = AppearanceProfileFile.Parse(text);
        Assert.Equal("unknown", profile.Rules[2].Kind);
        Assert.Contains(profile.Warnings, warning => warning.Contains("PersistedItemAppearanceProfileFutureRuleV9", StringComparison.Ordinal));
        // Review finding: one rule Navisworks may not read can drop the whole profile, so apply is blocked.
        Assert.Contains(profile.Errors, error => error.StartsWith("rule 3: ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(@"C:\profiles\12470.xml", true)]
    [InlineData(@"C:\profiles\12470.DAT", true)]
    [InlineData(@"C:\profiles\12470.json", false)]
    [InlineData("12470.xml", false)]
    [InlineData("", false)]
    public void OnlyAbsoluteXmlOrDatPathsAreAccepted(string path, bool accepted)
    {
        Assert.Equal(accepted, AppearanceProfileFile.DescribePathError(path) == null);
    }

    [Fact]
    public void ToolIsDryRunFirstAndExplainsHowToUndo()
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == "AppearanceProfileApply");
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));

        Assert.Equal("appearance_profile_apply", tool.ProtocolTool.Name);
        var description = tool.ProtocolTool.Description ?? "";
        Assert.Contains("Appearance Profiler", description, StringComparison.Ordinal);
        Assert.Contains("model_color_scheme", description, StringComparison.Ordinal);
        Assert.Contains("undo", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grey", description, StringComparison.OrdinalIgnoreCase);

        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!;
        Assert.Contains("profilePath", schema["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.False(schema["properties"]!["apply"]!["default"]!.GetValue<bool>());
    }
}
