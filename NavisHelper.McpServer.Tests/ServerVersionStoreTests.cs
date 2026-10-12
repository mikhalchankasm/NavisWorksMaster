using System.Security.Cryptography;
using NavisHelper.McpConfigurator;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Owner decision 2026-10-12: every server version lives in
// <root>\versions\<version>-<sha8>, and <root>\McpServer is a directory junction
// to the current one, so clients keep one stable path. An install copies the new
// version, health-checks it, then swaps the junction; the previous version stays
// for rollback. Swapping or deleting the junction must never touch the files of
// the version it pointed to.
public sealed class ServerVersionStoreTests : IDisposable
{
    private const string Exe = "NavisHelper.McpServer.exe";
    private const string Dll = "NavisHelper.McpServer.dll";
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "NavisHelper-VersionStore-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public ServerVersionStoreTests()
    {
        _root = Path.Combine(_temp, "NavisHelper");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // Remove junctions first so a recursive delete can never follow one.
        foreach (var link in Directory.EnumerateDirectories(_temp, "*", SearchOption.AllDirectories)
                     .Where(path => new DirectoryInfo(path).LinkTarget != null).ToList())
            Directory.Delete(link);
        try { Directory.Delete(_temp, recursive: true); } catch (IOException) { }
    }

    private string Source(string name, string version, string content)
    {
        var directory = Path.Combine(_temp, "src-" + name);
        Directory.CreateDirectory(Path.Combine(directory, "runtimes"));
        File.WriteAllText(Path.Combine(directory, Exe), "exe " + content);
        File.WriteAllText(Path.Combine(directory, Dll), "dll " + content);
        File.WriteAllText(Path.Combine(directory, "runtimes", "native.dll"), "native " + content);
        File.WriteAllText(Path.Combine(directory, "NavisHelper.McpServer.deps.json"),
            "{\"targets\":{\".NETCoreApp,Version=v9.0\":{\"NavisHelper.McpServer/" + version + "\":{}}}}");
        return directory;
    }

    private static string Sha8(string file) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))[..8].ToLowerInvariant();

    private string Link => Path.Combine(_root, "McpServer");

    private string Target => new DirectoryInfo(Link).LinkTarget is { } target
        ? Path.GetFullPath(target).TrimEnd('\\')
        : throw new InvalidOperationException("McpServer is not a junction");

    private ServerVersionStore Store(bool healthy = true) => new(_root, _ => healthy);

    [Fact]
    public void InstallIntoEmptyRootCreatesVersionDirectoryAndJunction()
    {
        var source = Source("a", "2.10.0.0", "A");
        var result = Store().Install(source);

        var expected = Path.Combine(_root, "versions", "2.10.0.0-" + Sha8(Path.Combine(source, Dll)));
        Assert.True(result.Activated);
        Assert.Equal(expected, result.VersionDirectory.TrimEnd('\\'));
        Assert.Equal(expected, Target);
        Assert.Equal("exe A", File.ReadAllText(Path.Combine(Link, Exe)));
        Assert.Equal("native A", File.ReadAllText(Path.Combine(expected, "runtimes", "native.dll")));
        Assert.Null(result.PreviousDirectory);
    }

    [Fact]
    public void LegacyRealDirectoryIsMovedIntoVersionsBeforeActivation()
    {
        var legacy = Source("legacy", "2.9.0.0", "L");
        CopyDirectory(legacy, Link);
        var result = Store().Install(Source("b", "2.10.0.0", "B"));

        var moved = Path.Combine(_root, "versions", "2.9.0.0-" + Sha8(Path.Combine(legacy, Dll)));
        Assert.True(result.Activated);
        Assert.Equal(moved, result.PreviousDirectory?.TrimEnd('\\'));
        Assert.Equal("exe L", File.ReadAllText(Path.Combine(moved, Exe)));
        Assert.Equal("exe B", File.ReadAllText(Path.Combine(Link, Exe)));
    }

    [Fact]
    public void FailedHealthCheckChangesNothing()
    {
        Store().Install(Source("a", "2.10.0.0", "A"));
        var before = Target;
        var result = Store(healthy: false).Install(Source("b", "2.11.0.0", "B"));

        Assert.False(result.Activated);
        Assert.Equal(before, Target);
        Assert.Equal("exe A", File.ReadAllText(Path.Combine(Link, Exe)));
        Assert.Single(Directory.GetDirectories(Path.Combine(_root, "versions")));
    }

    [Fact]
    public void ReinstallingIdenticalCodeReusesItsDirectory()
    {
        var source = Source("a", "2.10.0.0", "A");
        var first = Store().Install(source);
        var second = Store().Install(source);

        Assert.True(second.Activated);
        Assert.Equal(first.VersionDirectory, second.VersionDirectory);
        Assert.Single(Directory.GetDirectories(Path.Combine(_root, "versions")));
    }

    [Fact]
    public void SwitchKeepsPreviousVersionIntactAndRollbackReturnsToIt()
    {
        var first = Store().Install(Source("a", "2.10.0.0", "A"));
        var second = Store().Install(Source("b", "2.10.0.0", "B"));

        Assert.Equal(first.VersionDirectory, second.PreviousDirectory);
        Assert.Equal("exe A", File.ReadAllText(Path.Combine(first.VersionDirectory, Exe)));
        Assert.Equal("exe B", File.ReadAllText(Path.Combine(Link, Exe)));

        Assert.True(Store().Rollback());
        Assert.Equal(first.VersionDirectory.TrimEnd('\\'), Target);
        Assert.Equal("exe B", File.ReadAllText(Path.Combine(second.VersionDirectory, Exe)));
    }

    [Fact]
    public void PruneKeepsCurrentPreviousAndInUseVersions()
    {
        var a = Store().Install(Source("a", "2.8.0.0", "A")).VersionDirectory;
        var b = Store().Install(Source("b", "2.9.0.0", "B")).VersionDirectory;
        var c = Store().Install(Source("c", "2.10.0.0", "C")).VersionDirectory;
        var d = Store().Install(Source("d", "2.11.0.0", "D")).VersionDirectory;

        var removed = Store().Prune(directory => string.Equals(directory.TrimEnd('\\'), a.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

        Assert.Equal(new[] { b.TrimEnd('\\') }, removed.Select(path => path.TrimEnd('\\')));
        Assert.True(Directory.Exists(a));
        Assert.False(Directory.Exists(b));
        Assert.True(Directory.Exists(c));
        Assert.True(Directory.Exists(d));
        Assert.Equal(d.TrimEnd('\\'), Target);
    }

    [Fact]
    public void SourceWithoutServerIsRefused()
    {
        var source = Source("bad", "2.10.0.0", "X");
        File.Delete(Path.Combine(source, Exe));
        Assert.Throws<FileNotFoundException>(() => Store().Install(source));
        Assert.False(Directory.Exists(Link));
    }

    [Fact]
    public async Task HealthCheckAcceptsTheRealServerAndRejectsAFakeOne()
    {
        var serverDll = typeof(NavisHelper.McpServer.Resources.McpToolCatalogResources).Assembly.Location;
        Assert.True(await McpServerHealthCheck.RunAsync(serverDll, TimeSpan.FromSeconds(30)));

        var fake = Source("fake", "2.10.0.0", "F");
        Assert.False(await McpServerHealthCheck.RunAsync(Path.Combine(fake, Exe), TimeSpan.FromSeconds(10)));
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, destination));
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination));
    }
}
