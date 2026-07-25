using Microsoft.Win32;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed class NavisworksRecentFilesService
{
    private static readonly IReadOnlyDictionary<string, string> NavisworksToRegistryVersion =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["2024"] = "21.0",
            ["2025"] = "22.0",
            ["2026"] = "23.0",
            ["2027"] = "24.0",
        };

    private static readonly IReadOnlyDictionary<string, string> RegistryToNavisworksVersion =
        NavisworksToRegistryVersion.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public NavisworksRecentFilesResponse ListRecentFiles(string navisworksVersion, int limit, bool existingOnly)
    {
        if (limit < 1)
            limit = 1;
        if (limit > 100)
            limit = 100;

        var response = new NavisworksRecentFilesResponse
        {
            RequestedNavisworksVersion = navisworksVersion ?? string.Empty,
        };

        if (!OperatingSystem.IsWindows())
        {
            response.Warnings.Add("Navisworks recent files are read from HKCU registry and are available only on Windows.");
            return response;
        }

        var registryVersions = ResolveRegistryVersions(navisworksVersion, response.Warnings);
        var files = new List<NavisworksRecentFileInfo>();
        using var baseKey = Registry.CurrentUser.OpenSubKey(@"Software\Autodesk\Navisworks Manage");
        if (baseKey == null)
        {
            response.Warnings.Add(@"HKCU\Software\Autodesk\Navisworks Manage was not found.");
            return response;
        }

        foreach (var registryVersion in registryVersions)
        {
            using var recentRoot = baseKey.OpenSubKey(registryVersion + @"\Recent File List");
            if (recentRoot == null)
                continue;

            foreach (var slotName in recentRoot.GetSubKeyNames())
            {
                if (!int.TryParse(slotName, out var slot))
                    continue;

                using var slotKey = recentRoot.OpenSubKey(slotName);
                if (slotKey == null)
                    continue;

                var path = (slotKey.GetValue("Path") as string ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var displayName = (slotKey.GetValue("DisplayName") as string ?? string.Empty).Trim();
                var exists = File.Exists(path);
                if (existingOnly && !exists)
                    continue;

                var lastOpenedUtc = ReadFileTime(slotKey.GetValue("LastOpened"));
                files.Add(new NavisworksRecentFileInfo
                {
                    NavisworksVersion = RegistryToNavisworksVersion.TryGetValue(registryVersion, out var version) ? version : string.Empty,
                    RegistryVersion = registryVersion,
                    Slot = slot,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(path) : displayName,
                    Path = path,
                    LastOpenedUtc = lastOpenedUtc,
                    LastOpenedLocal = lastOpenedUtc.HasValue ? lastOpenedUtc.Value.ToLocalTime() : null,
                    Exists = exists,
                });
            }
        }

        response.Files = files
            .OrderByDescending(file => file.LastOpenedUtc ?? DateTime.MinValue)
            .ThenBy(file => file.NavisworksVersion)
            .ThenBy(file => file.Slot)
            .Take(limit)
            .ToList();
        response.ReturnedFileCount = response.Files.Count;
        return response;
    }

    public NavisworksRecentFileInfo GetLatestRecentFile(string navisworksVersion, bool existingOnly, out List<string> warnings)
    {
        var response = ListRecentFiles(navisworksVersion, 1, existingOnly);
        warnings = response.Warnings;
        return response.Files.FirstOrDefault();
    }

    public static string ResolveRegistryVersion(string navisworksVersion)
    {
        if (string.IsNullOrWhiteSpace(navisworksVersion))
            return string.Empty;

        return NavisworksToRegistryVersion.TryGetValue(navisworksVersion.Trim(), out var registryVersion)
            ? registryVersion
            : string.Empty;
    }

    private static List<string> ResolveRegistryVersions(string navisworksVersion, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(navisworksVersion))
            return NavisworksToRegistryVersion.Values.ToList();

        var version = navisworksVersion.Trim();
        if (NavisworksToRegistryVersion.TryGetValue(version, out var registryVersion))
            return new List<string> { registryVersion };

        warnings.Add("Unsupported Navisworks version '" + navisworksVersion + "'. Expected one of 2024, 2025, 2026, 2027.");
        return new List<string>();
    }

    private static DateTime? ReadFileTime(object value)
    {
        try
        {
            if (value == null)
                return null;

            if (value is long longValue)
                return DateTime.FromFileTimeUtc(longValue);

            if (value is int intValue)
                return DateTime.FromFileTimeUtc(intValue);

            if (long.TryParse(Convert.ToString(value), out var parsed))
                return DateTime.FromFileTimeUtc(parsed);
        }
        catch
        {
        }

        return null;
    }
}
