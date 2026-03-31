using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Helrift.Gate.Contracts;
using Helrift.Gate.Infrastructure;

namespace Helrift.Gate.Api.Services.Escrow;

public sealed class GameServerEscrowBridge : IGameServerEscrowBridge
{
    private readonly IPresenceService _presence;
    private readonly IGameServerConnectionRegistry _registry;
    private readonly ILogger<GameServerEscrowBridge> _logger;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<GameServerEscrowOperationCompleteRequest>> _pending = new(StringComparer.Ordinal);

    public GameServerEscrowBridge(
        IPresenceService presence,
        IGameServerConnectionRegistry registry,
        ILogger<GameServerEscrowBridge> logger)
    {
        _presence = presence;
        _registry = registry;
        _logger = logger;
    }

    public bool TryGetOnlineServerId(string characterId, out string serverId)
    {
        serverId = string.Empty;
        if (string.IsNullOrWhiteSpace(characterId)) return false;

        var player = _presence.GetAll().FirstOrDefault(p =>
            string.Equals(p.CharacterId, characterId, StringComparison.OrdinalIgnoreCase));

        if (player == null || string.IsNullOrWhiteSpace(player.GameServerId))
            return false;

        serverId = player.GameServerId;
        return true;
    }

    public async Task<CharacterItemData> RequestMoveFromSourceAsync(
        GameServerEscrowMoveFromSourceRequest request,
        CancellationToken ct = default)
    {
        if (!TryGetOnlineServerId(request.SourceCharacterId, out var serverId))
            throw new InvalidOperationException("Source character is not online on any known game server.");

        var result = await SendAndAwaitAsync(serverId, "escrow.move_from_source", request.OperationId, request, ct);
        if (!result.Success)
            throw new InvalidOperationException(result.Error ?? "Game server rejected escrow source move.");

        if (result.ItemPayload == null)
            throw new InvalidOperationException("Game server completed source move without ItemPayload.");

        return result.ItemPayload;
    }

    public async Task RequestGrantToTargetAsync(
        GameServerEscrowGrantToTargetRequest request,
        CancellationToken ct = default)
    {
        if (!TryGetOnlineServerId(request.TargetCharacterId, out var serverId))
            throw new InvalidOperationException("Target character is not online on any known game server.");

        var result = await SendAndAwaitAsync(serverId, "escrow.grant_to_target", request.OperationId, request, ct);
        if (!result.Success)
            throw new InvalidOperationException(result.Error ?? "Game server rejected escrow target grant.");
    }

    public bool Complete(GameServerEscrowOperationCompleteRequest completion)
    {
        if (string.IsNullOrWhiteSpace(completion.OperationId))
            return false;

        if (_pending.TryRemove(completion.OperationId, out var tcs))
        {
            return tcs.TrySetResult(completion);
        }

        return false;
    }

    private async Task<GameServerEscrowOperationCompleteRequest> SendAndAwaitAsync(
        string serverId,
        string messageType,
        string operationId,
        object payload,
        CancellationToken ct)
    {
        var ws = _registry.Get(serverId);
        if (ws == null || ws.State != WebSocketState.Open)
            throw new InvalidOperationException($"Game server '{serverId}' websocket is not connected.");

        var tcs = new TaskCompletionSource<GameServerEscrowOperationCompleteRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(operationId, tcs))
            throw new InvalidOperationException($"Operation '{operationId}' is already pending.");

        try
        {
            var envelope = JsonSerializer.Serialize(new
            {
                type = messageType,
                payload
            });

            var bytes = Encoding.UTF8.GetBytes(envelope);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

            await using var _ = timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));
            return await tcs.Task;
        }
        catch
        {
            _pending.TryRemove(operationId, out _);
            throw;
        }
    }
}