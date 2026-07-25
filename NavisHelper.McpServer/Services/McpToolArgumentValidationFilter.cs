using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace NavisHelper.McpServer.Services;

internal static class McpToolArgumentValidationFilter
{
    private static IReadOnlyDictionary<string, HashSet<string>> _allowedByTool =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

    internal static void Initialize(IEnumerable<McpServerTool> tools)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            if (tool.ProtocolTool.InputSchema.TryGetProperty("properties", out var properties))
            {
                foreach (var property in properties.EnumerateObject())
                    allowed.Add(property.Name);
            }
            map[tool.ProtocolTool.Name] = allowed;
        }
        _allowedByTool = map;
    }

    internal static void InitializeForTests(string toolName, params string[] allowedArguments)
    {
        _allowedByTool = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [toolName] = new HashSet<string>(allowedArguments ?? Array.Empty<string>(), StringComparer.Ordinal),
        };
    }

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        return (context, cancellationToken) =>
        {
            var toolName = context?.Params?.Name ?? string.Empty;
            var argumentNames = context?.Params?.Arguments?.Keys ?? Enumerable.Empty<string>();
            var error = Validate(toolName, argumentNames);
            if (error == null)
                return next(context, cancellationToken);

            return ValueTask.FromResult(new CallToolResult
            {
                IsError = true,
                Content = new List<ContentBlock> { new TextContentBlock { Text = error } },
            });
        };
    }

    internal static string Validate(string toolName, IEnumerable<string> argumentNames)
    {
        if (!_allowedByTool.TryGetValue(toolName ?? string.Empty, out var allowed))
            return null;

        var unknown = (argumentNames ?? Enumerable.Empty<string>())
            .Where(name => !allowed.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        if (unknown.Count == 0)
            return null;

        var details = unknown.Select(name =>
        {
            var suggestion = FindSuggestion(name, allowed);
            return suggestion == null ? "'" + name + "'" : "'" + name + "' (did you mean '" + suggestion + "'?)";
        });
        return "Unknown parameter(s) for tool '" + toolName + "': " + string.Join(", ", details) +
               ". The command was not executed.";
    }

    private static string FindSuggestion(string value, IEnumerable<string> candidates)
    {
        var normalized = value ?? string.Empty;
        if (string.Equals(normalized, "action", StringComparison.OrdinalIgnoreCase) && candidates.Contains("operation", StringComparer.Ordinal))
            return "operation";
        return candidates
            .Select(candidate => new { Candidate = candidate, Distance = EditDistance(normalized, candidate) })
            .Where(item => item.Distance <= Math.Max(2, normalized.Length / 3))
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Candidate, StringComparer.Ordinal)
            .Select(item => item.Candidate)
            .FirstOrDefault();
    }

    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[right.Length];
    }
}
