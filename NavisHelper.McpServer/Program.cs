using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<McpCallLogger>();
builder.Services.AddSingleton<HostBridgeClient>();
builder.Services.AddSingleton<NavisworksRecentFilesService>();
builder.Services.AddSingleton<NavisworksLaunchService>();
builder.Services.AddSingleton<McpTaskTimerService>();
builder.Services.AddSingleton<ScenarioLibraryService>();
builder.Services.AddSingleton<NavisworksToolContext>();
builder.Services
    .AddMcpServer()
    .WithRequestFilters(filters =>
    {
        filters.AddCallToolFilter(McpToolArgumentValidationFilter.Create);
        filters.AddCallToolFilter(McpToolTimingFilter.Create);
    })
    .WithStdioServerTransport()
    .WithTools<NavisworksTools>()
    .WithTools<NavisworksStartupTools>()
    .WithTools<NavisworksSelectionReportTools>()
    .WithTools<NavisworksModelColorSchemeTools>()
    .WithTools<NavisworksScenarioWorkflowTools>()
    .WithTools<NavisworksClashTools>()
    .WithTools<NavisworksClashIsolationTools>()
    .WithTools<NavisworksClashRootMatrixTools>()
    .WithTools<NavisworksClashSetTools>()
    .WithTools<NavisworksClashTransferTools>()
    .WithTools<NavisworksScenarioTools>();

var host = builder.Build();
var resolvedTools = host.Services.GetServices<McpServerTool>().ToList();
var normalization = McpToolSchemaCompatibility.Normalize(resolvedTools);
McpToolArgumentValidationFilter.Initialize(resolvedTools);
host.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("McpToolSchemaCompatibility")
    .LogInformation(
        "Normalized {RewriteCount} JSON Schema nodes across {ToolCount} MCP tools.",
        normalization.RewriteCount,
        normalization.ToolCount);
await host.RunAsync();
