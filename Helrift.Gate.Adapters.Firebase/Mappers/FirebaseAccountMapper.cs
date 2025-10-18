// Helrift.Gate.Adapters.Firebase/FirebaseAccountMapper.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Adapters.Firebase;

internal static class FirebaseAccountMapper
{
    public static AccountData FromFirebase(string accountId, JsonElement root)
    {
        var acc = new AccountData
        {
            Id = accountId,
            Username = J.Str(root, "username") ?? accountId,
            EmailAddress = J.Str(root, "email_address", "emailAddress") ?? "",
            LastLogIn = J.Date(root, "last_log_in", "lastLogIn") ?? DateTime.MinValue,
            Entitlements = ReadEntitlements(root, "owned_entitlements", "entitlements"),
            OwnedUnlockableIds = new List<string>(),
            Characters = ReadCharacters(accountId, root, "characters")
        };

        // best-effort password (if present)
        if (root.TryGetProperty("password", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            acc.Password = new PasswordData
            {
                Salt = J.Str(p, "salt") ?? "",
                Hash = J.Str(p, "hash") ?? ""
            };
        }

        // build OwnedUnlockableIds from entitlements
        if (acc.Entitlements is { Count: > 0 })
            acc.OwnedUnlockableIds = acc.Entitlements.Values
                .Select(e => e?.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        else
            acc.OwnedUnlockableIds = new List<string>();

        return acc;
    }

    private static Dictionary<string, EntitlementData> ReadEntitlements(JsonElement root, params string[] keys)
    {
        var d = new Dictionary<string, EntitlementData>(StringComparer.Ordinal);
        foreach (var k in keys)
        {
            if (!root.TryGetProperty(k, out var node) || node.ValueKind != JsonValueKind.Object) continue;
            foreach (var child in node.EnumerateObject())
            {
                var el = child.Value;
                var ent = new EntitlementData
                {
                    Id = J.Str(el, "id") ?? "",
                    Since = J.Str(el, "since") ?? "",
                    PurchaseCount = J.Int(el, "purchase_count", "purchaseCount")
                };
                d[child.Name] = ent;
            }
            break;
        }
        return d;
    }

    private static CharacterData[] ReadCharacters(string accountId, JsonElement root, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (!root.TryGetProperty(k, out var node) || node.ValueKind != JsonValueKind.Object) continue;
            var list = new List<CharacterData>();
            foreach (var prop in node.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.Object)
                    list.Add(FirebaseCharacterMapper.FromFirebase(accountId, prop.Name, prop.Value));
            return list.ToArray();
        }
        return Array.Empty<CharacterData>();
    }

    // minimal JSON helpers (same style as Character mapper)
    private static class J
    {
        public static string? Str(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
                if (obj.TryGetProperty(n, out var el))
                {
                    if (el.ValueKind == JsonValueKind.String) return el.GetString();
                    if (el.ValueKind == JsonValueKind.Number) return el.GetRawText();
                    if (el.ValueKind is JsonValueKind.True or JsonValueKind.False) return el.GetRawText();
                }
            return null;
        }
        public static int Int(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
                if (obj.TryGetProperty(n, out var el))
                {
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i;
                    if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s)) return s;
                }
            return 0;
        }
        public static DateTime? Date(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
                if (obj.TryGetProperty(n, out var el))
                {
                    if (el.ValueKind == JsonValueKind.String && DateTime.TryParse(el.GetString(), out var dt)) return dt.ToUniversalTime();
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var ms))
                        return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
                }
            return null;
        }
    }
}
