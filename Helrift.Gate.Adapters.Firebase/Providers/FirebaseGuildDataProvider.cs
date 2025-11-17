// Helrift.Gate.Adapters.Firebase/FirebaseGuildDataProvider.cs
using System.Net.Http.Json;
using System.Text.Json;
using Helrift.Gate.Contracts;
using Helrift.Gate.App.Repositories;

namespace Helrift.Gate.Adapters.Firebase;

public sealed class FirebaseGuildDataProvider(IHttpClientFactory httpFactory) : IGuildDataProvider
{
    private readonly HttpClient _http = httpFactory.CreateClient("firebase-admin");
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    private static string Path(string rel) => $"guilds/{rel}.json";

    public async Task<GuildData?> GetAsync(string guildId, CancellationToken ct)
    {
        using var res = await _http.GetAsync(Path(guildId), ct);
        if (!res.IsSuccessStatusCode) return null;
        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        using var doc = JsonDocument.Parse(json);
        return FromFirebase(guildId, doc.RootElement);
    }

    public async Task<bool> SaveAsync(GuildData g, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(g.GuildId)) return false;
        var dict = ToFirebaseDict(g);
        var res = await _http.PutAsJsonAsync(Path(g.GuildId), dict, J, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAsync(string guildId, CancellationToken ct)
    {
        using var res = await _http.DeleteAsync(Path(guildId), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<GuildData>> QueryAsync(string? side, string? partialName, CancellationToken ct)
    {
        using var res = await _http.GetAsync("guilds.json", ct);
        if (!res.IsSuccessStatusCode) return Array.Empty<GuildData>();
        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return Array.Empty<GuildData>();

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return Array.Empty<GuildData>();

        var list = new List<GuildData>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Object) continue;
            var g = FromFirebase(p.Name, p.Value);
            if (g == null) continue;

            if (!string.IsNullOrWhiteSpace(side) &&
                !string.Equals(g.Side, side, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrWhiteSpace(partialName) &&
                g.Name?.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            list.Add(g);
        }
        return list;
    }

    // ---------- mapping (snake_case <-> dto) ----------

    private static GuildData? FromFirebase(string id, JsonElement e)
    {
        string? Str(params string[] keys)
        {
            foreach (var k in keys)
                if (e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
            return null;
        }
        DateTime? Iso(params string[] keys)
        {
            var s = Str(keys);
            return DateTime.TryParse(s, out var dt) ? dt.ToUniversalTime() : null;
        }
        List<string> StrList(params string[] keys)
        {
            foreach (var k in keys)
                if (e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array)
                    return v.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()!)
                        .ToList();
            return new();
        }
        Dictionary<string, object>? AnyObj(string key)
        {
            if (!e.TryGetProperty(key, out var obj) || obj.ValueKind != JsonValueKind.Object)
                return null;
            var dict = new Dictionary<string, object>();
            foreach (var prop in obj.EnumerateObject())
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString()!,
                    JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l :
                                             prop.Value.TryGetDouble(out var d) ? d : prop.Value.GetRawText(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => prop.Value.GetRawText() // nested handled as raw JSON if needed
                };
            return dict;
        }

        return new GuildData
        {
            GuildId = id,
            Name = Str("name") ?? "",
            LeaderCharacterId = Str("leaderCharacterId") ?? "",
            CreatedAt = Iso("createdAt"),
            Side = Str("side") ?? "",
            MOTD = Str("motd"),
            Description = Str("description"),
            MemberCharacterIds = StrList("memberCharacterIds"),
            Emblem = AnyObj("emblem")
        };
    }

    private static Dictionary<string, object> ToFirebaseDict(GuildData g)
    {
        var d = new Dictionary<string, object>
        {
            ["name"] = g.Name ?? "",
            ["leaderCharacterId"] = g.LeaderCharacterId ?? "",
            ["memberCharacterIds"] = g.MemberCharacterIds ?? new(),
            ["side"] = g.Side ?? "",
            ["createdAt"] = (g.CreatedAt ?? DateTime.UtcNow).ToString("o")
        };
        if (!string.IsNullOrWhiteSpace(g.MOTD)) d["motd"] = g.MOTD!;
        if (!string.IsNullOrWhiteSpace(g.Description)) d["description"] = g.Description!;
        if (g.Emblem != null) d["emblem"] = g.Emblem!;
        return d;
    }
}
