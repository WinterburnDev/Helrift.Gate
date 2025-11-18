using Helrift.Gate.App.Domain;
using Helrift.Gate.App.Repositories;
using System.Collections.Concurrent;

namespace Helrift.Gate.Infrastructure.Parties;

public sealed class InMemoryPartyRepository : IPartyDataProvider
{
    // Key: partyId → Party
    private readonly ConcurrentDictionary<string, Party> _parties = new();

    // Key: characterId → partyId
    private readonly ConcurrentDictionary<string, string> _characterIndex = new();

    public Task<Party?> GetByIdAsync(string partyId, CancellationToken ct)
    {
        _parties.TryGetValue(partyId, out var party);
        return Task.FromResult(party);
    }

    public Task<Party?> GetByCharacterIdAsync(string characterId, CancellationToken ct)
    {
        if (_characterIndex.TryGetValue(characterId, out var pid))
        {
            _parties.TryGetValue(pid, out var party);
            return Task.FromResult(party);
        }

        return Task.FromResult<Party?>(null);
    }

    public Task SaveAsync(Party party, CancellationToken ct)
    {
        _parties[party.Id] = party;

        // Build a set of current members for this party
        var currentMemberIds = new HashSet<string>(
            party.Members.Select(m => m.CharacterId),
            StringComparer.OrdinalIgnoreCase);

        // Remove stale index entries for this party
        foreach (var kvp in _characterIndex)
        {
            // kvp.Value == partyId, kvp.Key == characterId
            if (string.Equals(kvp.Value, party.Id, StringComparison.OrdinalIgnoreCase) &&
                !currentMemberIds.Contains(kvp.Key))
            {
                _characterIndex.TryRemove(kvp.Key, out _);
            }
        }

        // Ensure index entries for all current members
        foreach (var member in party.Members)
        {
            _characterIndex[member.CharacterId] = party.Id;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string partyId, CancellationToken ct)
    {
        if (_parties.TryRemove(partyId, out var party) && party != null)
        {
            foreach (var member in party.Members)
            {
                _characterIndex.TryRemove(member.CharacterId, out _);
            }
        }

        return Task.CompletedTask;
    }

    public void ClearIndex(string characterId)
    {
        _characterIndex.TryRemove(characterId, out _);
    }

    public Task<IReadOnlyCollection<Party>> GetAllAsync(CancellationToken ct)
    {
        var all = _parties.Values.ToList();
        return Task.FromResult<IReadOnlyCollection<Party>>(all);
    }
}
