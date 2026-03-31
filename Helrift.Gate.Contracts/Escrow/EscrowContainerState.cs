using System;
using System.Collections.Generic;

namespace Helrift.Gate.Contracts;

public enum EscrowContainerState : byte
{
    Created = 1,
    Pending = 2,
    Active = 3,
    Resolved = 4,
    Expired = 5,
    Cancelled = 6,
    Failed = 7
}

public enum EscrowAssetState : byte
{
    Reserved = 1,
    Escrowed = 2,
    Claimable = 3,
    Claimed = 4,
    Returned = 5,
    Forfeited = 6,
    Expired = 7
}

public enum EscrowAssetType : byte
{
    ItemInstance = 1,
    Currency = 2,
    PointBalance = 3
}

public enum EscrowHoldingMode : byte
{
    Reserved = 1,
    Escrowed = 2
}

public enum EscrowParticipantRole : byte
{
    Source = 1,
    Recipient = 2,
    Controller = 3,
    Winner = 4,
    Seller = 5,
    Buyer = 6
}

public enum EscrowParticipantType : byte
{
    Character = 1,
    Account = 2,
    Guild = 3,
    System = 4,
    Admin = 5
}

public enum EscrowResolutionPolicy : byte
{
    Manual = 1,
    ReturnToSource = 2,
    ForfeitOnExpiry = 3,
    DestroyOnExpiry = 4
}

public sealed class EscrowContainer
{
    public string Id { get; set; } = string.Empty;
    public string RealmId { get; set; } = "default";
    public string EscrowType { get; set; } = string.Empty;
    public string SourceFeature { get; set; } = string.Empty;
    public string SourceEntityType { get; set; } = string.Empty;
    public string SourceEntityId { get; set; } = string.Empty;
    public EscrowContainerState State { get; set; } = EscrowContainerState.Created;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? ExpiresUtc { get; set; }
    public EscrowResolutionPolicy ResolutionPolicy { get; set; } = EscrowResolutionPolicy.Manual;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
    public List<EscrowParticipant> Participants { get; set; } = [];
    public List<EscrowAsset> Assets { get; set; } = [];
    public List<EscrowAuditEntry> AuditEntries { get; set; } = [];
    public List<EscrowOperation> Operations { get; set; } = [];
    public int Version { get; set; }
}

public sealed class EscrowParticipant
{
    public string Id { get; set; } = string.Empty;
    public EscrowParticipantRole Role { get; set; }
    public EscrowParticipantType Type { get; set; }
    public string ReferenceId { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
}

public sealed class EscrowAsset
{
    public string Id { get; set; } = string.Empty;
    public EscrowAssetType AssetType { get; set; }
    public EscrowHoldingMode HoldingMode { get; set; } = EscrowHoldingMode.Escrowed;
    public EscrowAssetState State { get; set; } = EscrowAssetState.Reserved;
    public string SubtypeKey { get; set; } = string.Empty;
    public long QuantityValue { get; set; }
    public string? SourceParticipantId { get; set; }
    public string? BeneficiaryParticipantId { get; set; }

    // Source/recipient resolution info (feature-level caller can override at action time)
    public string? SourceAccountId { get; set; }
    public string? SourceCharacterId { get; set; }
    public string? SourceInventory { get; set; } // inventory|warehouse
    public string? RecipientAccountId { get; set; }
    public string? RecipientCharacterId { get; set; }
    public string? RecipientInventory { get; set; } // inventory|warehouse

    // Item payload (full-fidelity instance)
    public string? ItemInstanceId { get; set; }
    public CharacterItemData? ItemInstancePayload { get; set; }

    // Balance payload
    public string? BalanceKey { get; set; }

    public DateTime? ClaimableUtc { get; set; }
    public DateTime? ClaimedUtc { get; set; }
    public DateTime? ReturnedUtc { get; set; }
    public DateTime? ForfeitedUtc { get; set; }
    public DateTime? ExpiredUtc { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
}

public sealed class EscrowOperation
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string ActorType { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string Status { get; set; } = "completed";
    public string? Error { get; set; }
}

public sealed class EscrowAuditEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ContainerId { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? SourceParticipantId { get; set; }
    public string? BeneficiaryParticipantId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public DateTime Utc { get; set; }
    public string? ItemInstanceId { get; set; }
    public string? BalanceKey { get; set; }
    public long? BalanceAmount { get; set; }
    public string? MetadataJson { get; set; }
}

public sealed class CreateEscrowContainerRequest
{
    public string RealmId { get; set; } = "default";
    public string? ContainerId { get; set; }
    public string EscrowType { get; set; } = string.Empty;
    public string SourceFeature { get; set; } = string.Empty;
    public string SourceEntityType { get; set; } = string.Empty;
    public string SourceEntityId { get; set; } = string.Empty;
    public DateTime? ExpiresUtc { get; set; }
    public EscrowResolutionPolicy ResolutionPolicy { get; set; } = EscrowResolutionPolicy.Manual;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
    public List<EscrowParticipant> Participants { get; set; } = [];
}

public sealed class EscrowAssetDraft
{
    public EscrowAssetType AssetType { get; set; }
    public EscrowHoldingMode HoldingMode { get; set; } = EscrowHoldingMode.Escrowed;
    public string SubtypeKey { get; set; } = string.Empty;
    public long QuantityValue { get; set; } = 1;
    public string? SourceParticipantId { get; set; }
    public string? BeneficiaryParticipantId { get; set; }

    public string? SourceAccountId { get; set; }
    public string? SourceCharacterId { get; set; }
    public string? SourceInventory { get; set; }

    public string? RecipientAccountId { get; set; }
    public string? RecipientCharacterId { get; set; }
    public string? RecipientInventory { get; set; }

    public string? ItemInstanceId { get; set; }
    public string? BalanceKey { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
}

public sealed class AddEscrowAssetsRequest
{
    public string RealmId { get; set; } = "default";
    public string ContainerId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string ActorType { get; set; } = "system";
    public string ActorId { get; set; } = "system";
    public List<EscrowAssetDraft> Assets { get; set; } = [];
}

public sealed class EscrowActionRequest
{
    public string RealmId { get; set; } = "default";
    public string ContainerId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string ActorType { get; set; } = "system";
    public string ActorId { get; set; } = "system";
    public IReadOnlyList<string>? AssetIds { get; set; }

    // Optional target override
    public string? TargetAccountId { get; set; }
    public string? TargetCharacterId { get; set; }
    public string? TargetInventory { get; set; } // inventory|warehouse
}

public sealed class EscrowSummary
{
    public string ContainerId { get; set; } = string.Empty;
    public string RealmId { get; set; } = "default";
    public EscrowContainerState State { get; set; }
    public int TotalAssets { get; set; }
    public int ClaimableAssets { get; set; }
    public int ClaimedAssets { get; set; }
    public int ReturnedAssets { get; set; }
    public int ExpiredAssets { get; set; }
    public DateTime UpdatedUtc { get; set; }
}