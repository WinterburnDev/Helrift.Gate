using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Adapters.Firebase;

public sealed class FirebaseBountyRepository : IBountyRepository
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FirebaseBountyRepository(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("firebase-admin");
    }

    private static string BountyPath(string realmId, string bountyId)
        => $"realms/{SafeKey(realmId)}/bounties/contracts/byId/{SafeKey(bountyId)}.json";

    private static string BountiesRootPath(string realmId)
        => $"realms/{SafeKey(realmId)}/bounties/contracts/byId.json";

    private static string TargetIndexPath(string realmId, string targetCharacterId)
        => $"realms/{SafeKey(realmId)}/bounties/contracts/byTarget/{SafeKey(targetCharacterId)}.json";

    private static string IssuerIndexPath(string realmId, string issuerCharacterId)
        => $"realms/{SafeKey(realmId)}/bounties/contracts/byIssuer/{SafeKey(issuerCharacterId)}.json";

    public async Task<BountyContract?> GetBountyAsync(string realmId, string bountyId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(BountyPath(realmId, bountyId), ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        return JsonSerializer.Deserialize<BountyContract>(json, Json);
    }

    public async Task<BountyRecordSnapshot?> GetBountySnapshotAsync(string realmId, string bountyId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, BountyPath(realmId, bountyId));
        req.Headers.TryAddWithoutValidation("X-Firebase-ETag", "true");

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        var record = JsonSerializer.Deserialize<BountyContract>(json, Json);
        if (record == null) return null;

        var etag = res.Headers.ETag?.Tag;
        if (string.IsNullOrWhiteSpace(etag) && res.Headers.TryGetValues("ETag", out var vals))
            etag = vals.FirstOrDefault();

        return new BountyRecordSnapshot(record, etag);
    }

    public async Task<bool> TryCreateBountyAsync(BountyContract bounty, CancellationToken ct)
    {
        var existing = await GetBountyAsync(bounty.RealmId, bounty.BountyId, ct);
        if (existing != null) return false;

        var patch = new Dictionary<string, object?>
        {
            [$"realms/{SafeKey(bounty.RealmId)}/bounties/contracts/byId/{SafeKey(bounty.BountyId)}"] = bounty,
            [$"realms/{SafeKey(bounty.RealmId)}/bounties/contracts/byTarget/{SafeKey(bounty.TargetCharacterId)}/{SafeKey(bounty.BountyId)}"] = ToIndexEntry(bounty),
            [$"realms/{SafeKey(bounty.RealmId)}/bounties/contracts/byIssuer/{SafeKey(bounty.IssuerCharacterId)}/{SafeKey(bounty.BountyId)}"] = ToIndexEntry(bounty)
        };

        using var req = new HttpRequestMessage(HttpMethod.Patch, ".json")
        {
            Content = new StringContent(JsonSerializer.Serialize(patch, Json), Encoding.UTF8, "application/json")
        };

        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> TryReplaceBountyAsync(string realmId, BountyContract bounty, string? concurrencyToken, CancellationToken ct)
    {
        using var put = new HttpRequestMessage(HttpMethod.Put, BountyPath(realmId, bounty.BountyId))
        {
            Content = JsonContent.Create(bounty, options: Json)
        };

        if (!string.IsNullOrWhiteSpace(concurrencyToken))
            put.Headers.TryAddWithoutValidation("If-Match", concurrencyToken);

        using var putRes = await _http.SendAsync(put, ct);
        if (!putRes.IsSuccessStatusCode) return false;

        var targetIndexPath = $"realms/{SafeKey(bounty.RealmId)}/bounties/contracts/byTarget/{SafeKey(bounty.TargetCharacterId)}/{SafeKey(bounty.BountyId)}.json";
        var issuerIndexPath = $"realms/{SafeKey(bounty.RealmId)}/bounties/contracts/byIssuer/{SafeKey(bounty.IssuerCharacterId)}/{SafeKey(bounty.BountyId)}.json";

        var targetRes = await _http.PutAsJsonAsync(targetIndexPath, ToIndexEntry(bounty), Json, ct);
        if (!targetRes.IsSuccessStatusCode) return false;

        var issuerRes = await _http.PutAsJsonAsync(issuerIndexPath, ToIndexEntry(bounty), Json, ct);
        return issuerRes.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<BountyContract>> ListAllBountiesAsync(string realmId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(BountiesRootPath(realmId), ct);
        if (!res.IsSuccessStatusCode) return [];

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];

        var dict = JsonSerializer.Deserialize<Dictionary<string, BountyContract>>(json, Json);
        if (dict == null || dict.Count == 0) return [];

        return dict.Values
            .Where(x => x != null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<BountyContract>> ListBountiesByTargetAsync(string realmId, string targetCharacterId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(TargetIndexPath(realmId, targetCharacterId), ct);
        if (!res.IsSuccessStatusCode) return [];

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];

        var dict = JsonSerializer.Deserialize<Dictionary<string, BountyIndexEntry>>(json, Json);
        if (dict == null || dict.Count == 0) return [];

        var ids = dict.Values
            .Where(x => !string.IsNullOrWhiteSpace(x.BountyId))
            .Select(x => x.BountyId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var records = new List<BountyContract>(ids.Count);
        foreach (var id in ids)
        {
            var record = await GetBountyAsync(realmId, id, ct);
            if (record != null) records.Add(record);
        }

        return records
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<BountyContract>> ListBountiesByIssuerAsync(string realmId, string issuerCharacterId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(IssuerIndexPath(realmId, issuerCharacterId), ct);
        if (!res.IsSuccessStatusCode) return [];

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];

        var dict = JsonSerializer.Deserialize<Dictionary<string, BountyIndexEntry>>(json, Json);
        if (dict == null || dict.Count == 0) return [];

        var ids = dict.Values
            .Where(x => !string.IsNullOrWhiteSpace(x.BountyId))
            .Select(x => x.BountyId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var records = new List<BountyContract>(ids.Count);
        foreach (var id in ids)
        {
            var record = await GetBountyAsync(realmId, id, ct);
            if (record != null) records.Add(record);
        }

        return records
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    public async Task<bool> DeleteBountyAsync(string realmId, string bountyId, CancellationToken ct)
    {
        var existing = await GetBountyAsync(realmId, bountyId, ct);
        if (existing == null) return false;

        var patch = new Dictionary<string, object?>
        {
            [$"realms/{SafeKey(realmId)}/bounties/contracts/byId/{SafeKey(bountyId)}"] = null,
            [$"realms/{SafeKey(realmId)}/bounties/contracts/byTarget/{SafeKey(existing.TargetCharacterId)}/{SafeKey(bountyId)}"] = null,
            [$"realms/{SafeKey(realmId)}/bounties/contracts/byIssuer/{SafeKey(existing.IssuerCharacterId)}/{SafeKey(bountyId)}"] = null
        };

        using var req = new HttpRequestMessage(HttpMethod.Patch, ".json")
        {
            Content = new StringContent(JsonSerializer.Serialize(patch, Json), Encoding.UTF8, "application/json")
        };

        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    private static BountyIndexEntry ToIndexEntry(BountyContract bounty)
        => new()
        {
            BountyId = bounty.BountyId,
            Status = bounty.Status,
            RewardGold = bounty.RewardGold,
            CreatedUnixUtc = new DateTimeOffset(bounty.CreatedAtUtc).ToUnixTimeSeconds(),
            ExpiresAtUtc = bounty.ExpiresAtUtc,
            FulfilledByCharacterId = bounty.FulfilledByCharacterId
        };

    private static string SafeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "_";
        foreach (var ch in new[] { '/', '.', '#', '$', '[', ']' })
            value = value.Replace(ch, '_');
        return value;
    }

    private sealed class BountyIndexEntry
    {
        public string BountyId { get; set; } = string.Empty;
        public BountyStatus Status { get; set; }
        public long RewardGold { get; set; }
        public long CreatedUnixUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public string? FulfilledByCharacterId { get; set; }
    }
}
