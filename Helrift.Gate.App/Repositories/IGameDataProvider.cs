// Helrift.Gate.App/IGameDataProvider.cs
using Helrift.Gate.Contracts;

namespace Helrift.Gate.App.Repositories
{
    public interface IGameDataProvider
    {
        Task<AccountData?> GetAccountAsync(string accountId, CancellationToken ct);
        Task<AccountData?> GetAccountBySteamIdAsync(string steamId, CancellationToken ct);
        Task<AccountData> CreateAccountAsync(NewAccountRequest req, CancellationToken ct);

        Task<IReadOnlyList<CharacterData>> GetCharactersAsync(string accountId, CancellationToken ct);
        Task<CharacterData?> GetCharacterAsync(string accountId, string charId, CancellationToken ct);
        Task<CharacterNameRecord?> GetCharacterByNameAsync(string characterName, CancellationToken ct);
        Task CreateCharacterAsync(CharacterData character, CancellationToken ct);
        Task SaveCharacterAsync(CharacterData character, CancellationToken ct);
        Task DeleteCharacterAsync(string accountId, string charId, string characterName, CancellationToken ct);
    }
}