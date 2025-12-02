namespace Helrift.Gate.Api.Services.Tokens
{
    public interface IRefreshTokenStore
    {
        Task SaveAsync(string token, string masterClientId, string steamId, string buildVersion, DateTimeOffset expiresUtc);
        Task<RefreshRecord?> GetAsync(string token);
    }

    public sealed class RefreshRecord
    {
        public string Token { get; init; }
        public string AccountId { get; init; }
        public string SteamId { get; init; }
        public string BuildVersion { get; init; }
        public DateTimeOffset ExpiresUtc { get; init; }
    }
}
