using System.Text.RegularExpressions;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #276: Settings -> About still showed "Copyright © 2019-2025" in the 2026 release.
// Every AssemblyCopyright declaration shares one format and one end year, and that
// year is not older than the 2.11 release year. Next year's bump moves all of them.
public sealed class AssemblyCopyrightTests
{
    private static readonly string[] AssemblyInfoFiles =
    {
        Path.Combine("NavisHelper", "Properties", "AssemblyInfo.cs"),
        Path.Combine("NavisHelper.Dev", "Properties", "AssemblyInfo.cs"),
    };

    [Fact]
    public void CopyrightDeclarationsEndInOneYearNoOlderThan2026()
    {
        var pattern = new Regex(@"AssemblyCopyright\(""Copyright © (\d{4})-(\d{4})""\)");
        var endYears = new List<int>();
        foreach (var relative in AssemblyInfoFiles)
        {
            var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, relative));
            var match = pattern.Match(source);
            Assert.True(match.Success, relative + " has no AssemblyCopyright(\"Copyright © YYYY-YYYY\")");
            Assert.True(int.Parse(match.Groups[1].Value) <= int.Parse(match.Groups[2].Value), relative);
            endYears.Add(int.Parse(match.Groups[2].Value));
        }

        Assert.Single(endYears.Distinct());
        Assert.True(endYears[0] >= 2026, "copyright ends in " + endYears[0]);
    }
}
