using Helrift.Gate.Contracts;

namespace Helrift.Gate.App.Repositories;

public sealed record BountyRecordSnapshot(BountyContract Record, string? ConcurrencyToken);

public interface IBountyRepository
{
    Task<BountyContract?> GetBountyAsync(string realmId, string bountyId, CancellationToken ct);
    Task<BountyRecordSnapshot?> GetBountySnapshotAsync(string realmId, string bountyId, CancellationToken ct);

    Task<bool> TryCreateBountyAsync(BountyContract bounty, CancellationToken ct);
    Task<bool> TryReplaceBountyAsync(string realmId, BountyContract bounty, string? concurrencyToken, CancellationToken ct);

    Task<IReadOnlyList<BountyContract>> ListAllBountiesAsync(string realmId, CancellationToken ct);
    Task<IReadOnlyList<BountyContract>> ListBountiesByTargetAsync(string realmId, string targetCharacterId, CancellationToken ct);
    Task<IReadOnlyList<BountyContract>> ListBountiesByIssuerAsync(string realmId, string issuerCharacterId, CancellationToken ct);

    Task<bool> DeleteBountyAsync(string realmId, string bountyId, CancellationToken ct);
}
