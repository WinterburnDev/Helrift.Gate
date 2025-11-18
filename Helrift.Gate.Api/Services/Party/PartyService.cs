using Helrift.Gate.Api.Services;
using Helrift.Gate.Api.Services.Friends;
using Helrift.Gate.App.Domain;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;
using System.IO;

public sealed class PartyService : IPartyService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPartyDataProvider _repo;
    private readonly IFriendsService _friends;

    public event Action<Party, IEnumerable<string>>? PartyChanged;

    public PartyService(IServiceScopeFactory scopeFactory, IPartyDataProvider repo, IFriendsService friends)
    {
        _scopeFactory = scopeFactory;
        _repo = repo;
        _friends = friends;
    }

    public Task<Party?> GetByIdAsync(string partyId, CancellationToken ct)
        => _repo.GetByIdAsync(partyId, ct);

    public Task<Party?> GetByCharacterIdAsync(string characterId, CancellationToken ct)
        => _repo.GetByCharacterIdAsync(characterId, ct);

    public async Task<Party> CreatePartyAsync(CreatePartyRequest request, CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        Enum.TryParse<OwnerSide>(request.Side, true, out var side);
        Enum.TryParse<PartyVisibility>(request.Visibility, true, out var visibility);

        // Only allow Aresden / Elvine
        if (side != OwnerSide.Aresden && side != OwnerSide.Elvine)
            throw new InvalidOperationException($"Invalid side for party: {request.Side}");

        // Member can only be in 1 party
        var existing = await _repo.GetByCharacterIdAsync(request.CharacterId, ct);
        if (existing != null)
            return existing;

        var party = new Party
        {
            LeaderCharacterId = request.CharacterId,
            Visibility = visibility,
            PartyName = request.PartyName,
            Side = side,
            Members =
            {
                new PartyMember
                {
                    CharacterId = request.CharacterId,
                    AccountId = request.AccountId,
                    CharacterName = request.CharacterName
                }
            }
        };

        await _repo.SaveAsync(party, ct);
        RaisePartyChanged(party);
        return party;
    }

    /// <summary>
    /// List parties for a side, filtered by visibility.
    /// Public: always visible.
    /// FriendsOnly: visible to members and to their friends.
    /// </summary>
    public async Task<IReadOnlyCollection<Party>> ListVisiblePartiesAsync(
        OwnerSide side,
        string? accountId,
        string? characterId,
        CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(ct);
        var sideParties = all.Where(p => p.Side == side).ToList();

        // If no viewer known, only show Public parties.
        if (string.IsNullOrWhiteSpace(characterId))
            return sideParties.Where(p => p.Visibility == PartyVisibility.Public).ToList();

        // Lazy-load viewer’s friends only if we hit a FriendsOnly party.
        HashSet<string>? viewerFriends = null;

        var result = new List<Party>();

        foreach (var party in sideParties)
        {
            if (party.Visibility == PartyVisibility.Public)
            {
                result.Add(party);
                continue;
            }

            if (party.Visibility == PartyVisibility.FriendsOnly)
            {
                // Always see parties you are already in.
                if (party.Members.Any(m => m.CharacterId == characterId))
                {
                    result.Add(party);
                    continue;
                }

                // Resolve viewer's friends once.
                if (viewerFriends == null)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var data = scope.ServiceProvider.GetRequiredService<IGameDataProvider>();

                    var caller = await data.GetCharacterAsync(accountId, characterId, ct);
                    if (caller != null)
                    {
                        var friendIds = await _friends.GetFriendsOfAsync(caller.CharacterName, ct);
                        viewerFriends = new HashSet<string>(friendIds ?? Array.Empty<string>(),
                            StringComparer.OrdinalIgnoreCase);
                    }
                }

                if (party.Members.Any(m => viewerFriends.Contains(m.CharacterId)))
                {
                    result.Add(party);
                }
            }
        }

        return result;
    }

    public async Task<IReadOnlyCollection<Party>> ListPartiesAsync(OwnerSide sideFilter, CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(ct);

        // If Any, or Neutral (you might treat these as "no filter")
        if (sideFilter == OwnerSide.Any || sideFilter == OwnerSide.Neutral)
            return all;

        var filtered = all
            .Where(p => p.Side == sideFilter)
            .ToList();

        return filtered;
    }

    public async Task<Party?> JoinPartyAsync(JoinPartyRequest request, CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var party = await _repo.GetByIdAsync(request.PartyId, ct);
        if (party == null)
            return null;

        // If member already in this party, just return it
        if (party.Members.Any(m => m.CharacterId == request.CharacterId))
            return party;

        // Member can only be in 1 party
        var existing = await _repo.GetByCharacterIdAsync(request.CharacterId, ct);
        if (existing != null)
            return existing;

        party.Members.Add(new PartyMember
        {
            CharacterId = request.CharacterId,
            AccountId = request.AccountId,
            CharacterName = request.CharacterName
        });

        await _repo.SaveAsync(party, ct);
        RaisePartyChanged(party);

        return party;
    }

    public async Task<Party?> LeavePartyAsync(LeavePartyRequest request, CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var party = await _repo.GetByCharacterIdAsync(request.CharacterId, ct);
        if (party == null)
            return null;

        // capture all members BEFORE mutation so we can notify everyone, including the leaver
        var previousMemberIds = party.Members
            .Select(m => m.CharacterId)
            .ToArray();

        var member = party.Members.FirstOrDefault(m => m.CharacterId == request.CharacterId);
        if (member == null)
            return party; // nothing to do, but not an error

        party.Members.Remove(member);
        _repo.ClearIndex(request.CharacterId);

        if (party.Members.Count == 0)
        {
            // Disband the party when the last member leaves
            await _repo.DeleteAsync(party.Id, ct);

            // still broadcast, using previous members as recipients
            RaisePartyChanged(party, previousMemberIds);

            return null;
        }

        // If leader left, reassign leader to first remaining member
        if (string.Equals(party.LeaderCharacterId, request.CharacterId, StringComparison.OrdinalIgnoreCase))
        {
            party.LeaderCharacterId = party.Members[0].CharacterId;
        }

        await _repo.SaveAsync(party, ct);

        // broadcast, again using the pre-mutation member set
        RaisePartyChanged(party, previousMemberIds);

        return party;
    }


    public async Task<Party?> SetLeaderAsync(SetLeaderRequest request, CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var party = await _repo.GetByIdAsync(request.PartyId, ct);
        if (party == null)
            return null;

        // Only allow leadership to be assigned to an existing member
        if (!party.Members.Any(m => m.CharacterId == request.NewLeaderCharacterId))
            return party;

        if (!string.Equals(party.LeaderCharacterId, request.NewLeaderCharacterId, StringComparison.OrdinalIgnoreCase))
        {
            party.LeaderCharacterId = request.NewLeaderCharacterId;
            await _repo.SaveAsync(party, ct);
            RaisePartyChanged(party);
        }

        return party;
    }

    public async Task<Party?> KickMemberAsync(KickMemberRequest request, CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var party = await _repo.GetByIdAsync(request.PartyId, ct);
        if (party == null)
            return null;

        // Only leader can kick (for now)
        if (!string.Equals(party.LeaderCharacterId, request.KickerCharacterId, StringComparison.OrdinalIgnoreCase))
            return party;

        var target = party.Members.FirstOrDefault(m => m.CharacterId == request.TargetCharacterId);
        if (target == null)
            return party;

        party.Members.Remove(target);

        if (party.Members.Count == 0)
        {
            await _repo.DeleteAsync(party.Id, ct);
            RaisePartyChanged(party);
            return null;
        }

        // If the kicked member was somehow the leader (shouldn't happen, but just in case)
        if (string.Equals(party.LeaderCharacterId, request.TargetCharacterId, StringComparison.OrdinalIgnoreCase))
        {
            party.LeaderCharacterId = party.Members[0].CharacterId;
        }

        await _repo.SaveAsync(party, ct);
        RaisePartyChanged(party);

        return party;
    }

    private void RaisePartyChanged(Party party, IEnumerable<string>? extraRecipients = null)
    {
        // Fire-and-forget, no try/catch here – let subscribers decide what to do.
        PartyChanged?.Invoke(party, extraRecipients);
    }
}