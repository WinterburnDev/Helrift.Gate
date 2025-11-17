using Helrift.Gate.Api.Services;
using Helrift.Gate.App.Domain;
using Helrift.Gate.Contracts;
using Helrift.Gate.Services; // wherever your IPartyService is
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Helrift.Gate.Services
{
    /// <summary>
    /// Listens to presence events and automatically removes players from parties
    /// when they go offline.
    /// </summary>
    public sealed class PartyPresenceCleanupListener
    {
        private readonly IPresenceService _presence;
        private readonly IPartyService _partyService;
        private readonly ILogger<PartyPresenceCleanupListener> _logger;

        public PartyPresenceCleanupListener(
            IPresenceService presence,
            IPartyService partyService,
            ILogger<PartyPresenceCleanupListener> logger)
        {
            _presence = presence;
            _partyService = partyService;
            _logger = logger;

            _presence.PlayerWentOffline += OnPlayerWentOffline;
        }

        private async void OnPlayerWentOffline(OnlinePlayer player)
        {
            try
            {
                // We only need CharacterId to drive LeavePartyAsync.
                var req = new LeavePartyRequest
                {
                    CharacterId = player.CharacterId
                };

                await _partyService.LeavePartyAsync(req, CancellationToken.None);
                _logger.LogInformation(
                    "Auto-removed offline player {CharacterId} from party (if any).",
                    player.CharacterId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error auto-removing offline player {CharacterId} from party.",
                    player.CharacterId);
            }
        }
    }
}
