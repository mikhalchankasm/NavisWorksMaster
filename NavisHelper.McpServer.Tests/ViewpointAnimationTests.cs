using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #403 (#397 part 1): a fly-through along an instrument route is a saved viewpoint animation built
// from the route's saved viewpoints. The owner marks a pause by saving the same view twice; the tool
// turns every repeated camera into a cut (a native pause) and can hold at each stop. Delay, Loops and
// Smoothing exist only from the 2026 SDK on, so the plugin reaches them through reflection.
public sealed class ViewpointAnimationTests
{
    private static ViewpointAnimationRequest Create(string sourceFolder = "Route", List<string> viewpoints = null) =>
        new ViewpointAnimationRequest { Action = "create", Name = "Flight", SourceFolder = sourceFolder, Viewpoints = viewpoints };

    [Fact]
    public void EachViewpointIsOneFrameWhenNothingRepeatsAndNothingHolds()
    {
        var frames = ViewpointAnimationRules.PlanFrames(new[] { "A", "B", "C" }, new[] { "k1", "k2", "k3" }, 0, 2);
        Assert.Equal(new[] { "viewpoint:A", "viewpoint:B", "viewpoint:C" }, Describe(frames));
    }

    [Fact]
    public void ARepeatedCameraBecomesACutAndKeepsEveryRepeatsOwnState()
    {
        var frames = ViewpointAnimationRules.PlanFrames(
            new[] { "A", "A again", "A third", "B" }, new[] { "k1", "k1", "k1", "k2" }, 0, 2.5);
        Assert.Equal(new[] { "viewpoint:A", "cut:2.5", "viewpoint:A again", "cut:2.5", "viewpoint:A third", "viewpoint:B" }, Describe(frames));
    }

    [Fact]
    public void HoldSecondsPausesOnceAtTheEndOfEveryStop()
    {
        var frames = ViewpointAnimationRules.PlanFrames(new[] { "A", "A2", "B" }, new[] { "k1", "k1", "k2" }, 3, 1);
        Assert.Equal(new[] { "viewpoint:A", "cut:1", "viewpoint:A2", "cut:3", "viewpoint:A2", "viewpoint:B", "cut:3", "viewpoint:B" }, Describe(frames));
    }

    [Fact]
    public void AZeroPauseLetsRepeatsFollowWithoutACut()
    {
        var frames = ViewpointAnimationRules.PlanFrames(new[] { "A", "A2", "B" }, new[] { "k1", "k1", "k2" }, 0, 0);
        Assert.Equal(new[] { "viewpoint:A", "viewpoint:A2", "viewpoint:B" }, Describe(frames));
        Assert.Empty(ViewpointAnimationRules.PlanFrames(new string[0], new string[0], 5, 5));
    }

    [Fact]
    public void OnlyConsecutiveRepeatsPause()
    {
        var frames = ViewpointAnimationRules.PlanFrames(new[] { "A", "B", "A" }, new[] { "k1", "k2", "k1" }, 0, 2);
        Assert.Equal(new[] { "viewpoint:A", "viewpoint:B", "viewpoint:A" }, Describe(frames));
    }

    [Fact]
    public void CameraKeysIgnoreNoiseAndTheQuaternionSign()
    {
        var key = ViewpointAnimationRules.CameraKey(1000.0004, -2.5, 3, 0.5, 0.5, 0.5, 0.5);
        Assert.Equal(key, ViewpointAnimationRules.CameraKey(1000.0001, -2.5002, 3.0003, 0.50001, 0.5, 0.49999, 0.5));
        Assert.Equal(key, ViewpointAnimationRules.CameraKey(1000, -2.5, 3, -0.5, -0.5, -0.5, -0.5));
        Assert.NotEqual(key, ViewpointAnimationRules.CameraKey(1000.01, -2.5, 3, 0.5, 0.5, 0.5, 0.5));
        Assert.NotEqual(key, ViewpointAnimationRules.CameraKey(1000, -2.5, 3, 0.5, 0.5, 0.5, -0.5));
    }

    [Theory]
    [InlineData(10.0, 1.0, 2.0, 5.0, 0.5)]
    [InlineData(0.0, 1.0, 2.0, null, 0.5)]
    [InlineData(10.0, 0.0, 4.0, 2.5, null)]
    [InlineData(0.0, 0.0, 4.0, null, null)]
    public void StepSpeedsCoverTheMoveInTheGivenTime(double distance, double angle, double seconds, double? linear, double? angular)
    {
        var speeds = ViewpointAnimationRules.StepSpeeds(distance, angle, seconds);
        Assert.Equal(linear, speeds.Linear);
        Assert.Equal(angular, speeds.Angular);
    }

