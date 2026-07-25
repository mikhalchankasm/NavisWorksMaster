using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NavisHelper.McpConfigurator;

internal static partial class Program
{
    private sealed record Options
    {
        public bool ShowHelp { get; private init; }
        public bool Detect { get; private init; }
        public bool Configure { get; private init; }
        public bool Remove { get; private init; }
        public bool DryRun { get; private init; }
        public bool CreateMissing { get; private init; }
        public string Clients { get; private init; } = "all";
        public string? McpServerPath { get; private init; }

        public static Options Parse(string[] args)
        {
            var options = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase))
                {
                    options = options with { ShowHelp = true };
                }
                else if (string.Equals(arg, "--detect", StringComparison.OrdinalIgnoreCase))
                {
                    options = options with { Detect = true };
                }
                else if (string.Equals(arg, "--configure", StringComparison.OrdinalIgnoreCase))
                {
                    options = options with { Configure = true };
                }
                else if (string.Equals(arg, "--remove", StringComparison.OrdinalIgnoreCase))
                {
                    options = options with { Remove = true };
                }
                else if (string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase))
                {
                    options = options with { DryRun = true };
                }
                else if (string.Equals(arg, "--create-missing", StringComparison.OrdinalIgnoreCase))
                {
                    options = options with { CreateMissing = true };
                }
                else if (string.Equals(arg, "--clients", StringComparison.OrdinalIgnoreCase))
                {
                    options = options with { Clients = ReadValue(args, ref i, arg) };
                }
                else if (string.Equals(arg, "--mcp-server", StringComparison.OrdinalIgnoreCase))
                {
                    options = options with { McpServerPath = ReadValue(args, ref i, arg) };
                }
                else
                {
                    throw new InvalidOperationException("Unknown option: " + arg);
                }
            }

            return options;
        }

        private static string ReadValue(string[] args, ref int index, string optionName)
        {
            if (index + 1 >= args.Length)
                throw new InvalidOperationException("Missing value for " + optionName);
            index++;
            return args[index];
        }
    }
}
