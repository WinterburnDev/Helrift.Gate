// Infrastructure/GameServerConnectionRegistry.cs
using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Helrift.Gate.Infrastructure;

public interface IGameServerConnectionRegistry
{
    void Add(string serverId, WebSocket socket);
    void Remove(string serverId);
    WebSocket? Get(string serverId);
    IReadOnlyDictionary<string, WebSocket> GetAll();
}