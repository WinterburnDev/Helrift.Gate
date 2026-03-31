using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.Escrow;

public interface IGameServerEscrowBridge
{
    bool TryGetOnlineServerId(string characterId, out string serverId);

    Task<CharacterItemData> RequestMoveFromSourceAsync(
        GameServerEscrowMoveFromSourceRequest request,
        CancellationToken ct = default);

    Task RequestGrantToTargetAsync(
        GameServerEscrowGrantToTargetRequest request,
        CancellationToken ct = default);

    bool Complete(GameServerEscrowOperationCompleteRequest completion);
}