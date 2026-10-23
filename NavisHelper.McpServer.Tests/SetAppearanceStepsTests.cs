using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #339, the part still open: colouring a model set by set took 72 x 2 calls
// (select_selection_set + model_color_scheme), about 20 minutes, and could not change
// transparency alone. selection_sets_apply_appearance runs ordered steps on saved sets in
// one call and one undo step; a set is overridden as a whole (containers included), as
// the Appearance Profiler does, instead of walking its leaves.
public sealed class SetAppearanceStepsTests
{
    [Theory]
    [InlineData("Systems/HVAC", "#FF0000", null, null, null)]
    [InlineData("Systems/HVAC", null, 50, null, null)]
    [InlineData("Systems/HVAC", null, null, true, null)]
    [InlineData("", "#FF0000", null, null, "setPath")]
    [InlineData("Systems/HVAC", null, null, null, "colorHex, transparency or hide")]
    [InlineData("Systems/HVAC", null, null, false, "colorHex, transparency or hide")]
    [InlineData("Systems/HVAC", "red", null, null, "#RRGGBB")]
    [InlineData("Systems/HVAC", null, 101, null, "0-100")]
    [InlineData("Systems/HVAC", null, -1, null, "0-100")]
    public void StepsAreValidated(string setPath, string colorHex, int? transparency, bool? hide, string expectedError)
    {
        var error = SetAppearanceStepRules.Describe(new SetAppearanceStep
        {
            SetPath = setPath, ColorHex = colorHex, Transparency = transparency, Hide = hide,
        });
        if (expectedError == null)
            Assert.Null(error);
        else
            Assert.Contains(expectedError, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#FF8000", true, 1.0, 128.0 / 255, 0.0)]
    [InlineData("#00ff00", true, 0.0, 1.0, 0.0)]
    [InlineData("FF8000", false, 0, 0, 0)]
    [InlineData("#FF80", false, 0, 0, 0)]
    public void ColoursParseToUnitChannels(string hex, bool ok, double r, double g, double b)
    {
        Assert.Equal(ok, SetAppearanceStepRules.TryParseColorHex(hex, out var red, out var green, out var blue));
        if (!ok)
            return;
        Assert.Equal(r, red, 6);
        Assert.Equal(g, green, 6);
        Assert.Equal(b, blue, 6);
    }

    [Fact]
    public void TheStepCountIsBounded()
    {
        Assert.Contains("steps", SetAppearanceStepRules.DescribeSteps(new List<SetAppearanceStep>()), StringComparison.Ordinal);
        var tooMany = Enumerable.Range(0, SetAppearanceStepRules.MaxSteps + 1)
            .Select(i => new SetAppearanceStep { SetPath = "S" + i, Hide = true }).ToList();
        Assert.Contains(SetAppearanceStepRules.MaxSteps.ToString(), SetAppearanceStepRules.DescribeSteps(tooMany), StringComparison.Ordinal);
        Assert.Null(SetAppearanceStepRules.DescribeSteps(tooMany.Take(3).ToList()));
        Assert.Equal(200, SetAppearanceStepRules.MaxSteps);
    }

    [Fact]
    public void ContractsReportEveryStep()
    {
        var response = new SelectionSetsApplyAppearanceResponse { Apply = true, Applied = true, AppliedStepCount = 2 };
        response.Steps.Add(new SetAppearanceStepResult { SetPath = "A", Resolved = true, ItemCount = 12 });
        response.Steps.Add(new SetAppearanceStepResult { SetPath = "B", Resolved = false, Error = "not found" });
        Assert.Equal(2, response.Steps.Count);
        Assert.Empty(response.Warnings);
        Assert.Empty(new SelectionSetsApplyAppearanceRequest().Steps);
        Assert.Equal("selection_sets_apply_appearance", HostCommandNames.SelectionSetsApplyAppearance);
    }

    [Fact]
    public void TheToolIsADryRunFirstDocumentWrite()
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == "SelectionSetsApplyAppearance");
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
        Assert.Equal("selection_sets_apply_appearance", tool.ProtocolTool.Name);
        Assert.Equal(ToolEffects.Document, method.GetCustomAttribute<ToolCapabilitiesAttribute>()!.Effects);
        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!;
        Assert.Contains("steps", schema["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.False(schema["properties"]!["apply"]!["default"]!.GetValue<bool>());
        Assert.Contains("reset_appearance", tool.ProtocolTool.Description ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void ThePluginOverridesSetsAsAWholeInOneTransaction()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "SetAppearanceService.cs"));
        Assert.Contains("BeginTransaction(", service, StringComparison.Ordinal);
        Assert.Contains("OverridePermanentColor(", service, StringComparison.Ordinal);
        Assert.Contains("OverridePermanentTransparency(", service, StringComparison.Ordinal);
        Assert.Contains("SetHidden(", service, StringComparison.Ordinal);
        Assert.Contains("SetAppearanceStepRules.", service, StringComparison.Ordinal);
    }
}
