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

    private static string SafeKey(string key)
        => key.Replace(".", "_").Replace("$", "_").Replace("#", "_")
               .Replace("[", "_").Replace("]", "_").Replace("/", "_");
}