using System.Net.Http.Json;
using System.Text.Json;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Adapters.Firebase;

public sealed class FirebaseEscrowRepository : IEscrowRepository
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FirebaseEscrowRepository(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("firebase-admin");
    }

    private static string Path(string realmId, string containerId)
        => $"realms/{SafeKey(realmId)}/escrow/containers/{SafeKey(containerId)}.json";

    public async Task<EscrowContainer?> GetContainerAsync(string realmId, string containerId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(Path(realmId, containerId), ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        return JsonSerializer.Deserialize<EscrowContainer>(json, Json);
    }

    public async Task<EscrowContainerSnapshot?> GetContainerSnapshotAsync(string realmId, string containerId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Path(realmId, containerId));
        req.Headers.TryAddWithoutValidation("X-Firebase-ETag", "true");

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;

        var raw = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return null;

        var container = JsonSerializer.Deserialize<EscrowContainer>(raw, Json);
        if (container == null) return null;

        var etag = res.Headers.ETag?.Tag;
        if (string.IsNullOrWhiteSpace(etag) && res.Headers.TryGetValues("ETag", out var vals))
            etag = vals.FirstOrDefault();

        return new EscrowContainerSnapshot(container, etag);
    }

    public async Task CreateContainerAsync(EscrowContainer container, CancellationToken ct)
    {
        var res = await _http.PutAsJsonAsync(Path(container.RealmId, container.Id), container, Json, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task<bool> TryReplaceContainerAsync(string realmId, EscrowContainer container, string? concurrencyToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, Path(realmId, container.Id))
        {
            Content = JsonContent.Create(container, options: Json)
        };

        if (!string.IsNullOrWhiteSpace(concurrencyToken))
            req.Headers.TryAddWithoutValidation("If-Match", concurrencyToken);

        using var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode) return true;
        if ((int)res.StatusCode == 412) return false;
        return false;
    }

    public async Task<bool> DeleteContainerAsync(string realmId, string containerId, CancellationToken ct)
    {
        using var res = await _http.DeleteAsync(Path(realmId, containerId), ct);
        if (res.IsSuccessStatusCode) return true;
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        return false;
    }

    private static string SafeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "_";
        foreach (var ch in new[] { '/', '.', '#', '$', '[', ']' })
            value = value.Replace(ch, '_');
        return value;
    }
}