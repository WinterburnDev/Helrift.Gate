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

    /// <summary>
    /// RTDB layout suggestion:
    /// /realms/{realmId}/bans/bySteam/{steamId}.json
    /// /realms/{realmId}/bans/byIp/{ip}.json
    /// </summary>
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
                {
                    steamBan = JsonSerializer.Deserialize<BanRecord>(json, Json);
                }
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
                {
                    ipBan = JsonSerializer.Deserialize<BanRecord>(json, Json);
                }
            }
        }

        // Prefer Steam ban over IP ban if both exist
        var candidate = steamBan ?? ipBan;
        if (candidate == null)
            return null;

        if (candidate.ExpiresAtUnixUtc.HasValue &&
            candidate.ExpiresAtUnixUtc.Value <= now)
        {
            // Ban expired; keep in Firebase as history but don't enforce
            return null;
        }

        return candidate;
    }

    public async Task SaveBanAsync(BanRecord ban, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ban.RealmId))
            ban.RealmId = "default";

        var realmId = ban.RealmId;

        // You can choose to always write both, or only one depending on which key is present.
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
}
