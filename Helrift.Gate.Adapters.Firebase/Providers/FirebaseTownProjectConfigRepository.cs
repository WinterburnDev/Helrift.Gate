// Adapters.Firebase/Providers/FirebaseTownProjectConfigRepository.cs
using System.Text.Json;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts.TownProjects;

namespace Helrift.Gate.Adapters.Firebase;

public sealed class FirebaseTownProjectConfigRepository : ITownProjectConfigRepository
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FirebaseTownProjectConfigRepository(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("firebase-admin");
    }

    public async Task<RealmProjectConfigRef?> GetRealmConfigRefAsync(string realmId, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/config.json";
        
        using var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        return JsonSerializer.Deserialize<RealmProjectConfigRef>(json, Json);
    }

    public async Task<TownProjectConfigRoot?> GetConfigVersionAsync(string version, CancellationToken ct = default)
    {
        var path = $"config/projects/versions/{SafeKey(version)}.json";
        
        using var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        return JsonSerializer.Deserialize<TownProjectConfigRoot>(json, Json);
    }

    public async Task SaveRealmConfigRefAsync(string realmId, RealmProjectConfigRef realmRef, CancellationToken ct = default)
    {
        var path = $"realms/{SafeKey(realmId)}/config.json";
        var payload = JsonSerializer.Serialize(realmRef, Json);
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        using var res = await _http.PutAsync(path, content, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task SaveConfigVersionAsync(string version, TownProjectConfigRoot config, CancellationToken ct = default)
    {
        var path = $"config/projects/versions/{SafeKey(version)}.json";
        var payload = JsonSerializer.Serialize(config, Json);
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        using var res = await _http.PutAsync(path, content, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ConfigVersionRecord<TownProjectConfigRoot>>> ListConfigVersionsAsync(CancellationToken ct = default)
    {
        var path = "config/projects/versions.json";

        using var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode) return Array.Empty<ConfigVersionRecord<TownProjectConfigRoot>>();

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return Array.Empty<ConfigVersionRecord<TownProjectConfigRoot>>();

        var map = JsonSerializer.Deserialize<Dictionary<string, TownProjectConfigRoot>>(json, Json)
            ?? new Dictionary<string, TownProjectConfigRoot>();

        return map
            .Where(kv => kv.Value != null)
            .Select(kv => new ConfigVersionRecord<TownProjectConfigRoot>
            {
                StorageKey = kv.Key,
                Config = kv.Value
            })
            .ToList();
    }

    public async Task<IReadOnlyList<RealmConfigRefRecord<RealmProjectConfigRef>>> ListRealmConfigRefsAsync(CancellationToken ct = default)
    {
        var path = "realms.json";

        using var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode) return Array.Empty<RealmConfigRefRecord<RealmProjectConfigRef>>();

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return Array.Empty<RealmConfigRefRecord<RealmProjectConfigRef>>();

        var realms = JsonSerializer.Deserialize<Dictionary<string, FirebaseRealmNode>>(json, Json)
            ?? new Dictionary<string, FirebaseRealmNode>();

        return realms
            .Where(kv => kv.Value?.Config != null)
            .Select(kv => new RealmConfigRefRecord<RealmProjectConfigRef>
            {
                RealmId = kv.Key,
                ConfigRef = kv.Value!.Config!
            })
            .ToList();
    }

    private sealed class FirebaseRealmNode
    {
        public RealmProjectConfigRef? Config { get; set; }
    }

    private static string SafeKey(string key)
        => key.Replace(".", "_").Replace("$", "_").Replace("#", "_")
               .Replace("[", "_").Replace("]", "_").Replace("/", "_");
}