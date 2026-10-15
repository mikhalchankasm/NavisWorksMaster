using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NavisHelper.McpConfigurator;

internal static partial class Program
{
    private const string ServerName = "navishelper";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var doctorRequested = args.Contains("--doctor", StringComparer.OrdinalIgnoreCase);
        var jsonRequested = args.Contains("--json", StringComparer.OrdinalIgnoreCase);

        try
        {
            if (args.Any(arg => arg.Equals("--install-server", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--rollback-server", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--prune-servers", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--install-root", StringComparison.OrdinalIgnoreCase)))
            {
                if (doctorRequested || jsonRequested)
                    return DoctorCommand.Usage(jsonRequested, Console.Out);
                return RunServerCommand(args);
            }
            Options options;
            try { options = Options.Parse(args); }
            catch when (doctorRequested || jsonRequested)
            {
                return DoctorCommand.Usage(jsonRequested, Console.Out);
            }
            if ((doctorRequested || jsonRequested) &&
                (doctorRequested != options.Doctor || jsonRequested != options.Json || options.ShowHelp))
                return DoctorCommand.Usage(jsonRequested, Console.Out);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            if (options.Doctor || options.Json)
            {
                if (!options.IsDoctorOnly)
                    return DoctorCommand.Usage(options.Json, Console.Out);
                var target = ResolveDoctorTarget(options.Clients);
                if (target == null)
                    return DoctorCommand.Usage(options.Json, Console.Out);
                return DoctorCommand.Run(target.Value.Id, target.Value.ConfigPath,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Autodesk", "ApplicationPlugins", "NavisHelper.bundle"),
                    options.Json, Console.Out);
            }

            if (options.Configure && options.Remove)
                throw new InvalidOperationException("Use either --configure or --remove, not both.");

            var mcpServerPath = options.Remove ? string.Empty : ResolveMcpServerPath(options.McpServerPath);
            var adapters = BuildAdapters();
            var selected = SelectAdapters(adapters, options.Clients);

            var hadErrors = false;

            if (options.Detect || (!options.Configure && !options.Remove))
            {
                foreach (var adapter in selected)
                    hadErrors |= !PrintDetection(adapter, mcpServerPath);
            }

            if (options.Configure)
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var extensionsRoot = Path.Combine(appData, "Claude", "Claude Extensions");
                foreach (var adapter in selected)
                {
                    try
                    {
                        if (adapter.Id == "claude-desktop" && ClaudeDesktopStaleEntry.ExtensionInstalled(extensionsRoot))
                        {
                            Console.WriteLine("claude-desktop: skipped - Claude Desktop uses the NavisHelper .mcpb extension.");
                            continue;
                        }
                        hadErrors |= !Configure(adapter, mcpServerPath, options.DryRun, options.CreateMissing);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"{adapter.Id}: failed - {ex.Message}");
                        hadErrors = true;
                    }
                }

                try
                {
                    var result = ClaudeDesktopStaleEntry.RemoveIfExtensionInstalled(
                        Path.Combine(appData, "Claude", "claude_desktop_config.json"), extensionsRoot, options.DryRun);
                    if (result.Status is "removed" or "dry-run")
                    {
                        Console.WriteLine($"claude-desktop: {result.Status} - {result.Detail}");
                        if (result.BackupPath != null)
                            Console.WriteLine($"  backup: {result.BackupPath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"claude-desktop: failed - {ex.Message}");
                    hadErrors = true;
                }
            }

            if (options.Remove)
            {
                foreach (var adapter in selected)
                    hadErrors |= !Remove(adapter, options.DryRun);
            }

            return hadErrors ? 1 : 0;
        }
        catch (Exception ex)
        {
            if (doctorRequested || jsonRequested)
                return DoctorCommand.Failed(jsonRequested, Console.Out);
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static int RunServerCommand(string[] args)
    {
        string? command = null;
        string? source = null;
        string? root = null;
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index].ToLowerInvariant();
            if (arg == "--install-root")
            {
                if (root != null) throw new ArgumentException("Duplicate --install-root.");
                root = Value();
            }
            else if (arg is "--install-server" or "--rollback-server" or "--prune-servers")
            {
                if (command != null) throw new ArgumentException("Use one server operation per invocation.");
                command = arg;
                if (arg == "--install-server") source = Value();
            }
            else throw new ArgumentException("Unsupported server option: " + args[index]);

            string Value()
            {
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--"))
                    throw new ArgumentException("Missing value for " + arg);
                return args[index];
            }
        }
        if (command == null) throw new ArgumentException("A server operation is required.");
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NavisHelper");
        var store = new ServerVersionStore(root, path =>
            McpServerHealthCheck.RunAsync(path, TimeSpan.FromSeconds(30)).GetAwaiter().GetResult());
        if (command == "--install-server")
        {
            var result = store.Install(source!);
            Console.WriteLine(JsonSerializer.Serialize(result));
            return result.Activated ? 0 : 1;
        }
        if (command == "--rollback-server")
        {
            var activated = store.Rollback();
            Console.WriteLine(JsonSerializer.Serialize(new { Activated = activated }));
            return activated ? 0 : 1;
        }
        var removed = store.Prune(ServerVersionInUse);
        foreach (var directory in removed)
            Console.WriteLine(JsonSerializer.Serialize(new { Pruned = directory }));
        if (removed.Count == 0) Console.WriteLine("{\"Pruned\":null}");
        return 0;
    }

    private static bool ServerVersionInUse(string directory)
    {
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var executable = process.MainModule?.FileName;
                    if (executable != null && Path.GetFullPath(executable).StartsWith(
                        Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)) return true;
                    // A stable-junction launch cannot be attributed to the loaded version after a switch.
                    if (executable != null && Path.GetFileName(executable).Equals("NavisHelper.McpServer.exe",
                        StringComparison.OrdinalIgnoreCase) && new DirectoryInfo(Path.GetDirectoryName(executable)!).LinkTarget != null)
                        return true;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    try
                    {
                        if (process.ProcessName.Equals("NavisHelper.McpServer", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch (InvalidOperationException) { /* The process exited during enumeration. */ }
                }
            }
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    internal static (string Id, string? ConfigPath)? ResolveDoctorTarget(string client)
    {
        var adapter = BuildAdapters().SingleOrDefault(candidate =>
            candidate.Id.Equals(client.Trim(), StringComparison.OrdinalIgnoreCase));
        return adapter == null ? null : (adapter.Id, (adapter as FileAdapter)?.ReadOnlyConfigPath);
    }

    private static IReadOnlyList<IMcpClientAdapter> BuildAdapters()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return new IMcpClientAdapter[]
        {
            new GenericMcpServersJsonAdapter(
                "claude-desktop",
                "Claude Desktop",
                Path.Combine(appData, "Claude", "claude_desktop_config.json"),
                Path.Combine(appData, "Claude")),
            new ClaudeCodeCliAdapter(),
            new CodexTomlAdapter(Path.Combine(userProfile, ".codex", "config.toml"), Path.Combine(userProfile, ".codex")),
            new GenericMcpServersJsonAdapter(
                "cursor",
                "Cursor",
                Path.Combine(userProfile, ".cursor", "mcp.json"),
                Path.Combine(userProfile, ".cursor")),
            new OpenCodeJsonAdapter(Path.Combine(appData, "OpenCode", "opencode.json"), Path.Combine(appData, "OpenCode")),
            new GenericMcpServersJsonAdapter(
                "kimi",
                "Kimi Code",
                Path.Combine(userProfile, ".kimi-code", "mcp.json"),
                Path.Combine(userProfile, ".kimi-code")),
            new ZCodeCliJsonAdapter(
                "zcode",
                "ZCode",
                Path.Combine(userProfile, ".zcode", "cli", "config.json"),
                Path.Combine(userProfile, ".zcode")),
        };
    }

    private static IReadOnlyList<IMcpClientAdapter> SelectAdapters(IReadOnlyList<IMcpClientAdapter> adapters, string clients)
    {
        if (string.IsNullOrWhiteSpace(clients) || string.Equals(clients, "all", StringComparison.OrdinalIgnoreCase))
            return adapters;

        var requested = clients
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selected = adapters
            .Where(adapter => requested.Contains(adapter.Id))
            .ToList();

        var unknown = requested
            .Where(id => adapters.All(adapter => !string.Equals(adapter.Id, id, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException("Unknown client id(s): " + string.Join(", ", unknown));

        return selected;
    }

    private static string ResolveMcpServerPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var resolved = Path.GetFullPath(explicitPath);
            if (!File.Exists(resolved))
                throw new FileNotFoundException("MCP server executable was not found.", resolved);
            return resolved;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var local = Path.Combine(baseDirectory, "NavisHelper.McpServer.exe");
        if (File.Exists(local))
            return local;

        var sibling = Path.Combine(baseDirectory, "McpServer", "NavisHelper.McpServer.exe");
        if (File.Exists(sibling))
            return sibling;

        var stable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NavisHelper", "McpServer", "NavisHelper.McpServer.exe");
        if (File.Exists(stable))
            return stable;

        var localVersioned = FindLatestVersionedServer(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NavisHelper"));
        if (localVersioned != null)
            return localVersioned;

        var packageSibling = Path.GetFullPath(Path.Combine(baseDirectory, "..", "McpServer", "NavisHelper.McpServer.exe"));
        if (File.Exists(packageSibling))
            return packageSibling;

        throw new FileNotFoundException("MCP server executable was not found in the per-user NavisHelper installation. Pass --mcp-server with an explicit path.");
    }

    private static string? FindLatestVersionedServer(string installRoot)
    {
        if (!Directory.Exists(installRoot))
            return null;

        return Directory
            .EnumerateDirectories(installRoot, "McpServer-*")
            .Select(directory => new
            {
                Directory = directory,
                Version = TryParseDirectoryVersion(Path.GetFileName(directory)),
            })
            .Where(candidate => candidate.Version != null)
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => Path.Combine(candidate.Directory, "NavisHelper.McpServer.exe"))
            .FirstOrDefault(File.Exists);
    }

    private static Version? TryParseDirectoryVersion(string directoryName)
    {
        const string prefix = "McpServer-";
        if (string.IsNullOrWhiteSpace(directoryName) ||
            !directoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Version.TryParse(directoryName.Substring(prefix.Length), out var version)
            ? version
            : null;
    }

    private static bool PrintDetection(IMcpClientAdapter adapter, string mcpServerPath)
    {
        try
        {
            var detection = adapter.Detect();
            Console.WriteLine($"{adapter.Id}: {detection.Status} - {detection.Detail}");
            if (!string.IsNullOrWhiteSpace(detection.ConfigPath))
                Console.WriteLine($"  config: {detection.ConfigPath}");
            if (!string.IsNullOrWhiteSpace(mcpServerPath))
                Console.WriteLine($"  server: {mcpServerPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{adapter.Id}: failed - {ex.Message}");
            return false;
        }
    }

    private static bool Configure(IMcpClientAdapter adapter, string mcpServerPath, bool dryRun, bool createMissing)
    {
        try
        {
            var result = adapter.Configure(mcpServerPath, dryRun, createMissing);
            var isFailure = string.Equals(result.Status, "failed", StringComparison.OrdinalIgnoreCase);
            var output = isFailure ? Console.Error : Console.Out;
            output.WriteLine($"{adapter.Id}: {result.Status} - {result.Detail}");
            if (!string.IsNullOrWhiteSpace(result.BackupPath))
                output.WriteLine($"  backup: {result.BackupPath}");
            return !isFailure;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{adapter.Id}: failed - {ex.Message}");
            return false;
        }
    }

    private static bool Remove(IMcpClientAdapter adapter, bool dryRun)
    {
        try
        {
            var result = adapter.Remove(dryRun);
            var isFailure = string.Equals(result.Status, "failed", StringComparison.OrdinalIgnoreCase);
            var output = isFailure ? Console.Error : Console.Out;
            output.WriteLine($"{adapter.Id}: {result.Status} - {result.Detail}");
            if (!string.IsNullOrWhiteSpace(result.BackupPath))
                output.WriteLine($"  backup: {result.BackupPath}");
            return !isFailure;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{adapter.Id}: failed - {ex.Message}");
            return false;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("NavisHelper MCP Configurator");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  NavisHelper.McpConfigurator.exe --detect");
        Console.WriteLine(DoctorCommand.Text("Help"));
        Console.WriteLine("  NavisHelper.McpConfigurator.exe --configure --clients all --mcp-server \"%LOCALAPPDATA%\\NavisHelper\\McpServer-<version>\\NavisHelper.McpServer.exe\"");
        Console.WriteLine("  NavisHelper.McpConfigurator.exe --configure --clients all --create-missing --mcp-server \"%LOCALAPPDATA%\\NavisHelper\\McpServer-<version>\\NavisHelper.McpServer.exe\"");
        Console.WriteLine("  NavisHelper.McpConfigurator.exe --configure --clients claude-desktop,cursor,opencode --dry-run");
        Console.WriteLine("  NavisHelper.McpConfigurator.exe --remove --clients all");
        Console.WriteLine("  NavisHelper.McpConfigurator.exe --install-server <sourceDir> [--install-root <dir>]");
        Console.WriteLine("  NavisHelper.McpConfigurator.exe --rollback-server [--install-root <dir>]");
        Console.WriteLine("  NavisHelper.McpConfigurator.exe --prune-servers [--install-root <dir>]");
        Console.WriteLine();
        Console.WriteLine("Client ids:");
        Console.WriteLine("  claude-desktop, claude-code, codex, cursor, opencode, kimi, zcode");
    }
















































}
