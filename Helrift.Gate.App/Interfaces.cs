using Helrift.Gate.Contracts;

namespace Helrift.Gate.App;

public interface IGameDataProvider
{
    Task<AccountSummary?> GetAccountAsync(string accountId, CancellationToken ct);
    Task<IReadOnlyList<Character>> GetCharactersAsync(string accountId, CancellationToken ct);
    Task<Character?> GetCharacterAsync(string accountId, string charId, CancellationToken ct);
}
