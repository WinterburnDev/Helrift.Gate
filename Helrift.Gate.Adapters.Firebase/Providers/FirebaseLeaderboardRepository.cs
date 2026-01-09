using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helrift.Gate.Contracts.Leaderboards;
using Helrift.Gate.Services.Leaderboards;

namespace Helrift.Gate.Adapters.Firebase.Leaderboards;

/// <summary>
/// Firebase RTDB leaderboards repository using Admin OAuth (no ?auth=).
/// Structure:
///   realms/{realmId}/leaderboards/events/{idempotencyKey}
///   realms/{realmId}/leaderboards/counters/{metricKey}/{window}/{bucket}/{side}/{subjectId} = long
/// </summary>
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
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return false;

        // NOTE: Firebase keys cannot contain '.', '#', '$', '[', ']', '/'.
        // If your idempotencyKey might contain unsafe chars, encode it first.
        var safeKey = SafeKey(idempotencyKey);

        var eventPath = Path($"{data.RealmId}/leaderboards/events/{safeKey}");

        // Best-effort idempotency:
        // 1) If exists -> false
        // 2) Else PUT -> true
        using (var check = await _http.GetAsync(eventPath, ct).ConfigureAwait(false))
        {
            if (check.IsSuccessStatusCode)
            {
                var body = await check.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(body) && body != "null")
                    return false;
            }
        }

        // Store a slim event doc (keep it tiny)
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
        if (delta == 0)
            return;

        var counterPath = Path(GetCounterRelPath(key));

        // Pragmatic RTDB "increment" with retries:
        // GET current value -> PUT new value
        // For playtest scale this is fine.
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
                    {
                        // Could be a number, or a quoted string if it was written incorrectly.
                        if (!TryParseLongFromJson(json, out current))
                            current = 0;
                    }
                }
            }

            var next = current + delta;
            var put = await _http.PutAsJsonAsync(counterPath, next, J, ct).ConfigureAwait(false);

            if (put.IsSuccessStatusCode)
                return;

            // brief backoff
            await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), ct).ConfigureAwait(false);
        }

        // If we get here, we failed to increment after retries.
        // It's OK to swallow for playtest, but you might want logging here.
    }

    public async Task<IReadOnlyList<(LeaderboardCounterKey Key, long Value)>> GetTopAsync(
        string realmId,
        SideType side,
        string metricKey,
        LeaderboardWindowType window,
        DateTime bucketStartUtc,
        int limit,
        CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);

        var bucketPath = Path(GetBucketSideRelPath(realmId, metricKey, window, bucketStartUtc, side));

        using var res = await _http.GetAsync(bucketPath, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            return Array.Empty<(LeaderboardCounterKey, long)>();

        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return Array.Empty<(LeaderboardCounterKey, long)>();

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return Array.Empty<(LeaderboardCounterKey, long)>();

        var results = new List<(LeaderboardCounterKey, long)>();

        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var subjectId = p.Name;
            long value = 0;

            if (p.Value.ValueKind == JsonValueKind.Number)
            {
                if (!p.Value.TryGetInt64(out value))
                    continue;
            }
            else if (p.Value.ValueKind == JsonValueKind.String)
            {
                if (!long.TryParse(p.Value.GetString(), out value))
                    continue;
            }
            else
            {
                continue;
            }

            var k = new LeaderboardCounterKey(
                RealmId: realmId,
                Side: side,
                MetricKey: metricKey,
                Window: window,
                BucketStartUtc: NormalizeBucket(bucketStartUtc),
                SubjectType: LeaderboardSubjectType.Character, // phase 1
                SubjectId: subjectId
            );

            results.Add((k, value));
        }

        results.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        if (results.Count > limit)
            results = results.Take(limit).ToList();

        return results;
    }

    // -------------------------
    // Paths
    // -------------------------

    private static string GetBucketSideRelPath(string realmId, string metricKey, LeaderboardWindowType window, DateTime bucketStartUtc, SideType side)
    {
        var bucket = NormalizeBucket(bucketStartUtc).ToString("yyyyMMdd");
        return $"{realmId}/leaderboards/counters/{SafeKey(metricKey)}/{(byte)window}/{bucket}/{(byte)side}";
    }

    private static string GetCounterRelPath(LeaderboardCounterKey key)
        => $"{GetBucketSideRelPath(key.RealmId, key.MetricKey, key.Window, key.BucketStartUtc, key.Side)}/{SafeKey(key.SubjectId)}";

    private static DateTime NormalizeBucket(DateTime dt)
        => dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();

    // Firebase key safety for path segments
    private static string SafeKey(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "_";

        // Disallow '/', '.', '#', '$', '[', ']'
        // Simple, readable encoding: replace with '_'
        // If you prefer reversible encoding, swap to Base64Url.
        var bad = new[] { '/', '.', '#', '$', '[', ']' };
        foreach (var ch in bad)
            s = s.Replace(ch, '_');

        return s;
    }

    private static bool TryParseLongFromJson(string raw, out long value)
    {
        value = 0;
        raw = raw.Trim();

        // number
        if (long.TryParse(raw, out value))
            return true;

        // quoted number
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            var inner = raw.Substring(1, raw.Length - 2);
            return long.TryParse(inner, out value);
        }

        // JSON number (double) - last resort
        if (double.TryParse(raw, out var d))
        {
            value = (long)d;
            return true;
        }

        return false;
    }
}
