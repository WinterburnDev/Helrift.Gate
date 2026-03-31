namespace Helrift.Gate.Contracts;

public sealed class GameServerEscrowMoveFromSourceRequest
{
    public string OperationId { get; set; } = string.Empty;
    public string RealmId { get; set; } = "default";
    public string SourceAccountId { get; set; } = string.Empty;
    public string SourceCharacterId { get; set; } = string.Empty;
    public string SourceInventory { get; set; } = "inventory";
    public string ItemInstanceId { get; set; } = string.Empty;
    public long Quantity { get; set; } = 1;
}

public sealed class GameServerEscrowGrantToTargetRequest
{
    public string OperationId { get; set; } = string.Empty;
    public string RealmId { get; set; } = "default";
    public string TargetAccountId { get; set; } = string.Empty;
    public string TargetCharacterId { get; set; } = string.Empty;
    public string TargetInventory { get; set; } = "inventory";
    public CharacterItemData ItemPayload { get; set; } = new();
}

public sealed class GameServerEscrowOperationCompleteRequest
{
    public string OperationId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public CharacterItemData? ItemPayload { get; set; } // required for move-from-source success
}