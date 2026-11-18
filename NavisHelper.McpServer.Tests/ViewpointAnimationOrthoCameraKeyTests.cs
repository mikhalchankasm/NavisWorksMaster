using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #538 (#422 follow-up): an orthographic view does not change when its eye slides along the view direction,
// and Navisworks pushes the eye out of a larger section box (2.23 L3: a 611 m slide along the iso view).
// Two such cameras must share a key so the planner inserts a cut instead of interpolating between them.
// Quaternions are Navisworks Rotation3D (A, B, C, D) = (x, y, z, w); the camera looks along its local -Z.
public sealed class ViewpointAnimationOrthoCameraKeyTests
{
    // Rotates local +Z onto (1, -1, 1) / sqrt(3): the iso view of the L3 pair.
    private const double IsoA = 0.3250575837, IsoB = 0.3250575837, IsoC = 0, IsoD = 0.8880738340;
    // 90 degrees about X: local +Z becomes world -Y.
    private const double XA = 0.7071067812, XB = 0, XC = 0, XD = 0.7071067812;

    private static string Ortho(double x, double y, double z, double a, double b, double c, double d, double height) =>
        ViewpointAnimationRules.CameraKey(x, y, z, a, b, c, d, orthographic: true, heightField: height);

    [Fact]
    public void OrthographicCamerasOffsetOnlyAlongTheViewAxisShareAKey()
    {
        var plain = Ortho(1000.25, -2000.5, 300.125, IsoA, IsoB, IsoC, IsoD, 3362.1);
        Assert.Equal(plain, Ortho(1611.25, -2611.5, 911.125, IsoA, IsoB, IsoC, IsoD, 3362.1));
        Assert.Equal(plain, Ortho(389.25, -1389.5, -310.875, -IsoA, -IsoB, -IsoC, -IsoD, 3362.1));
        Assert.Equal(Ortho(10, 20, 30, 0, 0, 0, 1, 50), Ortho(10, 20, -500, 0, 0, 0, 1, 50));
        Assert.Equal(Ortho(10, 20, 30, XA, XB, XC, XD, 50), Ortho(10, 631, 30, XA, XB, XC, XD, 50));
    }

    [Fact]
    public void APerpendicularOffsetOrAnotherHeightFieldIsAnotherCamera()
    {
        var plain = Ortho(1000.25, -2000.5, 300.125, IsoA, IsoB, IsoC, IsoD, 3362.1);
        Assert.NotEqual(plain, Ortho(1000.75, -2000, 300.125, IsoA, IsoB, IsoC, IsoD, 3362.1));
        Assert.NotEqual(plain, Ortho(1611.25, -2611.5, 911.125, IsoA, IsoB, IsoC, IsoD, 3363.1));
        Assert.NotEqual(Ortho(10, 20, 30, XA, XB, XC, XD, 50), Ortho(10, 20, 641, XA, XB, XC, XD, 50));
        Assert.NotEqual(Ortho(10, 20, 30, 0, 0, 0, 1, 50), Ortho(10, 20, 30, XA, XB, XC, XD, 50));
    }

    [Fact]
    public void PerspectiveCamerasStillCompareByPosition()
    {
        Assert.NotEqual(
            ViewpointAnimationRules.CameraKey(10, 20, 30, 0, 0, 0, 1, orthographic: false, heightField: 50),
            ViewpointAnimationRules.CameraKey(10, 20, -500, 0, 0, 0, 1, orthographic: false, heightField: 50));
        Assert.Equal(
            ViewpointAnimationRules.CameraKey(10, 20, 30, 0, 0, 0, 1),
            ViewpointAnimationRules.CameraKey(10, 20, 30, 0, 0, 0, 1, orthographic: false, heightField: 50));
        Assert.NotEqual(Ortho(10, 20, 30, 0, 0, 0, 1, 50), ViewpointAnimationRules.CameraKey(10, 20, 30, 0, 0, 0, 1));
    }

    [Fact]
    public void ThePlainAndTopPairPlansOneInstantCut()
    {
        var keys = new[]
        {
            Ortho(1000.25, -2000.5, 300.125, IsoA, IsoB, IsoC, IsoD, 3362.1),
            Ortho(1611.25, -2611.5, 911.125, IsoA, IsoB, IsoC, IsoD, 3362.1),
        };
        var frames = ViewpointAnimationRules.PlanFrames(new[] { "plain", "top" }, keys, 0, 0);
        Assert.Equal(new[] { "viewpoint", "cut", "viewpoint" }, frames.Select(frame => frame.Kind).ToArray());
        Assert.Equal((double?)0, frames[1].DelaySeconds);
    }

    [Fact]
    public void ThePluginPassesTheProjectionAndHeightField()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "ViewpointAnimationService.cs"));
        Assert.Contains("ViewpointProjection.Orthographic", service, StringComparison.Ordinal);
        Assert.Contains(".HeightField", service, StringComparison.Ordinal);
    }
}
