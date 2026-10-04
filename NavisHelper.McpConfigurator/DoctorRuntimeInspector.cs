using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NavisHelper.Agent.Contracts;
using NavisHelper.Diagnostics;

namespace NavisHelper.McpConfigurator;

internal sealed record HostObservation(string ProcessIdentity, int? Pid = null, DateTime? StartedAtUtc = null,
    string? NavisworksVersion = null, string? DiscoveryPluginVersion = null, string VersionAgreement = "unverified");
internal sealed record ServerObservation(int Pid, string Observation, string? ExecutablePath = null);
internal sealed record RuntimeEvidence(string HostScan, HostObservation[] Hosts,
    string ServerScan, ServerObservation[] Servers)
{
    public string Scope => "doctor_environment_not_client_session";
    public string ServerCoverage => "named_executables_only";
    public string ClientAssociation => "unverified";
}

internal static class DoctorRuntimeInspector
{
    internal const int MaximumRecords = 64;
    internal const int MaximumRecordBytes = 32 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // AgentHostService.CreateJsonSettings writes snake_case and ISO timestamps.
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, MaxDepth = 32
    };

    internal static RuntimeEvidence Inspect(string? configuredServer, string? serverVersion)
    {
        var directory = Environment.GetEnvironmentVariable("NAVISHELPER_INSTANCES_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NavisHelper", "Mcp", "instances");
        var (hostScan, hosts) = ReadHosts(directory, serverVersion,
            record => HostProcessProbe.Inspect(record, requireStartTime: true));
        var (serverScan, servers) = ReadServers(configuredServer);
        return new(hostScan, hosts, serverScan, servers);
    }

    internal static (string Status, HostObservation[] Hosts) ReadHosts(string directory, string? serverVersion,
        Func<InstanceDiscoveryRecord, ProcessIdentityEvidence> probe)
    {
        var hosts = new List<HostObservation>();
        try
        {
            if (!Path.IsPathFullyQualified(directory) || directory.StartsWith(@"\\") || directory.StartsWith("//"))
                return ("unverified", []);
            for (var parent = new DirectoryInfo(directory); parent != null; parent = parent.Parent)
            {
                var attributes = File.GetAttributes(parent.FullName);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                    return ("unverified", []);
            }
            // Bound materialization and record reads; filesystem enumeration has no hard time guarantee.
            var files = Directory.EnumerateFiles(directory, "*.json").Take(MaximumRecords + 1).ToArray();
            foreach (var file in files.Take(MaximumRecords))
            {
                try
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    {
                        hosts.Add(new("unverified"));
                        continue;
                    }
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    var bytes = new byte[MaximumRecordBytes + 1];
                    var length = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
                    if (length > MaximumRecordBytes)
                    {
                        hosts.Add(new("record_too_large"));
                        continue;
                    }
                    var text = new UTF8Encoding(false, true).GetString(bytes, 0, length).TrimStart('\uFEFF');
                    var record = JsonSerializer.Deserialize<InstanceDiscoveryRecord>(text, JsonOptions);
                    hosts.Add(Observe(record, serverVersion, probe));
                }
                catch (IOException) { hosts.Add(new("unverified")); }
                catch (UnauthorizedAccessException) { hosts.Add(new("unverified")); }
                catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
                {
                    hosts.Add(new("invalid_record"));
                }
            }
            return (files.Length > MaximumRecords ? "truncated" : "observed", hosts.ToArray());
        }
        catch (DirectoryNotFoundException) { return ("no_records", []); }
        catch (FileNotFoundException) { return ("no_records", []); }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            return ("unverified", hosts.ToArray());
        }
    }

    internal static HostObservation Observe(InstanceDiscoveryRecord? record, string? serverVersion,
        Func<InstanceDiscoveryRecord, ProcessIdentityEvidence> probe)
    {
        if (record == null || record.Pid <= 0)
            return new("invalid_record");
        // No source paths, instance IDs, document titles, logs or arbitrary version strings.
        var timestamp = record.ProcessStartedAtUtc;
        var identity = timestamp?.Kind == DateTimeKind.Unspecified
            ? new ProcessIdentityEvidence("unverified") : probe(new InstanceDiscoveryRecord
            {
                Pid = record.Pid,
                ProcessStartedAtUtc = timestamp?.Kind == DateTimeKind.Local ? timestamp.Value.ToUniversalTime() : timestamp
            });
        var version = Version.TryParse(record.PluginVersion, out var parsed) ? parsed.ToString() : null;
        var year = record.NavisworksVersion is "2024" or "2025" or "2026" or "2027" ? record.NavisworksVersion : null;
        var agreement = identity.Status == "alive" && version != null && Version.TryParse(serverVersion, out var server)
            ? Normalize(parsed!) == Normalize(server) ? "consistent" : "mismatch" : "unverified";
        return new(identity.Status, record.Pid, identity.StartedAtUtc, year, version, agreement);
    }

    private static (string Status, ServerObservation[] Servers) ReadServers(string? configuredServer)
    {
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName("NavisHelper.McpServer");
            var observations = new List<ServerObservation>();
            foreach (var process in processes.Take(MaximumRecords))
            {
                var pid = process.Id;
                try
                {
                    var path = process.MainModule?.FileName;
                    if (process.HasExited)
                        observations.Add(new(pid, "unverified"));
                    else
                        observations.Add(ObserveServer(pid, path, configuredServer));
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
                {
                    observations.Add(new(pid, "unverified"));
                }
            }
            return (processes.Length > MaximumRecords ? "truncated" : "observed", observations.ToArray());
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            return ("unverified", []);
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    internal static ServerObservation ObserveServer(int pid, string? path, string? configuredServer)
    {
        if (path == null || !ClientConfigurationReader.IsLocalPath(path, "NavisHelper.McpServer.exe"))
            return new(pid, "unverified");
        if (configuredServer == null || !configuredServer.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return new(pid, "not_comparable", path);
        var same = Path.GetFullPath(path).Equals(Path.GetFullPath(configuredServer), StringComparison.OrdinalIgnoreCase);
        return new(pid, same ? "same_path_unverified_build" : "different_path", path);
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
}
