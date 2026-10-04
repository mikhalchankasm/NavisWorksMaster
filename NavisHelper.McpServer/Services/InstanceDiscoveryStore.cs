using System.Text;
using System.Text.Json;
using NavisHelper.Agent.Contracts;
using NavisHelper.Diagnostics;

namespace NavisHelper.McpServer.Services;

internal static class InstanceDiscoveryStore
{
    internal static List<InstanceDiscoveryRecord> LoadAliveRecords(string directory, JsonSerializerOptions jsonOptions)
    {
        var records = new List<InstanceDiscoveryRecord>();

        foreach (var filePath in Directory.GetFiles(directory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(filePath, Encoding.UTF8);
                var record = JsonSerializer.Deserialize<InstanceDiscoveryRecord>(json, jsonOptions);
                if (record == null)
                    continue;

                if (!IsProcessAlive(record))
                {
                    File.Delete(filePath);
                    continue;
                }

                records.Add(record);
            }
            catch
            {
            }
        }

        return records;
    }

    internal static bool TryDelete(InstanceDiscoveryRecord record, string instancesDirectory)
    {
        if (record == null)
            return false;

        var safeInstanceId = Path.GetFileName(record.InstanceId ?? string.Empty);
        if (string.IsNullOrWhiteSpace(safeInstanceId) ||
            !string.Equals(safeInstanceId, record.InstanceId, StringComparison.Ordinal))
            return false;

        var path = Path.Combine(instancesDirectory, safeInstanceId + ".json");
        try
        {
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcessAlive(InstanceDiscoveryRecord record) =>
        HostProcessProbe.Inspect(record, requireStartTime: false).Status == "alive";
}
