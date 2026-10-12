namespace NavisHelper.McpServer.Services;

internal static class McpSessionIdentity
{
    internal static string Id { get; } = Guid.NewGuid().ToString("D");
}
