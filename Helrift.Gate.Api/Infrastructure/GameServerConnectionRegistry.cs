using Helrift.Gate.Infrastructure;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;

public sealed class GameServerConnectionRegistry : IGameServerConnectionRegistry
{
    private readonly ConcurrentDictionary<string, WebSocket> _sockets = new();

    public void Add(string serverId, WebSocket socket) => _sockets[serverId] = socket;

    public void Remove(string serverId) => _sockets.TryRemove(serverId, out _);

    public WebSocket? Get(string serverId) => _sockets.TryGetValue(serverId, out WebSocket socket) ? socket : null;

    public IReadOnlyList<string> GetAllServerIds() => _sockets.Keys.ToList();

    public IReadOnlyDictionary<string, WebSocket> GetAll() => _sockets;

    public async Task BroadcastAsync<T>(T message, CancellationToken ct = default)
    {
        if (_sockets.IsEmpty)
            return;

        var json = JsonConvert.SerializeObject(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        var tasks = new List<Task>();

        foreach (var (serverId, socket) in _sockets)
        {
            if (socket.State == WebSocketState.Open)
            {
                tasks.Add(SendSafeAsync(serverId, socket, bytes, ct));
            }
        }

        await Task.WhenAll(tasks);
    }

    private static async Task SendSafeAsync(string serverId, WebSocket socket, byte[] bytes, CancellationToken ct)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
        }
        catch (Exception)
        {
            // Silently ignore send failures - connection will be cleaned up naturally
        }
    }
}
