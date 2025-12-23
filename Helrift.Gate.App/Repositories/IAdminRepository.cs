namespace Helrift.Gate.App.Repositories;

public interface IAdminRepository
{
    Task<bool> IsAdminCharacterAsync(string realmId, string characterId, CancellationToken ct);
    Task SetAdminCharacterAsync(string realmId, string characterId, bool isAdmin, CancellationToken ct);
}
