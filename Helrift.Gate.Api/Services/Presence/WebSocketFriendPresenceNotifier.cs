using Helrift.Gate.Api.Services.Friends;
using Helrift.Gate.Contracts;
using Helrift.Gate.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Helrift.Gate.Services;

// This replaces the IClientNotificationRouter-based notifier
public sealed class WebSocketFriendPresenceNotifier
{
    private readonly IPresenceService _presence;
    private readonly IFriendsService _friendsService;
    private readonly IGameServerConnectionRegistry _registry;
    private readonly ILogger<WebSocketFriendPresenceNotifier> _logger;

    public WebSocketFriendPresenceNotifier(
        IPresenceService presence,
        IFriendsService friendsService,
        IGameServerConnectionRegistry registry,
        ILogger<WebSocketFriendPresenceNotifier> logger)
    {
        _presence = presence;
        _friendsService = friendsService;
        _registry = registry;
        _logger = logger;

        // subscribe to presence transitions
        _presence.PlayerCameOnline += OnPlayerCameOnline;
        _presence.PlayerWentOffline += OnPlayerWentOffline;
    }

    private async void OnPlayerCameOnline(OnlinePlayer player)
    {
        try
        {
            await NotifyAsync(player, isOnline: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling PlayerCameOnline for {Name}", player.CharacterName);
        }
    }

    private async void OnPlayerWentOffline(OnlinePlayer player)
    {
        try
        {
            await NotifyAsync(player, isOnline: false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling PlayerWentOffline for {Name}", player.CharacterName);
        }
    }

    private async Task NotifyAsync(OnlinePlayer player, bool isOnline, CancellationToken ct)
    {
        // 1) Who has this player as a friend (by name; you already wired this)?
        var friendIds = await _friendsService.GetFriendsOfAsync(player.CharacterName, ct);
        if (friendIds == null || friendIds.Count == 0)
            return;

        // 2) Of those friends, who is currently online (and where)?
        var onlineFriends = _presence.GetOnlineByIds(friendIds.ToArray());
        if (onlineFriends == null || onlineFriends.Count == 0)
            return;

        // Group by GameServerId so we only ping relevant servers
        var byServer = onlineFriends
            .Where(f => !string.IsNullOrWhiteSpace(f.GameServerId))
            .GroupBy(f => f.GameServerId, StringComparer.OrdinalIgnoreCase);

        var allSockets = _registry.GetAll(); // same pattern as WebSocketChatBroadcaster

        foreach (var group in byServer)
        {
            var serverId = group.Key;
            if (!allSockets.TryGetValue(serverId, out var ws))
            {
                _logger.LogDebug("No websocket for GameServer {ServerId} when sending friend presence", serverId);
                continue;
            }

            if (ws.State != WebSocketState.Open)
            {
                _logger.LogDebug("Websocket for GameServer {ServerId} not open when sending friend presence", serverId);
                continue;
            }

            var recipientIds = group.Select(f => f.CharacterId).ToArray();
            if (recipientIds.Length == 0)
                continue;

            // Shape of the message Gate → GameServer
            var envelope = new
            {
                type = "friend.presence",
                payload = new
                {
                    friendId = player.CharacterId,
                    friendName = player.CharacterName,
                    isOnline = isOnline,
                    recipients = recipientIds
                }
            };

            var json = JsonSerializer.Serialize(envelope);
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            try
            {
                await ws.SendAsync(segment, WebSocketMessageType.Text, true, ct);
                _logger.LogInformation(
                    "Sent friend presence {Online} for {FriendName} to {ServerId} (recipients: {RecipientCount})",
                    isOnline, player.CharacterName, serverId, recipientIds.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to send friend presence for {FriendName} to {ServerId}",
                    player.CharacterName, serverId);
            }
        }
    }
}
