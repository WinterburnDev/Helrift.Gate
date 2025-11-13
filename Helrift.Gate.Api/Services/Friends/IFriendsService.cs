using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.Friends
{
    public interface IFriendsService
    {
        Task<List<FriendStatusDto>> GetFriendsSnapshotAsync(string accountId, string characterId, CancellationToken ct);
        Task<FriendStatusDto?> AddFriendAsync(string accountId, string characterId, string friendCharacterId, string? friendName, CancellationToken ct);
        Task<bool> DeleteFriendAsync(string accountId, string characterId, string friendCharacterId, CancellationToken ct);
    }
}
