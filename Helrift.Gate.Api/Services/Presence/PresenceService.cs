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

        List<OnlinePlayer> wentOffline = null;

        lock (_lock)
        {
            if (_playersByServer.TryGetValue(gameServerId, out var names))
            {
                foreach (var name in names)
                {
                    if (_onlineByName.TryGetValue(name, out var existing) &&
                        string.Equals(existing.GameServerId, gameServerId, StringComparison.OrdinalIgnoreCase))
                    {
                        _onlineByName.Remove(name);

                        wentOffline ??= new List<OnlinePlayer>();
                        wentOffline.Add(existing);
                    }
                    else
                    {
                        _onlineByName.Remove(name);
                    }
                }
            }

            _playersByServer.Remove(gameServerId);
        }

        // fire outside lock
        if (wentOffline != null)
        {
            foreach (var p in wentOffline)
                PlayerWentOffline?.Invoke(p);
        }
    }

    public void AddOrUpdatePlayer(string gameServerId, string characterId, string characterName, string side)
    {
        if (string.IsNullOrWhiteSpace(gameServerId) || string.IsNullOrWhiteSpace(characterName))
            return;

        OnlinePlayer cameOnline = null;

        lock (_lock)
        {
            if (!_playersByServer.TryGetValue(gameServerId, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _playersByServer[gameServerId] = set;
            }

            set.Add(characterName);

            var wasOnline = _onlineByName.ContainsKey(characterName);

            var online = new OnlinePlayer
            {
                CharacterId = characterId,
                CharacterName = characterName,
                GameServerId = gameServerId,
                Side = side,
                LastSeenUtc = DateTime.UtcNow
            };

            _onlineByName[characterName] = online;

            // capture event outside lock
            if (!wasOnline)
                cameOnline = online;
        }

        if (cameOnline != null)
            PlayerCameOnline?.Invoke(cameOnline);
    }

    public void RemovePlayer(string gameServerId, string characterId, string characterName)
    {
        if (string.IsNullOrWhiteSpace(gameServerId) || string.IsNullOrWhiteSpace(characterName))
            return;

        OnlinePlayer wentOffline = null;

        lock (_lock)
        {
            if (_playersByServer.TryGetValue(gameServerId, out var set))
                set.Remove(characterName);

            if (_onlineByName.TryGetValue(characterName, out var existing) &&
                string.Equals(existing.GameServerId, gameServerId, StringComparison.OrdinalIgnoreCase))
            {
                _onlineByName.Remove(characterName);

                // capture event outside lock
                wentOffline = existing;
            }
        }

        if (wentOffline != null)
            PlayerWentOffline?.Invoke(wentOffline);
    }

    public void ReplacePlayersForServer(string gameServerId, IEnumerable<PlayerOnlineDto> players)
    {
        if (string.IsNullOrWhiteSpace(gameServerId))
            return;

        List<OnlinePlayer> cameOnline = null;
        List<OnlinePlayer> wentOffline = null;

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
                if (pl == null || string.IsNullOrWhiteSpace(pl.CharacterName))
                    continue;

                newSet.Add(pl.CharacterName);

                var wasOnline = _onlineByName.ContainsKey(pl.CharacterName);

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
                {
                    cameOnline ??= new List<OnlinePlayer>();
                    cameOnline.Add(online);
                }
            }

            // detect which names disappeared from this server
            foreach (var oldName in oldNames)
            {
                if (newSet.Contains(oldName))
                    continue;

                if (_onlineByName.TryGetValue(oldName, out var existing) &&
                    string.Equals(existing.GameServerId, gameServerId, StringComparison.OrdinalIgnoreCase))
                {
                    _onlineByName.Remove(oldName);

                    wentOffline ??= new List<OnlinePlayer>();
                    wentOffline.Add(existing);
                }
            }

            _playersByServer[gameServerId] = newSet;
        }

        // fire outside lock
        if (cameOnline != null)
        {
            foreach (var p in cameOnline)
                PlayerCameOnline?.Invoke(p);
        }

        if (wentOffline != null)
        {
            foreach (var p in wentOffline)
                PlayerWentOffline?.Invoke(p);
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
