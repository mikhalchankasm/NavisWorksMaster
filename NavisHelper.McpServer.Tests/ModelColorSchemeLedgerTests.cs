using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #338: a second model_color_scheme apply silently restored every override the
// first one made. The runtime scheme now keeps its original states in a ledger:
// replace (the default) restores them before the new scheme and says so in the
// response; accumulate keeps them, so batches add up and one reset undoes all.
public sealed class ModelColorSchemeLedgerTests
{
    [Theory]
    [InlineData(null, ModelColorSchemeApplyMode.Replace)]
    [InlineData("", ModelColorSchemeApplyMode.Replace)]
    [InlineData("replace", ModelColorSchemeApplyMode.Replace)]
    [InlineData(" Accumulate ", ModelColorSchemeApplyMode.Accumulate)]
    [InlineData("ACCUMULATE", ModelColorSchemeApplyMode.Accumulate)]
    public void ModeParsesCaseInsensitivelyAndDefaultsToReplace(string value, ModelColorSchemeApplyMode expected)
    {
        Assert.True(ModelColorSchemeApplyModes.TryParse(value, out var mode));
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData("append")]
    [InlineData("add")]
    [InlineData("merge")]
    public void UnknownModeIsRejected(string value)
    {
        Assert.False(ModelColorSchemeApplyModes.TryParse(value, out _));
    }

    [Fact]
    public void ModesHaveStableWireNames()
    {
        Assert.Equal("replace", ModelColorSchemeApplyModes.ToWire(ModelColorSchemeApplyMode.Replace));
        Assert.Equal("accumulate", ModelColorSchemeApplyModes.ToWire(ModelColorSchemeApplyMode.Accumulate));
    }

    [Fact]
    public void FirstCaptureWinsSoResetRestoresThePreSchemeState()
    {
        var ledger = new ModelColorSchemeLedger<string, string>();
        var captures = 0;

        Assert.True(ledger.TryCapture("a", item => { captures++; return item + ":original"; }));
        // A later batch recolours "a": its state is now the first batch's colour, which must not be recorded.
        Assert.False(ledger.TryCapture("a", item => { captures++; return item + ":first-batch"; }));
        Assert.True(ledger.TryCapture("b", item => { captures++; return item + ":original"; }));

        Assert.Equal(2, captures);
        Assert.Equal(2, ledger.Count);
        Assert.True(ledger.Contains("a"));
        Assert.False(ledger.Contains("c"));
        Assert.Equal(new[] { "a:original", "b:original" }, ledger.States);
    }

    [Fact]
    public void ClearEmptiesTheLedger()
    {
        var ledger = new ModelColorSchemeLedger<string, string>();
        ledger.TryCapture("a", item => item);
        ledger.Clear();

        Assert.Equal(0, ledger.Count);
        Assert.Empty(ledger.States);
        Assert.True(ledger.TryCapture("a", item => item + ":again"));
        Assert.Equal(new[] { "a:again" }, ledger.States);
    }

    [Fact]
    public void ComparerDecidesItemIdentity()
    {
        var ledger = new ModelColorSchemeLedger<string, string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(ledger.TryCapture("Pipe", item => "first"));
        Assert.False(ledger.TryCapture("PIPE", item => "second"));
        Assert.Equal(new[] { "first" }, ledger.States);
    }

    [Fact]
    public void CaptureRejectsNullArguments()
    {
        var ledger = new ModelColorSchemeLedger<string, string>();
        Assert.Throws<ArgumentNullException>(() => ledger.TryCapture(null, item => item));
        Assert.Throws<ArgumentNullException>(() => ledger.TryCapture("a", null));
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void ContractsCarryModeAndRestoreReport()
    {
        var request = new ModelColorSchemeRequest { Mode = "accumulate" };
        var response = new ModelColorSchemeResponse
        {
            Mode = "replace",
            PreviousSchemeRestored = true,
            RestoredItemCount = 35616,
            ActiveSchemeItemCount = 1,
        };

        Assert.Equal("accumulate", request.Mode);
        Assert.True(response.PreviousSchemeRestored);
        Assert.Equal(35616, response.RestoredItemCount);
        Assert.Equal(1, response.ActiveSchemeItemCount);
    }

    [Fact]
    public void ToolExposesModeAndDescribesTheRestore()
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == "ModelColorScheme");
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));

        var description = tool.ProtocolTool.Description ?? "";
        Assert.Contains("accumulate", description, StringComparison.Ordinal);
        Assert.Contains("replace", description, StringComparison.Ordinal);

        var mode = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!["properties"]!["mode"]!.AsObject();
        Assert.Equal("string", mode["type"]!.GetValue<string>());
        Assert.Equal("replace", mode["default"]!.GetValue<string>());
        var modeText = mode["description"]!.GetValue<string>();
        Assert.Contains("accumulate", modeText, StringComparison.Ordinal);
        Assert.Contains("reset", modeText, StringComparison.Ordinal);
    }
}
