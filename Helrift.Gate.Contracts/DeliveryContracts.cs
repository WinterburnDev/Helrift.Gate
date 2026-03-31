using System;
using System.Collections.Generic;

namespace Helrift.Gate.Contracts;

public enum DeliveryType : byte
{
    PlayerMessage = 1,
    Parcel = 2,
    GuildBroadcast = 3,
    SystemDelivery = 4
}

public enum DeliveryChannel : byte
{
    Personal = 1,
    Guild = 2,
    System = 3,
    Rewards = 4
}

public enum DeliveryTargetType : byte
{
    Character = 1,
    Account = 2,
    Guild = 3
}

public enum DeliveryState : byte
{
    PendingValidation = 1,
    Accepted = 2,
    Delivered = 3,
    Read = 4,
    ClaimedPartial = 5,
    ClaimedComplete = 6,
    Expired = 7,
    Returned = 8,
    Failed = 9
}

public enum DeliverySenderType : byte
{
    Character = 1,
    Account = 2,
    Guild = 3,
    System = 4,
    Admin = 5
}

public sealed class DeliverySenderRef
{
    public DeliverySenderType Type { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class DeliveryRecipientRef
{
    public DeliveryTargetType Type { get; set; } = DeliveryTargetType.Character;
    public string Id { get; set; } = string.Empty;
}

public sealed class DeliveryAttachmentRef
{
    public string EscrowAssetId { get; set; } = string.Empty;
    public string ItemInstanceId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public long Quantity { get; set; }
}

public sealed class DeliveryStateTransition
{
    public DeliveryState State { get; set; }
    public DateTime Utc { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public sealed class DeliveryRecord
{
    public string Id { get; set; } = string.Empty;
    public string RealmId { get; set; } = "default";
    public string ThreadId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;

    public DeliveryType Type { get; set; }
    public DeliveryChannel Channel { get; set; }
    public DeliveryState State { get; set; } = DeliveryState.PendingValidation;

    public DeliverySenderRef Sender { get; set; } = new();
    public DeliveryRecipientRef Recipient { get; set; } = new();

    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public string? EscrowContainerId { get; set; }
    public List<DeliveryAttachmentRef> Attachments { get; set; } = [];

    public bool IsRead { get; set; }
    public bool HasUnclaimedEscrowAssets { get; set; }
    public bool IsArchived { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? ReadUtc { get; set; }
    public DateTime? ClaimedUtc { get; set; }
    public DateTime? ExpiresUtc { get; set; }

    public bool ReturnToSenderOnExpiry { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
    public List<DeliveryStateTransition> StateHistory { get; set; } = [];
    public int Version { get; set; }
}

public sealed class DeliveryRecipientIndexEntry
{
    public string DeliveryId { get; set; } = string.Empty;
    public DeliveryType Type { get; set; }
    public DeliveryChannel Channel { get; set; }
    public DeliveryState State { get; set; }

    public bool IsRead { get; set; }
    public bool HasUnclaimedEscrowAssets { get; set; }
    public bool IsArchived { get; set; }

    public string Subject { get; set; } = string.Empty;
    public string SenderDisplayName { get; set; } = string.Empty;

    public long CreatedUnixUtc { get; set; }
    public long UpdatedUnixUtc { get; set; }
    public DateTime? ExpiresUtc { get; set; }
}

public sealed class ParcelItemAttachmentRequest
{
    public string ItemInstanceId { get; set; } = string.Empty;
    public long Quantity { get; set; } = 1;
    public string? ItemId { get; set; } // Optional metadata hint
}

public sealed class CreatePlayerMessageRequest
{
    public string RealmId { get; set; } = "default";
    public string IdempotencyKey { get; set; } = string.Empty;

    public string SenderCharacterId { get; set; } = string.Empty;
    public string SenderDisplayName { get; set; } = string.Empty;

    public string RecipientCharacterId { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime? ExpiresUtc { get; set; }
}

public sealed class CreateParcelDeliveryRequest
{
    public string RealmId { get; set; } = "default";
    public string IdempotencyKey { get; set; } = string.Empty;

    public string SenderAccountId { get; set; } = string.Empty;
    public string SenderCharacterId { get; set; } = string.Empty;
    public string SenderDisplayName { get; set; } = string.Empty;
    public string SourceInventory { get; set; } = "inventory";

    public string RecipientAccountId { get; set; } = string.Empty;
    public string RecipientCharacterId { get; set; } = string.Empty;
    public string RecipientDisplayName { get; set; } = string.Empty;
    public string RecipientInventory { get; set; } = "inventory";

    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public bool ReturnToSenderOnExpiry { get; set; } = true;
    public DateTime? ExpiresUtc { get; set; }

    public string ActorType { get; set; } = "delivery";
    public string ActorId { get; set; } = "delivery-service";

    public List<ParcelItemAttachmentRequest> Attachments { get; set; } = [];
}

public sealed class CreateSystemDeliveryRecipient
{
    public string RecipientAccountId { get; set; } = string.Empty;
    public string RecipientCharacterId { get; set; } = string.Empty;
    public string RecipientInventory { get; set; } = "inventory";
}

public sealed class CreateSystemDeliveryRequest
{
    public string RealmId { get; set; } = "default";
    public string IdempotencyKey { get; set; } = string.Empty;

    public string SenderId { get; set; } = "system";
    public string SenderDisplayName { get; set; } = "System";

    public string RecipientAccountId { get; set; } = string.Empty;
    public string RecipientCharacterId { get; set; } = string.Empty;
    public string RecipientInventory { get; set; } = "inventory";

    public List<CreateSystemDeliveryRecipient> Recipients { get; set; } = [];

    public string? SourceAccountId { get; set; }
    public string? SourceCharacterId { get; set; }
    public string SourceInventory { get; set; } = "inventory";

    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public bool ReturnToSenderOnExpiry { get; set; }
    public DateTime? ExpiresUtc { get; set; }

    public string CreatedByActorType { get; set; } = "system";
    public string CreatedByActorId { get; set; } = "system";

    public List<ParcelItemAttachmentRequest> Attachments { get; set; } = [];
}

public sealed class CreateGuildBroadcastRequest
{
    public string RealmId { get; set; } = "default";
    public string IdempotencyKey { get; set; } = string.Empty;

    public string GuildId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string SenderDisplayName { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime? ExpiresUtc { get; set; }

    public List<string> RecipientCharacterIds { get; set; } = [];
}

public sealed class DeliveryListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 30;
    public bool IncludeArchived { get; set; }
}

public sealed class DeliveryInboxSummary
{
    public string RealmId { get; set; } = "default";
    public string RecipientCharacterId { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int UnreadCount { get; set; }
    public int UnclaimedEscrowCount { get; set; }
    public int ExpiredCount { get; set; }
}

public sealed class DeliveryNotificationSummary
{
    public string RealmId { get; set; } = "default";
    public string RecipientCharacterId { get; set; } = string.Empty;
    public int UnreadCount { get; set; }
    public int UnclaimedEscrowCount { get; set; }
    public bool ShouldNotifyHud { get; set; }
    public Dictionary<string, int> ChannelBreakdown { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MarkDeliveryReadRequest
{
    public string RealmId { get; set; } = "default";
    public string DeliveryId { get; set; } = string.Empty;
    public string RecipientCharacterId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ClaimDeliveryAssetsRequest
{
    public string RealmId { get; set; } = "default";
    public string DeliveryId { get; set; } = string.Empty;
    public string RecipientCharacterId { get; set; } = string.Empty;
    public string RecipientAccountId { get; set; } = string.Empty;
    public string RecipientInventory { get; set; } = "inventory";
    public string IdempotencyKey { get; set; } = string.Empty;
    public List<string>? EscrowAssetIds { get; set; }
}

public sealed class DeliveryAdminSearchQuery
{
    public string RealmId { get; set; } = "default";
    public string? CharacterId { get; set; }
    public string? SenderContains { get; set; }
    public DeliveryType? Type { get; set; }
    public DateTime? CreatedFromUtc { get; set; }
    public DateTime? CreatedToUtc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class DeliveryAdminSearchResult
{
    public int Total { get; set; }
    public IReadOnlyList<DeliveryRecord> Items { get; set; } = [];
}

public sealed class DeliveryAdminDetail
{
    public DeliveryRecord Delivery { get; set; } = new();
    public EscrowContainer? EscrowContainer { get; set; }
    public EscrowSummary? EscrowSummary { get; set; }
}