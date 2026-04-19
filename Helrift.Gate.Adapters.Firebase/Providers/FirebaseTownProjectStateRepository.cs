// Adapters.Firebase/Providers/FirebaseTownProjectStateRepository.cs
using System.Text;
using System.Text.Json;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts.TownProjects;

namespace Helrift.Gate.Adapters.Firebase;

public sealed class FirebaseTownProjectStateRepository : ITownProjectStateRepository
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private sealed class WeeklyResetLease
    {
        public string OwnerId { get; set; } = string.Empty;
        public DateTime AcquiredAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }

    public FirebaseTownProjectStateRepository(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("firebase-admin");
    }

    // ========== PROJECT INSTANCES ==========

    public async Task<TownProjectInstance?> GetInstanceAsync(string realmId, string townId, string instanceId, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/townProjects/instances/{SafeKey(townId)}/{SafeKey(instanceId)}.json";
        using var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        return JsonSerializer.Deserialize<TownProjectInstance>(json, Json);
    }

    public async Task<IReadOnlyList<TownProjectInstance>> GetActiveInstancesAsync(string realmId, string townId, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/townProjects/instances/{SafeKey(townId)}.json";
        using var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode) return Array.Empty<TownProjectInstance>();

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return Array.Empty<TownProjectInstance>();

        var dict = JsonSerializer.Deserialize<Dictionary<string, TownProjectInstance>>(json, Json);
        return dict?.Values.ToList() ?? (IReadOnlyList<TownProjectInstance>)Array.Empty<TownProjectInstance>();
    }

    public async Task<bool> SaveInstanceAsync(TownProjectInstance instance, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(instance.RealmId)}/townProjects/instances/{SafeKey(instance.TownId)}/{SafeKey(instance.Id)}.json";
        var json = JsonSerializer.Serialize(instance, Json);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var res = await _http.PutAsync(path, content, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteInstanceAsync(string realmId, string townId, string instanceId, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/townProjects/instances/{SafeKey(townId)}/{SafeKey(instanceId)}.json";
        using var res = await _http.DeleteAsync(path, ct);
        return res.IsSuccessStatusCode;
    }

    // ========== REWARD STATE ==========

    public async Task<TownProjectRewardState?> GetRewardAsync(string realmId, string townId, string rewardId, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/townProjects/rewards/{SafeKey(townId)}/{SafeKey(rewardId)}.json";
        using var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        return JsonSerializer.Deserialize<TownProjectRewardState>(json, Json);
    }

    public async Task<IReadOnlyList<TownProjectRewardState>> GetActiveRewardsAsync(string realmId, string townId, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/townProjects/rewards/{SafeKey(townId)}.json";
        using var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode) return Array.Empty<TownProjectRewardState>();

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return Array.Empty<TownProjectRewardState>();

        var dict = JsonSerializer.Deserialize<Dictionary<string, TownProjectRewardState>>(json, Json);
        return dict?.Values.ToList() ?? (IReadOnlyList<TownProjectRewardState>)Array.Empty<TownProjectRewardState>();
    }

    public async Task<bool> SaveRewardAsync(TownProjectRewardState reward, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(reward.RealmId)}/townProjects/rewards/{SafeKey(reward.TownId)}/{SafeKey(reward.Id)}.json";
        var json = JsonSerializer.Serialize(reward, Json);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var res = await _http.PutAsync(path, content, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteRewardAsync(string realmId, string townId, string rewardId, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/townProjects/rewards/{SafeKey(townId)}/{SafeKey(rewardId)}.json";
        using var res = await _http.DeleteAsync(path, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<TownProjectRequirementSelectionHistory?> GetSelectionHistoryAsync(
        string realmId,
        string townId,
        string definitionId,
        CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/townProjects/selectionHistory/{SafeKey(townId)}/{SafeKey(definitionId)}.json";
        using var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode)
            return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return null;

        return JsonSerializer.Deserialize<TownProjectRequirementSelectionHistory>(json, Json);
    }

    public async Task<bool> SaveSelectionHistoryAsync(TownProjectRequirementSelectionHistory history, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(history.RealmId)}/townProjects/selectionHistory/{SafeKey(history.TownId)}/{SafeKey(history.DefinitionId)}.json";
        var json = JsonSerializer.Serialize(history, Json);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var res = await _http.PutAsync(path, content, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<DateTime?> GetLastWeeklyResetSlotUtcAsync(string realmId, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/townProjects/metadata/lastWeeklyResetSlotUtc.json";
        using var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode)
            return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return null;

        return JsonSerializer.Deserialize<DateTime>(json, Json);
    }

    public async Task<bool> SaveLastWeeklyResetSlotUtcAsync(string realmId, DateTime slotUtc, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/townProjects/metadata/lastWeeklyResetSlotUtc.json";
        var json = JsonSerializer.Serialize(slotUtc, Json);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var res = await _http.PutAsync(path, content, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> TryAcquireWeeklyResetLeaseAsync(
        string realmId,
        string ownerId,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/townProjects/metadata/weeklyResetLease.json";

        var (current, etag) = await GetWeeklyResetLeaseWithEtagAsync(path, ct);
        if (etag is null)
            return false;

        if (current is not null &&
            !string.Equals(current.OwnerId, ownerId, StringComparison.Ordinal) &&
            current.ExpiresAtUtc > nowUtc)
        {
            return false;
        }

        var next = new WeeklyResetLease
        {
            OwnerId = ownerId,
            AcquiredAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(leaseDuration)
        };

        return await PutWithIfMatchAsync(path, etag, next, ct);
    }

    public async Task<bool> ReleaseWeeklyResetLeaseAsync(string realmId, string ownerId, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/townProjects/metadata/weeklyResetLease.json";

        var (current, etag) = await GetWeeklyResetLeaseWithEtagAsync(path, ct);
        if (etag is null || current is null)
            return true;

        if (!string.Equals(current.OwnerId, ownerId, StringComparison.Ordinal))
            return true;

        using var req = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("if-match", etag);

        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode || (int)res.StatusCode == 412;
    }

    private async Task<(WeeklyResetLease? Lease, string? Etag)> GetWeeklyResetLeaseWithEtagAsync(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.TryAddWithoutValidation("X-Firebase-ETag", "true");

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
            return (null, null);

        var etag = res.Headers.TryGetValues("ETag", out var values)
            ? values.FirstOrDefault()
            : null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return (null, etag ?? "null");

        var lease = JsonSerializer.Deserialize<WeeklyResetLease>(json, Json);
        return (lease, etag);
    }

    private async Task<bool> PutWithIfMatchAsync(string path, string etag, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        using var req = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("if-match", etag);

        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    private static string SafeKey(string key)
        => key.Replace(".", "_").Replace("$", "_").Replace("#", "_")
               .Replace("[", "_").Replace("]", "_").Replace("/", "_");
}