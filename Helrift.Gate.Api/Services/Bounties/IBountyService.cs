using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.Bounties;

public interface IBountyService
{
    Task<BountyOrderSnapshot> CreateBountyAsync(CreateBountyContractRequest request, CancellationToken ct = default);
    Task<BountyOrderSnapshot> CancelBountyAsync(CancelBountyContractRequest request, CancellationToken ct = default);
    Task<ResolveBountyKillResult> ResolveKillAsync(ResolveBountyKillRequest request, CancellationToken ct = default);

    Task<BountyBrowseResult> BrowseActiveAsync(BountyBrowseQuery query, CancellationToken ct = default);
    Task<BountyBrowseResult> GetMyIssuedAsync(string realmId, string issuerCharacterId, int page = 1, int pageSize = 50, CancellationToken ct = default);

    Task<BountyAdminSearchResult> SearchAdminAsync(BountyAdminSearchQuery query, CancellationToken ct = default);
    Task<BountyAdminDetail?> GetAdminDetailAsync(string realmId, string bountyId, CancellationToken ct = default);

    Task<int> ExpireDueBountiesAsync(string realmId, CancellationToken ct = default);
}
