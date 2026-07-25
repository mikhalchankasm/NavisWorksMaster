using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NavisHelper.McpConfigurator;

internal static partial class Program
{
    private abstract class CliAdapter : IMcpClientAdapter
    {
        protected CliAdapter(string id, string displayName, string executable)
        {
            Id = id;
            DisplayName = displayName;
            Executable = executable;
        }

        public string Id { get; }
        protected string DisplayName { get; }
        protected string Executable { get; }

        public DetectionResult Detect()
        {
            var command = FindExecutable(Executable);
            return command == null
                ? new DetectionResult("missing", DisplayName + " CLI was not found in PATH.")
                : new DetectionResult("found", DisplayName + " CLI found: " + command);
        }

        public ConfigureResult Configure(string mcpServerPath, bool dryRun, bool createMissing)
        {
            var command = FindExecutable(Executable);
            if (command == null)
                return new ConfigureResult("skipped", DisplayName + " CLI was not found.");

            var removeArgs = BuildRemoveArguments().ToList();
            var addArgs = BuildAddArguments(mcpServerPath).ToList();
            if (dryRun)
            {
                return new ConfigureResult(
                    "dry-run",
                    $"Would run: {command} {string.Join(" ", removeArgs.Select(QuoteForDisplay))}; then {command} {string.Join(" ", addArgs.Select(QuoteForDisplay))}");
            }

            var removeResult = RunProcess(command, removeArgs);
            if (removeResult.ExitCode == -1)
                return new ConfigureResult("failed", DisplayName + " CLI remove timed out: " + removeResult.Output);

            var result = RunProcess(command, addArgs);
            return result.ExitCode == 0
                ? new ConfigureResult("configured", DisplayName + " accepted MCP server '" + ServerName + "'.")
                : new ConfigureResult("failed", DisplayName + " CLI failed: " + result.Output);
        }

        public ConfigureResult Remove(bool dryRun)
        {
            var command = FindExecutable(Executable);
            if (command == null)
                return new ConfigureResult("skipped", DisplayName + " CLI was not found.");

            var removeArgs = BuildRemoveArguments().ToList();
            if (dryRun)
                return new ConfigureResult("dry-run", $"Would run: {command} {string.Join(" ", removeArgs.Select(QuoteForDisplay))}");

            var result = RunProcess(command, removeArgs);
            if (result.ExitCode == -1)
                return new ConfigureResult("failed", DisplayName + " CLI remove timed out: " + result.Output);

            return result.ExitCode == 0
                ? new ConfigureResult("removed", DisplayName + " removed MCP server '" + ServerName + "'.")
                : new ConfigureResult("skipped", DisplayName + " CLI did not remove MCP server '" + ServerName + "': " + result.Output);
        }

        protected abstract IEnumerable<string> BuildRemoveArguments();

        protected abstract IEnumerable<string> BuildAddArguments(string mcpServerPath);
    }

    private sealed class ClaudeCodeCliAdapter : CliAdapter
    {
        public ClaudeCodeCliAdapter()
            : base("claude-code", "Claude Code", "claude")
        {
        }

        protected override IEnumerable<string> BuildAddArguments(string mcpServerPath)
        {
            yield return "mcp";
            yield return "add";
            yield return "--scope";
            yield return "user";
            yield return ServerName;
            yield return "--";
            yield return mcpServerPath;
        }

        protected override IEnumerable<string> BuildRemoveArguments()
        {
            yield return "mcp";
            yield return "remove";
            yield return "--scope";
            yield return "user";
            yield return ServerName;
        }
    }

}
