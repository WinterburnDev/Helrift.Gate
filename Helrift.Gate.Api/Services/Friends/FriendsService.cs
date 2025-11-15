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
            var friends = me?.Friends ?? new Dictionary<string, FriendEntry>(StringComparer.Ordinal);

            if (me == null || friends.Count == 0)
                return new List<FriendStatusDto>();

            var validIds = new List<string>(friends.Count);
            var staleIds = new List<string>();

            // --- 1) Validate symmetric friendships, mark stale if broken ---
            foreach (var kv in friends)
            {
                var fid = kv.Key;
                var meta = kv.Value;

                if (string.IsNullOrEmpty(fid) || meta == null || string.IsNullOrWhiteSpace(meta.name))
                {
                    staleIds.Add(fid);
                    continue;
                }

                try
                {
                    // Resolve via character_names registry using stored name
                    var nameRecord = await data.GetCharacterByNameAsync(meta.name, ct);
                    if (nameRecord == null || !string.Equals(nameRecord.CharacterId, fid, StringComparison.Ordinal))
                    {
                        // Name no longer resolves, or resolves to different character -> stale
                        staleIds.Add(fid);
                        continue;
                    }

                    var other = await data.GetCharacterAsync(nameRecord.AccountId, nameRecord.CharacterId, ct);
                    if (other?.Friends == null || !other.Friends.ContainsKey(characterId))
                    {
                        // Other side no longer has me in their Friends -> stale
                        staleIds.Add(fid);
                        continue;
                    }

                    // Looks symmetric and valid
                    validIds.Add(fid);
                }
                catch
                {
                    // On transient errors (Firebase hiccup etc.), don't aggressively delete.
                    // Just treat as "still valid" for now and include it.
                    validIds.Add(fid);
                }
            }

            // --- 2) Self-heal: remove stale links from me.Friends, if any ---
            if (staleIds.Count > 0)
            {
                foreach (var sid in staleIds)
                    friends.Remove(sid);

                try
                {
                    // Persist the cleaned-up friends map
                    await data.SaveCharacterAsync(me, ct);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FriendsService] GetFriendsSnapshotAsync: failed to persist stale cleanup: {ex}");
                }
            }

            // --- 3) Build presence-based snapshot from the *validated* ids only ---
            if (validIds.Count == 0)
                return new List<FriendStatusDto>();

            var presence = _presence.GetOnlineByIds(validIds.ToArray());
            var result = new List<FriendStatusDto>(validIds.Count);

            foreach (var fid in validIds)
            {
                friends.TryGetValue(fid, out var meta);
                var p = presence.FirstOrDefault(p => p.CharacterId == fid);

                result.Add(new FriendStatusDto
                {
                    characterId = fid,
                    name = meta?.name ?? p?.CharacterName ?? string.Empty,
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

            using var scope = _scopeFactory.CreateScope();
            var data = scope.ServiceProvider.GetRequiredService<IGameDataProvider>();

            var nameRecord = await data.GetCharacterByNameAsync(friendCharacterId, ct);
            if (nameRecord == null)
                return null; // no such character

            var targetAccountId = nameRecord.AccountId;
            var targetCharacterId = nameRecord.CharacterId;
            var canonicalFriendName = nameRecord.CharacterName;

            if (string.Equals(targetAccountId, accountId, StringComparison.Ordinal) &&
                string.Equals(targetCharacterId, characterId, StringComparison.Ordinal))
            {
                return null;
            }

            var me = await data.GetCharacterAsync(accountId, characterId, ct);
            if (me == null)
                return null;

            me.Friends ??= new Dictionary<string, FriendEntry>(StringComparer.Ordinal);

            if (!me.Friends.TryGetValue(friendCharacterId, out var entry))
            {
                // friendName from client is optional; prefer canonical name from registry
                var nameToStore = !string.IsNullOrWhiteSpace(friendName)
                    ? friendName
                    : canonicalFriendName;

                entry = new FriendEntry
                {
                    name = nameToStore ?? string.Empty,
                    note = string.Empty,
                    since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                };

                me.Friends[targetCharacterId] = entry;

                // persist character document back to Firebase
                await data.SaveCharacterAsync(me, ct);
            }
            else
            {
                // update name if we got a non-empty one and it's different
                var nameToStore = !string.IsNullOrWhiteSpace(friendName)
                                    ? friendName
                                    : canonicalFriendName;

                if (!string.IsNullOrWhiteSpace(nameToStore) &&
                    !string.Equals(entry.name, nameToStore, StringComparison.Ordinal))
                {
                    entry.name = nameToStore;
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
            if (me == null)
                return false;

            me.Friends ??= new Dictionary<string, FriendEntry>(StringComparer.Ordinal);

            // Grab the entry first so we still have the name after removal
            me.Friends.TryGetValue(friendCharacterId, out var entry);

            if (!me.Friends.Remove(friendCharacterId))
                return false; // we didn't have them as a friend at all

            // Try to clean up the other side as well
            CharacterData? other = null;

            if (!string.IsNullOrWhiteSpace(entry?.name))
            {
                try
                {
                    // Resolve via character_names registry
                    var nameRecord = await data.GetCharacterByNameAsync(entry.name, ct);
                    if (nameRecord != null)
                    {
                        var otherAccountId = nameRecord.AccountId;
                        var otherCharacterId = nameRecord.CharacterId;

                        // Defensive: ensure we’re talking about the same friendCharacterId
                        if (string.Equals(otherCharacterId, friendCharacterId, StringComparison.Ordinal))
                        {
                            other = await data.GetCharacterAsync(otherAccountId, otherCharacterId, ct);
                            if (other != null)
                            {
                                other.Friends ??= new Dictionary<string, FriendEntry>(StringComparer.Ordinal);
                                other.Friends.Remove(characterId); // remove *me* from their friends
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // best-effort: log and keep going so at least "me" is cleaned up
                    Console.WriteLine($"[FriendsService] DeleteFriendAsync: failed to update other side: {ex}");
                }
            }

            // Persist both sides (me is mandatory, other is best-effort)
            await data.SaveCharacterAsync(me, ct);
            if (other != null)
                await data.SaveCharacterAsync(other, ct);

            return true;
        }


        public async Task<bool> SendFriendRequestAsync(string accountId, string characterId, string targetName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(targetName))
                return false;

            using var scope = _scopeFactory.CreateScope();
            var data = scope.ServiceProvider.GetRequiredService<IGameDataProvider>();

            // Resolve target via character_names registry
            var nameRecord = await data.GetCharacterByNameAsync(targetName, ct);
            if (nameRecord == null)
                return false; // no such character

            var targetAccountId = nameRecord.AccountId;
            var targetCharacterId = nameRecord.CharacterId;
            var canonicalTargetName = nameRecord.CharacterName;

            // prevent self
            if (string.Equals(targetAccountId, accountId, StringComparison.Ordinal) &&
                string.Equals(targetCharacterId, characterId, StringComparison.Ordinal))
            {
                return false;
            }

            var me = await data.GetCharacterAsync(accountId, characterId, ct);
            if (me == null)
                return false;

            var target = await data.GetCharacterAsync(targetAccountId, targetCharacterId, ct);
            if (target == null)
                return false;

            me.Friends ??= new Dictionary<string, FriendEntry>(StringComparer.Ordinal);
            target.Friends ??= new Dictionary<string, FriendEntry>(StringComparer.Ordinal);
            me.FriendRequests ??= new Dictionary<string, FriendRequestEntry>(StringComparer.Ordinal);
            target.FriendRequests ??= new Dictionary<string, FriendRequestEntry>(StringComparer.Ordinal);

            // If already mutual friends, nothing to do
            if (me.Friends.ContainsKey(targetCharacterId) && target.Friends.ContainsKey(characterId))
                return true;

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            // Outgoing on me (keyed by target's characterId)
            me.FriendRequests[targetCharacterId] = new FriendRequestEntry
            {
                name = canonicalTargetName ?? targetName,
                direction = "outgoing",
                since = nowMs
            };

            // Incoming on target (keyed by my characterId)
            target.FriendRequests[characterId] = new FriendRequestEntry
            {
                name = me.CharacterName,
                direction = "incoming",
                since = nowMs
            };

            await data.SaveCharacterAsync(me, ct);
            await data.SaveCharacterAsync(target, ct);

            return true;
        }


        public async Task<bool> AcceptFriendRequestAsync(string accountId, string characterId, string fromCharacterId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(fromCharacterId))
                return false;

            using var scope = _scopeFactory.CreateScope();
            var data = scope.ServiceProvider.GetRequiredService<IGameDataProvider>();

            var me = await data.GetCharacterAsync(accountId, characterId, ct);
            if (me == null)
                return false;

            me.FriendRequests ??= new Dictionary<string, FriendRequestEntry>(StringComparer.Ordinal);
            me.Friends ??= new Dictionary<string, FriendEntry>(StringComparer.Ordinal);

            // The key in my FriendRequests is the other character's *characterId*
            if (!me.FriendRequests.TryGetValue(fromCharacterId, out var incomingReq))
                return false;

            if (!string.Equals(incomingReq.direction, "incoming", StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrWhiteSpace(incomingReq.name))
                return false;

            // Resolve the other side via their name (character_names index)
            var nameRecord = await data.GetCharacterByNameAsync(incomingReq.name, ct);
            if (nameRecord == null)
                return false;

            var otherAccountId = nameRecord.AccountId;
            var otherCharacterId = nameRecord.CharacterId;

            // Defensive: ensure the resolved characterId matches what we stored as key
            if (!string.Equals(otherCharacterId, fromCharacterId, StringComparison.Ordinal))
                return false;

            var other = await data.GetCharacterAsync(otherAccountId, otherCharacterId, ct);
            if (other == null)
                return false;

            other.FriendRequests ??= new Dictionary<string, FriendRequestEntry>(StringComparer.Ordinal);
            other.Friends ??= new Dictionary<string, FriendEntry>(StringComparer.Ordinal);

            // Remove pending requests on both sides
            me.FriendRequests.Remove(fromCharacterId);
            other.FriendRequests.Remove(characterId);

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            // Add to friends on both sides (mutual friendship)
            me.Friends[otherCharacterId] = new FriendEntry
            {
                name = incomingReq.name ?? other.CharacterName,
                note = string.Empty,
                since = nowMs
            };

            other.Friends[characterId] = new FriendEntry
            {
                name = me.CharacterName,
                note = string.Empty,
                since = nowMs
            };

            await data.SaveCharacterAsync(me, ct);
            await data.SaveCharacterAsync(other, ct);

            return true;
        }

        public async Task<bool> RejectFriendRequestAsync(string accountId, string characterId, string fromCharacterId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(fromCharacterId))
                return false;

            using var scope = _scopeFactory.CreateScope();
            var data = scope.ServiceProvider.GetRequiredService<IGameDataProvider>();

            var me = await data.GetCharacterAsync(accountId, characterId, ct);
            if (me == null)
                return false;

            me.FriendRequests ??= new Dictionary<string, FriendRequestEntry>(StringComparer.Ordinal);

            if (!me.FriendRequests.TryGetValue(fromCharacterId, out var incomingReq))
                return false;

            if (!string.Equals(incomingReq.direction, "incoming", StringComparison.OrdinalIgnoreCase))
                return false;

            // We'll try to clean up the outgoing entry on the other side too, if we can resolve it
            CharacterData? other = null;

            if (!string.IsNullOrWhiteSpace(incomingReq.name))
            {
                var nameRecord = await data.GetCharacterByNameAsync(incomingReq.name, ct);
                if (nameRecord != null)
                {
                    var otherAccountId = nameRecord.AccountId;
                    var otherCharacterId = nameRecord.CharacterId;

                    // Defensive: ensure IDs match
                    if (string.Equals(otherCharacterId, fromCharacterId, StringComparison.Ordinal))
                    {
                        other = await data.GetCharacterAsync(otherAccountId, otherCharacterId, ct);
                        if (other != null)
                        {
                            other.FriendRequests ??= new Dictionary<string, FriendRequestEntry>(StringComparer.Ordinal);
                            other.FriendRequests.Remove(characterId); // remove their outgoing request
                        }
                    }
                }
            }

            // Remove my incoming request
            var removedFromMe = me.FriendRequests.Remove(fromCharacterId);

            if (!removedFromMe && other == null)
                return false;

            await data.SaveCharacterAsync(me, ct);
            if (other != null)
                await data.SaveCharacterAsync(other, ct);

            return true;
        }

        public async Task<bool> CancelFriendRequestAsync(string accountId, string characterId, string targetCharacterId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(targetCharacterId))
                return false;

            using var scope = _scopeFactory.CreateScope();
            var data = scope.ServiceProvider.GetRequiredService<IGameDataProvider>();

            var me = await data.GetCharacterAsync(accountId, characterId, ct);
            if (me == null)
                return false;

            me.FriendRequests ??= new Dictionary<string, FriendRequestEntry>(StringComparer.Ordinal);

            // Outgoing requests on "me" are keyed by the target's characterId
            if (!me.FriendRequests.TryGetValue(targetCharacterId, out var outgoingReq))
                return false;

            if (!string.Equals(outgoingReq.direction, "outgoing", StringComparison.OrdinalIgnoreCase))
                return false;

            // Try to clean up the matching incoming request on the other side, if possible
            CharacterData? other = null;

            if (!string.IsNullOrWhiteSpace(outgoingReq.name))
            {
                var nameRecord = await data.GetCharacterByNameAsync(outgoingReq.name, ct);
                if (nameRecord != null)
                {
                    var otherAccountId = nameRecord.AccountId;
                    var otherCharacterId = nameRecord.CharacterId;

                    // Defensive: make sure the id matches what we stored as key
                    if (string.Equals(otherCharacterId, targetCharacterId, StringComparison.Ordinal))
                    {
                        other = await data.GetCharacterAsync(otherAccountId, otherCharacterId, ct);
                        if (other != null)
                        {
                            other.FriendRequests ??= new Dictionary<string, FriendRequestEntry>(StringComparer.Ordinal);
                            // On the other side, this should be an "incoming" request keyed by *my* characterId
                            other.FriendRequests.Remove(characterId);
                        }
                    }
                }
            }

            // Remove my outgoing request
            var removedFromMe = me.FriendRequests.Remove(targetCharacterId);

            if (!removedFromMe && other == null)
                return false;

            await data.SaveCharacterAsync(me, ct);
            if (other != null)
                await data.SaveCharacterAsync(other, ct);

            return true;
        }

        public async Task<IReadOnlyList<string>> GetFriendsOfAsync(string characterName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(characterName))
                return Array.Empty<string>();

            using var scope = _scopeFactory.CreateScope();
            var data = scope.ServiceProvider.GetRequiredService<IGameDataProvider>();

            var characterNameRecord = await data.GetCharacterByNameAsync(characterName, ct);

            if (characterNameRecord == null)
                return Array.Empty<string>();

            var character = await data.GetCharacterAsync((string)characterNameRecord.AccountId, (string)characterNameRecord.CharacterId, ct);

            if (character?.Friends == null || character.Friends.Count == 0)
                return Array.Empty<string>();

            return character.Friends.Keys.ToList();
        }
    }
}
