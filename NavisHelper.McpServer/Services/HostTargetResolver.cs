using System.Text.Json;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed class HostTargetOptions
{
    public string InstanceId { get; set; } = string.Empty;
    public string NavisworksVersion { get; set; } = string.Empty;

    public bool HasExplicitTarget =>
        !string.IsNullOrWhiteSpace(InstanceId) ||
        !string.IsNullOrWhiteSpace(NavisworksVersion);
}

internal static class HostTargetResolver
{
    internal static InstanceDiscoveryRecord Resolve(
        IReadOnlyList<InstanceDiscoveryRecord> records,
        HostTargetOptions target,
        string configuredInstanceId,
        JsonSerializerOptions jsonOptions)
    {
        var availableRecords = records ?? Array.Empty<InstanceDiscoveryRecord>();

        if (target != null && !string.IsNullOrWhiteSpace(target.InstanceId))
            return ResolveByInstanceId(availableRecords, target.InstanceId);

        if (target != null && !string.IsNullOrWhiteSpace(target.NavisworksVersion))
            return ResolveByVersion(availableRecords, target.NavisworksVersion, jsonOptions);

        if (!string.IsNullOrWhiteSpace(configuredInstanceId))
            return ResolveByInstanceId(availableRecords, configuredInstanceId);

        if (availableRecords.Count == 0)
            throw new HostCallException(ErrorCodes.InstanceNotFound, "No Navisworks MCP host is running.");

        if (availableRecords.Count == 1)
            return availableRecords[0];

        throw new HostCallException(ErrorCodes.MultipleHostsDetected, SerializeHostDetails(availableRecords, jsonOptions));
    }

    private static InstanceDiscoveryRecord ResolveByInstanceId(IReadOnlyList<InstanceDiscoveryRecord> records, string instanceId)
    {
        var configured = records.FirstOrDefault(record =>
            string.Equals(record.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        if (configured != null)
            return configured;

        throw new HostCallException(ErrorCodes.InstanceNotFound, "Navisworks host instance was not found: " + instanceId);
    }

    private static InstanceDiscoveryRecord ResolveByVersion(IReadOnlyList<InstanceDiscoveryRecord> records, string navisworksVersion, JsonSerializerOptions jsonOptions)
    {
        var matchingRecords = records
            .Where(record => string.Equals(record.NavisworksVersion, navisworksVersion, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matchingRecords.Count == 1)
            return matchingRecords[0];

        if (matchingRecords.Count > 1)
            throw new HostCallException(ErrorCodes.MultipleHostsDetected, SerializeHostDetails(matchingRecords, jsonOptions));

        throw new HostCallException(ErrorCodes.InstanceNotFound, "No Navisworks MCP host is running for version " + navisworksVersion + ".");
    }

    private static string SerializeHostDetails(IEnumerable<InstanceDiscoveryRecord> records, JsonSerializerOptions jsonOptions)
    {
        var details = records.Select(record => new
        {
            instance_id = record.InstanceId,
            pid = record.Pid,
            navisworks_version = record.NavisworksVersion,
            document_title = record.DocumentTitle,
            process_started_at_utc = record.ProcessStartedAtUtc,
            plugin_version = record.PluginVersion,
            plugin_assembly_path = record.PluginAssemblyPath,
            plugin_assembly_last_write_utc = record.PluginAssemblyLastWriteUtc,
            plugin_assembly_length = record.PluginAssemblyLength,
            host_log_file_path = record.HostLogFilePath,
        });

        return JsonSerializer.Serialize(details, jsonOptions);
    }
}
