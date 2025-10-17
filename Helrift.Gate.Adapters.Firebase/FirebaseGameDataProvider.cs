using Helrift.Gate.App;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Adapters.Firebase;

public sealed class FirebaseGameDataProvider : IGameDataProvider
{
    public Task<AccountSummary?> GetAccountAsync(string accountId, CancellationToken ct)
        => Task.FromResult<AccountSummary?>(new AccountSummary(accountId, "stub", 0));

    public Task<IReadOnlyList<Character>> GetCharactersAsync(string accountId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Character>>(Array.Empty<Character>());

    public Task<Character?> GetCharacterAsync(string accountId, string charId, CancellationToken ct)
        => Task.FromResult<Character?>(null);
}
