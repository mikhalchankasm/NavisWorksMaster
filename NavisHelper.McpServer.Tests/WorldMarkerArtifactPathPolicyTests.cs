using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class WorldMarkerArtifactPathPolicyTests
{
    [Fact]
    public void ResolveManagedRoot_SavedDocumentUsesSiblingManagedDirectory()
    {
        var document = Path.Combine(Path.GetTempPath(), "project", "model.nwf");

        var result = WorldMarkerArtifactPathPolicy.ResolveManagedRoot(document, null, Path.GetTempPath());

        Assert.True(result.IsPortableWithDocument);
        Assert.Equal(string.Empty, result.Warning);
        Assert.StartsWith(Path.Combine(Path.GetDirectoryName(document)!, "NavisHelper.WorldMarkers"), result.Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveManagedRoot_UnsavedDocumentUsesLocalApplicationDataWithWarning()
    {
        var local = Path.Combine(Path.GetTempPath(), "LocalAppData-test");

        var result = WorldMarkerArtifactPathPolicy.ResolveManagedRoot(null, null, local);

        Assert.False(result.IsPortableWithDocument);
        Assert.Equal(WorldMarkerArtifactPathPolicy.UnsavedPortabilityWarning, result.Warning);
        Assert.Equal(Path.Combine(local, "NavisHelper", "WorldMarkers", "Unsaved"), result.Path);
    }

    [Fact]
    public void ResolveManagedRoot_ExplicitDirectoryWins()
    {
        var explicitDirectory = Path.Combine(Path.GetTempPath(), "durable-markers");

        var result = WorldMarkerArtifactPathPolicy.ResolveManagedRoot("C:\\project\\model.nwf", explicitDirectory, Path.GetTempPath());

        Assert.Equal(Path.GetFullPath(explicitDirectory), result.Path);
        Assert.False(result.IsPortableWithDocument);
        Assert.Equal(WorldMarkerArtifactPathPolicy.ExplicitDirectoryPortabilityWarning, result.Warning);
    }

    [Fact]
    public void ResolveManagedRoot_RejectsWindowsDriveRelativeExplicitDirectory()
    {
        Assert.Throws<ArgumentException>(() =>
            WorldMarkerArtifactPathPolicy.ResolveManagedRoot(null, "C:dir", Path.GetTempPath()));
    }

    [Fact]
    public void ResolveManagedRoot_TrimsExplicitDirectoryTrailingSeparator()
    {
        var explicitDirectory = Path.Combine(Path.GetTempPath(), "durable-markers") + Path.DirectorySeparatorChar;

        var result = WorldMarkerArtifactPathPolicy.ResolveManagedRoot(null, explicitDirectory, Path.GetTempPath());

        Assert.Equal(Path.GetFullPath(explicitDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), result.Path);
        Assert.False(result.IsPortableWithDocument);
        Assert.Equal(WorldMarkerArtifactPathPolicy.ExplicitDirectoryPortabilityWarning, result.Warning);
    }

    [Fact]
    public void ResolveManagedRoot_AcceptsFullyQualifiedUncDirectory()
    {
        const string uncDirectory = @"\\server\share\durable-markers";

        var result = WorldMarkerArtifactPathPolicy.ResolveManagedRoot(null, uncDirectory, Path.GetTempPath());

        Assert.Equal(Path.GetFullPath(uncDirectory), result.Path);
        Assert.False(result.IsPortableWithDocument);
    }

    [Fact]
    public void ResolveManagedRoot_DocumentAtFilesystemRootUsesSiblingManagedDirectory()
    {
        var filesystemRoot = Path.GetPathRoot(Path.GetTempPath())!;
        var document = Path.Combine(filesystemRoot, "model.nwf");

        var result = WorldMarkerArtifactPathPolicy.ResolveManagedRoot(document, null, Path.GetTempPath());

        Assert.True(result.IsPortableWithDocument);
        Assert.StartsWith(Path.Combine(filesystemRoot, WorldMarkerArtifactPathPolicy.ManagedDirectoryName), result.Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveManagedRoot_RejectsRelativeAndFilesystemRoot()
    {
        Assert.Throws<ArgumentException>(() => WorldMarkerArtifactPathPolicy.ResolveManagedRoot(null, "relative", Path.GetTempPath()));
        Assert.Throws<ArgumentException>(() => WorldMarkerArtifactPathPolicy.ResolveManagedRoot(null, Path.GetPathRoot(Path.GetTempPath()), Path.GetTempPath()));
        Assert.Throws<ArgumentException>(() => WorldMarkerArtifactPathPolicy.ResolveManagedRoot("relative.nwf", null, Path.GetTempPath()));
    }

    [Fact]
    public void BuildArtifactPath_ProducesToolOwnedContainedDxf()
    {
        var root = Path.Combine(Path.GetTempPath(), "managed");
        var markerId = WorldMarkerInputPolicy.CreateMarkerId("Marker");
        var revision = WorldMarkerArtifactPathPolicy.CreateRevisionId(new DateTime(2026, 8, 30, 12, 34, 56, 789, DateTimeKind.Utc), Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));

        var path = WorldMarkerArtifactPathPolicy.BuildArtifactPath(root, markerId, revision);

        Assert.Equal(Path.Combine(root, markerId + "--" + revision + ".dxf"), path);
        Assert.True(WorldMarkerArtifactPathPolicy.IsCleanupCandidate(root, path));
    }

    [Fact]
    public void IsCleanupCandidate_RejectsTraversalSiblingWrongExtensionAndUserFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "managed");
        var sibling = root + "-other";
        var markerId = WorldMarkerInputPolicy.CreateMarkerId("Marker");
        var revision = WorldMarkerArtifactPathPolicy.CreateRevisionId(DateTime.UtcNow, Guid.NewGuid());

        Assert.False(WorldMarkerArtifactPathPolicy.IsCleanupCandidate(root, Path.Combine(sibling, markerId + "--" + revision + ".dxf")));
        Assert.False(WorldMarkerArtifactPathPolicy.IsCleanupCandidate(root, Path.Combine(root, markerId + "--" + revision + ".txt")));
        Assert.False(WorldMarkerArtifactPathPolicy.IsCleanupCandidate(root, Path.Combine(root, "user.dxf")));
        Assert.False(WorldMarkerArtifactPathPolicy.IsCleanupCandidate(root, Path.Combine(root, "..", "outside.dxf")));
        Assert.False(WorldMarkerArtifactPathPolicy.IsCleanupCandidate(root, Path.Combine(root, "nested", markerId + "--" + revision + ".dxf")));
        Assert.False(WorldMarkerArtifactPathPolicy.IsCleanupCandidate(root, markerId + "--" + revision + ".dxf"));
        Assert.False(WorldMarkerArtifactPathPolicy.IsCleanupCandidate(root, null));
    }

    [Fact]
    public void BuildArtifactPath_RejectsInjectedIds()
    {
        var root = Path.Combine(Path.GetTempPath(), "managed");

        Assert.Throws<ArgumentException>(() => WorldMarkerArtifactPathPolicy.BuildArtifactPath(root, "..\\marker", "revision"));
    }

    [Fact]
    public void CreateRevisionId_IsUtcAndNonceBased()
    {
        var revision = WorldMarkerArtifactPathPolicy.CreateRevisionId(
            new DateTime(2026, 8, 30, 15, 0, 1, 2, DateTimeKind.Local),
            Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789"));

        Assert.Matches("^r[0-9]{8}T[0-9]{9}Z-[0-9a-f]{12}$", revision);
        Assert.True(WorldMarkerArtifactPathPolicy.IsRevisionId(revision));
        Assert.False(WorldMarkerArtifactPathPolicy.IsRevisionId("r20261399T999999999Z-abcdef012345"));
    }
}
