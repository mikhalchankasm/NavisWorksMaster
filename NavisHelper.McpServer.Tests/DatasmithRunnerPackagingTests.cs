using System.Diagnostics;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #440 slice gamma: the Automation runner ships inside the MCP server's own output, at the
// path DatasmithExportPlan.ResolveRunnerPath resolves, so every package that copies the
// server tree (publish, ZIP, installer, .mcpb, McpConfigurator --install-server) carries it.
public sealed class DatasmithRunnerPackagingTests
{
    [Fact]
    public void TheServerBuildOutputContainsTheRunnerWhereTheServiceLooksForIt()
    {
        var serverOutput = ServerBuildOutput();
        Assert.True(File.Exists(Path.Combine(serverOutput, "NavisHelper.McpServer.dll")),
            "The MCP server is not built at " + serverOutput);

        var runner = DatasmithExportPlan.ResolveRunnerPath(null, serverOutput);

        Assert.Equal(Path.Combine(serverOutput, "AutomationRunner", "NavisHelper.AutomationRunner.exe"), runner);
        Assert.True(File.Exists(runner), "The Automation runner is missing from the server output: " + runner);
    }

    [Fact]
    public void ThePackagedRunnerStartsAndSpeaksTheResultProtocol()
    {
        var runner = DatasmithExportPlan.ResolveRunnerPath(null, ServerBuildOutput());
        Assert.True(File.Exists(runner), "The Automation runner is missing from the server output: " + runner);

        var startInfo = new ProcessStartInfo(runner)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(30_000), "The runner did not exit within 30 s without arguments.");
        process.WaitForExit();

        var outcome = DatasmithExportPlan.ParseRunnerOutput(stdout.Result, process.ExitCode);

        Assert.False(outcome.Ok);
        Assert.Equal(2, outcome.ExitCode);
        Assert.Equal("Expected the datasmith-export command.", outcome.Error);
        Assert.Equal(string.Empty, stderr.Result.Trim());
    }

    [Fact]
    public void TheRunnerShipsWithoutDebugSymbolsOrFrameworkSideFiles()
    {
        var runnerDirectory = Path.Combine(ServerBuildOutput(), "AutomationRunner");
        Assert.True(Directory.Exists(runnerDirectory), "Missing " + runnerDirectory);

        var names = Directory.EnumerateFiles(runnerDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(runnerDirectory, path))
            .ToArray();

        Assert.Contains("NavisHelper.AutomationRunner.exe", names);
        Assert.DoesNotContain(names, name => name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
        // The runner is a net48 executable: a .NET Core host file here would mean the wrong project was copied.
        Assert.DoesNotContain(names, name => name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase));
        // Navisworks supplies its own Automation assembly; the package must never carry one.
        Assert.DoesNotContain(names, name => name.StartsWith("Autodesk.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheDistributionValidatorRequiresTheRunner()
    {
        var validator = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "validate_distribution.ps1"));

        Assert.Contains("\"McpServer/AutomationRunner/NavisHelper.AutomationRunner.exe\"", validator);
    }

    // The test project builds the server project into its own bin folder with the same configuration.
    private static string ServerBuildOutput()
    {
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var targetFramework = testOutput.Name;
        var configuration = testOutput.Parent.Name;
        return Path.Combine(RepositoryRoot(), "NavisHelper.McpServer", "bin", configuration, targetFramework);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }
}
