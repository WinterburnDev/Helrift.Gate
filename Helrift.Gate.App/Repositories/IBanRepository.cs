using Helrift.Gate.Contracts;

namespace Helrift.Gate.App.Repositories;

public interface IBanRepository
{
    /// <summary>
    /// Returns an active ban for this realm + (steam or ip), or null if none / expired.
    /// </summary>
    Task<BanRecord?> GetActiveBanAsync(
        string realmId,
        string steamId,
        string ipAddress,
        CancellationToken ct);

    /// <summary>
    /// Upsert a ban record (for admin tools).
    /// </summary>
    Task SaveBanAsync(BanRecord ban, CancellationToken ct);

    /// <summary>
    /// Returns all active (non-expired) bans for the given realm.
    /// </summary>
    Task<IReadOnlyList<BanRecord>> ListActiveBansAsync(string realmId, CancellationToken ct);

    /// <summary>
    /// Removes a ban by its composite key. Deletes from both bySteam and byIp buckets.
    /// </summary>
    Task RevokeBanAsync(string realmId, string? steamId, string? ipAddress, CancellationToken ct);
}
