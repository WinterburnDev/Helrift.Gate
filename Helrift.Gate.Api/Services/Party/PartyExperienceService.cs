using Microsoft.Extensions.Options;

namespace Helrift.Gate.Api.Services
{
    public sealed class PartyExperienceService(
    IPartyService partyService,
    IPresenceService presence,
    IPartyExperienceBroadcaster broadcaster,
    IOptions<PartyExperienceOptions> options,
    ILogger<PartyExperienceService> logger)
    : IPartyExperienceService
    {
        public async Task ProcessBatchAsync(PartyExperienceEventBatchDto batch, CancellationToken ct)
        {
            if (batch?.Events == null || batch.Events.Count == 0)
                return;

            foreach (var e in batch.Events)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    await ProcessSingleAsync(e, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error processing party XP event {EventId} for Party {PartyId}", e?.EventId, e?.PartyId);
                }
            }
        }

        private async Task ProcessSingleAsync(PartyExperienceEventDto e, CancellationToken ct)
        {
            if (e == null) return;
            if (string.IsNullOrWhiteSpace(e.PartyId)) return;
            if (string.IsNullOrWhiteSpace(e.EventId)) return;
            if (e.BaseXp <= 0) return;

            var party = await partyService.GetByIdAsync(e.PartyId, ct);
            if (party == null) return;

            var memberIds = party.Members
                .Select(m => m.CharacterId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (memberIds.Length == 0) return;

            // Only online members can receive (and are worth routing to servers).
            var online = presence.GetOnlineByIds(memberIds);
            if (online == null || online.Count == 0)
                return;

            // Group by server (like WebSocketPartyNotifier)
            var byServer = online
                .Where(p => !string.IsNullOrWhiteSpace(p.GameServerId))
                .GroupBy(p => p.GameServerId!, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (byServer.Length == 0) return;

            // Split across ONLINE members
            var onlineIds = online
                .Select(p => p.CharacterId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (onlineIds.Length == 0) return;

            var share = e.BaseXp / onlineIds.Length;
            if (share <= 0) return;

            var remainder = e.BaseXp - (share * onlineIds.Length);

            // Build delta lookup for quick use per server group
            var deltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in onlineIds)
                deltas[id] = share;

            if (options.Value.RemainderToEarner && remainder > 0 && !string.IsNullOrWhiteSpace(e.EarnerCharacterId))
            {
                if (deltas.ContainsKey(e.EarnerCharacterId))
                    deltas[e.EarnerCharacterId] += remainder;
            }

            // Broadcast per server
            foreach (var group in byServer)
            {
                var serverId = group.Key;

                var recipients = group
                    .Select(x => x.CharacterId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (recipients.Length == 0)
                    continue;

                var payload = new PartyExperiencePayloadDto
                {
                    PartyId = e.PartyId,
                    SourceX = e.SourceX,
                    SourceY = e.SourceY,
                    SourceZ = e.SourceZ,
                    ShareRange = options.Value.ShareRange,
                    Recipients = recipients,
                    Deltas = recipients.Select(r => new PartyExperienceDeltaDto
                    {
                        EventId = e.EventId,
                        CharacterId = r,
                        BaseXpShare = deltas.TryGetValue(r, out var amt) ? amt : 0
                    }).Where(d => d.BaseXpShare > 0).ToList()
                };

                if (payload.Deltas.Count == 0)
                    continue;

                await broadcaster.BroadcastAsync(serverId, payload, ct);
            }
        }
    }
}
