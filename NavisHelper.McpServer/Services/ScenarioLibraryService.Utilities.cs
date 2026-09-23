using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;

namespace NavisHelper.McpServer.Services;

internal sealed partial class ScenarioLibraryService
{
    private static string SafeMessage(Exception exception)
    {
        return exception is JsonException ? "invalid JSON schema" : exception.Message;
    }

    private static string GetDefaultRootPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("NAVISHELPER_SCENARIO_DIR");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NavisHelper", "Scenarios");
    }
}
