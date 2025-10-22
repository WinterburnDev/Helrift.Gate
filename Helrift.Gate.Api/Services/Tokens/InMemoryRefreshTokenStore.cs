namespace Helrift.Gate.Api.Services.Tokens
{
    public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
    {
        private readonly Dictionary<string, RefreshRecord> _db = new();

        public Task SaveAsync(string token, string masterClientId, string steamId, string buildVersion, DateTimeOffset expiresUtc)
        {
            _db[token] = new RefreshRecord
            {
                Token = token,
                MasterClientId = masterClientId,
                SteamId = steamId,
                BuildVersion = buildVersion,
                ExpiresUtc = expiresUtc
            };
            return Task.CompletedTask;
        }

        public Task<RefreshRecord?> GetAsync(string token)
            => Task.FromResult(_db.TryGetValue(token, out var r) ? r : null);
    }
}
