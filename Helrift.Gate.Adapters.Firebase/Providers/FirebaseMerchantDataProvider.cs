// Helrift.Gate.Adapters.Firebase/Providers/FirebaseMerchantDataProvider.cs
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Adapters.Firebase;

public sealed class FirebaseMerchantDataProvider(IHttpClientFactory httpFactory) : IMerchantDataProvider
{
    private readonly HttpClient _http = httpFactory.CreateClient("firebase-admin");
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    private static string NpcPath(string npcId) => $"merchants/{npcId}/listings.json";
    private static string OnePath(string npcId, string listingId) => $"merchants/{npcId}/listings/{listingId}.json";

    public async Task<MerchantPageResult> QueryAsync(string npcId, MerchantQuery q, CancellationToken ct)
    {
        // Fetch all, filter/sort/paginate in-process
        using var res = await _http.GetAsync(NpcPath(npcId), ct);
        var list = new List<MerchantItemRow>();
        if (res.IsSuccessStatusCode)
        {
            var json = await res.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(json) && json != "null")
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var c in doc.RootElement.EnumerateObject())
                    {
                        var row = ParseRow(c.Name, c.Value);
                        if (row != null) list.Add(row);
                    }
                }
            }
        }

        if (!q.IncludeExpired)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            list = list.Where(r => r.ExpireAtUnix <= 0 || r.ExpireAtUnix > now).ToList();
        }

        if (q.ItemType.HasValue)
        {
            list = list.Where(r => r.ItemType == q.ItemType.Value).ToList();
        }

        // ItemType: look for prop "itemType" (string-int) OR a numeric prop "itemTypeId"
        /*if (q.ItemType is int itemTypeId)
        {
            list = list.Where(r =>
            {
                if (r?.Props is null || r.Props.Length == 0) return false;
                foreach (var p in r.Props)
                {
                    if (string.Equals(p.k, "itemType", StringComparison.OrdinalIgnoreCase))
                        if (int.TryParse(p.v, out var v) && v == itemTypeId) return true;
                    if (string.Equals(p.k, "itemTypeId", StringComparison.OrdinalIgnoreCase))
                        if (int.TryParse(p.v, out var v2) && v2 == itemTypeId) return true;
                }
                return false;
            }).ToList();
        }*/

        // Min/MaxStatValue — if set, require ANY stat within that range
        if (q.MinStatValue > 0f || q.MaxStatValue < float.MaxValue)
        {
            list = list.Where(r =>
            {
                if (r?.Stats is null || r.Stats.Length == 0) return false;
                foreach (var s in r.Stats)
                {
                    var val = s.statValue;
                    if (val >= q.MinStatValue && val <= q.MaxStatValue) return true;
                }
                return false;
            }).ToList();
        }

        // StatRanges (AND across filters; within a filter, match the stat type range)
        if (q.StatRanges is { Length: > 0 })
        {
            list = list.Where(r =>
            {
                if (r?.Stats is null || r.Stats.Length == 0) return false;
                // For every range, require at least one matching stat of that type within [min,max]
                foreach (var f in q.StatRanges)
                {
                    var matched = r.Stats.Any(s => (int)s.statType == f.Type && s.statValue >= f.Min && s.statValue <= f.Max);
                    if (!matched) return false;
                }
                return true;
            }).ToList();
        }

        // Sorting
        list = q.Sort switch
        {
            MerchantQuerySort.PriceDesc => list.OrderByDescending(r => r.BuyPriceWithoutTax).ToList(),
            MerchantQuerySort.RarityDesc => list.OrderByDescending(r => (int)r.Rarity).ThenBy(r => r.ListedAtUnix).ToList(),
            MerchantQuerySort.ListedAtDesc => list.OrderByDescending(r => r.ListedAtUnix).ToList(),
            _ => list.OrderBy(r => r.BuyPriceWithoutTax).ToList(), // PriceAsc
        };

        // Paging
        var page = Math.Max(1, q.Page);
        var pageSize = Math.Clamp(q.PageSize, 1, 200);
        var total = list.Count;
        var items = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new MerchantPageResult { Page = page, PageSize = pageSize, Total = total, Items = items };
    }

    public async Task<MerchantItemRow?> GetAsync(string npcId, string listingId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(OnePath(npcId, listingId), ct);
        if (!res.IsSuccessStatusCode) return null;
        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;
        using var doc = JsonDocument.Parse(json);
        return ParseRow(listingId, doc.RootElement);
    }

    public async Task<string?> TryInsertAsync(string npcId, MerchantItemRow row, CancellationToken ct)
    {
        var id = string.IsNullOrWhiteSpace(row.ListingId) ? Guid.NewGuid().ToString("N") : row.ListingId;
        row.ListingId = id;

        // write row as a full object
        var dict = MerchantMapper.ToFirebaseDict(row);
        var res = await _http.PutAsJsonAsync(OnePath(npcId, id), dict, J, ct);
        return res.IsSuccessStatusCode ? id : null;
    }

    public async Task<bool> TryDeleteAsync(string npcId, string listingId, CancellationToken ct)
    {
        using var res = await _http.DeleteAsync(OnePath(npcId, listingId), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<(bool ok, int newQty)> TryDecrementQuantityOrDeleteAsync(string npcId, string listingId, int count, CancellationToken ct)
    {
        // Read-modify-write: best-effort (RTDB admin REST doesn’t have transactions here)
        var row = await GetAsync(npcId, listingId, ct);
        if (row == null) return (false, -1);

        var newQty = Math.Max(0, row.Quantity - Math.Max(1, count));
        if (newQty == 0)
        {
            var del = await TryDeleteAsync(npcId, listingId, ct);
            return del ? (true, 0) : (false, row.Quantity);
        }

        var patch = new Dictionary<string, object> { ["quantity"] = newQty };
        var res = await _http.PatchAsJsonAsync(OnePath(npcId, listingId), patch, J, ct);
        return res.IsSuccessStatusCode ? (true, newQty) : (false, row.Quantity);
    }

    public async Task<bool> TryIncrementQuantityAsync(string npcId, string listingId, int delta, CancellationToken ct)
    {
        if (delta <= 0) return true;
        var row = await GetAsync(npcId, listingId, ct);
        if (row == null) return false;

        var newQty = row.Quantity + delta;
        var patch = new Dictionary<string, object> { ["quantity"] = newQty };
        var res = await _http.PatchAsJsonAsync(OnePath(npcId, listingId), patch, J, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<(bool merged, string? listingId)> TryMergeStackableAsync(string npcId, MerchantItemRow row, CancellationToken ct)
    {
        // naive: find first compatible by ItemId + merge-relevant props
        var existing = await GetAllForMergeAsync(npcId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ct);
        var key = MakeMergeKey(row);
        var target = existing.FirstOrDefault(x => x.Quantity > 0 && x.ItemId == row.ItemId && MakeMergeKey(x) == key);
        if (target == null) return (false, null);

        var ok = await TryIncrementQuantityAsync(npcId, target.ListingId, row.Quantity, ct);
        return ok ? (true, target.ListingId) : (false, null);
    }

    public async Task<int> DeleteExpiredAsync(string npcId, long nowUnix, int maxBatch, CancellationToken ct)
    {
        using var res = await _http.GetAsync(NpcPath(npcId), ct);
        if (!res.IsSuccessStatusCode) return 0;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return 0;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return 0;

        // Collect up to maxBatch expired listing ids
        var expiredIds = new List<string>(Math.Max(1, maxBatch));
        foreach (var c in doc.RootElement.EnumerateObject())
        {
            var row = ParseRow(c.Name, c.Value);
            if (row == null) continue;

            if (row.ExpireAtUnix > 0 && row.ExpireAtUnix <= nowUnix)
            {
                expiredIds.Add(row.ListingId);
                if (expiredIds.Count >= Math.Max(1, maxBatch))
                    break;
            }
        }

        // Delete them
        int n = 0;
        foreach (var id in expiredIds)
            if (await TryDeleteAsync(npcId, id, ct))
                n++;

        return n;
    }

    public async Task<int> TrimOverflowAsync(string npcId, int maxItems, long nowUnix, CancellationToken ct)
    {
        var all = await GetAllForMergeAsync(npcId, nowUnix, ct);
        if (all.Count <= maxItems) return 0;

        var toDelete = all.OrderBy(v => (int)v.Rarity)
                          .ThenBy(v => v.ListedAtUnix)
                          .Take(all.Count - maxItems)
                          .ToList();

        int n = 0;
        foreach (var r in toDelete)
            if (await TryDeleteAsync(npcId, r.ListingId, ct)) n++;
        return n;
    }

    public async Task<IReadOnlyList<MerchantItemRow>> GetAllForMergeAsync(string npcId, long nowUnix, CancellationToken ct)
    {
        using var res = await _http.GetAsync(NpcPath(npcId), ct);
        var list = new List<MerchantItemRow>();
        if (!res.IsSuccessStatusCode) return list;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return list;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;

        foreach (var c in doc.RootElement.EnumerateObject())
        {
            var row = ParseRow(c.Name, c.Value);
            if (row == null) continue;
            if (row.ExpireAtUnix > 0 && row.ExpireAtUnix <= nowUnix) continue;
            list.Add(row);
        }
        return list;
    }

    // -------- helpers --------
    private static MerchantItemRow? ParseRow(string listingId, JsonElement e)
        => MerchantMapper.FromFirebase(listingId, e);

    private static string MakeMergeKey(MerchantItemRow r)
    {
        // mirror your Unity logic
        var parts = new List<string> { r.ItemId };
        if (r.Props != null)
        {
            foreach (var p in r.Props.Where(p => p.k == "spellId").OrderBy(p => p.k, StringComparer.Ordinal))
                parts.Add($"p:{p.k}={p.v ?? ""}");
        }
        return string.Join("|", parts);
    }
}
