using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;
using System.Net.Http.Json;
using System.Text.Json;

namespace Helrift.Gate.Adapters.Firebase;

public sealed class FirebaseBanRepository : IBanRepository
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FirebaseBanRepository(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("firebase-admin");
    }

    public async Task<BanRecord?> GetActiveBanAsync(
        string realmId,
        string steamId,
        string ipAddress,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        BanRecord? steamBan = null;
        BanRecord? ipBan = null;

        if (!string.IsNullOrWhiteSpace(steamId))
        {
            using var res = await _http.GetAsync(
                $"realms/{realmId}/bans/bySteam/{steamId}.json", ct);

            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(ct);
                if (!string.IsNullOrWhiteSpace(json) && json != "null")
                    steamBan = JsonSerializer.Deserialize<BanRecord>(json, Json);
            }
        }

        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            using var res = await _http.GetAsync(
                $"realms/{realmId}/bans/byIp/{ipAddress}.json", ct);

            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(ct);
                if (!string.IsNullOrWhiteSpace(json) && json != "null")
                    ipBan = JsonSerializer.Deserialize<BanRecord>(json, Json);
            }
        }

        var candidate = steamBan ?? ipBan;
        if (candidate == null)
            return null;

        if (candidate.ExpiresAtUnixUtc.HasValue &&
            candidate.ExpiresAtUnixUtc.Value <= now)
            return null;

        return candidate;
    }

    public async Task SaveBanAsync(BanRecord ban, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ban.RealmId))
            ban.RealmId = "default";

        var realmId = ban.RealmId;

        if (!string.IsNullOrWhiteSpace(ban.SteamId))
        {
            var path = $"realms/{realmId}/bans/bySteam/{ban.SteamId}.json";
            var put = await _http.PutAsJsonAsync(path, ban, Json, ct);
            put.EnsureSuccessStatusCode();
        }

        if (!string.IsNullOrWhiteSpace(ban.IpAddress))
        {
            var path = $"realms/{realmId}/bans/byIp/{ban.IpAddress}.json";
            var put = await _http.PutAsJsonAsync(path, ban, Json, ct);
            put.EnsureSuccessStatusCode();
        }
    }

    public async Task<IReadOnlyList<BanRecord>> ListActiveBansAsync(string realmId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var results = new List<BanRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Fetch all bySteam entries
        using var steamRes = await _http.GetAsync($"realms/{realmId}/bans/bySteam.json", ct);
        if (steamRes.IsSuccessStatusCode)
        {
            var json = await steamRes.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(json) && json != "null")
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, BanRecord>>(json, Json);
                if (dict != null)
                {
                    foreach (var (key, record) in dict)
                    {
                        if (record is null) continue;
                        if (record.ExpiresAtUnixUtc.HasValue && record.ExpiresAtUnixUtc.Value <= now) continue;
                        results.Add(record);
                        if (!string.IsNullOrWhiteSpace(record.SteamId))
                            seen.Add("steam:" + record.SteamId);
                    }
                }
            }
        }

        // Fetch all byIp entries (skip any already captured via SteamId)
        using var ipRes = await _http.GetAsync($"realms/{realmId}/bans/byIp.json", ct);
        if (ipRes.IsSuccessStatusCode)
        {
            var json = await ipRes.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(json) && json != "null")
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, BanRecord>>(json, Json);
                if (dict != null)
                {
                    foreach (var (key, record) in dict)
                    {
                        if (record is null) continue;
                        if (record.ExpiresAtUnixUtc.HasValue && record.ExpiresAtUnixUtc.Value <= now) continue;
                        // Skip IP-only entries that were already captured as a steam ban
                        if (!string.IsNullOrWhiteSpace(record.SteamId) && seen.Contains("steam:" + record.SteamId)) continue;
                        results.Add(record);
                    }
                }
            }
        }

        return results.OrderByDescending(b => b.BannedAtUnixUtc).ToList();
    }

    public async Task RevokeBanAsync(string realmId, string? steamId, string? ipAddress, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(steamId))
        {
            var path = $"realms/{realmId}/bans/bySteam/{steamId}.json";
            await _http.DeleteAsync(path, ct);
        }

        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            // Sanitise dots — Firebase keys cannot contain dots; encode them
            var safeIp = ipAddress.Replace(".", ",");
            var path = $"realms/{realmId}/bans/byIp/{safeIp}.json";
            await _http.DeleteAsync(path, ct);
        }
    }
}
