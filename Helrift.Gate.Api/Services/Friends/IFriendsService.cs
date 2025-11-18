using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.Friends
{
    public interface IFriendsService
    {
        Task<List<FriendStatusDto>> GetFriendsSnapshotAsync(string accountId, string characterId, CancellationToken ct);
        Task<FriendStatusDto?> AddFriendAsync(string accountId, string characterId, string friendCharacterId, string? friendName, CancellationToken ct);
        Task<bool> DeleteFriendAsync(string accountId, string characterId, string friendCharacterId, CancellationToken ct);
        Task<bool> SendFriendRequestAsync(string accountId, string characterId, string targetName, CancellationToken ct);
        Task<bool> AcceptFriendRequestAsync(string accountId, string characterId, string fromCharacterId, CancellationToken ct);
        Task<bool> RejectFriendRequestAsync(string accountId, string characterId, string fromCharacterId, CancellationToken ct);
        Task<bool> CancelFriendRequestAsync(string accountId, string characterId, string targetCharacterId, CancellationToken ct);
        Task<IReadOnlyList<string>> GetFriendsOfAsync(string characterName, CancellationToken ct);
    }
}
