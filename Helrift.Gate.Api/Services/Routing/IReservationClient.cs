using Helrift.Gate.Api.Services.GameServers.Models;

namespace Helrift.Gate.Api.Services.Routing
{
    public interface IReservationClient
    {
        Task<bool> ReserveAsync(GameServerDescriptor gs, string accountId, string characterId, string jti, DateTimeOffset expiresAt, CancellationToken ct);
    }
}
