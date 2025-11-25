using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.Accounts
{
    public sealed class BanService : IBanService
    {
        private readonly IBanRepository _repo;

        public BanService(IBanRepository repo)
        {
            _repo = repo;
        }

        public Task<BanRecord?> GetActiveBanAsync(string realmId, string steamId, string ipAddress)
        {
            // Any additional logic like picking the "strongest" ban could go here later.
            return _repo.GetActiveBanAsync(realmId, steamId, ipAddress, CancellationToken.None);
        }

        public async Task<BanRecord> CreateBanAsync(CreateBanRequest request, CancellationToken ct = default)
        {
            if (request is null)
                throw new ArgumentNullException(nameof(request));

            if (string.IsNullOrWhiteSpace(request.RealmId))
                throw new ArgumentException("RealmId is required.", nameof(request));

            if (string.IsNullOrWhiteSpace(request.SteamId) &&
                string.IsNullOrWhiteSpace(request.IpAddress))
                throw new ArgumentException("Either SteamId or IpAddress must be provided.", nameof(request));

            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new ArgumentException("Reason is required.", nameof(request));

            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            long? expiresUnix = request.DurationMinutes.HasValue
                ? DateTimeOffset.UtcNow.AddMinutes(request.DurationMinutes.Value).ToUnixTimeSeconds()
                : null;

            var record = new BanRecord
            {
                Id = "", // Firebase will generate key (or repo will assign)
                RealmId = request.RealmId,
                SteamId = request.SteamId ?? "",
                IpAddress = request.IpAddress ?? "",
                Reason = request.Reason,
                BannedBy = "Admin",
                BannedAtUnixUtc = nowUnix,
                ExpiresAtUnixUtc = expiresUnix
            };

            await _repo.SaveBanAsync(record, ct);
            return record;
        }
    }
}
