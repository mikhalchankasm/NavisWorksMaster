using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed partial class HostBridgeClient
{
    private static InstanceDiscoveryRecord ResolveTargetHost(HostTargetOptions target = null)
    {
        var directory = GetInstancesDirectory();
        Directory.CreateDirectory(directory);

        var records = InstanceDiscoveryStore.LoadAliveRecords(directory, JsonOptions);

        var configuredInstanceId = Environment.GetEnvironmentVariable("NAVISHELPER_INSTANCE_ID");
        return HostTargetResolver.Resolve(records, target, configuredInstanceId, JsonOptions);
    }

    private static string GetInstancesDirectory()
    {
        var configuredDirectory = Environment.GetEnvironmentVariable("NAVISHELPER_INSTANCES_DIR");
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
            return configuredDirectory;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NavisHelper",
            "Mcp",
            "instances");
    }

    private static string GetRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            throw new HostCallException(ErrorCodes.SchemaViolation, "Missing " + propertyName + ".");

        return property.GetString() ?? string.Empty;
    }

    private static string GetOptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;

        return property.GetString();
    }

    private static void ValidateResponseProtocolVersion(JsonElement root)
    {
        var protocolVersion = GetOptionalString(root, "protocol_version");
        if (string.IsNullOrWhiteSpace(protocolVersion))
            return;

        if (!string.Equals(protocolVersion, ProtocolConstants.CurrentProtocolVersion, StringComparison.Ordinal))
        {
            throw new HostCallException(
                ErrorCodes.SchemaViolation,
                "protocol_version mismatch. Host sent " + protocolVersion + ", MCP server expects " + ProtocolConstants.CurrentProtocolVersion + ".");
        }
    }

    private static void TryDeleteStaleRecord(InstanceDiscoveryRecord record)
    {
        InstanceDiscoveryStore.TryDelete(record, GetInstancesDirectory());
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
        }
    }
}
