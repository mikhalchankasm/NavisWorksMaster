using System.Reflection;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #422 remainder: Navisworks interpolates a section plane between keyframes as (normal, distance from the
// origin), so on a model far from the origin a change of normal sweeps the plane through the scene. With
// cutOnClipChange=true the plan puts an instant cut before every frame whose clipping differs from the
// previous frame's, so the planes switch at the keyframe. The plugin compares Viewpoint.ClipPlanes.
public sealed class ViewpointAnimationClipCutTests
{
    private static string[] Describe(IEnumerable<ViewpointAnimationFrame> frames) =>
        frames.Select(frame => frame.Kind == "cut" ? "cut:" + frame.DelaySeconds : "viewpoint:" + frame.Source).ToArray();

    [Fact]
    public void AClipChangeBetweenDifferentCamerasGetsAnInstantCut()
    {
        var frames = ViewpointAnimationRules.PlanFrames(new[] { "A", "B", "C" }, new[] { "k1", "k2", "k3" },
            new[] { false, true, false }, 0, 2, cutOnClipChange: true);
        Assert.Equal(new[] { "viewpoint:A", "cut:0", "viewpoint:B", "viewpoint:C" }, Describe(frames));
    }

    [Fact]
    public void WithoutTheFlagTheClipChangesAreIgnored()
    {
        var frames = ViewpointAnimationRules.PlanFrames(new[] { "A", "B" }, new[] { "k1", "k2" },
            new[] { false, true }, 0, 2, cutOnClipChange: false);
        Assert.Equal(new[] { "viewpoint:A", "viewpoint:B" }, Describe(frames));
        Assert.Equal(Describe(ViewpointAnimationRules.PlanFrames(new[] { "A", "B" }, new[] { "k1", "k2" }, 0, 2)), Describe(frames));
    }

    [Fact]
    public void ARepeatedCameraKeepsItsPauseCutAndGetsNoSecondCut()
    {
        var frames = ViewpointAnimationRules.PlanFrames(new[] { "A", "A2" }, new[] { "k1", "k1" },
            new[] { false, true }, 0, 2, cutOnClipChange: true);
        Assert.Equal(new[] { "viewpoint:A", "cut:2", "viewpoint:A2" }, Describe(frames));
    }

    [Fact]
    public void AHoldAtTheEndOfARunComesBeforeTheClipCut()
    {
        var frames = ViewpointAnimationRules.PlanFrames(new[] { "A", "B" }, new[] { "k1", "k2" },
            new[] { false, true }, 3, 2, cutOnClipChange: true);
        Assert.Equal(new[] { "viewpoint:A", "cut:3", "viewpoint:A", "cut:0", "viewpoint:B", "cut:3", "viewpoint:B" }, Describe(frames));
    }

    [Fact]
    public void TheFirstFrameNeverGetsACutAndLengthsMustAgree()
    {
        var frames = ViewpointAnimationRules.PlanFrames(new[] { "A", "B" }, new[] { "k1", "k2" },
            new[] { true, false }, 0, 2, cutOnClipChange: true);
        Assert.Equal(new[] { "viewpoint:A", "viewpoint:B" }, Describe(frames));
        Assert.Throws<ArgumentException>(() => ViewpointAnimationRules.PlanFrames(new[] { "A", "B" }, new[] { "k1", "k2" },
            new[] { false }, 0, 2, cutOnClipChange: true));
    }

    [Fact]
    public void TheToolAndThePluginCarryTheFlag()
    {
        Assert.NotNull(typeof(ViewpointAnimationRequest).GetProperty("CutOnClipChange"));
        var method = typeof(NavisworksViewpointAnimationTools).GetMethods().Single(item => item.Name == "ViewpointAnimation");
        var parameter = method.GetParameters().SingleOrDefault(item => item.Name == "cutOnClipChange");
        Assert.NotNull(parameter);
        Assert.Equal(false, parameter!.DefaultValue);
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "ViewpointAnimationService.cs"));
        Assert.Contains(".ClipPlanes", service, StringComparison.Ordinal);
        Assert.Contains("cutOnClipChange", service, StringComparison.OrdinalIgnoreCase);
    }
}
