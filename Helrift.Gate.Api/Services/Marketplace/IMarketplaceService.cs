using Helrift.Gate.Contracts;

namespace Helrift.Gate.Api.Services.Marketplace;

public interface IMarketplaceService
{
    Task<MarketplaceOrderSnapshot> CreateSellOrderAsync(CreateMarketplaceSellOrderRequest request, CancellationToken ct = default);
    Task<MarketplaceOrderSnapshot> CreateBuyOrderAsync(CreateMarketplaceBuyOrderRequest request, CancellationToken ct = default);
    Task<MarketplaceOrderSnapshot> FulfillOrderAsync(FulfillMarketplaceOrderRequest request, CancellationToken ct = default);
    Task<MarketplaceOrderSnapshot> CancelOrderAsync(CancelMarketplaceOrderRequest request, CancellationToken ct = default);
    Task<MarketplaceOfferSnapshot> CreateOfferAsync(CreateMarketplaceOfferRequest request, CancellationToken ct = default);
    Task<MarketplaceOfferSnapshot> RespondOfferAsync(RespondMarketplaceOfferRequest request, CancellationToken ct = default);
    Task<MarketplaceOfferSnapshot> CancelOfferAsync(CancelMarketplaceOfferRequest request, CancellationToken ct = default);

    Task<MarketplaceBrowseResult> BrowseAsync(MarketplaceBrowseQuery query, CancellationToken ct = default);
    Task<MarketplaceBrowseResult> GetMyOrdersAsync(string realmId, string ownerCharacterId, int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<MarketplaceOfferBrowseResult> GetOffersByOrderAsync(string realmId, string orderId, bool includeHistory = false, int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<MarketplaceOfferBrowseResult> GetMyOffersAsync(string realmId, string creatorCharacterId, bool includeHistory = true, int page = 1, int pageSize = 50, CancellationToken ct = default);

    Task<MarketplaceAdminSearchResult> SearchAdminAsync(MarketplaceAdminSearchQuery query, CancellationToken ct = default);
    Task<MarketplaceAdminDetail?> GetAdminDetailAsync(string realmId, string orderId, CancellationToken ct = default);
    Task<MarketplaceOfferAdminSearchResult> SearchOffersAdminAsync(MarketplaceOfferAdminSearchQuery query, CancellationToken ct = default);
    Task<MarketplaceOfferAdminDetail?> GetOfferAdminDetailAsync(string realmId, string offerId, CancellationToken ct = default);
}
