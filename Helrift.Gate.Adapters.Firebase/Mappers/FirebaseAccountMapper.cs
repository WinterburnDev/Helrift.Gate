using System;
using System.Collections.Generic;
using System.Text.Json;
using Helrift.Gate.Contracts;
using Helrift.Gate.Adapters.Firebase; // <-- for FirebaseCharacterMapper

internal static class FirebaseAccountMapper
{
    // READ (accounts/{accountId} JSON -> AccountData)
    public static AccountData FromFirebase(string accountId, JsonElement e)
    {
        string username = Str(e, "username");
        string email = Str(e, "email_address", "emailAddress");
        DateTime lastLogin = Date(e, "last_log_in", "lastLogIn") ?? DateTime.MinValue;

        // characters: object map of { <charId>: { ... } }
        var characters = new List<CharacterData>();
        if (e.TryGetProperty("characters", out var charsEl) && charsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in charsEl.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                var ch = FirebaseCharacterMapper.FromFirebase(accountId, prop.Name, prop.Value);
                if (ch != null) characters.Add(ch);
            }
        }

        // entitlements: leave empty for now (commented)
        var entitlements = new Dictionary<string, EntitlementData>(StringComparer.OrdinalIgnoreCase);
        // if (e.TryGetProperty("entitlements", out var entEl) && entEl.ValueKind == JsonValueKind.Object)
        // {
        //     foreach (var kv in entEl.EnumerateObject())
        //     {
        //         if (kv.Value.ValueKind != JsonValueKind.Object) continue;
        //         var ent = EntitlementFirebaseMapper.FromFirebase(kv.Value);
        //         if (ent != null) entitlements[kv.Name] = ent;
        //     }
        // }

        // owned unlockables can be array or map-of-true
        var owned = new List<string>();
        if (e.TryGetProperty("owned_unlockable_ids", out var ou) && ou.ValueKind != JsonValueKind.Undefined && ou.ValueKind != JsonValueKind.Null)
        {
            if (ou.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in ou.EnumerateArray())
                    if (it.ValueKind == JsonValueKind.String) owned.Add(it.GetString()!);
            }
            else if (ou.ValueKind == JsonValueKind.Object)
            {
                foreach (var kv in ou.EnumerateObject())
                    if (kv.Value.ValueKind == JsonValueKind.True) owned.Add(kv.Name);
            }
        }

        return new AccountData
        {
            Id = accountId,
            Username = username,
            EmailAddress = email,
            LastLogIn = lastLogin,
            Characters = characters.ToArray(),
            Entitlements = entitlements,
            OwnedUnlockableIds = owned
        };
    }

    // WRITE (create multi-location payload for RTDB root PATCH)
    public static Dictionary<string, object?> ToFirebaseCreatePayload(
        string accountId, NewAccountRequest req, DateTime utcNow)
    {
        var iso = utcNow.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

        var accountObject = new Dictionary<string, object?>
        {
            ["username"] = req.Username,
            ["email_address"] = req.EmailAddress,
            ["created_at"] = iso,
            ["last_log_in"] = iso,

            // initialize empty collections
            ["characters"] = new Dictionary<string, object?>(),   // {}
            ["entitlements"] = new Dictionary<string, object?>(), // {}
            ["owned_unlockable_ids"] = Array.Empty<string>(),     // []

            ["links"] = new Dictionary<string, object?>
            {
                ["steam_id"] = req.SteamId64
            }
        };

        return new Dictionary<string, object?>
        {
            [$"accounts/{accountId}"] = accountObject,
            [$"links/steam/{req.SteamId64}"] = accountId
        };
    }

    // ------- small local helpers (string / date) -------
    private static string Str(JsonElement obj, params string[] keys)
    {
        foreach (var k in keys)
            if (obj.TryGetProperty(k, out var el))
            {
                if (el.ValueKind == JsonValueKind.String) return el.GetString()!;
                if (el.ValueKind == JsonValueKind.Number) return el.GetRawText();
                if (el.ValueKind is JsonValueKind.True or JsonValueKind.False) return el.GetRawText();
            }
        return null;
    }

    private static DateTime? Date(JsonElement obj, params string[] keys)
    {
        foreach (var k in keys)
            if (obj.TryGetProperty(k, out var el))
            {
                if (el.ValueKind == JsonValueKind.String && DateTime.TryParse(el.GetString(), out var dt))
                    return dt.ToUniversalTime();
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var ms))
                    return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            }
        return null;
    }
}
