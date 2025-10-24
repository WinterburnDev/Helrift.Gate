using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.Accounts
{
    public interface IAccountService
    {
        Task<AccountData> GetOrCreateBySteamAsync(string steamId64);
    }
}
