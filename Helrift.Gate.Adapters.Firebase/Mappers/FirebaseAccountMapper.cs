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

        // owned unlockables can be array or map-of-true
        var owned = new List<string>();
        if (e.TryGetProperty("owned_entitlements", out var oe) &&
                oe.ValueKind != JsonValueKind.Undefined &&
                oe.ValueKind != JsonValueKind.Null &&
                oe.ValueKind == JsonValueKind.Object)
        {
            foreach (var kv in oe.EnumerateObject())
            {
                // skip known non-entitlement nodes like "source"
                if (kv.NameEquals("source"))
                    continue;

                // each entitlement should be an object
                if (kv.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var entObj = kv.Value;

                string? id = null;

                // prefer the canonical id field if present
                if (entObj.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                {
                    id = idEl.GetString();
                }

                // fallback to the firebase key if "id" is missing
                if (string.IsNullOrEmpty(id))
                {
                    id = kv.Name;
                }

                if (!string.IsNullOrEmpty(id))
                {
                    owned.Add(id);
                }
            }
        }

        return new AccountData
        {
            Id = accountId,
            Username = username,
            EmailAddress = email,
            LastLogIn = lastLogin,
            Characters = characters.ToArray(),
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
