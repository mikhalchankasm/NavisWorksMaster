using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace NavisHelper.McpServer.Services;

internal static class McpClientIdentityFilter
{
    public static McpMessageHandler Create(McpMessageHandler next)
    {
        var clientConnected = 0;
        return async (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcNotification notification &&
                notification.Method == NotificationMethods.InitializedNotification &&
                context.Server.ClientInfo is { } client &&
                Interlocked.Exchange(ref clientConnected, 1) == 0)
            {
                var logger = context.Services.GetRequiredService<McpCallLogger>();
                logger.SetClient(client.Name, client.Version);
                logger.Log(new
                {
                    event_name = "client_connected",
                    timestamp_utc = DateTime.UtcNow,
                    server_version = context.Server.ServerOptions.ServerInfo?.Version,
                    protocol_version = context.Server.NegotiatedProtocolVersion,
                });
            }

            await next(context, cancellationToken);
        };
    }
}
