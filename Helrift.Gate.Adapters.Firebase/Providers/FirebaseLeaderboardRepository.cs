using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helrift.Gate.Contracts.Leaderboards;

namespace Helrift.Gate.Adapters.Firebase.Leaderboards;

public sealed class FirebaseLeaderboardRepository : ILeaderboardRepository
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FirebaseLeaderboardRepository(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("firebase-admin");
    }

    private static string Path(string rel) => $"realms/{rel}.json";

    public async Task<bool> TryInsertEventAsync(string idempotencyKey, LeaderboardIncrementData data, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return false;

        var safeKey = SafeKey(idempotencyKey);
        var eventPath = Path($"{data.RealmId}/leaderboards/events/{safeKey}");

        using (var check = await _http.GetAsync(eventPath, ct).ConfigureAwait(false))
        {
            if (check.IsSuccessStatusCode)
            {
                var body = await check.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(body) && body != "null")
                    return false;
            }
        }

        var evt = new
        {
            occurred_utc = data.OccurredUtc.ToUniversalTime().ToString("o"),
            realm_id = data.RealmId,
            side = (byte)data.Side,
            metric_key = data.MetricKey,
            subject_type = (byte)data.SubjectType,
            subject_id = data.SubjectId,
            delta = data.Delta,
            source_game_server_id = data.SourceGameServerId
        };

        var put = await _http.PutAsJsonAsync(eventPath, evt, J, ct).ConfigureAwait(false);
        return put.IsSuccessStatusCode;
    }

    public async Task UpsertCounterIncrementAsync(LeaderboardCounterKey key, long delta, DateTime updatedUtc, CancellationToken ct)
    {
        if (delta == 0) return;

        var counterPath = Path(GetCounterRelPath(key));
        const int maxAttempts = 4;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            long current = 0;

            using (var get = await _http.GetAsync(counterPath, ct).ConfigureAwait(false))
            {
                if (get.IsSuccessStatusCode)
                {
                    var json = await get.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(json) && json != "null")
                        if (!TryParseLongFromJson(json, out current)) current = 0;
                }
            }

            var put = await _http.PutAsJsonAsync(counterPath, current + delta, J, ct).ConfigureAwait(false);
            if (put.IsSuccessStatusCode) return;

            await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<(LeaderboardCounterKey Key, long Value)>> GetTopAsync(
        string realmId, SideType side, string metricKey, LeaderboardWindowType window,
        DateTime bucketStartUtc, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);
        var bucketPath = Path(GetBucketSideRelPath(realmId, metricKey, window, bucketStartUtc, side));

        using var res = await _http.GetAsync(bucketPath, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return [];

        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return [];

        var results = new List<(LeaderboardCounterKey, long)>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            long value = 0;
            if (p.Value.ValueKind == JsonValueKind.Number) { if (!p.Value.TryGetInt64(out value)) continue; }
            else if (p.Value.ValueKind == JsonValueKind.String) { if (!long.TryParse(p.Value.GetString(), out value)) continue; }
            else continue;

            results.Add((new LeaderboardCounterKey(
                RealmId: realmId, Side: side, MetricKey: metricKey, Window: window,
                BucketStartUtc: NormalizeBucket(bucketStartUtc),
                SubjectType: LeaderboardSubjectType.Character, SubjectId: p.Name), value));
        }

        results.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return results.Count > limit ? results.Take(limit).ToList() : results;
    }

    public IReadOnlyList<string> GetDistinctMetricKeys()
        => GetDistinctMetricKeysAsync(CancellationToken.None).GetAwaiter().GetResult();

    private async Task<IReadOnlyList<string>> GetDistinctMetricKeysAsync(CancellationToken ct)
    {
        // Use explicit HttpRequestMessage so the auth handler appends its token
        // as an additional param rather than replacing our shallow=true param
        using var req = new HttpRequestMessage(HttpMethod.Get,
            new Uri(_http.BaseAddress!, "realms/default/leaderboards/counters.json?shallow=true"));

        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return [];

        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return [];

        return doc.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .OrderBy(k => k)
            .ToList();
    }

    // ── Path helpers ──────────────────────────────────────────────────────────

    private static string GetBucketSideRelPath(string realmId, string metricKey, LeaderboardWindowType window, DateTime bucketStartUtc, SideType side)
    {
        var bucket = NormalizeBucket(bucketStartUtc).ToString("yyyyMMdd");
        return $"{realmId}/leaderboards/counters/{SafeKey(metricKey)}/{(byte)window}/{bucket}/{(byte)side}";
    }

    private static string GetCounterRelPath(LeaderboardCounterKey key)
        => $"{GetBucketSideRelPath(key.RealmId, key.MetricKey, key.Window, key.BucketStartUtc, key.Side)}/{SafeKey(key.SubjectId)}";

    private static DateTime NormalizeBucket(DateTime dt)
        => dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();

    private static string SafeKey(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "_";
        foreach (var ch in new[] { '/', '.', '#', '$', '[', ']' })
            s = s.Replace(ch, '_');
        return s;
    }

    private static bool TryParseLongFromJson(string raw, out long value)
    {
        value = 0;
        raw = raw.Trim();
        if (long.TryParse(raw, out value)) return true;
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"' && long.TryParse(raw[1..^1], out value)) return true;
        if (double.TryParse(raw, out var d)) { value = (long)d; return true; }
        return false;
    }
}
