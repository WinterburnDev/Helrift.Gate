using Helrift.Gate.App.Repositories;

namespace Helrift.Gate.Api.Services.Accounts;

public sealed class AdminService : IAdminService
{
    private readonly IAdminRepository _repo;

    public AdminService(IAdminRepository repo)
    {
        _repo = repo;
    }

    public Task<bool> IsAdminAsync(string realmId, string characterId, CancellationToken ct = default)
        => _repo.IsAdminCharacterAsync(realmId, characterId, ct);

    public Task GrantAsync(string realmId, string characterId, CancellationToken ct = default)
        => _repo.SetAdminCharacterAsync(realmId, characterId, isAdmin: true, ct);

    public Task RevokeAsync(string realmId, string characterId, CancellationToken ct = default)
        => _repo.SetAdminCharacterAsync(realmId, characterId, isAdmin: false, ct);
}
