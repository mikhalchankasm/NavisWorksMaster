using System.Globalization;
using System.Resources;
using System.Text.Json;

namespace NavisHelper.McpConfigurator;

internal sealed record DoctorReport(int SchemaVersion, string Client, string ConfigurationStatus,
    string? ServerPath, string LauncherStatus, AssemblyEvidence ServerAssembly,
    Dictionary<string, AssemblyEvidence> Plugins, string VersionAgreement, string Readiness, string[] NextActions)
{
    public string ConfigurationScope => "user_file_only";
    public RuntimeEvidence? Runtime { get; init; }
}

internal static class DoctorCommand
{
    private static readonly ResourceManager Resources = new(
        "NavisHelper.McpConfigurator.Properties.Resources", typeof(DoctorCommand).Assembly);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true
    };
    private static readonly string[] Years = ["2024", "2025", "2026", "2027"];

    internal static string Text(string key, CultureInfo? culture = null) =>
        Resources.GetString(key, culture ?? CultureInfo.CurrentUICulture) ?? key;

    internal static int Run(string client, string? configPath, string bundleRoot, bool json, TextWriter output)
    {
        var binding = configPath == null ? new ClientBinding("unsupported_client") :
            ClientConfigurationReader.Read(client, configPath);
        var report = Inspect(client, binding, bundleRoot, AssemblyMetadata.Read, File.Exists);
        report = report with { Runtime = DoctorRuntimeInspector.Inspect(report.ServerPath, report.ServerAssembly.Version) };
        Write(report, json, output);
        return ExitCode(report);
    }

    internal static DoctorReport Inspect(string client, ClientBinding binding, string bundleRoot,
        Func<string, string, AssemblyEvidence> readAssembly, Func<string, bool> fileExists)
    {
        var server = binding.ServerPath == null ? new AssemblyEvidence("unverified") :
            readAssembly(Path.ChangeExtension(binding.ServerPath, ".dll"), "NavisHelper.McpServer");
        var launcher = binding.ServerPath == null ? "unverified" :
            binding.ServerPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "unverified" :
            fileExists(binding.ServerPath) ? "present" : "missing";
        var plugins = Years.ToDictionary(year => year, year =>
            readAssembly(Path.Combine(bundleRoot, "Contents", year, "NavisHelper.dll"), "NavisHelper"));
        var mismatch = plugins.Values.Append(server).Where(item => item.Status == "readable")
            .Select(item => item.Version).Distinct(StringComparer.Ordinal).Skip(1).Any();
        var agreement = mismatch ? "mismatch" : server.Status == "readable" &&
            plugins.Values.All(plugin => plugin.Status == "readable") ? "consistent" : "unverified";
        var actions = new List<string>();
        if (binding.Status != "configured")
            actions.Add(binding.Status is "no_config" or "not_configured" ? "ConfigureClient" : "InspectClient");
        if (binding.Status == "configured" && (server.Status != "readable" || launcher == "missing") ||
            plugins.Values.Any(plugin => plugin.Status != "readable") || mismatch)
            actions.Add("RepairInstallation");
        actions.Add("VerifyBuild");
        if (binding.ServerPath != null && launcher == "unverified")
            actions.Add("VerifyLauncher");
        actions.Add("ReloadClient");
        actions.Add("HealthCheck");
        return new(1, client, binding.Status, binding.ServerPath, launcher, server, plugins,
            agreement, "unverified", actions.ToArray());
    }

    // Zero means only that the inspected disk versions agree, never live readiness.
    internal static int ExitCode(DoctorReport report) =>
        report.ConfigurationStatus == "configured" && report.LauncherStatus == "present" &&
        report.VersionAgreement == "consistent" ? 0 : 1;

    internal static void Write(DoctorReport report, bool json, TextWriter output)
    {
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
            return;
        }
        output.WriteLine(Text("Title") + ": " + report.Client);
        output.WriteLine(Text("Scope"));
        output.WriteLine(Text("Configuration") + ": " + Text(report.ConfigurationStatus));
        if (report.ServerPath != null)
            output.WriteLine(Text("ServerPath") + ": " + report.ServerPath);
        output.WriteLine(Text("Launcher") + ": " + Text(report.LauncherStatus));
        WriteAssembly(Text("ServerAssembly"), report.ServerAssembly, output);
        foreach (var (year, plugin) in report.Plugins)
            WriteAssembly("Navisworks " + year, plugin, output);
        output.WriteLine(Text("VersionAgreement") + ": " + Text(report.VersionAgreement));
        output.WriteLine(Text("Readiness") + ": " + Text(report.Readiness));
        if (report.Runtime is { } runtime)
        {
            output.WriteLine(Text("RuntimeScope"));
            output.WriteLine(Text("HostObservations") + ": " + Text(runtime.HostScan));
            foreach (var host in runtime.Hosts)
            {
                if (!host.Pid.HasValue)
                {
                    output.WriteLine("  " + Text(host.ProcessIdentity));
                    continue;
                }
                output.WriteLine($"  PID={host.Pid} / {Text(host.ProcessIdentity)} / " +
                    $"Navisworks {host.NavisworksVersion ?? Text("unverified")} / {host.DiscoveryPluginVersion ?? Text("unverified")} / " +
                    $"{Text(host.VersionAgreement)} / UTC={host.StartedAtUtc:O}");
            }
            output.WriteLine(Text("ServerObservations") + ": " + Text(runtime.ServerScan));
            foreach (var process in runtime.Servers)
                output.WriteLine($"  PID={process.Pid} / {Text(process.Observation)} / {process.ExecutablePath}");
        }
        foreach (var action in report.NextActions)
            output.WriteLine("- " + string.Format(CultureInfo.CurrentUICulture, Text(action), report.Client));
    }

    private static void WriteAssembly(string label, AssemblyEvidence evidence, TextWriter output)
    {
        output.WriteLine(label + ": " + Text(evidence.Status) +
            (evidence.Version == null ? "" : " (" + evidence.Version + ")"));
        if (evidence.Sha256 != null)
            output.WriteLine("  SHA256: " + evidence.Sha256);
    }

    internal static int Usage(bool json, TextWriter output)
    {
        output.WriteLine(json ? JsonSerializer.Serialize(new { schemaVersion = 1, status = "usage_error" }) : Text("Usage"));
        return 2;
    }

    internal static int Failed(bool json, TextWriter output)
    {
        output.WriteLine(json ? JsonSerializer.Serialize(new { schemaVersion = 1, status = "read_failed" }) : Text("read_failed"));
        return 1;
    }
}