    [Fact]
    public void RequestsAreValidated()
    {
        Assert.Null(ViewpointAnimationRules.Describe(new ViewpointAnimationRequest { Action = "list" }));
        Assert.Null(ViewpointAnimationRules.Describe(new ViewpointAnimationRequest()));
        Assert.Null(ViewpointAnimationRules.Describe(Create()));
        Assert.Null(ViewpointAnimationRules.Describe(Create(sourceFolder: "", viewpoints: new List<string> { "A", "Route/B" })));
        Assert.Null(ViewpointAnimationRules.Describe(new ViewpointAnimationRequest { Action = "delete", Name = "Flight" }));
        Assert.Equal("action must be list, create or delete.", ViewpointAnimationRules.Describe(new ViewpointAnimationRequest { Action = "play" }));
        Assert.Equal("name is required for create and delete.", ViewpointAnimationRules.Describe(new ViewpointAnimationRequest { Action = "delete", Name = " " }));
        Assert.Equal("create needs exactly one of sourceFolder or viewpoints.", ViewpointAnimationRules.Describe(Create(sourceFolder: "")));
        Assert.Equal("create needs exactly one of sourceFolder or viewpoints.", ViewpointAnimationRules.Describe(Create(viewpoints: new List<string> { "A" })));
        Assert.Equal("viewpoints must not contain blank entries.", ViewpointAnimationRules.Describe(Create(sourceFolder: "", viewpoints: new List<string> { "A", "" })));
        Assert.Equal("create accepts at most 1000 viewpoints.", ViewpointAnimationRules.Describe(Create(sourceFolder: "", viewpoints: Enumerable.Repeat("A", 1001).ToList())));
        var request = Create();
        request.Smoothing = "bezier";
        Assert.Equal("smoothing must be none, sync or spline.", ViewpointAnimationRules.Describe(request));
        request = Create();
        request.PauseSeconds = -1;
        Assert.Equal("holdSeconds, pauseSeconds and stepSeconds must be between 0 and 3600.", ViewpointAnimationRules.Describe(request));
        request = Create();
        request.StepSeconds = 3601;
        Assert.Equal("holdSeconds, pauseSeconds and stepSeconds must be between 0 and 3600.", ViewpointAnimationRules.Describe(request));
        request = Create();
        request.Smoothing = "Spline";
        Assert.Null(ViewpointAnimationRules.Describe(request));
    }

    [Fact]
    public void ContractsNameTheCommandAndReportFramesAndAnimations()
    {
        Assert.Equal("viewpoint_animation", HostCommandNames.ViewpointAnimation);
        Assert.True(HostCommandEffects.IsKnown(HostCommandNames.ViewpointAnimation));
        Assert.Equal(new[] { "list", "create", "delete" }, ViewpointAnimationRules.Actions);
        Assert.Equal(new[] { "none", "sync", "spline" }, ViewpointAnimationRules.Smoothings);
        Assert.Equal(1000, ViewpointAnimationRules.MaxViewpoints);
        var response = new ViewpointAnimationResponse();
        response.Frames.Add(new ViewpointAnimationFrame { Kind = "cut", DelaySeconds = 2 });
        response.Animations.Add(new ViewpointAnimationSummary { Name = "Flight", Path = "Anim/Flight", FrameCount = 3, CutCount = 1, Loops = false, Smoothing = "none" });
        Assert.Empty(response.Warnings);
        Assert.Empty(new ViewpointAnimationRequest().Viewpoints);
    }

    [Fact]
    public void TheToolListsByDefaultAndPreviewsWritesFirst()
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == "ViewpointAnimation");
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
        Assert.Equal("viewpoint_animation", tool.ProtocolTool.Name);
        Assert.Equal(ToolEffects.View | ToolEffects.Document, method.GetCustomAttribute<ToolCapabilitiesAttribute>()!.Effects);
        var properties = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!["properties"]!;
        Assert.Equal("list", properties["action"]!["default"]!.GetValue<string>());
        Assert.False(properties["apply"]!["default"]!.GetValue<bool>());
        Assert.False(properties["overwrite"]!["default"]!.GetValue<bool>());
        Assert.Equal(0, properties["holdSeconds"]!["default"]!.GetValue<double>());
        Assert.Equal(2, properties["pauseSeconds"]!["default"]!.GetValue<double>());
        Assert.Equal(0, properties["stepSeconds"]!["default"]!.GetValue<double>());
        Assert.Equal("", properties["smoothing"]!["default"]!.GetValue<string>());
        Assert.NotNull(properties["viewpoints"]);
        Assert.NotNull(properties["sourceFolder"]);
    }

    [Fact]
    public void ThePluginCopiesFramesAndReachesNewerMembersThroughReflection()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "ViewpointAnimationService.cs"));
        foreach (var expected in new[]
        {
            "ViewpointAnimationRules.Describe(", "ViewpointAnimationRules.PlanFrames(", "ViewpointAnimationRules.CameraKey(",
            "ViewpointAnimationRules.StepSpeeds(", "new SavedViewpointAnimation(", "new SavedViewpointAnimationCut(",
            ".CreateCopy()", "GetProperty(\"Delay\")", "GetProperty(\"Loops\")", "GetProperty(\"Smoothing\")",
            "LinearSpeed", "AngularSpeed",
        })
            Assert.Contains(expected, service, StringComparison.Ordinal);
        foreach (var forbidden in new[] { ".Delay =", ".Loops =", ".Smoothing =", "SavedViewpointAnimationSmoothing." })
            Assert.DoesNotContain(forbidden, service, StringComparison.Ordinal);
    }

    private static string[] Describe(IEnumerable<ViewpointAnimationFrame> frames) =>
        frames.Select(frame => frame.Kind == "cut"
            ? "cut:" + frame.DelaySeconds!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "viewpoint:" + frame.Source).ToArray();
}
