using Helrift.Gate.App.Domain;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services
{
    public interface IPartyService
    {
        event Action<Party, IEnumerable<string>> PartyChanged;

        Task<Party?> GetByCharacterIdAsync(string characterId, CancellationToken ct);
        Task<Party> CreatePartyAsync(CreatePartyRequest request, CancellationToken ct);
        Task<Party?> JoinPartyAsync(JoinPartyRequest request, CancellationToken ct);
        Task<Party?> LeavePartyAsync(LeavePartyRequest request, CancellationToken ct);
        Task<Party?> SetLeaderAsync(SetLeaderRequest request, CancellationToken ct);
        Task<Party?> KickMemberAsync(KickMemberRequest request, CancellationToken ct);
        Task<IReadOnlyCollection<Party>> ListPartiesAsync(OwnerSide sideFilter, CancellationToken ct);
        Task<IReadOnlyCollection<Party>> ListVisiblePartiesAsync(OwnerSide side, string? viewerCharacterId, CancellationToken ct);
    }

}
