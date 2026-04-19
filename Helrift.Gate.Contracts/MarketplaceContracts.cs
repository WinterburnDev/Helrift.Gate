using System;
using System.Collections.Generic;

namespace Helrift.Gate.Contracts;

public enum MarketplaceOrderType : byte
{
    Buy = 1,
    Sell = 2
}

public enum MarketplaceOrderStatus : byte
{
    Active = 1,
    PartiallyFilled = 2,
    Fulfilled = 3,
    Cancelled = 4,
    Expired = 5
}

public enum MarketplaceOfferStatus : byte
{
    Pending = 1,
    Accepted = 2,
    Rejected = 3,
    Cancelled = 4,
    Expired = 5
}

public enum MarketplaceOfferResolutionType : byte
{
    ManualAccept = 1,
    ManualReject = 2,
    CreatorCancelled = 3,
    OfferExpired = 4,
    OrderResolvedElsewhere = 5,
    OrderCancelled = 6,
    OrderExpired = 7
}

public sealed class MarketplaceTaxBreakdown
{
    public long GrossGold { get; set; }
    public long ListingFeeGold { get; set; }
    public long CompletionTaxGold { get; set; }
    public long NetSettlementGold { get; set; }
}

public sealed class MarketplaceOrder
{
    public string OrderId { get; set; } = string.Empty;
    public string RealmId { get; set; } = "default";
    public string IdempotencyKey { get; set; } = string.Empty;

    public MarketplaceOrderType OrderType { get; set; }
    public MarketplaceOrderStatus Status { get; set; } = MarketplaceOrderStatus.Active;

    public string OwnerAccountId { get; set; } = string.Empty;
    public string OwnerCharacterId { get; set; } = string.Empty;

    public string ItemDefinitionId { get; set; } = string.Empty;
    public string? ItemInstanceId { get; set; }

    public int QuantityTotal { get; set; }
    public int QuantityRemaining { get; set; }
    public long UnitPriceGold { get; set; }

    public long ListingFeePaid { get; set; }
    public long CompletionTaxReserved { get; set; }

    public string EscrowContainerId { get; set; } = string.Empty;
    public string? EscrowPrimaryAssetId { get; set; }
    public string? EscrowTaxAssetId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public int PendingOfferCount { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; }
}

public sealed class MarketplaceOfferAsset
{
    public string OfferAssetId { get; set; } = string.Empty;
    public string OfferId { get; set; } = string.Empty;
    public string ItemInstanceId { get; set; } = string.Empty;
    public string ItemDefinitionId { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public string? EscrowAssetId { get; set; }
    public Dictionary<string, string> ItemSnapshot { get; set; } = new(StringComparer.Ordinal);
}

public sealed class MarketplaceOfferResolution
{
    public MarketplaceOfferResolutionType ResolutionType { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime ResolvedAtUtc { get; set; }
    public string ResolvedByCharacterId { get; set; } = string.Empty;
}

public sealed class MarketplaceOffer
{
    public string OfferId { get; set; } = string.Empty;
    public string RealmId { get; set; } = "default";
    public string IdempotencyKey { get; set; } = string.Empty;

    public string MarketplaceOrderId { get; set; } = string.Empty;
    public string OfferCreatorAccountId { get; set; } = string.Empty;
    public string OfferCreatorCharacterId { get; set; } = string.Empty;
    public string OrderOwnerCharacterId { get; set; } = string.Empty;

    public MarketplaceOfferStatus Status { get; set; } = MarketplaceOfferStatus.Pending;

    public string EscrowContainerId { get; set; } = string.Empty;
    public long OfferedGold { get; set; }
    public string? OfferedGoldEscrowAssetId { get; set; }
    public long SubmissionFeeGold { get; set; }

