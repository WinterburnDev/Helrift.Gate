using Helrift.Gate.Contracts;

public sealed class PresenceService : IPresenceService
{
    private readonly object _lock = new();
    private readonly Dictionary<string, OnlinePlayer> _onlineByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _playersByServer =
        new(StringComparer.OrdinalIgnoreCase);

    public void RegisterGameServer(string gameServerId)
    {
        if (string.IsNullOrWhiteSpace(gameServerId))
            return;

        lock (_lock)
        {
            if (!_playersByServer.ContainsKey(gameServerId))
                _playersByServer[gameServerId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void UnregisterGameServer(string gameServerId)
    {
        if (string.IsNullOrWhiteSpace(gameServerId))
            return;

        lock (_lock)
        {
            if (_playersByServer.TryGetValue(gameServerId, out var names))
            {
                foreach (var name in names)
                {
                    _onlineByName.Remove(name);
                }
            }
            _playersByServer.Remove(gameServerId);
        }
    }

    public void AddOrUpdatePlayer(string gameServerId, string characterId, string characterName, string side)
    {
        if (string.IsNullOrWhiteSpace(gameServerId) || string.IsNullOrWhiteSpace(characterName))
            return;

        lock (_lock)
        {
            if (!_playersByServer.TryGetValue(gameServerId, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _playersByServer[gameServerId] = set;
            }

            set.Add(characterName);

            _onlineByName[characterName] = new OnlinePlayer
            {
                CharacterId = characterId,
                CharacterName = characterName,
                GameServerId = gameServerId,
                Side = side,
                LastSeenUtc = DateTime.UtcNow
            };
        }
    }

    public void RemovePlayer(string gameServerId, string characterId, string characterName)
    {
        if (string.IsNullOrWhiteSpace(gameServerId) || string.IsNullOrWhiteSpace(characterName))
            return;

        lock (_lock)
        {
            if (_playersByServer.TryGetValue(gameServerId, out var set))
            {
                set.Remove(characterName);
            }

            // only remove if this player was on this GS (defensive)
            if (_onlineByName.TryGetValue(characterName, out var p) &&
                string.Equals(p.GameServerId, gameServerId, StringComparison.OrdinalIgnoreCase))
            {
                _onlineByName.Remove(characterName);
            }
        }
    }

    public void ReplacePlayersForServer(string gameServerId, IEnumerable<PlayerOnlineDto> players)
    {
        lock (_lock)
        {
            // clear old
            if (_playersByServer.TryGetValue(gameServerId, out var old))
            {
                foreach (var name in old)
                {
                    if (_onlineByName.TryGetValue(name, out var p) &&
                        string.Equals(p.GameServerId, gameServerId, StringComparison.OrdinalIgnoreCase))
                    {
                        _onlineByName.Remove(name);
                    }
                }
            }

            var newSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pl in players)
            {
                newSet.Add(pl.CharacterName);
                _onlineByName[pl.CharacterName] = new OnlinePlayer
                {
                    CharacterId = pl.CharacterId,
                    CharacterName = pl.CharacterName,
                    GameServerId = gameServerId,
                    Side = pl.Side,
                    LastSeenUtc = DateTime.UtcNow
                };
            }

            _playersByServer[gameServerId] = newSet;
        }
    }

    public OnlinePlayer GetByName(string characterName)
    {
        lock (_lock)
        {
            _onlineByName.TryGetValue(characterName, out var p);
            return p;
        }
    }

    public IReadOnlyCollection<OnlinePlayer> GetAll()
    {
        lock (_lock)
        {
            return _onlineByName.Values.ToList();
        }
    }

    public IReadOnlyCollection<OnlinePlayer> GetOnlineByIds(string[] ids)
    {
        lock (_lock)
        {
            return _onlineByName.Values.Where(p => ids.Contains(p.CharacterId)).ToList();
        }
    }

    public IReadOnlyCollection<OnlinePlayer> GetByServer(string gameServerId)
    {
        lock (_lock)
        {
            if (_playersByServer.TryGetValue(gameServerId, out var set))
            {
                return set
                    .Select(n => _onlineByName.TryGetValue(n, out var p) ? p : null)
                    .Where(p => p != null)
                    .ToList();
            }
            return Array.Empty<OnlinePlayer>();
        }
    }
}

