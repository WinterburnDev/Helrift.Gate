namespace Helrift.Gate.Api.Services.Accounts
{
    public sealed class InMemoryAccountService : IAccountService
    {
        private readonly Dictionary<string, AccountRecord> _bySteam = new();

        public Task<AccountRecord> GetOrCreateBySteamAsync(string steamId64)
        {
            if (!_bySteam.TryGetValue(steamId64, out var rec))
            {
                rec = new AccountRecord
                {
                    AccountId = $"2F917914C4B3B41C",
                    SteamId = steamId64
                };
                _bySteam[steamId64] = rec;
            }
            return Task.FromResult(rec);
        }
    }
}
