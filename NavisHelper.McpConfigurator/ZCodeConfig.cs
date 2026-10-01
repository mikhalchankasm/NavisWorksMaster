using System.Text.Json.Nodes;

namespace NavisHelper.McpConfigurator;

internal static class ZCodeConfig
{
    private const string ServerName = "navishelper";

    internal static bool SetServer(JsonObject root, string mcpServerPath)
    {
        var before = root.ToJsonString();
        var servers = EnsureObject(EnsureObject(root, "mcp"), "servers");
        var server = EnsureObject(servers, ServerName);
        server["command"] = mcpServerPath;
        server["args"] = new JsonArray();
        return root.ToJsonString() != before;
    }

    internal static bool RemoveServer(JsonObject root)
    {
        var servers = (root["mcp"] as JsonObject)?["servers"] as JsonObject;
        return servers != null && servers.Remove(ServerName);
    }

    private static JsonObject EnsureObject(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonObject existing)
            return existing;

        var created = new JsonObject();
        parent[propertyName] = created;
        return created;
    }
}
