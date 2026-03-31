using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Adapters.Firebase;

public sealed class FirebaseDeliveryRepository : IDeliveryRepository
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FirebaseDeliveryRepository(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("firebase-admin");
    }

    private static string DeliveryPath(string realmId, string deliveryId)
        => $"realms/{SafeKey(realmId)}/deliveries/byId/{SafeKey(deliveryId)}.json";

    private static string DeliveryRootPath(string realmId)
        => $"realms/{SafeKey(realmId)}/deliveries/byId.json";

    private static string RecipientIndexPath(string realmId, string recipientCharacterId)
        => $"realms/{SafeKey(realmId)}/deliveries/byRecipientCharacter/{SafeKey(recipientCharacterId)}.json";

    public async Task<DeliveryRecord?> GetAsync(string realmId, string deliveryId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(DeliveryPath(realmId, deliveryId), ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        return JsonSerializer.Deserialize<DeliveryRecord>(json, Json);
    }

    public async Task<DeliveryRecordSnapshot?> GetSnapshotAsync(string realmId, string deliveryId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, DeliveryPath(realmId, deliveryId));
        req.Headers.TryAddWithoutValidation("X-Firebase-ETag", "true");

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        var record = JsonSerializer.Deserialize<DeliveryRecord>(json, Json);
        if (record == null) return null;

        var etag = res.Headers.ETag?.Tag;
        if (string.IsNullOrWhiteSpace(etag) && res.Headers.TryGetValues("ETag", out var vals))
            etag = vals.FirstOrDefault();

        return new DeliveryRecordSnapshot(record, etag);
    }

    public async Task<bool> TryCreateAsync(DeliveryRecord record, CancellationToken ct)
    {
        var existing = await GetAsync(record.RealmId, record.Id, ct);
        if (existing != null) return false;

        var patch = new Dictionary<string, object?>
        {
            [$"realms/{SafeKey(record.RealmId)}/deliveries/byId/{SafeKey(record.Id)}"] = record,
            [$"realms/{SafeKey(record.RealmId)}/deliveries/byRecipientCharacter/{SafeKey(record.Recipient.Id)}/{SafeKey(record.Id)}"] = ToIndex(record)
        };

        using var req = new HttpRequestMessage(HttpMethod.Patch, ".json")
        {
            Content = new StringContent(JsonSerializer.Serialize(patch, Json), Encoding.UTF8, "application/json")
        };

        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> TryReplaceAsync(string realmId, DeliveryRecord record, string? concurrencyToken, CancellationToken ct)
    {
        using var put = new HttpRequestMessage(HttpMethod.Put, DeliveryPath(realmId, record.Id))
        {
            Content = JsonContent.Create(record, options: Json)
        };

        if (!string.IsNullOrWhiteSpace(concurrencyToken))
            put.Headers.TryAddWithoutValidation("If-Match", concurrencyToken);

        using var putRes = await _http.SendAsync(put, ct);
        if (!putRes.IsSuccessStatusCode) return false;

        // Keep recipient index aligned (best-effort but immediate).
        var indexPath = $"realms/{SafeKey(record.RealmId)}/deliveries/byRecipientCharacter/{SafeKey(record.Recipient.Id)}/{SafeKey(record.Id)}.json";
        var indexRes = await _http.PutAsJsonAsync(indexPath, ToIndex(record), Json, ct);
        return indexRes.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAsync(string realmId, string deliveryId, CancellationToken ct)
    {
        var existing = await GetAsync(realmId, deliveryId, ct);
        if (existing == null) return false;

        var patch = new Dictionary<string, object?>
        {
            [$"realms/{SafeKey(realmId)}/deliveries/byId/{SafeKey(deliveryId)}"] = null
        };

        var recipientId = existing.Recipient?.Id;
        if (!string.IsNullOrWhiteSpace(recipientId))
        {
            patch[$"realms/{SafeKey(realmId)}/deliveries/byRecipientCharacter/{SafeKey(recipientId)}/{SafeKey(deliveryId)}"] = null;
        }

        using var req = new HttpRequestMessage(HttpMethod.Patch, ".json")
        {
            Content = new StringContent(JsonSerializer.Serialize(patch, Json), Encoding.UTF8, "application/json")
        };

        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<DeliveryRecipientIndexEntry>> ListRecipientEntriesAsync(string realmId, string recipientCharacterId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(RecipientIndexPath(realmId, recipientCharacterId), ct);
        if (!res.IsSuccessStatusCode) return [];

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];

        var dict = JsonSerializer.Deserialize<Dictionary<string, DeliveryRecipientIndexEntry>>(json, Json);
        if (dict == null || dict.Count == 0) return [];

        return dict.Values
            .OrderByDescending(x => x.UpdatedUnixUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<DeliveryRecord>> ListAllAsync(string realmId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(DeliveryRootPath(realmId), ct);
        if (!res.IsSuccessStatusCode) return [];

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];

        var dict = JsonSerializer.Deserialize<Dictionary<string, DeliveryRecord>>(json, Json);
        if (dict == null || dict.Count == 0) return [];

        return dict.Values
            .Where(x => x != null)
            .OrderByDescending(x => x.UpdatedUtc)
            .ToList();
    }

    private static DeliveryRecipientIndexEntry ToIndex(DeliveryRecord record)
    {
        return new DeliveryRecipientIndexEntry
        {
            DeliveryId = record.Id,
            Type = record.Type,
            Channel = record.Channel,
            State = record.State,
            IsRead = record.IsRead,
            HasUnclaimedEscrowAssets = record.HasUnclaimedEscrowAssets,
            IsArchived = record.IsArchived,
            Subject = record.Subject,
            SenderDisplayName = record.Sender.DisplayName,
            CreatedUnixUtc = new DateTimeOffset(record.CreatedUtc).ToUnixTimeSeconds(),
            UpdatedUnixUtc = new DateTimeOffset(record.UpdatedUtc).ToUnixTimeSeconds(),
            ExpiresUtc = record.ExpiresUtc
        };
    }

    private static string SafeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "_";
        foreach (var ch in new[] { '/', '.', '#', '$', '[', ']' })
            value = value.Replace(ch, '_');
        return value;
    }
}