using Helrift.Gate.Contracts;
using Helrift.Gate.App.Domain;
public sealed class PresenceService : IPresenceService
{
    private readonly object _lock = new();
    private readonly Dictionary<string, OnlinePlayer> _onlineByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _playersByServer =
        new(StringComparer.OrdinalIgnoreCase);

    public event Action<OnlinePlayer> PlayerCameOnline;
    public event Action<OnlinePlayer> PlayerWentOffline;

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

            _onlineByName.TryGetValue(characterName, out var existing);
            bool wasOnline = existing != null;

            var online = new OnlinePlayer
            {
                CharacterId = characterId,
                CharacterName = characterName,
                GameServerId = gameServerId,
                Side = side,
                LastSeenUtc = DateTime.UtcNow
            };

            _onlineByName[characterName] = online;

            // 🔔 fire only on transition offline -> online
            if (!wasOnline)
                PlayerCameOnline?.Invoke(online);
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

            if (_onlineByName.TryGetValue(characterName, out var existing) &&
                string.Equals(existing.GameServerId, gameServerId, StringComparison.OrdinalIgnoreCase))
            {
                _onlineByName.Remove(characterName);

                // 🔔 fire only when we actually removed them
                PlayerWentOffline?.Invoke(existing);
            }
        }
    }

    public void ReplacePlayersForServer(string gameServerId, IEnumerable<PlayerOnlineDto> players)
    {
        lock (_lock)
        {
            // old set of names on this server
            var oldNames = _playersByServer.TryGetValue(gameServerId, out var oldSet)
                ? new HashSet<string>(oldSet, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var newSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // build new presence + detect new online for this server
            foreach (var pl in players)
            {
                newSet.Add(pl.CharacterName);

                bool wasOnline = _onlineByName.TryGetValue(pl.CharacterName, out var existing);
                var online = new OnlinePlayer
                {
                    CharacterId = pl.CharacterId,
                    CharacterName = pl.CharacterName,
                    GameServerId = gameServerId,
                    Side = pl.Side,
                    LastSeenUtc = DateTime.UtcNow
                };

                _onlineByName[pl.CharacterName] = online;

                if (!wasOnline)
                    PlayerCameOnline?.Invoke(online);
            }

            // detect which names disappeared from this server
            foreach (var oldName in oldNames)
            {
                if (!newSet.Contains(oldName) &&
                    _onlineByName.TryGetValue(oldName, out var existing) &&
                    string.Equals(existing.GameServerId, gameServerId, StringComparison.OrdinalIgnoreCase))
                {
                    _onlineByName.Remove(oldName);
                    PlayerWentOffline?.Invoke(existing);
                }
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

