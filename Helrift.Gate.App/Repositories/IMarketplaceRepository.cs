using Helrift.Gate.Contracts;

namespace Helrift.Gate.App.Repositories;

public sealed record MarketplaceOrderRecordSnapshot(MarketplaceOrder Record, string? ConcurrencyToken);
public sealed record MarketplaceOfferRecordSnapshot(MarketplaceOffer Record, string? ConcurrencyToken);

public interface IMarketplaceRepository
{
    Task<MarketplaceOrder?> GetOrderAsync(string realmId, string orderId, CancellationToken ct);
    Task<MarketplaceOrderRecordSnapshot?> GetOrderSnapshotAsync(string realmId, string orderId, CancellationToken ct);

    Task<bool> TryCreateOrderAsync(MarketplaceOrder order, CancellationToken ct);
    Task<bool> TryReplaceOrderAsync(string realmId, MarketplaceOrder order, string? concurrencyToken, CancellationToken ct);

    Task<IReadOnlyList<MarketplaceOrder>> ListAllOrdersAsync(string realmId, CancellationToken ct);
    Task<IReadOnlyList<MarketplaceOrder>> ListOrdersByOwnerAsync(string realmId, string ownerCharacterId, CancellationToken ct);

    Task<bool> DeleteOrderAsync(string realmId, string orderId, CancellationToken ct);

    Task<bool> TryCreateTransactionAsync(MarketplaceTransaction transaction, CancellationToken ct);
    Task<IReadOnlyList<MarketplaceTransaction>> ListTransactionsByOrderAsync(string realmId, string orderId, CancellationToken ct);

    Task<MarketplaceOffer?> GetOfferAsync(string realmId, string offerId, CancellationToken ct);
    Task<MarketplaceOfferRecordSnapshot?> GetOfferSnapshotAsync(string realmId, string offerId, CancellationToken ct);
    Task<bool> TryCreateOfferAsync(MarketplaceOffer offer, CancellationToken ct);
    Task<bool> TryReplaceOfferAsync(string realmId, MarketplaceOffer offer, string? concurrencyToken, CancellationToken ct);
    Task<IReadOnlyList<MarketplaceOffer>> ListOffersByOrderAsync(string realmId, string orderId, CancellationToken ct);
    Task<IReadOnlyList<MarketplaceOffer>> ListOffersByCreatorAsync(string realmId, string creatorCharacterId, CancellationToken ct);
    Task<IReadOnlyList<MarketplaceOffer>> ListOffersByOwnerAsync(string realmId, string ownerCharacterId, CancellationToken ct);
    Task<IReadOnlyList<MarketplaceOffer>> ListAllOffersAsync(string realmId, CancellationToken ct);
}
