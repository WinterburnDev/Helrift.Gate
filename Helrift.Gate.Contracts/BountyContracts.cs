using System;
using System.Collections.Generic;

namespace Helrift.Gate.Contracts;

public enum BountyStatus : byte
{
    Active = 1,
    Fulfilled = 2,
    Cancelled = 3,
    Expired = 4
}

public sealed class BountyFulfillmentContext
{
    public string RealmId { get; set; } = "default";
    public string EventId { get; set; } = string.Empty;
    public string TargetCharacterId { get; set; } = string.Empty;
    public string TargetAccountId { get; set; } = string.Empty;
    public string KillerCharacterId { get; set; } = string.Empty;
    public string KillerAccountId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string MapId { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public sealed class BountyContract
{
    public string BountyId { get; set; } = string.Empty;
    public string RealmId { get; set; } = "default";
    public string IdempotencyKey { get; set; } = string.Empty;

    public string IssuerAccountId { get; set; } = string.Empty;
    public string IssuerCharacterId { get; set; } = string.Empty;
    public string TargetAccountId { get; set; } = string.Empty;
    public string TargetCharacterId { get; set; } = string.Empty;

    public BountyStatus Status { get; set; } = BountyStatus.Active;

    public long RewardGold { get; set; }
    public long ListingFeePaid { get; set; }
    public long TaxAmount { get; set; }

    public string EscrowContainerId { get; set; } = string.Empty;
    public string? RewardEscrowAssetId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    public string? FulfilledByAccountId { get; set; }
    public string? FulfilledByCharacterId { get; set; }
    public BountyFulfillmentContext? FulfillmentContext { get; set; }

    public int MaxClaims { get; set; } = 1;

    // Future-facing fields for non-breaking evolution.
    public int? KillCountRequired { get; set; }
    public string? AreaRestriction { get; set; }
    public string? EligibleHunterScope { get; set; }
    public string? IssuedByGuildId { get; set; }
    public string? Notes { get; set; }
    public bool AnonymousIssuer { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; }
}

public sealed class BountyOrderSnapshot
{
    public BountyContract Bounty { get; set; } = new();
}

public sealed class CreateBountyContractRequest
{
    public string RealmId { get; set; } = "default";
    public string IdempotencyKey { get; set; } = string.Empty;

    public string IssuerAccountId { get; set; } = string.Empty;
    public string IssuerCharacterId { get; set; } = string.Empty;

    public string TargetAccountId { get; set; } = string.Empty;
    public string TargetCharacterId { get; set; } = string.Empty;

    public long RewardGold { get; set; }
    public string IssuerGoldItemInstanceId { get; set; } = string.Empty;
    public string SourceInventory { get; set; } = "inventory";

    public int? DurationHours { get; set; }
    public string? Notes { get; set; }
}

public sealed class CancelBountyContractRequest
{
    public string RealmId { get; set; } = "default";
    public string BountyId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;

    public string ActorAccountId { get; set; } = string.Empty;
    public string ActorCharacterId { get; set; } = string.Empty;
    public bool IsAdminOverride { get; set; }
}

public sealed class ResolveBountyKillRequest
{
    public string RealmId { get; set; } = "default";
    public string IdempotencyKey { get; set; } = string.Empty;

    public string EventId { get; set; } = string.Empty;

    public string TargetAccountId { get; set; } = string.Empty;
    public string TargetCharacterId { get; set; } = string.Empty;

    public string KillerAccountId { get; set; } = string.Empty;
    public string KillerCharacterId { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }
    public string MapId { get; set; } = string.Empty;

    public bool IsValidPvpContext { get; set; } = true;
    public bool HasAuthoritativeKiller { get; set; } = true;
}

public sealed class BountyBrowseQuery
{
    public string RealmId { get; set; } = "default";
    public BountyStatus? Status { get; set; }
    public string? TargetCharacterId { get; set; }
    public string? IssuerCharacterId { get; set; }
    public string? FulfilledByCharacterId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class BountyBrowseResult
{
    public int Total { get; set; }
    public IReadOnlyList<BountyOrderSnapshot> Items { get; set; } = [];
}

public sealed class BountyAdminSearchQuery
{
    public string RealmId { get; set; } = "default";
    public BountyStatus? Status { get; set; }
    public string? TargetCharacterId { get; set; }
    public string? IssuerCharacterId { get; set; }
    public string? FulfilledByCharacterId { get; set; }
    public DateTime? CreatedFromUtc { get; set; }
    public DateTime? CreatedToUtc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

public sealed class BountyAdminSearchResult
{
    public int Total { get; set; }
    public IReadOnlyList<BountyOrderSnapshot> Items { get; set; } = [];
}

public sealed class BountyAdminDetail
{
    public BountyOrderSnapshot Bounty { get; set; } = new();
    public EscrowContainer? EscrowContainer { get; set; }
    public EscrowSummary? EscrowSummary { get; set; }
}

public sealed class ResolveBountyKillResult
{
    public string RealmId { get; set; } = "default";
    public string TargetCharacterId { get; set; } = string.Empty;
    public string KillerCharacterId { get; set; } = string.Empty;
    public int BountiesResolved { get; set; }
    public List<string> ResolvedBountyIds { get; set; } = [];
    public List<string> SkippedBountyIds { get; set; } = [];
}
