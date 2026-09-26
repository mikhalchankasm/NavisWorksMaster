using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class WorldMarkerOverlayServiceArchitectureTests
{
    [Fact]
    public void Host_CallsMarkerServiceFromTrackedDocumentFileNameChanged()
    {
        var host = Read("NavisHelper", "Agent", "Host", "AgentHostService.Document.cs");
        var body = MethodBody(host, "private void OnTrackedDocumentFileNameChanged(");

        Assert.Contains("_clashIsolationService.HandleDocumentFileNameChanged(document);", body);
        Assert.Contains("_modelColorSchemeService.HandleDocumentFileNameChanged(document);", body);
        Assert.Contains("_worldMarkerOverlayService.HandleDocumentFileNameChanged(document);", body);
    }

    [Fact]
    public void Service_KeepsMarkersOnlyWhileTheModelContentMatchesTheCapturedIdentity()
    {
        var source = ReadService();
        var body = MethodBody(source, "public void HandleDocumentFileNameChanged(");

        Assert.Contains("Snapshot.Count == 0", body);
        Assert.Contains("_documentIdentity != null && _documentIdentity.HasSameModelContent(document)", body);
        Assert.Contains("ModelColorSchemeDocumentIdentity.Capture(document)", body);
        Assert.Contains("WorldMarkerOverlayStore.Clear()", body);
    }

    [Fact]
    public void Service_CapturesTheIdentityOnlyForANonEmptyAppliedSnapshot()
    {
        var source = ReadService();
        var body = MethodBody(source, "private void ApplySnapshot(");

        Assert.Contains("WorldMarkerOverlayStore.Update(document, next)", body);
        Assert.Contains("next.Count > 0", body);
        Assert.Contains("ModelColorSchemeDocumentIdentity.Capture(document)", body);
    }

    private static string ReadService() =>
        Read("NavisHelper", "Agent", "Services", "WorldMarkerOverlayService.cs");

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

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"expected `{signature}` in the source");
        var brace = source.IndexOf('{', start + signature.Length);
        Assert.True(brace >= 0, $"expected a body for `{signature}`");

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

        throw new InvalidOperationException("Unbalanced braces in the source.");
    }
}
