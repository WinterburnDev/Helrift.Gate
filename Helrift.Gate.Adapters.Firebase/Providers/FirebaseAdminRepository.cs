using Helrift.Gate.App.Repositories;
using System.Net.Http.Json;
using System.Text.Json;

namespace Helrift.Gate.Adapters.Firebase;

public sealed class FirebaseAdminRepository : IAdminRepository
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FirebaseAdminRepository(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("firebase-admin");
    }

    /// <summary>
    /// RTDB layout:
    /// /realms/{realmId}/admins/byCharacter/{characterId}.json = true
    /// </summary>
    public async Task<bool> IsAdminCharacterAsync(string realmId, string characterId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(realmId))
            realmId = "default";

        if (string.IsNullOrWhiteSpace(characterId))
            return false;

        using var res = await _http.GetAsync(
            $"realms/{realmId}/admins/byCharacter/{characterId}.json", ct);

        if (!res.IsSuccessStatusCode)
            return false;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return false;

        // stored as literal true/false
        if (bool.TryParse(json, out var b))
            return b;

        // in case RTDB returns "true\n" etc
        try
        {
            return JsonSerializer.Deserialize<bool>(json, Json);
        }
        catch
        {
            return false;
        }
    }

    public async Task SetAdminCharacterAsync(string realmId, string characterId, bool isAdmin, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(realmId))
            realmId = "default";

        if (string.IsNullOrWhiteSpace(characterId))
            return;

        var path = $"realms/{realmId}/admins/byCharacter/{characterId}.json";

        if (isAdmin)
        {
            var put = await _http.PutAsJsonAsync(path, true, Json, ct);
            put.EnsureSuccessStatusCode();
        }
        else
        {
            // remove key entirely so list stays clean
            using var del = await _http.DeleteAsync(path, ct);
            del.EnsureSuccessStatusCode();
        }
    }
}
