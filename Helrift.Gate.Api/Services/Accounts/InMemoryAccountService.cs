using System.Collections.Concurrent;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Helrift.Gate.Api.Services.Accounts
{
    public sealed class InMemoryAccountService : IAccountService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ConcurrentDictionary<string, AccountData> _bySteam = new();

        public InMemoryAccountService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<AccountData> GetOrCreateBySteamAsync(string steamId64)
        {
            if (string.IsNullOrWhiteSpace(steamId64))
                throw new ArgumentException("steamId64 is required.", nameof(steamId64));

            if (_bySteam.TryGetValue(steamId64, out var cached))
                return cached;

            using var scope = _scopeFactory.CreateScope();
            var data = scope.ServiceProvider.GetRequiredService<IGameDataProvider>();

            var existing = await data.GetAccountBySteamIdAsync(steamId64, CancellationToken.None);
            if (existing != null)
                return _bySteam.GetOrAdd(steamId64, existing);

            var created = await data.CreateAccountAsync(new NewAccountRequest
            {
                SteamId64 = steamId64,
                Username = null,
                EmailAddress = null
            }, CancellationToken.None);

            return _bySteam.GetOrAdd(steamId64, created);
        }
    }
}
