namespace Helrift.Gate.Api.Services
{
    public interface IPartyExperienceService
    {
        Task ProcessBatchAsync(PartyExperienceEventBatchDto batch, CancellationToken ct);
    }
}
