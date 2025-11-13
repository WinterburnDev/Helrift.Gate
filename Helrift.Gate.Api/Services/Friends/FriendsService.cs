using Helrift.Gate.App;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.Friends
{
    public class FriendsService : IFriendsService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IPresenceService _presence;

        public FriendsService(IServiceScopeFactory scopeFactory, IPresenceService presence)
        {
            _scopeFactory = scopeFactory;
            _presence = presence;
        }

        public async Task<List<FriendStatusDto>> GetFriendsSnapshotAsync(string accountId, string characterId, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var data = scope.ServiceProvider.GetRequiredService<IGameDataProvider>();

            var me = await data.GetCharacterAsync(accountId, characterId, ct);
            var friends = me?.Friends ?? new Dictionary<string, FriendEntry>();

            if (friends.Count == 0)
                return new List<FriendStatusDto>();

            var ids = friends.Keys.ToArray();
            var presence = _presence.GetOnlineByIds(ids);

            var result = new List<FriendStatusDto>(ids.Length);
            foreach (var fid in ids)
            {
                friends.TryGetValue(fid, out var meta);

                var p = presence.FirstOrDefault(p => p.CharacterId == fid);

                result.Add(new FriendStatusDto
                {
                    characterId = fid,
                    name = meta?.name ?? p.CharacterName,              // might be null if not stored
                    online = p != null,
                    server = p?.GameServerId ?? string.Empty
                });
            }

            return result;
        }

        public async Task<FriendStatusDto?> AddFriendAsync(string accountId, string characterId, string friendCharacterId, string? friendName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(friendCharacterId))
                return null;

            if (string.Equals(friendCharacterId, characterId, StringComparison.Ordinal))
                return null; // no self-friend

            using var scope = _scopeFactory.CreateScope();
            var data = scope.ServiceProvider.GetRequiredService<IGameDataProvider>();

            var me = await data.GetCharacterAsync(accountId, characterId, ct);
            if (me == null)
                return null;

            me.Friends ??= new Dictionary<string, FriendEntry>(StringComparer.Ordinal);

            if (!me.Friends.TryGetValue(friendCharacterId, out var entry))
            {
                entry = new FriendEntry
                {
                    name = friendName ?? string.Empty,
                    note = string.Empty,
                    since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                };
                me.Friends[friendCharacterId] = entry;

                // persist character document back to Firebase
                await data.SaveCharacterAsync(me, ct);
            }
            else
            {
                // update name if we got a non-empty one and it's different
                if (!string.IsNullOrWhiteSpace(friendName) && !string.Equals(entry.name, friendName, StringComparison.Ordinal))
                {
                    entry.name = friendName;
                    await data.SaveCharacterAsync(me, ct);
                }
            }

            // resolve presence for this friend to build the status dto
            var presence = _presence.GetOnlineByIds(new[] { friendCharacterId });
            var p = presence.FirstOrDefault(p => p.CharacterId == friendCharacterId);

            return new FriendStatusDto
            {
                characterId = friendCharacterId,
                name = entry.name ?? p?.CharacterName ?? string.Empty,
                online = p != null,
                server = p?.GameServerId ?? string.Empty
            };
        }

        public async Task<bool> DeleteFriendAsync(string accountId, string characterId, string friendCharacterId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(friendCharacterId))
                return false;

            using var scope = _scopeFactory.CreateScope();
            var data = scope.ServiceProvider.GetRequiredService<IGameDataProvider>();

            var me = await data.GetCharacterAsync(accountId, characterId, ct);
            if (me == null || me.Friends == null || me.Friends.Count == 0)
                return false;

            if (!me.Friends.Remove(friendCharacterId))
                return false;

            await data.SaveCharacterAsync(me, ct);
            return true;
        }
    }
}
