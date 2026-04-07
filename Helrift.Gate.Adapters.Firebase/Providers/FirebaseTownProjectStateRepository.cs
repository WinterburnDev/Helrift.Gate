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

    private static string SafeKey(string key)
        => key.Replace(".", "_").Replace("$", "_").Replace("#", "_")
               .Replace("[", "_").Replace("]", "_").Replace("/", "_");

    // ========== REQUIREMENT HISTORY ==========

    public async Task<string?> GetLastRequirementEntryAsync(string realmId, string townId, string definitionId, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/townProjects/requirementHistory/{SafeKey(townId)}/{SafeKey(definitionId)}/lastEntryId.json";
        using var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        // Firebase returns string values with surrounding quotes when fetched as a leaf
        return json.Trim('"');
    }

    public async Task SaveLastRequirementEntryAsync(string realmId, string townId, string definitionId, string entryId, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/townProjects/requirementHistory/{SafeKey(townId)}/{SafeKey(definitionId)}/lastEntryId.json";
        var json = $"\"{entryId}\"";
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await _http.PutAsync(path, content, ct);
        res.EnsureSuccessStatusCode();
    }
}