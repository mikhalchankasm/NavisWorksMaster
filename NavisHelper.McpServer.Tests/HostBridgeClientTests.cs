using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class InstanceDiscoveryStoreTests : IDisposable
{
    private readonly string _tempDirectory;

    public InstanceDiscoveryStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "NavisHelper-HostBridgeClientTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void TryDelete_RemovesUnreachableRecordEvenWhenPidIsPresent()
    {
        var record = new InstanceDiscoveryRecord
        {
            InstanceId = "nw-2027-unreachable",
            Pid = Environment.ProcessId,
        };
        var path = Path.Combine(_tempDirectory, record.InstanceId + ".json");
        File.WriteAllText(path, "{}");

        var deleted = InstanceDiscoveryStore.TryDelete(record, _tempDirectory);

        Assert.True(deleted);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TryDelete_RejectsMissingOrUnsafeInstanceId()
    {
        var missing = InstanceDiscoveryStore.TryDelete(new InstanceDiscoveryRecord(), _tempDirectory);
        var unsafePath = InstanceDiscoveryStore.TryDelete(
            new InstanceDiscoveryRecord { InstanceId = "..\\outside" },
            _tempDirectory);

        Assert.False(missing);
        Assert.False(unsafePath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }
        catch
        {
        }
    }
}
