public interface IPresenceService
{
    // game-server level
    void RegisterGameServer(string gameServerId);
    void UnregisterGameServer(string gameServerId);

    // player level
    void AddOrUpdatePlayer(string gameServerId, string characterId, string characterName, string side);
    void RemovePlayer(string gameServerId, string characterId, string characterName);

    // bulk (for fullsync)
    void ReplacePlayersForServer(string gameServerId, IEnumerable<PlayerOnlineDto> players);

    // queries
    OnlinePlayer GetByName(string characterName);
    IReadOnlyCollection<OnlinePlayer> GetAll();
    IReadOnlyCollection<OnlinePlayer> GetOnlineByIds(string[] ids);
    IReadOnlyCollection<OnlinePlayer> GetByServer(string gameServerId);
}
