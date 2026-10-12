using Microsoft.Extensions.Logging;

namespace NavisHelper.McpServer.Services;

internal static class McpServerLogging
{
    internal static void Configure(ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });
    }
}
