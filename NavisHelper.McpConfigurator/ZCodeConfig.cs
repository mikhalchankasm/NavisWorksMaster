using System.Text.Json.Nodes;

namespace NavisHelper.McpConfigurator;

internal static class ZCodeConfig
{
    private const string ServerName = "navishelper";

    internal static bool SetServer(JsonObject root, string mcpServerPath)
    {
        var before = root.ToJsonString();
        var servers = Program.EnsureObject(Program.EnsureObject(root, "mcp"), "servers");
        var server = Program.EnsureObject(servers, ServerName);
        server["command"] = mcpServerPath;
        server["args"] = new JsonArray();
        return root.ToJsonString() != before;
    }

    internal static bool RemoveServer(JsonObject root)
    {
        var servers = (root["mcp"] as JsonObject)?["servers"] as JsonObject;
        return servers != null && servers.Remove(ServerName);
    }

}
