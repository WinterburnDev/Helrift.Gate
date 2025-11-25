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
}
