using NavisHelper.McpServer.Services;
using NavisHelper.Agent.Contracts;
using System.Text.Json;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class McpCallLoggerTests : IDisposable
{
    private readonly string _previousLogDir;
    private readonly string _tempDirectory;

    public McpCallLoggerTests()
    {
        _previousLogDir = Environment.GetEnvironmentVariable("NAVISHELPER_MCP_LOG_DIR");
        _tempDirectory = Path.Combine(Path.GetTempPath(), "NavisHelper-McpCallLoggerTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        Environment.SetEnvironmentVariable("NAVISHELPER_MCP_LOG_DIR", _tempDirectory);
    }

    [Fact]
    public void Log_UsesCurrentDatedFileAndDeletesExpiredDatedLogs()
    {
        var expiredDate = DateTime.UtcNow.Date.AddDays(-30).ToString("yyyyMMdd");
        var expiredLogPath = Path.Combine(_tempDirectory, "mcp-calls-" + expiredDate + ".jsonl");
        File.WriteAllText(expiredLogPath, "{}" + Environment.NewLine);

        var logger = new McpCallLogger();
        var expectedCurrentLogPath = Path.Combine(_tempDirectory, "mcp-calls-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".jsonl");

        Assert.Equal(expectedCurrentLogPath, logger.LogFilePath);

        logger.Log(new { event_name = "test" });

        Assert.True(File.Exists(expectedCurrentLogPath));
        Assert.False(File.Exists(expiredLogPath));
    }

    [Theory]
    [InlineData(StartNavisworksOutcomes.HostReady, true, "ok")]
    [InlineData(StartNavisworksOutcomes.ProcessExited, false, "process_exited")]
    [InlineData(StartNavisworksOutcomes.HostTimeout, false, "host_timeout")]
    [InlineData(StartNavisworksOutcomes.ProcessCreated, false, "process_created")]
    public void LogStartNavisworks_UsesOutcomeStatusAndSafeEnvironmentFacts(
        string outcome,
        bool hostReady,
        string expectedStatus)
    {
        var logger = new McpCallLogger();
        logger.LogStartNavisworks(
            new StartNavisworksResponse
            {
                Outcome = outcome,
                HostReady = hostReady,
                ProcessCreated = true,
                ProcessExited = outcome == StartNavisworksOutcomes.ProcessExited,
                ExitCode = outcome == StartNavisworksOutcomes.ProcessExited ? -1 : null,
                ProcessId = 123,
                RoamerPath = @"C:\Program Files\Autodesk\Navisworks Manage 2027\Roamer.exe",
                FilePath = @"D:\Example Project\sample model.nwd",
                WaitedForHost = outcome != StartNavisworksOutcomes.ProcessCreated,
                FailureReason = hostReady ? null : "test failure",
                StartupElapsedMs = 25,
                ElapsedMs = 25,
                ElapsedHuman = "25 ms",
            },
            new WindowsLaunchEnvironmentFacts
            {
                ProcessWindirPresent = false,
                ProcessWindirValid = false,
                WindirSource = "machine",
                ProcessSystemRootPresent = true,
                ProcessSystemRootValid = true,
                SystemRootSource = "process",
                FontsUriValid = true,
                WorkingDirectorySet = true,
            });

        using var document = JsonDocument.Parse(File.ReadLines(logger.LogFilePath).Last());
        var root = document.RootElement;
        Assert.Equal(expectedStatus, root.GetProperty("status").GetString());
        Assert.Equal(outcome, root.GetProperty("outcome").GetString());
        Assert.Equal("sample model.nwd", root.GetProperty("file_name").GetString());
        Assert.False(root.TryGetProperty("file_path", out _));
        Assert.Equal("machine", root.GetProperty("environment").GetProperty("windir_source").GetString());
        Assert.True(root.GetProperty("environment").GetProperty("fonts_uri_valid").GetBoolean());
    }

    [Fact]
    public void LogStartNavisworks_CleanHandoffTimeoutPreservesTimeoutAndExitFacts()
    {
        var logger = new McpCallLogger();
        logger.LogStartNavisworks(
            new StartNavisworksResponse
            {
                Outcome = StartNavisworksOutcomes.HostTimeout,
                ProcessCreated = true,
                ProcessExited = true,
                ExitCode = 0,
                ProcessId = 123,
                WaitedForHost = true,
                FailureReason = "Clean handoff timed out.",
                StartupElapsedMs = 1000,
                ElapsedMs = 1001,
                ElapsedHuman = "1.001 s",
            },
            new WindowsLaunchEnvironmentFacts
            {
                WindirSource = "process",
                SystemRootSource = "process",
                FontsUriValid = true,
                WorkingDirectorySet = true,
            });

        using var document = JsonDocument.Parse(File.ReadLines(logger.LogFilePath).Last());
        var root = document.RootElement;
        Assert.Equal("host_timeout", root.GetProperty("status").GetString());
        Assert.Equal("host_timeout", root.GetProperty("outcome").GetString());
        Assert.True(root.GetProperty("process_exited").GetBoolean());
        Assert.Equal(0, root.GetProperty("exit_code").GetInt32());
        Assert.Equal(1000, root.GetProperty("startup_elapsed_ms").GetInt64());
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("NAVISHELPER_MCP_LOG_DIR", _previousLogDir);
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }
        catch
        {
        }
    }
}
