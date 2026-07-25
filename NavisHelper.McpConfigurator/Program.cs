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

        try
        {
            var options = Options.Parse(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
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
                foreach (var adapter in selected)
                    hadErrors |= !Configure(adapter, mcpServerPath, options.DryRun, options.CreateMissing);
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
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
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
        Console.WriteLine("  NavisHelper.McpConfigurator.exe --configure --clients all --mcp-server \"%LOCALAPPDATA%\\NavisHelper\\McpServer-<version>\\NavisHelper.McpServer.exe\"");
        Console.WriteLine("  NavisHelper.McpConfigurator.exe --configure --clients all --create-missing --mcp-server \"%LOCALAPPDATA%\\NavisHelper\\McpServer-<version>\\NavisHelper.McpServer.exe\"");
        Console.WriteLine("  NavisHelper.McpConfigurator.exe --configure --clients claude-desktop,cursor,opencode --dry-run");
        Console.WriteLine("  NavisHelper.McpConfigurator.exe --remove --clients all");
        Console.WriteLine();
        Console.WriteLine("Client ids:");
        Console.WriteLine("  claude-desktop, claude-code, codex, cursor, opencode, kimi");
    }
















































}
