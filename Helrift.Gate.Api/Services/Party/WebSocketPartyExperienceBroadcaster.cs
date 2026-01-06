// Services/WebSocketPartyExperienceBroadcaster.cs
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Helrift.Gate.Contracts;
using Helrift.Gate.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Helrift.Gate.Api.Services
{

    public sealed class WebSocketPartyExperienceBroadcaster(
    IGameServerConnectionRegistry registry,
    ILogger<WebSocketPartyExperienceBroadcaster> logger)
    : IPartyExperienceBroadcaster
    {
        public async Task BroadcastAsync(string serverId, PartyExperiencePayloadDto payload, CancellationToken ct)
        {
            var sockets = registry.GetAll();

            if (!sockets.TryGetValue(serverId, out var ws))
            {
                logger.LogDebug("No websocket for GameServer {ServerId} when sending party.experience", serverId);
                return;
            }

            if (ws.State != WebSocketState.Open)
            {
                logger.LogDebug("Websocket for GameServer {ServerId} not open when sending party.experience", serverId);
                return;
            }

            var envelope = new
            {
                type = "party.experience",
                payload
            };

            var json = JsonSerializer.Serialize(envelope);
            var bytes = Encoding.UTF8.GetBytes(json);

            try
            {
                await ws.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken: ct);

                logger.LogInformation(
                    "Sent party.experience Party {PartyId} to {ServerId} (recipients: {Count}, deltas: {Deltas})",
                    payload.PartyId, serverId, payload.Recipients?.Length ?? 0, payload.Deltas?.Count ?? 0);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send party.experience for Party {PartyId} to {ServerId}", payload.PartyId, serverId);
            }
        }
    }
}