namespace Helrift.Gate.Api.Services.Marketplace;

public sealed class MarketplaceOptions
{
    public decimal ListingFeePercent { get; set; } = 1.0m;
    public decimal CompletionTaxPercent { get; set; } = 5.0m;
    public int DefaultOrderDurationHours { get; set; } = 24;
    public int MaxOrderDurationHours { get; set; } = 72;
    public int MaxActiveOrdersPerCharacter { get; set; } = 20;
    public bool AllowPartialFills { get; set; }
    public bool AllowSelfFulfillment { get; set; }
    public string GoldItemDefinitionId { get; set; } = "gold";

    public bool EnableOffersOnSellOrders { get; set; } = true;
    public bool EnableOffersOnBuyOrders { get; set; }
    public bool AllowOfferGoldMix { get; set; } = true;
    public int DefaultOfferDurationHours { get; set; } = 24;
    public int MaxOfferDurationHours { get; set; } = 72;
    public int MaxPendingOffersPerOrder { get; set; } = 25;
    public int MaxActiveOffersPerCharacter { get; set; } = 30;
    public long OfferSubmissionFeeGold { get; set; }
}