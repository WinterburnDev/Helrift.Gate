namespace Helrift.Gate.Api.Services
{
    public interface IPartyExperienceBroadcaster
    {
        Task BroadcastAsync(string serverId, PartyExperiencePayloadDto payload, CancellationToken ct);
    }
}