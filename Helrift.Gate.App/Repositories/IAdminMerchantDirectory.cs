namespace Helrift.Gate.App.Repositories;

/// <summary>
/// Admin-only: enumerates all NPC merchant IDs stored in the database.
/// Kept separate from IMerchantDataProvider so the game-server path doesn't
/// need to know about enumeration.
/// </summary>
public interface IAdminMerchantDirectory
{
    Task<IReadOnlyList<string>> GetAllNpcIdsAsync(CancellationToken ct);
}