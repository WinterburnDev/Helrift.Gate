// Services/WebSocketChatBroadcaster.cs
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Helrift.Gate.Contracts;
using Helrift.Gate.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Helrift.Gate.Services;

public sealed class WebSocketChatBroadcaster(
    IGameServerConnectionRegistry registry,
    ILogger<WebSocketChatBroadcaster> logger)
    : IChatBroadcaster
{
    public async Task BroadcastAsync(ChatBroadcastData data, CancellationToken ct)
    {
        var envelope = new
        {
            type = "chat.broadcast",
            payload = data
        };

        var json = JsonSerializer.Serialize(envelope);
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);

        foreach (var kvp in registry.GetAll())
        {
            var ws = kvp.Value;
            if (ws.State != WebSocketState.Open)
                continue;

            try
            {
                await ws.SendAsync(segment, WebSocketMessageType.Text, true, ct);
                logger.LogInformation($"Sent {data.Message} message to {kvp.Key}");

            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send chat to {ServerId}", kvp.Key);
            }
        }
    }
}
