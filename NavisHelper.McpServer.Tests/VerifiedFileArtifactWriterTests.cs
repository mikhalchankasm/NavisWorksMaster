using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class VerifiedFileArtifactWriterTests
{
    [Fact]
    public void WriteUtf8_AtomicallyCompletesAndVerifiesSizeAndHash()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "plan.json");
            var result = VerifiedFileArtifactWriter.WriteUtf8(path, "{\"schema\":\"synthetic\"}", false);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".partial"));
            Assert.Equal(new FileInfo(path).Length, result.BytesWritten);
            Assert.Equal(64, result.Sha256.Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void WriteUtf8_FailureDoesNotClaimSuccessOrLeavePartial()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "plan.json");
            File.WriteAllText(path, "existing");
            Assert.Throws<IOException>(() => VerifiedFileArtifactWriter.WriteUtf8(path, "replacement", false));
            Assert.Equal("existing", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".partial"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void WriteUtf8_OverwriteCompletesWithoutLeavingRecoveryArtifacts()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "plan.json");
            File.WriteAllText(path, "original");
            var result = VerifiedFileArtifactWriter.WriteUtf8(path, "replacement", true);
            Assert.Equal("replacement", File.ReadAllText(path));
            Assert.Equal(new FileInfo(path).Length, result.BytesWritten);
            Assert.Empty(Directory.GetFiles(directory, "*.partial"));
            Assert.Empty(Directory.GetFiles(directory, "*.backup.*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void BboxPreview_PreservesHonestDryRunArtifactState()
    {
        var full = new ClashBboxPairPlanResponse
        {
            Applied = false,
            CalculatedOutputPath = @"C:\Temp\synthetic-plan.json",
            OutputWritten = false,
            ArtifactStatus = ClashTransferArtifactStatuses.NotWrittenDryRun,
        };
        var preview = ClashBboxPlanHelper.BuildPreview(full, 10, false);
        Assert.False(preview.OutputWritten);
        Assert.Equal(ClashTransferArtifactStatuses.NotWrittenDryRun, preview.ArtifactStatus);
        Assert.Null(preview.OutputPath);
        Assert.Equal(full.CalculatedOutputPath, preview.CalculatedOutputPath);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "NavisHelperTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
