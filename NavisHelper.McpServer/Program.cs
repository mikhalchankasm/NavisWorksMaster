using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    .WithTools<NavisworksSectionBoxTools>()
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
var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
loggerFactory
    .CreateLogger("McpToolSchemaCompatibility")
    .LogInformation(
        "Normalized {RewriteCount} JSON Schema nodes across {ToolCount} MCP tools.",
        normalization.RewriteCount,
        normalization.ToolCount);

var toolCollection = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection
    ?? throw new InvalidOperationException("MCP server options do not expose a tool collection.");
var profileLogger = loggerFactory.CreateLogger("McpToolProfile");
var profile = McpToolProfile.Resolve(
    McpToolProfile.ReadSpec(args),
    toolCollection.Select(tool => tool.ProtocolTool?.Name).Where(name => !string.IsNullOrEmpty(name)));
if (profile.IsFullSurface)
{
    profileLogger.LogInformation(
        "Advertising the full surface of {ToolCount} MCP tools. Narrow it with {Variable} or {Prefix}<sets> to cut the per-request manifest.",
        toolCollection.Count,
        McpToolProfile.EnvironmentVariableName,
        McpToolProfile.CommandLinePrefix);
}
else
{
    var advertisedBefore = toolCollection.Count;
    var removed = McpToolProfile.Apply(toolCollection, profile);
    profileLogger.LogInformation(
        "Tool profile '{Profile}' advertises {Advertised} of {Total} MCP tools; {Removed} withheld.",
        profile.Description,
        toolCollection.Count,
        advertisedBefore,
        removed.Count);
}

await host.RunAsync();
