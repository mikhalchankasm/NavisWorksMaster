using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace NavisHelper.McpServer.Services;

internal static class McpClientIdentityFilter
{
    public static McpMessageHandler Create(McpMessageHandler next)
    {
        return async (context, cancellationToken) =>
        {
            // Handshake-free clients identify themselves in per-request metadata.
            // Legacy clients still become connected on notifications/initialized.
            var client = context.JsonRpcMessage is JsonRpcRequest
                ? context.JsonRpcMessage.Context?.ClientInfo
                : context.JsonRpcMessage is JsonRpcNotification notification &&
                    notification.Method == NotificationMethods.InitializedNotification
                    ? context.Server.ClientInfo
                    : null;
            var logger = client is null ? null : context.Services.GetRequiredService<McpCallLogger>();
            if (logger != null && logger.TrySetClientOnce(client!.Name, client.Version))
            {
                logger.Log(new
                {
                    event_name = "client_connected",
                    timestamp_utc = DateTime.UtcNow,
                    server_version = context.Server.ServerOptions.ServerInfo?.Version,
                    protocol_version = context.JsonRpcMessage.Context?.ProtocolVersion
                        ?? context.Server.NegotiatedProtocolVersion,
                });
            }

            await next(context, cancellationToken);
        };
    }
}
