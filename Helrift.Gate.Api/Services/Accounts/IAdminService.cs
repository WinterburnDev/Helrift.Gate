namespace Helrift.Gate.Api.Services.Accounts;

public interface IAdminService
{
    Task<bool> IsAdminAsync(string realmId, string characterId, CancellationToken ct = default);
    Task GrantAsync(string realmId, string characterId, CancellationToken ct = default);
    Task RevokeAsync(string realmId, string characterId, CancellationToken ct = default);
}
