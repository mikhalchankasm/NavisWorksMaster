using Xunit;

namespace NavisHelper.McpServer.Tests;

// ModelColorSchemeService.cs reached 72 KB of the 80 KB per-file limit and was split
// into partial files by concern (#349). That PR also proved, by a hash of the sorted
// code lines and the declaration list, that the split only moved code; those one-off
// checks were retired once it merged. What stays is the shape: several partial files,
// none of them growing back towards the limit.
public sealed class ModelColorSchemeServiceSplitTests
{
    private static readonly string ServicesFolder = Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services");

    [Fact]
    public void TheServiceStaysSplitIntoSmallPartialFiles()
    {
        var parts = Directory.GetFiles(ServicesFolder, "ModelColorSchemeService*.cs");
        Assert.True(parts.Length >= 4, "expected at least 4 ModelColorSchemeService*.cs files, found " + parts.Length);
        foreach (var part in parts)
        {
            Assert.True(new FileInfo(part).Length <= 32 * 1024, Path.GetFileName(part) + " is over 32 KB");
            Assert.Matches(@"\bsealed\s+partial\s+class\s+ModelColorSchemeService\b", File.ReadAllText(part));
        }
    }
}
