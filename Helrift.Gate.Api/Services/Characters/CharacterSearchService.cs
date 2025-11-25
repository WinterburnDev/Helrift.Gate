using Helrift.Gate.App.Repositories; // for IGameDataProvider
using Microsoft.Extensions.DependencyInjection;

namespace Helrift.Gate.Api.Services.Accounts;

public sealed class CharacterSearchResult
{
    public string RealmId { get; set; } = "default";
    public string AccountId { get; set; } = string.Empty;
    public string CharacterId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public int Level { get; set; }
    public long LastSeenUnixUtc { get; set; }  // keep as long for consistency with your other models
}

public interface ICharacterSearchService
{
    Task<IReadOnlyList<CharacterSearchResult>> SearchByNameAsync(string name, CancellationToken ct = default);
}

public sealed class CharacterSearchService : ICharacterSearchService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CharacterSearchService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<IReadOnlyList<CharacterSearchResult>> SearchByNameAsync(
        string name,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Array.Empty<CharacterSearchResult>();

        using var scope = _scopeFactory.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IGameDataProvider>();

        // 1. This already does the character_names lookup and resolves accountId/characterId.
        //    Pattern copied from FriendService.AddFriendAsync.
        var nameRecord = await data.GetCharacterByNameAsync(name, ct);
        if (nameRecord == null)
            return Array.Empty<CharacterSearchResult>();

        var accountId = nameRecord.AccountId;
        var characterId = nameRecord.CharacterId;
        var canonicalName = nameRecord.CharacterName;

        // 2. Load the character document itself.
        var character = await data.GetCharacterAsync(accountId, characterId, ct);
        if (character == null)
            return Array.Empty<CharacterSearchResult>();

        // NOTE: property names below are guesses – tweak to match your actual character model.
        var result = new CharacterSearchResult
        {
            RealmId = "default",      // or nameRecord.RealmId, if present
            AccountId = accountId,
            CharacterId = characterId,
            Name = canonicalName ?? character.CharacterName ?? string.Empty,
            //Side = character.Side ?? string.Empty,         // e.g. "Aresden", "Elvine", "Traveller"
            Level = character.Level,                       // adjust type/name if different
            //LastSeenUnixUtc = character.LastSeenUnixUtc    // or map from DateTime -> ToUnixTimeSeconds()
        };

        return new[] { result };
    }
}
