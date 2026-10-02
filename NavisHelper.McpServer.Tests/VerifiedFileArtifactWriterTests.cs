using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class VerifiedFileArtifactWriterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteUtf8_PreservesExistingPartialFile(bool failCommit)
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "plan.json");
            var sibling = path + ".partial";
            var original = new byte[] { 0, 1, 2, 255 };
            File.WriteAllBytes(sibling, original);
            if (failCommit)
            {
                Directory.CreateDirectory(path);
                Assert.ThrowsAny<IOException>(() =>
                    VerifiedFileArtifactWriter.WriteUtf8(path, "replacement", false));
            }
            else
            {
                var result = VerifiedFileArtifactWriter.WriteUtf8(path, "replacement", false);
                Assert.Equal("replacement", File.ReadAllText(path));
                Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes("replacement"))), result.Sha256,
                    ignoreCase: true);
            }

            Assert.True(File.Exists(sibling));
            Assert.Equal(original, File.ReadAllBytes(sibling));
            Assert.Equal(failCommit ? 1 : 2, Directory.GetFiles(directory).Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void WriteUtf8_AtomicallyCompletesAndVerifiesSizeAndHash()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "plan.json");
            var result = VerifiedFileArtifactWriter.WriteUtf8(path, "{\"schema\":\"synthetic\"}", false);
            Assert.True(File.Exists(path));
            Assert.Single(Directory.GetFiles(directory));
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
            Assert.Single(Directory.GetFiles(directory));
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
            Assert.Single(Directory.GetFiles(directory));
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
