// Infrastructure/GameServerWebSocketEndpoint.cs
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Helrift.Gate.Infrastructure;

public static class GameServerWebSocketEndpoint
{
    public static IApplicationBuilder MapGameServerWebSockets(this IApplicationBuilder app, string path = "/ws/gameservers")
    {
        app.Map(path, builder =>
        {
            builder.Run(async ctx =>
            {
                var registry = ctx.RequestServices.GetRequiredService<IGameServerConnectionRegistry>();
                var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GameServerWs");
                var config = ctx.RequestServices.GetRequiredService<IConfiguration>();

                if (!ctx.WebSockets.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("WebSocket required");
                    return;
                }

                // simple shared-secret auth for now
                var incomingKey = ctx.Request.Headers["X-GameServer-Key"].ToString();
                var expectedKey = config["ServerApi:Key"];

                if (string.IsNullOrWhiteSpace(expectedKey) || incomingKey != expectedKey)
                {
                    ctx.Response.StatusCode = 401;
                    return;
                }

                var socket = await ctx.WebSockets.AcceptWebSocketAsync();

                // prefer id from query, else random
                var serverId = ctx.Request.Query["id"].ToString();
                if (string.IsNullOrWhiteSpace(serverId))
                    serverId = Guid.NewGuid().ToString("N");

                registry.Add(serverId, socket);
                logger.LogInformation("Game server connected: {ServerId}", serverId);

                var buffer = new byte[1024];

                try
                {
                    while (socket.State == WebSocketState.Open)
                    {
                        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            // client initiated close -> we'll exit loop and close gracefully
                            break;
                        }

                        // handle inbound GS messages here if needed
                    }
                }
                catch (WebSocketException wsex)
                {
                    // this is usually the editor / server dying without close handshake
                    logger.LogDebug(wsex, "WebSocket closed ungracefully from {ServerId}", serverId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected error in WS loop for {ServerId}", serverId);
                }
                finally
                {
                    registry.Remove(serverId);

                    // only attempt graceful close if socket is still in a closable state
                    if (socket.State == WebSocketState.Open ||
                        socket.State == WebSocketState.CloseReceived)
                    {
                        try
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                                "closing", CancellationToken.None);
                        }
                        catch (WebSocketException)
                        {
                            // ignore – at this point we just want to finish
                        }
                    }

                    logger.LogInformation("Game server disconnected: {ServerId}", serverId);
                }
            });
        });

        return app;
    }
}
