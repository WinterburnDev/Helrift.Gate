using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.Accounts
{
    public interface IBanService
    {
        Task<BanRecord?> GetActiveBanAsync(string realmId, string steamId, string ipAddress);
        Task<BanRecord> CreateBanAsync(CreateBanRequest request, CancellationToken ct = default);
    }
}
