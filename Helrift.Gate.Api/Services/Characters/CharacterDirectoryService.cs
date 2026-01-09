using Helrift.Gate.App.Repositories; // IGameDataProvider
using Microsoft.Extensions.DependencyInjection;

namespace Helrift.Gate.Api.Services.Accounts;

public interface ICharacterDirectoryService
{
    /// <summary>
    /// Returns characterId -> display name for the given IDs.
    /// Missing IDs simply won't be present in the dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetNamesByCharacterIdsAsync(
        string realmId,
        IEnumerable<string> characterIds,
        CancellationToken ct = default);
}

public sealed class CharacterDirectoryService : ICharacterDirectoryService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CharacterDirectoryService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetNamesByCharacterIdsAsync(
        string realmId,
        IEnumerable<string> characterIds,
        CancellationToken ct = default)
    {
        if (characterIds == null)
            return new Dictionary<string, string>();

        var ids = characterIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
            return new Dictionary<string, string>();

        using var scope = _scopeFactory.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IGameDataProvider>();

        // Best option: add a batch method to the provider (recommended).
        // This keeps Firebase round-trips low for top lists.
        //
        // Implement this in your Firebase provider against the characterNames entity.
        var names = await data.GetCharacterNamesByIdsAsync(realmId, ids, ct);

        // Expected: Dictionary<string characterId, string name>
        return names ?? new Dictionary<string, string>();
    }
}
