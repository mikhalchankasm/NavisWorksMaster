using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NavisHelper.McpConfigurator;

internal static partial class Program
{
    private static string? FindExecutable(string name)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathExt = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.PS1")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);

        var candidates = new List<string>();
        if (Path.HasExtension(name))
        {
            candidates.Add(name);
        }
        else
        {
            candidates.Add(name);
            candidates.AddRange(pathExt.Select(ext => name + ext));
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(directory.Trim(), candidate);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        return null;
    }

    private static ProcessResult RunProcess(string fileName, IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start " + fileName);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort cleanup after a stuck third-party CLI.
            }

            return new ProcessResult(-1, "Process timed out after 60000 ms.");
        }

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        return new ProcessResult(process.ExitCode, (output + Environment.NewLine + error).Trim());
    }

    private static string QuoteForDisplay(string value)
    {
        return value.Contains(' ') ? "\"" + value + "\"" : value;
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
