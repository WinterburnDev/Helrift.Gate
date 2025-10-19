// Helrift.Gate.Adapters.Firebase/FirebaseEntitlementsDataProvider.cs
using System.Net.Http.Json;
using System.Text.Json;
using Helrift.Gate.Contracts;
using Helrift.Gate.App;

namespace Helrift.Gate.Adapters.Firebase;

public sealed class FirebaseEntitlementsDataProvider(IHttpClientFactory httpFactory) : IEntitlementsDataProvider
{
    private readonly HttpClient _http = httpFactory.CreateClient("firebase-admin");
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    // ---- paths (new first, legacy fallbacks second) ----
    private static string AliasPathNew() => "entitlementsAliases/current.json";
    private static string AliasPathLegacy() => "catalogAliases/current.json";
    private static string UnlockablesPathNew(string id) => $"entitlements/{id}/unlockables.json";
    private static string UnlockablesPathLegacy(string id) => $"catalogs/{id}/unlockables.json";

    // -------- API --------

    public async Task<string?> GetCurrentIdAsync(CancellationToken ct)
    {
        // Try new alias first
        var id = await ReadAliasAsync(AliasPathNew(), ct);
        if (!string.IsNullOrWhiteSpace(id))
            return id;

        // Legacy fallback
        return await ReadAliasAsync(AliasPathLegacy(), ct);
    }

    public async Task<IReadOnlyDictionary<string, EntitlementUnlockableRow>> GetUnlockablesByIdAsync(string entitlementsId, CancellationToken ct)
    {
        var map = new Dictionary<string, EntitlementUnlockableRow>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(entitlementsId)) return map;

        // Try new path first
        var json = await GetStringAsync(UnlockablesPathNew(entitlementsId), ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            // Legacy fallback
            json = await GetStringAsync(UnlockablesPathLegacy(entitlementsId), ct);
            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return map;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Expect an object: { "<nodeKey>": { id:"...", ... }, ... }
        if (root.ValueKind != JsonValueKind.Object)
            return map;

        foreach (var p in root.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Object) continue;
            var row = FromFirebaseUnlockable(p.Value);
            if (row is null) continue;
            if (!row.Active) continue;
            if (string.IsNullOrWhiteSpace(row.Id)) continue;

            map[row.Id] = row;
        }

        return map;
    }

    // -------- internals --------

    private async Task<string?> ReadAliasAsync(string path, CancellationToken ct)
    {
        var json = await GetStringAsync(path, ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        using var doc = JsonDocument.Parse(json);
        var e = doc.RootElement;
        if (e.ValueKind != JsonValueKind.Object) return null;

        // Prefer new names, fall back to legacy catalog names.
        return Str(e, "entitlement_id", "entitlementId", "catalog_id", "catalogId");
    }

    private async Task<string?> GetStringAsync(string path, CancellationToken ct)
    {
        using var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadAsStringAsync(ct);
    }

    // ---------- mapping (snake_case <-> dto) ----------

    private static EntitlementUnlockableRow? FromFirebaseUnlockable(JsonElement e)
    {
        var id = Str(e, "id");
        if (string.IsNullOrWhiteSpace(id)) return null;

        var grantsNode = Obj(e, "grants");

        return new EntitlementUnlockableRow
        {
            Id = id!,
            Type = Str(e, "type") ?? "cosmetic",
            Scope = Str(e, "scope") ?? "account",
            Active = Bool(e, "active", true),
            Version = Int(e, "version", 1),
            Grants = new EntitlementGrants
            {
                ItemSkinIds = StrList(grantsNode, "itemSkinIds"),
                CastEffectIds = StrList(grantsNode, "castEffectIds"),
                HairStyleIds = StrList(grantsNode, "hairStyleIds"),
                HairColorIds = StrList(grantsNode, "hairColorIds"),
                FeatureFlags = StrList(grantsNode, "featureFlags")
            }
        };
    }

    private static string? Str(JsonElement e, params string[] keys)
    {
        foreach (var k in keys)
            if (e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        return null;
    }

    private static JsonElement Obj(JsonElement e, string key)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    private static bool Bool(JsonElement e, string key, bool def)
    {
        if (!e.TryGetProperty(key, out var v)) return def;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when v.TryGetInt32(out var i) => i != 0,
            JsonValueKind.String when bool.TryParse(v.GetString(), out var b) => b,
            _ => def
        };
    }

    private static int Int(JsonElement e, string key, int def)
    {
        if (!e.TryGetProperty(key, out var v)) return def;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var j)) return j;
        return def;
    }

    private static List<string> StrList(JsonElement obj, string key)
    {
        var list = new List<string>();
        if (obj.ValueKind != JsonValueKind.Object) return list;
        if (!obj.TryGetProperty(key, out var node)) return list;

        switch (node.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var el in node.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString()))
                        list.Add(el.GetString()!);
                break;

            case JsonValueKind.Object:
                foreach (var p in node.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                        list.Add(p.Value.GetString()!);
                break;
        }
        return list;
    }
}
