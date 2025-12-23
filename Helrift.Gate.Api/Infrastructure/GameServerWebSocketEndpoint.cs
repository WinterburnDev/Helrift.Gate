// Infrastructure/GameServerWebSocketEndpoint.cs
using Helrift.Gate.Contracts.Realm;
using Helrift.Gate.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Text;

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
                var realmService = ctx.RequestServices.GetRequiredService<IRealmService>();

                var ct = ctx.RequestAborted;

                if (!ctx.WebSockets.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await ctx.Response.WriteAsync("WebSocket required", ct);
                    return;
                }

                // simple shared-secret auth for now
                var incomingKey = ctx.Request.Headers["X-GameServer-Key"].ToString();
                var expectedKey = config["ServerApi:Key"];

                if (string.IsNullOrWhiteSpace(expectedKey) || incomingKey != expectedKey)
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                using var socket = await ctx.WebSockets.AcceptWebSocketAsync();

                // prefer id from query, else random
                var serverId = ctx.Request.Query["id"].ToString();
                if (string.IsNullOrWhiteSpace(serverId))
                    serverId = Guid.NewGuid().ToString("N");

                registry.Add(serverId, socket);
                logger.LogInformation("Game server connected: {ServerId}", serverId);

                // Immediately push current realm state to the newly connected GS
                await TryPushRealmSnapshotAsync(socket, serverId, realmService, logger, ct);

                var buffer = new byte[1024];

                try
                {
                    while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        WebSocketReceiveResult result;
                        try
                        {
                            result = await socket.ReceiveAsync(buffer, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            // client initiated close -> we'll exit loop and close gracefully
                            break;
                        }

                        // Drain fragments (we currently ignore inbound GS messages)
                        while (!result.EndOfMessage && socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                        {
                            result = await socket.ReceiveAsync(buffer, ct);
                        }

                        // handle inbound GS messages here if needed (currently unused)
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
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", ct);
                        }
                        catch (WebSocketException)
                        {
                            // ignore – at this point we just want to finish
                        }
                        catch (OperationCanceledException)
                        {
                            // ignore
                        }
                    }

                    logger.LogInformation("Game server disconnected: {ServerId}", serverId);
                }
            });
        });

        return app;
    }

    private static async Task TryPushRealmSnapshotAsync(
        WebSocket socket,
        string serverId,
        IRealmService realmService,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var state = realmService.GetState();

            var payload = new RealmStateDto
            {
                denyNewLogins = state.DenyNewLogins,
                denyNewJoins = state.DenyNewJoins,
                shutdownAtUnixUtc = state.ShutdownAtUtc?.ToUnixTimeSeconds(),
                realmMessage = state.ShutdownAtUtc.HasValue
                    ? "Server restart scheduled"
                    : (state.DenyNewLogins ? "Server in maintenance mode" : null)
            };

            var envelope = new
            {
                type = "realm.state.updated",
                payload
            };

            var json = JsonConvert.SerializeObject(envelope);
            var bytes = Encoding.UTF8.GetBytes(json);

            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                logger.LogInformation("Pushed realm state snapshot to GS {ServerId}", serverId);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push realm state snapshot to GS {ServerId}", serverId);
        }
    }
}
