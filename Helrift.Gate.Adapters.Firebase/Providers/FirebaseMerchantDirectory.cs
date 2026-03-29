using System.Text.Json;
using Helrift.Gate.App.Repositories;
using Microsoft.Extensions.Logging;

namespace Helrift.Gate.Adapters.Firebase;

public sealed class FirebaseMerchantDirectory(
    IHttpClientFactory httpFactory,
    ILogger<FirebaseMerchantDirectory> logger) : IAdminMerchantDirectory
{
    private readonly HttpClient _http = httpFactory.CreateClient("firebase-admin");

    public async Task<IReadOnlyList<string>> GetAllNpcIdsAsync(CancellationToken ct)
    {
        // Firebase shallow query — must be a separate param so the auth handler
        // can append its own token without clobbering ours.
        using var req = new HttpRequestMessage(HttpMethod.Get,
            new Uri(_http.BaseAddress!, "merchants.json?shallow=true"));

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            logger.LogWarning("[MerchantDirectory] Firebase returned {Status} for merchants shallow query", res.StatusCode);
            return [];
        }

        var json = await res.Content.ReadAsStringAsync(ct);

        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return [];

        // Shallow returns { "npcId": true } — we only need the keys
        return doc.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToList();
    }
}