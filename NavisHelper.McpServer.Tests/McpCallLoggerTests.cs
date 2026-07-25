using NavisHelper.McpServer.Services;
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
