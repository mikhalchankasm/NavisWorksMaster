using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Host.CreateApplicationBuilder adds an EventLog provider on Windows, so every
// tool failure the MCP SDK logs at Error landed in the Windows Application log.
// The server logs to stderr only; stdout belongs to the MCP protocol.
public sealed class McpServerLoggingTests
{
    [Fact]
    public void ServerLogsToStandardErrorOnlyAndNeverToTheWindowsEventLog()
    {
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        McpServerLogging.Configure(builder.Logging);
        using var host = builder.Build();

        var providers = host.Services.GetServices<ILoggerProvider>().Select(provider => provider.GetType().Name).ToList();
        Assert.DoesNotContain(providers, name => name.Contains("EventLog", StringComparison.Ordinal));
        Assert.Contains(providers, name => name.Contains("Console", StringComparison.Ordinal));

        var console = host.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;
        Assert.Equal(LogLevel.Trace, console.LogToStandardErrorThreshold);
    }
}
