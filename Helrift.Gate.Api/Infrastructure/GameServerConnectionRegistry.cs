using Helrift.Gate.Infrastructure;
using System.Collections.Concurrent;
using System.Net.WebSockets;

public sealed class GameServerConnectionRegistry : IGameServerConnectionRegistry
{
    private readonly ConcurrentDictionary<string, WebSocket> _sockets = new();

    public void Add(string serverId, WebSocket socket) => _sockets[serverId] = socket;

    public void Remove(string serverId) => _sockets.TryRemove(serverId, out _);

    public WebSocket? Get(string serverId) => _sockets.TryGetValue(serverId, out WebSocket socket) ? socket : null;

    public IReadOnlyDictionary<string, WebSocket> GetAll() => _sockets;
}
