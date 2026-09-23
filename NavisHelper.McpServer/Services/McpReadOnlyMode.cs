using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;

namespace NavisHelper.McpServer.Services;

internal sealed class McpReadOnlyMode
{
    internal const string EnvironmentVariableName = "NAVISHELPER_MCP_READ_ONLY";

    private readonly IReadOnlyDictionary<string, ToolEffects> _effects;

    internal McpReadOnlyMode(bool enabled, IEnumerable<MethodInfo> toolMethods)
    {
        Enabled = enabled;
        var effects = new Dictionary<string, ToolEffects>(StringComparer.Ordinal);
        foreach (var method in toolMethods)
        {
            var attribute = method.GetCustomAttribute<McpServerToolAttribute>(inherit: false);
            var capabilities = method.GetCustomAttribute<ToolCapabilitiesAttribute>(inherit: false)
                ?? throw new InvalidOperationException("Missing tool capabilities: " + method.Name);
            var name = string.IsNullOrWhiteSpace(attribute.Name) ? ToSnakeCase(method.Name) : attribute.Name;
            if (!effects.TryAdd(name, capabilities.Effects))
                throw new InvalidOperationException("Duplicate MCP tool name: " + name);
        }
        _effects = effects;
    }

    internal bool Enabled { get; }

    internal static bool Parse(string[] args, string environmentValue) =>
        (args ?? Array.Empty<string>()).Contains("--read-only", StringComparer.Ordinal) ||
        string.Equals(environmentValue, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environmentValue, "true", StringComparison.OrdinalIgnoreCase);

    internal static IEnumerable<MethodInfo> RegisteredToolMethods() =>
        typeof(NavisworksHostTools).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.IsDefined(typeof(McpServerToolAttribute), inherit: false));

    internal void VerifyRegisteredTools(IEnumerable<McpServerTool> tools)
    {
        var registered = tools.Select(tool => tool.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal);
        if (!_effects.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(registered))
            throw new InvalidOperationException("MCP tool capability names differ from registered tools.");
    }

    internal bool IsAllowed(string name) => !Enabled || (_effects.TryGetValue(name, out var effects) && effects == ToolEffects.None);

    public McpRequestHandler<ListToolsRequestParams, ListToolsResult> FilterList(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
    {
        return async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken).ConfigureAwait(false);
            if (Enabled)
                result.Tools = result.Tools.Where(tool => IsAllowed(tool.Name)).ToList();
            return result;
        };
    }

    public McpRequestHandler<CallToolRequestParams, CallToolResult> FilterCall(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        return (context, cancellationToken) =>
        {
            var name = context?.Params?.Name ?? string.Empty;
            if (IsAllowed(name) || !_effects.ContainsKey(name))
                return next(context, cancellationToken);
            return ValueTask.FromResult(Refuse(name));
        };
    }

    internal static CallToolResult Refuse(string name) => new()
    {
        IsError = true,
        Content = new List<ContentBlock>
        {
            new TextContentBlock { Text = ErrorCodes.ReadOnlyMode + ": Tool '" + name + "' was refused because the server was started read-only." },
        },
    };

    private static string ToSnakeCase(string name)
    {
        var separated = Regex.Replace(name, "(.)([A-Z][a-z]+)", "$1_$2");
        return Regex.Replace(separated, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
    }
}
