using System.Collections.Generic;
using System.Text.RegularExpressions;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class WorldMarkerOverlayRendererArchitectureTests
{
    private static readonly string[] RendererForbiddenCalls =
    {
        "SetCustomToolPlugin", "Descendants", "Search", "CurrentSelection", "SetHidden",
        "OverridePermanent", "SavedViewpoints", "Append", "Save",
    };

    [Fact]
    public void Renderer_IsAnOverlayOnlyRenderPlugin()
    {
        var source = ReadRenderer();
        Assert.Contains("[Plugin(\"NavisHelper.WorldMarkers\", \"CBC\")]", source);
        Assert.Contains("class WorldMarkerOverlayRenderer : RenderPlugin", source);
        Assert.Contains("override void OverlayRender(", source);
        Assert.Contains("override BoundingBox3D MakeRenderBoundingBox(", source);
        Assert.DoesNotContain("override void Render(", source);
        Assert.DoesNotMatch(new Regex(@"override\s+[\w.]+\s+Render\s*\("), source);
    }

    [Fact]
    public void Renderer_MakesNoDocumentToolSelectionOrViewpointCalls()
    {
        var source = ReadRenderer();
        foreach (var forbidden in RendererForbiddenCalls)
            Assert.DoesNotContain(forbidden, source);
    }

    [Fact]
    public void EveryRendererOverrideBody_IsGuardedByTryCatch()
    {
        var source = ReadRenderer();
        var overrides = OverrideBodies(source);
        Assert.True(overrides.Count >= 2, $"expected the two render callbacks, found {overrides.Count}");
        Assert.Contains(overrides, item => item.Name == "OverlayRender");
        Assert.Contains(overrides, item => item.Name == "MakeRenderBoundingBox");
        foreach (var (name, body) in overrides)
        {
            Assert.Contains("try", body);
            Assert.Contains("catch", body);
        }
    }

    [Fact]
    public void Renderer_ProjectsGeometrySegmentsAndDrawsText2DLabels()
    {
        var source = ReadRenderer();
        Assert.Contains("WorldMarkerOverlayGeometry", source);
        Assert.Contains("ProjectPoint", source);
        Assert.Contains("Text2D", source);
        Assert.Contains("DepthTest(false)", source);
        Assert.Contains("Blend(true)", source);
    }

    [Fact]
    public void Renderer_DisposesWhatProjectPointProduces()
    {
        var source = ReadRenderer();
        // ProjectionResult is plain managed data; the native handles a projection creates are
        // the Point2Ds built from its X/Y, so each one must be a `using` resource.
        var created = Regex.Matches(source, @"new\s+Point2D\s*\(").Count;
        var disposed = Regex.Matches(source, @"using\s*\(\s*var\s+\w+\s*=\s*new\s+Point2D\s*\(").Count;
        Assert.True(created > 0, "expected the renderer to create Point2D handles per frame");
        Assert.Equal(created, disposed);

        foreach (Match projection in Regex.Matches(source, @"var\s+(?<name>\w+)\s*=\s*view\.ProjectPoint\("))
        {
            var name = projection.Groups["name"].Value;
            Assert.True(
                Regex.IsMatch(source, @"new\s+Point2D\s*\(\s*" + name + @"\.X"),
                $"{name} must feed a disposed Point2D handle");
        }
    }

    [Fact]
    public void Store_HoldsVersionedFramesAndRequestsDelayedRedraw()
    {
        var source = ReadStore();
        Assert.Contains("public static void Clear()", source);
        Assert.Contains("RequestDelayedRedraw", source);
        Assert.Contains("ViewRedrawRequests.OverlayRender", source);
        Assert.Contains("ViewRedrawRequests.All", source);
        Assert.Contains("public static void OnDocumentChanged(", source);
        Assert.Contains("WorldMarkerOverlayGeometry.Build", source);
    }

    private static string ReadRenderer() => Read("NavisHelper", "WorldMarkerOverlayRenderer.cs");

    private static string ReadStore() => Read("NavisHelper", "WorldMarkerOverlayStore.cs");

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate NavisHelper.sln.");
    }

    private static IReadOnlyList<(string Name, string Body)> OverrideBodies(string source)
    {
        var bodies = new List<(string Name, string Body)>();
        foreach (Match match in Regex.Matches(source, @"override\s+[\w.]+\s+(?<name>\w+)\s*\("))
        {
            var body = BodyOf(source, match);
            if (body.Length > 0)
                bodies.Add((match.Groups["name"].Value, body));
        }

        return bodies;
    }

    private static string BodyOf(string source, Match start)
    {
        var brace = source.IndexOf('{', start.Index + start.Length);
        if (brace < 0)
            return string.Empty;

        var depth = 0;
        for (var index = brace; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                    return source.Substring(brace, index - brace + 1);
            }
        }

        return string.Empty;
    }
}