    public List<MarketplaceOfferAsset> OfferedAssets { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? ExpiredAtUtc { get; set; }

    public string? ResponseCharacterId { get; set; }
    public MarketplaceOfferResolution? Resolution { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; }
}

public sealed class MarketplaceOfferSnapshot
{
    public MarketplaceOffer Offer { get; set; } = new();
}

public sealed class MarketplaceOrderSnapshot
{
    public MarketplaceOrder Order { get; set; } = new();
    public MarketplaceTaxBreakdown Tax { get; set; } = new();
}

public sealed class MarketplaceTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string RealmId { get; set; } = "default";
    public string OrderId { get; set; } = string.Empty;
    public MarketplaceOrderType OrderType { get; set; }

    public string BuyerCharacterId { get; set; } = string.Empty;
    public string SellerCharacterId { get; set; } = string.Empty;

    public string ItemDefinitionId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public long UnitPriceGold { get; set; }
    public long GrossGold { get; set; }
    public long TaxGold { get; set; }
    public long NetSettlementGold { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

public sealed class CreateMarketplaceSellOrderRequest
{
    public string RealmId { get; set; } = "default";
    public string IdempotencyKey { get; set; } = string.Empty;

    public string OwnerAccountId { get; set; } = string.Empty;
    public string OwnerCharacterId { get; set; } = string.Empty;

    public string ItemDefinitionId { get; set; } = string.Empty;
    public string ItemInstanceId { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public long UnitPriceGold { get; set; }

    public string SourceInventory { get; set; } = "inventory";
    public string OwnerGoldItemInstanceId { get; set; } = string.Empty;

    public int? DurationHours { get; set; }
}

public sealed class CreateMarketplaceOfferAssetRequest
{
    public string ItemInstanceId { get; set; } = string.Empty;
    public string ItemDefinitionId { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public Dictionary<string, string> ItemSnapshot { get; set; } = new(StringComparer.Ordinal);
}

public sealed class CreateMarketplaceOfferRequest
{
    public string RealmId { get; set; } = "default";
    public string IdempotencyKey { get; set; } = string.Empty;

    public string MarketplaceOrderId { get; set; } = string.Empty;
    public string OfferCreatorAccountId { get; set; } = string.Empty;
    public string OfferCreatorCharacterId { get; set; } = string.Empty;

    public List<CreateMarketplaceOfferAssetRequest> OfferedAssets { get; set; } = [];

    public long OfferedGold { get; set; }
    public string? OfferedGoldItemInstanceId { get; set; }

    public string SourceInventory { get; set; } = "inventory";
    public int? DurationHours { get; set; }
    public string? OfferMessage { get; set; }
}

public sealed class RespondMarketplaceOfferRequest
{
    public string RealmId { get; set; } = "default";
    public string OfferId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;

    public string ResponseAccountId { get; set; } = string.Empty;
    public string ResponseCharacterId { get; set; } = string.Empty;
    public bool Accept { get; set; }
    public string? Summary { get; set; }
}

public sealed class CancelMarketplaceOfferRequest
{
    public string RealmId { get; set; } = "default";
    public string OfferId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;

    public string ActorAccountId { get; set; } = string.Empty;
    public string ActorCharacterId { get; set; } = string.Empty;
    public string? Summary { get; set; }
}

public sealed class CreateMarketplaceBuyOrderRequest
{
    public string RealmId { get; set; } = "default";
    public string IdempotencyKey { get; set; } = string.Empty;

    public string OwnerAccountId { get; set; } = string.Empty;
    public string OwnerCharacterId { get; set; } = string.Empty;

    public string ItemDefinitionId { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public long UnitPriceGold { get; set; }

    public string SourceInventory { get; set; } = "inventory";
    public string OwnerGoldItemInstanceId { get; set; } = string.Empty;

    public int? DurationHours { get; set; }
}

public sealed class FulfillMarketplaceOrderRequest
{
    public string RealmId { get; set; } = "default";
    public string OrderId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;

    public string FulfillerAccountId { get; set; } = string.Empty;
    public string FulfillerCharacterId { get; set; } = string.Empty;

    // Required when fulfilling sell orders (buyer pays with gold item stack)
    public string? BuyerGoldItemInstanceId { get; set; }

    // Required when fulfilling buy orders (seller submits item)
    public string? SellerItemInstanceId { get; set; }
    public string? SellerItemDefinitionId { get; set; }

    public int Quantity { get; set; } = 1;
    public string SourceInventory { get; set; } = "inventory";
    public string TargetInventory { get; set; } = "inventory";
}

public sealed class CancelMarketplaceOrderRequest
{
    public string RealmId { get; set; } = "default";
    public string OrderId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;

    public string ActorAccountId { get; set; } = string.Empty;
    public string ActorCharacterId { get; set; } = string.Empty;

    public bool IsAdminOverride { get; set; }
}

public sealed class MarketplaceBrowseQuery
{
    public string RealmId { get; set; } = "default";
    public MarketplaceOrderType? OrderType { get; set; }
    public MarketplaceOrderStatus? Status { get; set; }
    public string? ItemDefinitionId { get; set; }
    public string? CharacterId { get; set; }

    public string Sort { get; set; } = "newest"; // newest|price_asc|price_desc
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class MarketplaceBrowseResult
{
    public int Total { get; set; }
    public IReadOnlyList<MarketplaceOrderSnapshot> Items { get; set; } = [];
}

public sealed class MarketplaceOfferBrowseQuery
{
    public string RealmId { get; set; } = "default";
    public string? MarketplaceOrderId { get; set; }
    public string? CreatorCharacterId { get; set; }
    public string? OwnerCharacterId { get; set; }
    public string? ItemDefinitionId { get; set; }
    public MarketplaceOfferStatus? Status { get; set; }
    public bool IncludeHistory { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class MarketplaceOfferBrowseResult
{
    public int Total { get; set; }
    public IReadOnlyList<MarketplaceOfferSnapshot> Items { get; set; } = [];
}

public sealed class MarketplaceAdminSearchQuery
{
    public string RealmId { get; set; } = "default";
    public MarketplaceOrderType? OrderType { get; set; }
    public MarketplaceOrderStatus? Status { get; set; }
    public string? CharacterId { get; set; }
    public string? ItemDefinitionId { get; set; }
    public DateTime? CreatedFromUtc { get; set; }
    public DateTime? CreatedToUtc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

public sealed class MarketplaceAdminSearchResult
{
    public int Total { get; set; }
    public IReadOnlyList<MarketplaceOrderSnapshot> Items { get; set; } = [];
}

public sealed class MarketplaceOfferAdminSearchQuery
{
    public string RealmId { get; set; } = "default";
    public MarketplaceOfferStatus? Status { get; set; }
    public string? MarketplaceOrderId { get; set; }
    public string? CharacterId { get; set; }
    public string? ItemDefinitionId { get; set; }
    public DateTime? CreatedFromUtc { get; set; }
    public DateTime? CreatedToUtc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

public sealed class MarketplaceOfferAdminSearchResult
{
    public int Total { get; set; }
    public IReadOnlyList<MarketplaceOfferSnapshot> Items { get; set; } = [];
}

public sealed class MarketplaceAdminDetail
{
    public MarketplaceOrderSnapshot Order { get; set; } = new();
    public EscrowContainer? EscrowContainer { get; set; }
    public EscrowSummary? EscrowSummary { get; set; }
    public IReadOnlyList<MarketplaceTransaction> Transactions { get; set; } = [];
}

public sealed class MarketplaceOfferAdminDetail
{
    public MarketplaceOfferSnapshot Offer { get; set; } = new();
    public MarketplaceOrderSnapshot? Order { get; set; }
    public EscrowContainer? EscrowContainer { get; set; }
    public EscrowSummary? EscrowSummary { get; set; }
}
