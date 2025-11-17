using Helrift.Gate.Api.Services;
using Helrift.Gate.App.Domain;
using Helrift.Gate.Contracts;
using Helrift.Gate.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Helrift.Gate.Services;

public sealed class WebSocketPartyNotifier
{
    private readonly IPresenceService _presence;
    private readonly IPartyService _partyService;
    private readonly IGameServerConnectionRegistry _registry;
    private readonly ILogger<WebSocketPartyNotifier> _logger;

    public WebSocketPartyNotifier(
        IPresenceService presence,
        IPartyService partyService,
        IGameServerConnectionRegistry registry,
        ILogger<WebSocketPartyNotifier> logger)
    {
        _presence = presence;
        _partyService = partyService;
        _registry = registry;
        _logger = logger;

        // subscribe to presence transitions
        _presence.PlayerCameOnline += OnPlayerPresenceChanged;
        _presence.PlayerWentOffline += OnPlayerPresenceChanged;

        // subscribe to party changes (create/join/leave/kick/leader change)
        _partyService.PartyChanged += OnPartyChanged;
    }

    // Presence change: find their party (if any) and broadcast fresh state
    private async void OnPlayerPresenceChanged(OnlinePlayer player)
    {
        try
        {
            var party = await _partyService.GetByCharacterIdAsync(player.CharacterId, CancellationToken.None);
            if (party == null)
                return;

            await BroadcastPartyStateAsync(party, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error handling presence change for {Name} in WebSocketPartyNotifier",
                player.CharacterName);
        }
    }

    // Party membership change: broadcast directly
    private async void OnPartyChanged(Party party, IEnumerable<string>? extraRecipients = null)
    {
        try
        {
            await BroadcastPartyStateAsync(party, extraRecipients, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error broadcasting party state for Party {PartyId}",
                party.Id);
        }
    }

    private async Task BroadcastPartyStateAsync(Party party, IEnumerable<string>? extraRecipients, CancellationToken ct)
    {
        var memberIds = party.Members.Select(m => m.CharacterId).ToList();

        if (extraRecipients != null)
            memberIds.AddRange(extraRecipients);

        var distinctIds = memberIds
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        if (distinctIds.Length == 0)
            return;

        var onlineMembers = _presence.GetOnlineByIds(distinctIds);
        if (onlineMembers == null || onlineMembers.Count == 0)
            return;

        var byServer = onlineMembers
            .Where(p => !string.IsNullOrWhiteSpace(p.GameServerId))
            .GroupBy(p => p.GameServerId, StringComparer.OrdinalIgnoreCase);

        var sockets = _registry.GetAll();

        var dto = PartyMapper.ToDto(party);

        foreach (var group in byServer)
        {
            var serverId = group.Key;
            if (!sockets.TryGetValue(serverId, out var ws))
            {
                _logger.LogDebug("No websocket for GameServer {ServerId} when sending party state", serverId);
                continue;
            }

            if (ws.State != WebSocketState.Open)
            {
                _logger.LogDebug("Websocket for GameServer {ServerId} not open when sending party state", serverId);
                continue;
            }

            var recipients = group.Select(x => x.CharacterId).ToArray();
            if (recipients.Length == 0)
                continue;

            var envelope = new
            {
                type = "party.state",
                payload = new
                {
                    party = dto,
                    recipients
                }
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

                _logger.LogInformation(
                    "Sent party state for Party {PartyId} to {ServerId} (recipients: {Count})",
                    party.Id, serverId, recipients.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to send party state for Party {PartyId} to {ServerId}",
                    party.Id, serverId);
            }
        }
    }
}
