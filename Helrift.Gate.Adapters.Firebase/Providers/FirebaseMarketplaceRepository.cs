using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Adapters.Firebase;

public sealed class FirebaseMarketplaceRepository : IMarketplaceRepository
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FirebaseMarketplaceRepository(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("firebase-admin");
    }

    private static string OrderPath(string realmId, string orderId)
        => $"realms/{SafeKey(realmId)}/marketplace/orders/byId/{SafeKey(orderId)}.json";

    private static string OrdersRootPath(string realmId)
        => $"realms/{SafeKey(realmId)}/marketplace/orders/byId.json";

    private static string OwnerIndexPath(string realmId, string ownerCharacterId)
        => $"realms/{SafeKey(realmId)}/marketplace/orders/byOwner/{SafeKey(ownerCharacterId)}.json";

    private static string TransactionPath(string realmId, string transactionId)
        => $"realms/{SafeKey(realmId)}/marketplace/transactions/byId/{SafeKey(transactionId)}.json";

    private static string TransactionOrderIndexPath(string realmId, string orderId)
        => $"realms/{SafeKey(realmId)}/marketplace/transactions/byOrder/{SafeKey(orderId)}.json";

    private static string OfferPath(string realmId, string offerId)
        => $"realms/{SafeKey(realmId)}/marketplace/offers/byId/{SafeKey(offerId)}.json";

    private static string OffersRootPath(string realmId)
        => $"realms/{SafeKey(realmId)}/marketplace/offers/byId.json";

    private static string OffersByOrderPath(string realmId, string orderId)
        => $"realms/{SafeKey(realmId)}/marketplace/offers/byOrder/{SafeKey(orderId)}.json";

    private static string OffersByCreatorPath(string realmId, string creatorCharacterId)
        => $"realms/{SafeKey(realmId)}/marketplace/offers/byCreator/{SafeKey(creatorCharacterId)}.json";

    private static string OffersByOwnerPath(string realmId, string ownerCharacterId)
        => $"realms/{SafeKey(realmId)}/marketplace/offers/byOwner/{SafeKey(ownerCharacterId)}.json";

    public async Task<MarketplaceOrder?> GetOrderAsync(string realmId, string orderId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(OrderPath(realmId, orderId), ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        return JsonSerializer.Deserialize<MarketplaceOrder>(json, Json);
    }

    public async Task<MarketplaceOrderRecordSnapshot?> GetOrderSnapshotAsync(string realmId, string orderId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, OrderPath(realmId, orderId));
        req.Headers.TryAddWithoutValidation("X-Firebase-ETag", "true");

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        var record = JsonSerializer.Deserialize<MarketplaceOrder>(json, Json);
        if (record == null) return null;

        var etag = res.Headers.ETag?.Tag;
        if (string.IsNullOrWhiteSpace(etag) && res.Headers.TryGetValues("ETag", out var vals))
            etag = vals.FirstOrDefault();

        return new MarketplaceOrderRecordSnapshot(record, etag);
    }

    public async Task<bool> TryCreateOrderAsync(MarketplaceOrder order, CancellationToken ct)
    {
        var existing = await GetOrderAsync(order.RealmId, order.OrderId, ct);
        if (existing != null) return false;

        var patch = new Dictionary<string, object?>
        {
            [$"realms/{SafeKey(order.RealmId)}/marketplace/orders/byId/{SafeKey(order.OrderId)}"] = order,
            [$"realms/{SafeKey(order.RealmId)}/marketplace/orders/byOwner/{SafeKey(order.OwnerCharacterId)}/{SafeKey(order.OrderId)}"] = ToOwnerIndex(order)
        };

        using var req = new HttpRequestMessage(HttpMethod.Patch, ".json")
        {
            Content = new StringContent(JsonSerializer.Serialize(patch, Json), Encoding.UTF8, "application/json")
        };

        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> TryReplaceOrderAsync(string realmId, MarketplaceOrder order, string? concurrencyToken, CancellationToken ct)
    {
        using var put = new HttpRequestMessage(HttpMethod.Put, OrderPath(realmId, order.OrderId))
        {
            Content = JsonContent.Create(order, options: Json)
        };

        if (!string.IsNullOrWhiteSpace(concurrencyToken))
            put.Headers.TryAddWithoutValidation("If-Match", concurrencyToken);

        using var putRes = await _http.SendAsync(put, ct);
        if (!putRes.IsSuccessStatusCode) return false;

        var indexPath = $"realms/{SafeKey(order.RealmId)}/marketplace/orders/byOwner/{SafeKey(order.OwnerCharacterId)}/{SafeKey(order.OrderId)}.json";
        var indexRes = await _http.PutAsJsonAsync(indexPath, ToOwnerIndex(order), Json, ct);
        return indexRes.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<MarketplaceOrder>> ListAllOrdersAsync(string realmId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(OrdersRootPath(realmId), ct);
        if (!res.IsSuccessStatusCode) return [];

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];

        var dict = JsonSerializer.Deserialize<Dictionary<string, MarketplaceOrder>>(json, Json);
        if (dict == null || dict.Count == 0) return [];

        return dict.Values
            .Where(x => x != null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<MarketplaceOrder>> ListOrdersByOwnerAsync(string realmId, string ownerCharacterId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(OwnerIndexPath(realmId, ownerCharacterId), ct);
        if (!res.IsSuccessStatusCode) return [];

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];

        var dict = JsonSerializer.Deserialize<Dictionary<string, MarketplaceOwnerIndexEntry>>(json, Json);
        if (dict == null || dict.Count == 0) return [];

        var ids = dict.Values
            .Where(x => !string.IsNullOrWhiteSpace(x.OrderId))
            .Select(x => x.OrderId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var records = new List<MarketplaceOrder>(ids.Count);
        foreach (var id in ids)
        {
            var record = await GetOrderAsync(realmId, id, ct);
            if (record != null) records.Add(record);
        }

        return records
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    public async Task<bool> DeleteOrderAsync(string realmId, string orderId, CancellationToken ct)
    {
        var existing = await GetOrderAsync(realmId, orderId, ct);
        if (existing == null) return false;

        var patch = new Dictionary<string, object?>
        {
            [$"realms/{SafeKey(realmId)}/marketplace/orders/byId/{SafeKey(orderId)}"] = null,
            [$"realms/{SafeKey(realmId)}/marketplace/orders/byOwner/{SafeKey(existing.OwnerCharacterId)}/{SafeKey(orderId)}"] = null
        };

        using var req = new HttpRequestMessage(HttpMethod.Patch, ".json")
        {
            Content = new StringContent(JsonSerializer.Serialize(patch, Json), Encoding.UTF8, "application/json")
        };

        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> TryCreateTransactionAsync(MarketplaceTransaction transaction, CancellationToken ct)
    {
        var patch = new Dictionary<string, object?>
        {
            [$"realms/{SafeKey(transaction.RealmId)}/marketplace/transactions/byId/{SafeKey(transaction.TransactionId)}"] = transaction,
            [$"realms/{SafeKey(transaction.RealmId)}/marketplace/transactions/byOrder/{SafeKey(transaction.OrderId)}/{SafeKey(transaction.TransactionId)}"] = transaction
        };

        using var req = new HttpRequestMessage(HttpMethod.Patch, ".json")
        {
            Content = new StringContent(JsonSerializer.Serialize(patch, Json), Encoding.UTF8, "application/json")
        };

        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<MarketplaceTransaction>> ListTransactionsByOrderAsync(string realmId, string orderId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(TransactionOrderIndexPath(realmId, orderId), ct);
        if (!res.IsSuccessStatusCode) return [];

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];

        var dict = JsonSerializer.Deserialize<Dictionary<string, MarketplaceTransaction>>(json, Json);
        if (dict == null || dict.Count == 0) return [];

        return dict.Values
            .Where(x => x != null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    public async Task<MarketplaceOffer?> GetOfferAsync(string realmId, string offerId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(OfferPath(realmId, offerId), ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        return JsonSerializer.Deserialize<MarketplaceOffer>(json, Json);
    }

    public async Task<MarketplaceOfferRecordSnapshot?> GetOfferSnapshotAsync(string realmId, string offerId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, OfferPath(realmId, offerId));
        req.Headers.TryAddWithoutValidation("X-Firebase-ETag", "true");

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        var record = JsonSerializer.Deserialize<MarketplaceOffer>(json, Json);
        if (record == null) return null;

        var etag = res.Headers.ETag?.Tag;
        if (string.IsNullOrWhiteSpace(etag) && res.Headers.TryGetValues("ETag", out var vals))
            etag = vals.FirstOrDefault();

        return new MarketplaceOfferRecordSnapshot(record, etag);
    }

    public async Task<bool> TryCreateOfferAsync(MarketplaceOffer offer, CancellationToken ct)
    {
        var existing = await GetOfferAsync(offer.RealmId, offer.OfferId, ct);
        if (existing != null) return false;

        var patch = new Dictionary<string, object?>
        {
            [$"realms/{SafeKey(offer.RealmId)}/marketplace/offers/byId/{SafeKey(offer.OfferId)}"] = offer,
            [$"realms/{SafeKey(offer.RealmId)}/marketplace/offers/byOrder/{SafeKey(offer.MarketplaceOrderId)}/{SafeKey(offer.OfferId)}"] = ToOfferIndex(offer),
            [$"realms/{SafeKey(offer.RealmId)}/marketplace/offers/byCreator/{SafeKey(offer.OfferCreatorCharacterId)}/{SafeKey(offer.OfferId)}"] = ToOfferIndex(offer),
            [$"realms/{SafeKey(offer.RealmId)}/marketplace/offers/byOwner/{SafeKey(offer.OrderOwnerCharacterId)}/{SafeKey(offer.OfferId)}"] = ToOfferIndex(offer)
        };

        using var req = new HttpRequestMessage(HttpMethod.Patch, ".json")
        {
            Content = new StringContent(JsonSerializer.Serialize(patch, Json), Encoding.UTF8, "application/json")
        };

        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> TryReplaceOfferAsync(string realmId, MarketplaceOffer offer, string? concurrencyToken, CancellationToken ct)
    {
        using var put = new HttpRequestMessage(HttpMethod.Put, OfferPath(realmId, offer.OfferId))
        {
            Content = JsonContent.Create(offer, options: Json)
        };

        if (!string.IsNullOrWhiteSpace(concurrencyToken))
            put.Headers.TryAddWithoutValidation("If-Match", concurrencyToken);

        using var putRes = await _http.SendAsync(put, ct);
        if (!putRes.IsSuccessStatusCode) return false;

        var patch = new Dictionary<string, object?>
        {
            [$"realms/{SafeKey(offer.RealmId)}/marketplace/offers/byOrder/{SafeKey(offer.MarketplaceOrderId)}/{SafeKey(offer.OfferId)}"] = ToOfferIndex(offer),
            [$"realms/{SafeKey(offer.RealmId)}/marketplace/offers/byCreator/{SafeKey(offer.OfferCreatorCharacterId)}/{SafeKey(offer.OfferId)}"] = ToOfferIndex(offer),
            [$"realms/{SafeKey(offer.RealmId)}/marketplace/offers/byOwner/{SafeKey(offer.OrderOwnerCharacterId)}/{SafeKey(offer.OfferId)}"] = ToOfferIndex(offer)
        };

        using var req = new HttpRequestMessage(HttpMethod.Patch, ".json")
        {
            Content = new StringContent(JsonSerializer.Serialize(patch, Json), Encoding.UTF8, "application/json")
        };

        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<MarketplaceOffer>> ListOffersByOrderAsync(string realmId, string orderId, CancellationToken ct)
    {
        var ids = await ListOfferIdsFromIndexAsync(OffersByOrderPath(realmId, orderId), ct);
        return await HydrateOffersAsync(realmId, ids, ct);
    }

    public async Task<IReadOnlyList<MarketplaceOffer>> ListOffersByCreatorAsync(string realmId, string creatorCharacterId, CancellationToken ct)
    {
        var ids = await ListOfferIdsFromIndexAsync(OffersByCreatorPath(realmId, creatorCharacterId), ct);
        return await HydrateOffersAsync(realmId, ids, ct);
    }

    public async Task<IReadOnlyList<MarketplaceOffer>> ListOffersByOwnerAsync(string realmId, string ownerCharacterId, CancellationToken ct)
    {
        var ids = await ListOfferIdsFromIndexAsync(OffersByOwnerPath(realmId, ownerCharacterId), ct);
        return await HydrateOffersAsync(realmId, ids, ct);
    }

    public async Task<IReadOnlyList<MarketplaceOffer>> ListAllOffersAsync(string realmId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(OffersRootPath(realmId), ct);
        if (!res.IsSuccessStatusCode) return [];

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];

        var dict = JsonSerializer.Deserialize<Dictionary<string, MarketplaceOffer>>(json, Json);
        if (dict == null || dict.Count == 0) return [];

        return dict.Values
            .Where(x => x != null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> ListOfferIdsFromIndexAsync(string indexPath, CancellationToken ct)
    {
        using var res = await _http.GetAsync(indexPath, ct);
        if (!res.IsSuccessStatusCode) return [];

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];

        var dict = JsonSerializer.Deserialize<Dictionary<string, MarketplaceOfferIndexEntry>>(json, Json);
        if (dict == null || dict.Count == 0) return [];

        return dict.Values
            .Where(x => !string.IsNullOrWhiteSpace(x.OfferId))
            .Select(x => x.OfferId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<IReadOnlyList<MarketplaceOffer>> HydrateOffersAsync(string realmId, IReadOnlyList<string> ids, CancellationToken ct)
    {
        var records = new List<MarketplaceOffer>(ids.Count);
        foreach (var id in ids)
        {
            var record = await GetOfferAsync(realmId, id, ct);
            if (record != null) records.Add(record);
        }

        return records
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    private static MarketplaceOwnerIndexEntry ToOwnerIndex(MarketplaceOrder order)
        => new()
        {
            OrderId = order.OrderId,
            OrderType = order.OrderType,
            Status = order.Status,
            ItemDefinitionId = order.ItemDefinitionId,
            QuantityRemaining = order.QuantityRemaining,
            UnitPriceGold = order.UnitPriceGold,
            CreatedUnixUtc = new DateTimeOffset(order.CreatedAtUtc).ToUnixTimeSeconds(),
            UpdatedUnixUtc = new DateTimeOffset(order.UpdatedAtUtc).ToUnixTimeSeconds(),
            ExpiresAtUtc = order.ExpiresAtUtc
        };

    private static MarketplaceOfferIndexEntry ToOfferIndex(MarketplaceOffer offer)
        => new()
        {
            OfferId = offer.OfferId,
            MarketplaceOrderId = offer.MarketplaceOrderId,
            Status = offer.Status,
            OfferCreatorCharacterId = offer.OfferCreatorCharacterId,
            OrderOwnerCharacterId = offer.OrderOwnerCharacterId,
            OfferedGold = offer.OfferedGold,
            OfferedItemCount = offer.OfferedAssets?.Count ?? 0,
            CreatedUnixUtc = new DateTimeOffset(offer.CreatedAtUtc).ToUnixTimeSeconds(),
            UpdatedUnixUtc = new DateTimeOffset(offer.UpdatedAtUtc).ToUnixTimeSeconds(),
            ExpiresAtUtc = offer.ExpiresAtUtc
        };

    private static string SafeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "_";
        foreach (var ch in new[] { '/', '.', '#', '$', '[', ']' })
            value = value.Replace(ch, '_');
        return value;
    }

    private sealed class MarketplaceOwnerIndexEntry
    {
        public string OrderId { get; set; } = string.Empty;
        public MarketplaceOrderType OrderType { get; set; }
        public MarketplaceOrderStatus Status { get; set; }
        public string ItemDefinitionId { get; set; } = string.Empty;
        public int QuantityRemaining { get; set; }
        public long UnitPriceGold { get; set; }
        public long CreatedUnixUtc { get; set; }
        public long UpdatedUnixUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
    }

    private sealed class MarketplaceOfferIndexEntry
    {
        public string OfferId { get; set; } = string.Empty;
        public string MarketplaceOrderId { get; set; } = string.Empty;
        public MarketplaceOfferStatus Status { get; set; }
        public string OfferCreatorCharacterId { get; set; } = string.Empty;
        public string OrderOwnerCharacterId { get; set; } = string.Empty;
        public long OfferedGold { get; set; }
        public int OfferedItemCount { get; set; }
        public long CreatedUnixUtc { get; set; }
        public long UpdatedUnixUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
    }
}
