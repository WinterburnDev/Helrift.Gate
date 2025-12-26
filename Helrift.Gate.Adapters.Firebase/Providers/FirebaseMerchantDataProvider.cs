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
    private static string OnePath(string npcId, string listingId) => $"{OnePathNoJson(npcId, listingId)}.json";

    private static string OnePathNoJson(string npcId, string listingId) => $"merchants/{npcId}/listings/{listingId}";

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

        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            Debug.WriteLine("Merchant insert failed. npcId={NpcId} id={Id} status={Status} body={Body}",
                npcId, id, (int)res.StatusCode, body);
        }

        return res.IsSuccessStatusCode ? id : null;
    }

    public async Task<bool> TryDeleteAsync(string npcId, string listingId, CancellationToken ct)
    {
        using var res = await _http.DeleteAsync(OnePath(npcId, listingId), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<(bool ok, int newQty)> TryDecrementQuantityOrDeleteAsync(
    string npcId,
    string listingId,
    int count,
    CancellationToken ct)
    {
        if (count <= 0) return (false, -1);

        const int maxAttempts = 6;
        int dec = Math.Max(1, count);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var (row, etag) = await GetWithEtagAsync(npcId, listingId, ct);
            if (row == null) return (false, -1);

            if (row.Quantity <= 0) return (false, 0);

            int newQty = Math.Max(0, row.Quantity - dec);

            HttpRequestMessage writeReq;

            if (newQty == 0)
            {
                // RTDB: PUT null deletes the node (and works nicely with If-Match)
                writeReq = new HttpRequestMessage(HttpMethod.Put, OnePath(npcId, listingId))
                {
                    Content = JsonContent.Create<object?>(null, options: J)
                };
            }
            else
            {
                var patch = new Dictionary<string, object> { ["quantity"] = newQty };
                writeReq = new HttpRequestMessage(new HttpMethod("PATCH"), OnePath(npcId, listingId))
                {
                    Content = JsonContent.Create(patch, options: J)
                };
            }

            if (!string.IsNullOrWhiteSpace(etag))
                writeReq.Headers.TryAddWithoutValidation("If-Match", etag);

            using var res = await _http.SendAsync(writeReq, ct);

            if (res.IsSuccessStatusCode)
                return (true, newQty);

            // 412 = someone else changed it. Retry.
            if ((int)res.StatusCode == 412)
                continue;

            // Anything else: fail closed (GS refunds)
            return (false, row.Quantity);
        }

        // Too much contention: fail closed
        return (false, -1);
    }



    public async Task<bool> TryIncrementQuantityAsync(string npcId, string listingId, int delta, CancellationToken ct)
    {
        if (delta <= 0) return true;

        const int maxAttempts = 6;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var (row, etag) = await GetWithEtagAsync(npcId, listingId, ct);
            if (row == null) return false;

            int newQty = row.Quantity + delta;

            var patch = new Dictionary<string, object> { ["quantity"] = newQty };

            using var req = new HttpRequestMessage(new HttpMethod("PATCH"), OnePath(npcId, listingId))
            {
                Content = JsonContent.Create(patch, options: J)
            };

            if (!string.IsNullOrWhiteSpace(etag))
                req.Headers.TryAddWithoutValidation("If-Match", etag);

            using var res = await _http.SendAsync(req, ct);

            if (res.IsSuccessStatusCode) return true;
            if ((int)res.StatusCode == 412) continue;

            return false;
        }

        return false;
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

    private async Task<(MerchantItemRow? row, string? etag)> GetWithEtagAsync(
    string npcId,
    string listingId,
    CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, OnePath(npcId, listingId));
        req.Headers.TryAddWithoutValidation("X-Firebase-ETag", "true");

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return (null, null);

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return (null, null);

        using var doc = JsonDocument.Parse(json);
        var row = ParseRow(listingId, doc.RootElement);

        // Prefer strongly-typed ETag if present
        var etag = res.Headers.ETag?.Tag;

        // Fallback: raw header lookup
        if (string.IsNullOrWhiteSpace(etag) && res.Headers.TryGetValues("ETag", out var vals))
            etag = vals.FirstOrDefault();

        return (row, etag);
    }

    public async Task<bool> TryApplySellEventAsync(string npcId, MerchantSellEvent evt, CancellationToken ct)
    {
        if (evt?.Rows == null || evt.Rows.Length == 0)
            return true;

        // Option B: best-effort apply. We do NOT guarantee atomicity across the batch.
        // We also keep it simple: try merge if stackable-ish, else insert.
        // (Later, for better merges, include MaxStack in payload.)
        foreach (var row in evt.Rows)
        {
            ct.ThrowIfCancellationRequested();

            if (row == null) continue;
            if (!string.Equals(row.NpcId, npcId, StringComparison.Ordinal))
            {
                // Don't let clients write to another NPC by mistake.
                return false;
            }

            bool stored = false;

            // Heuristic: if Quantity > 1, likely stackable; attempt merge then insert fallback.
            if (row.Quantity > 1)
            {
                var (merged, _) = await TryMergeStackableAsync(npcId, row, ct);
                if (merged) stored = true;
            }

            if (!stored)
            {
                var id = await TryInsertAsync(npcId, row, ct);
                stored = !string.IsNullOrWhiteSpace(id);
            }

            if (!stored)
                return false;
        }

        return true;
    }
}
