namespace Helrift.Gate.Api.Services.Accounts
{
    public interface IAccountService
    {
        Task<AccountRecord> GetOrCreateBySteamAsync(string steamId64);
    }

    public sealed class AccountRecord
    {
        public string AccountId { get; init; }
        public string SteamId { get; init; }
    }
}
