extern alias ConfiguratorAssembly;
using System.Text.Json;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpConfigurator;
using NavisHelper.McpServer.Services;
using Xunit;
using HostProcessProbe = ConfiguratorAssembly::NavisHelper.Diagnostics.HostProcessProbe;

namespace NavisHelper.McpServer.Tests;

public sealed class DoctorRuntimeInspectorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NavisHelperTests", Guid.NewGuid().ToString("N"));
    private static readonly DateTime Started = new(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc);

    public DoctorRuntimeInspectorTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(0, "alive")]
    [InlineData(2, "alive")]
    [InlineData(-2, "alive")]
    [InlineData(3, "stale")]
    [InlineData(-3, "stale")]
    public void SharedIdentityProbeRejectsPidReuseOutsideTolerance(int delta, string expected) =>
        Assert.Equal(expected, HostProcessProbe.CompareStart(Started.AddSeconds(delta), Started).Status);

    [Fact]
    public void LegacyHostIdentityRequiresTimestampOnlyInDoctor()
    {
        foreach (var name in new[] { "Roamer", "Navisworks" })
        {
            Assert.Equal("alive", HostProcessProbe.Classify(name, false, null, false,
                () => throw new Exception("Legacy server must not read start time")).Status);
            Assert.Equal("unverified", HostProcessProbe.Classify(name, false, null, true,
                () => throw new Exception("No recorded start time to compare")).Status);
            Assert.Equal("stale", HostProcessProbe.Classify(name, true, Started, false, () => Started).Status);
            Assert.Equal("alive", HostProcessProbe.Classify(name, false, Started, true, () => Started).Status);
        }
        Assert.Equal("stale", HostProcessProbe.Classify("Other", false, Started, false, () => Started).Status);
    }

    [Fact]
    public void PluginWireFormatAndExplicitOffsetAreReadAsUtcEvidence()
    {
        Write("writer.json", new InstanceDiscoveryRecord
        {
            Pid = 42, ProcessStartedAtUtc = Started, NavisworksVersion = "2027", PluginVersion = "2.10.0.0"
        });
        File.WriteAllText(Path.Combine(_directory, "offset.json"),
            """{"pid":43,"process_started_at_utc":"2026-10-04T13:00:00+03:00","plugin_version":"2.10.0.0","navisworks_version":"2027"}""");
        var result = DoctorRuntimeInspector.ReadHosts(_directory, "2.10.0.0", record =>
        {
            Assert.Equal(Started, record.ProcessStartedAtUtc);
            Assert.Equal(DateTimeKind.Utc, record.ProcessStartedAtUtc.Value.Kind);
            return new("alive", Started);
        });
        Assert.Equal(2, result.Hosts.Length);
        Assert.All(result.Hosts, host => Assert.Equal("consistent", host.VersionAgreement));
        Assert.All(result.Hosts, host => Assert.Equal("2027", host.NavisworksVersion));
    }

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData("//server/share")]
    [InlineData("relative")]
    public void UnsupportedDiscoveryRootsStayUnverified(string directory) =>
        Assert.Equal("unverified", DoctorRuntimeInspector.ReadHosts(directory, null, _ => new("alive")).Status);

    [Fact]
    public void MissingTimestampAndNonHostProcessCannotProveIdentity()
    {
        Assert.Equal("unverified", HostProcessProbe.CompareStart(null, Started).Status);
        Assert.Equal("stale", HostProcessProbe.Inspect(
            new InstanceDiscoveryRecord { Pid = Environment.ProcessId }, requireStartTime: true).Status);
        Assert.Equal("stale", HostProcessProbe.Inspect(
            new InstanceDiscoveryRecord { Pid = -1 }, requireStartTime: false).Status);
    }

    [Fact]
    public void DoctorPreservesStaleRecordsWhileExistingServerStillPrunesThem()
    {
        var path = Write("stale.json", new InstanceDiscoveryRecord { Pid = int.MaxValue, InstanceId = "stale" });
        var bytes = File.ReadAllBytes(path);
        var result = DoctorRuntimeInspector.ReadHosts(_directory, "2.10.0.0",
            record => HostProcessProbe.Inspect(record, requireStartTime: true));
        Assert.Equal("observed", result.Status);
        Assert.Equal("stale", Assert.Single(result.Hosts).ProcessIdentity);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(_directory));
        Assert.Empty(InstanceDiscoveryStore.LoadAliveRecords(_directory,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("alive", "2.9.0.0", "mismatch")]
    [InlineData("alive", "2.10.0.0", "consistent")]
    [InlineData("alive", "2.10.0", "consistent")]
    [InlineData("stale", "2.9.0.0", "unverified")]
    [InlineData("unverified", "2.9.0.0", "unverified")]
    [InlineData("alive", "SENTINEL_SECRET", "unverified")]
    public void DiscoveryMetadataIsSanitizedAndNeverBecomesLiveReadiness(string identity, string version, string agreement)
    {
        var record = new InstanceDiscoveryRecord
        {
            Pid = 1234, ProcessStartedAtUtc = Started, PluginVersion = version,
            NavisworksVersion = "2027", DocumentTitle = "SENTINEL_SECRET", PipeName = "SENTINEL_SECRET",
            InstanceId = "SENTINEL_SECRET", PluginAssemblyPath = "SENTINEL_SECRET", HostLogFilePath = "SENTINEL_SECRET"
        };
        var result = DoctorRuntimeInspector.Observe(record, "2.10.0.0", _ => new(identity, Started));
        Assert.Equal(agreement, result.VersionAgreement);
        Assert.DoesNotContain("SENTINEL_SECRET", JsonSerializer.Serialize(result));
        var evidence = new RuntimeEvidence("observed", [result], "observed", []);
        Assert.Equal("unverified", evidence.ClientAssociation);
        Assert.Equal("named_executables_only", evidence.ServerCoverage);
    }

    [Fact]
    public void ProcessLocationsRemainUnattributedAndUnknownPayloadsAreWithheld()
    {
        const string path = @"C:\bin\NavisHelper.McpServer.exe";
        Assert.Equal("same_path_unverified_build", DoctorRuntimeInspector.ObserveServer(1, path, path).Observation);
        Assert.Equal("different_path", DoctorRuntimeInspector.ObserveServer(2, path, @"C:\old\NavisHelper.McpServer.exe").Observation);
        Assert.Equal("not_comparable", DoctorRuntimeInspector.ObserveServer(2, path, null).Observation);
        Assert.Equal("not_comparable", DoctorRuntimeInspector.ObserveServer(2, path, @"C:\bin\NavisHelper.McpServer.dll").Observation);
        var invalid = DoctorRuntimeInspector.ObserveServer(3, "SENTINEL_SECRET", path);
        Assert.Equal("unverified", invalid.Observation);
        Assert.Null(invalid.ExecutablePath);
        Assert.DoesNotContain("SENTINEL_SECRET", JsonSerializer.Serialize(invalid));
    }

    [Fact]
    public void UnspecifiedStartTimeIsUnverifiedWithoutGuessingTimeZone()
    {
        var record = new InstanceDiscoveryRecord { Pid = 1, ProcessStartedAtUtc = DateTime.SpecifyKind(Started, DateTimeKind.Unspecified) };
        var result = DoctorRuntimeInspector.Observe(record, "2.10.0.0", _ => throw new Exception("Should not probe"));
        Assert.Equal("unverified", result.ProcessIdentity);
    }

    [Fact]
    public void MalformedOversizedAndInvalidUtf8RecordsAreBoundedAndPreserved()
    {
        File.WriteAllText(Path.Combine(_directory, "bad.json"), "{\"Pid\": SENTINEL_SECRET}");
        File.WriteAllBytes(Path.Combine(_directory, "utf8.json"), [0xC0, 0xAF]);
        File.WriteAllText(Path.Combine(_directory, "large.json"), new string(' ', DoctorRuntimeInspector.MaximumRecordBytes + 1));
        var before = Directory.GetFiles(_directory).ToDictionary(path => path, File.ReadAllBytes);
        var result = DoctorRuntimeInspector.ReadHosts(_directory, null, _ => throw new Exception("Should not probe"));
        Assert.Equal(2, result.Hosts.Count(host => host.ProcessIdentity == "invalid_record"));
        Assert.Single(result.Hosts, host => host.ProcessIdentity == "record_too_large");
        Assert.DoesNotContain("SENTINEL_SECRET", JsonSerializer.Serialize(result.Hosts));
        foreach (var (path, bytes) in before)
            Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(3, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public void ExcessRecordsAreExplicitAndMultipleHostsRemainSeparate()
    {
        for (var i = 1; i <= DoctorRuntimeInspector.MaximumRecords + 1; i++)
            Write(i + ".json", new InstanceDiscoveryRecord { Pid = i, ProcessStartedAtUtc = Started });
        var result = DoctorRuntimeInspector.ReadHosts(_directory, null, _ => new("alive", Started));
        Assert.Equal("truncated", result.Status);
        Assert.Equal(DoctorRuntimeInspector.MaximumRecords, result.Hosts.Length);
        Assert.Equal(result.Hosts.Length, result.Hosts.Select(host => host.Pid).Distinct().Count());
        Assert.Equal(DoctorRuntimeInspector.MaximumRecords + 1, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public void MissingOrUnreadableDiscoveryDirectoryIsNotCreatedOrSilentlyHealthy()
    {
        var missing = Path.Combine(_directory, "missing");
        Assert.Equal("no_records", DoctorRuntimeInspector.ReadHosts(missing, null, _ => new("alive")).Status);
        Assert.False(Directory.Exists(missing));
        var file = Path.Combine(_directory, "not-directory");
        File.WriteAllText(file, "keep");
        var result = DoctorRuntimeInspector.ReadHosts(file, null, _ => new("alive"));
        Assert.Equal("unverified", result.Status);
        Assert.Empty(result.Hosts);
        Assert.Equal("keep", File.ReadAllText(file));
    }

    private string Write(string name, InstanceDiscoveryRecord record)
    {
        var path = Path.Combine(_directory, name);
        // Match the actual Newtonsoft writer in AgentHostService.CreateJsonSettings.
        File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(record,
            new Newtonsoft.Json.JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
                {
                    NamingStrategy = new Newtonsoft.Json.Serialization.SnakeCaseNamingStrategy()
                },
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                DateParseHandling = Newtonsoft.Json.DateParseHandling.None
            }));
        return path;
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
