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
    public long LastSeenUnixUtc { get; set; }
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

        var nameRecord = await data.GetCharacterByNameAsync(name, ct);
        if (nameRecord == null)
            return Array.Empty<CharacterSearchResult>();

        var accountId = nameRecord.AccountId;
        var characterId = nameRecord.CharacterId;
        var canonicalName = nameRecord.CharacterName;

        var character = await data.GetCharacterAsync(accountId, characterId, ct);
        if (character == null)
            return Array.Empty<CharacterSearchResult>();

        var result = new CharacterSearchResult
        {
            RealmId = "default",
            AccountId = accountId,
            CharacterId = characterId,
            Name = canonicalName ?? character.CharacterName ?? string.Empty,
            Side = ResolveSideName(character.Side),
            Level = character.Level,
            LastSeenUnixUtc = ToUnixUtc(character.LastLoggedIn)
        };

        return new[] { result };
    }

    private static string ResolveSideName(int side) => side switch
    {
        1 => "Aresden",
        2 => "Elvine",
        3 => "Traveller",
        _ => "Neutral"
    };

    private static long ToUnixUtc(DateTime? value)
    {
        if (!value.HasValue)
            return 0;

        var dt = value.Value;
        if (dt.Kind == DateTimeKind.Unspecified)
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        return new DateTimeOffset(dt.ToUniversalTime()).ToUnixTimeSeconds();
    }
}
