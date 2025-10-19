// Helrift.Gate.Adapters.Firebase/Providers/MerchantMapper.cs
using Helrift.Gate.Contracts;
using System.Text.Json;

namespace Helrift.Gate.Adapters.Firebase;

internal static class MerchantMapper
{
    public static MerchantItemRow? FromFirebase(string listingId, JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;

        string S(string k1, string? alt = null)
            => e.TryGetProperty(k1, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! :
               (alt != null && e.TryGetProperty(alt, out var v2) && v2.ValueKind == JsonValueKind.String ? v2.GetString()! : "");

        int I(string k1, string? alt = null, int def = 0)
        {
            if (e.TryGetProperty(k1, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i))
                return i;
            if (alt != null && e.TryGetProperty(alt, out var v2) && v2.ValueKind == JsonValueKind.Number && v2.TryGetInt32(out var i2))
                return i2;
            return def;
        }

        long L(string k1, string? alt = null, long def = 0)
        {
            if (e.TryGetProperty(k1, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l))
                return l;
            if (alt != null && e.TryGetProperty(alt, out var v2) && v2.ValueKind == JsonValueKind.Number && v2.TryGetInt64(out var l2))
                return l2;
            return def;
        }

        float F(string k1, string? alt = null, float def = 0f)
        {
            if (e.TryGetProperty(k1, out var v) && v.ValueKind == JsonValueKind.Number)
            {
                if (v.TryGetSingle(out var f)) return f;
                if (v.TryGetDouble(out var d)) return (float)d;
            }
            if (alt != null && e.TryGetProperty(alt, out var v2) && v2.ValueKind == JsonValueKind.Number)
            {
                if (v2.TryGetSingle(out var f2)) return f2;
                if (v2.TryGetDouble(out var d2)) return (float)d2;
            }
            return def;
        }

        bool B(string k1, string? alt = null, bool def = false)
        {
            if (e.TryGetProperty(k1, out var v))
            {
                if (v.ValueKind == JsonValueKind.True) return true;
                if (v.ValueKind == JsonValueKind.False) return false;
                if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
            }
            if (alt != null && e.TryGetProperty(alt, out var v2))
            {
                if (v2.ValueKind == JsonValueKind.True) return true;
                if (v2.ValueKind == JsonValueKind.False) return false;
                if (v2.ValueKind == JsonValueKind.String && bool.TryParse(v2.GetString(), out var b2)) return b2;
            }
            return def;
        }

        ItemType ReadItemType()
        {
            // Accept string or number from: itemType, ItemType, or type
            if (e.TryGetProperty("itemType", out var t) || e.TryGetProperty("ItemType", out t) || e.TryGetProperty("type", out t))
            {
                if (t.ValueKind == JsonValueKind.String)
                {
                    var s = t.GetString();
                    if (!string.IsNullOrWhiteSpace(s) && Enum.TryParse<ItemType>(s, true, out var parsed))
                        return parsed;
                }
                else if (t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var n))
                {
                    if (Enum.IsDefined(typeof(ItemType), n))
                        return (ItemType)n;
                }
            }
            // fallback if missing/unknown
            return ItemType.Weapon;
        }

        var row = new MerchantItemRow
        {
            ListingId = listingId,
            NpcId = S("npcId", "NpcId"),
            OwnerCharacterId = S("ownerCharacterId", "SellerCharacterId"),
            ItemUniqueId = S("itemUniqueId", "ItemInstanceId"),
            ItemId = S("itemId", "ItemDataId"),
            ItemName = S("itemName", "ItemName"),

            // type + basic item facets
            ItemType = ReadItemType(),
            Quantity = I("quantity", "Quantity"),
            Quality = (ItemQuality)I("quality", "Quality"),
            Colour = S("colour", "Colour"),
            Rarity = (ItemRarity)I("rarity", "Rarity"),
            IsCrafted = B("isCrafted"),

            // physicals / timing
            CurrentEndurance = I("currentEndurance", "CurrentEndurance"),
            Weight = F("weight", "Weight"),
            ListedAtUnix = L("listedAtUnix", "ListedAtUnix"),
            ExpireAtUnix = L("expireAtUnix", "ExpireAtUnix"),

            // valuation snapshot
            BaseValue = L("baseValue", "BaseValue"),
            ResaleValueNow = L("resaleValueNow", "ResaleValueNow"),
            BuyPriceWithoutTax = L("buyPriceWithoutTax", "BuyPriceWithoutTax"),
            SellPriceToVendor = L("sellPriceToVendor", "SellPriceToVendor"),
        };

        // stats (lowercase & PascalCase)
        if ((e.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Array) ||
            (e.TryGetProperty("Stats", out stats) && stats.ValueKind == JsonValueKind.Array))
        {
            var list = new List<ItemStat>();
            foreach (var s in stats.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object) continue;
                int type = s.TryGetProperty("type", out var tVal) && tVal.TryGetInt32(out var ti) ? ti : 0;
                int value = s.TryGetProperty("value", out var vVal) && vVal.TryGetInt32(out var vi) ? vi : 0;
                int bq = s.TryGetProperty("bonusPerQuality", out var bqVal) && bqVal.TryGetInt32(out var bqi) ? bqi : 0;
                list.Add(new ItemStat { statType = (ItemStatType)type, statValue = value, bonusPerQuality = bq });
            }
            row.Stats = list.ToArray();
        }

        // props (lowercase & PascalCase)
        if ((e.TryGetProperty("props", out var props) && props.ValueKind == JsonValueKind.Array) ||
            (e.TryGetProperty("Props", out props) && props.ValueKind == JsonValueKind.Array))
        {
            var list = new List<ItemProp>();
            foreach (var p in props.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object) continue;
                var k = p.TryGetProperty("k", out var kk) && kk.ValueKind == JsonValueKind.String ? kk.GetString()! : null;
                var v = p.TryGetProperty("v", out var vv) && vv.ValueKind == JsonValueKind.String ? vv.GetString()! : "";
                if (!string.IsNullOrEmpty(k)) list.Add(new ItemProp { k = k, v = v });
            }
            row.Props = list.ToArray();
        }

        return row;
    }

    public static Dictionary<string, object> ToFirebaseDict(MerchantItemRow r)
    {
        var d = new Dictionary<string, object>
        {
            ["npcId"] = r.NpcId,
            ["listingId"] = r.ListingId,
            ["ownerCharacterId"] = r.OwnerCharacterId,

            ["itemUniqueId"] = r.ItemUniqueId,
            ["itemId"] = r.ItemId,
            ["itemName"] = r.ItemName,
            // write itemType as string to normalize storage
            ["itemType"] = r.ItemType.ToString(),

            ["quality"] = (int)r.Quality,
            ["quantity"] = r.Quantity,
            ["colour"] = r.Colour ?? "",
            ["rarity"] = (int)r.Rarity,
            ["isCrafted"] = r.IsCrafted,

            ["currentEndurance"] = r.CurrentEndurance,
            ["weight"] = r.Weight,

            ["listedAtUnix"] = r.ListedAtUnix,
            ["expireAtUnix"] = r.ExpireAtUnix,

            ["baseValue"] = r.BaseValue,
            ["resaleValueNow"] = r.ResaleValueNow,
            ["buyPriceWithoutTax"] = r.BuyPriceWithoutTax,
            ["sellPriceToVendor"] = r.SellPriceToVendor,

            ["stats"] = (r.Stats ?? Array.Empty<ItemStat>()).Select(s => new Dictionary<string, object>
            {
                ["type"] = (int)s.statType,
                ["value"] = s.statValue,
                ["bonusPerQuality"] = s.bonusPerQuality
            }).ToList(),

            ["props"] = (r.Props ?? Array.Empty<ItemProp>()).Select(p => new Dictionary<string, object>
            {
                ["k"] = p.k,
                ["v"] = p.v ?? ""
            }).ToList()
        };
        return d;
    }

    // Exposed helper used by your filtering code elsewhere
    public static string? ReadPropertyAsString(MerchantItemRow r, string key)
        => key?.ToLowerInvariant() switch
        {
            "itemid" => r.ItemId,
            "itemname" => r.ItemName,
            "itemtype" => r.ItemType.ToString(),
            "colour" => r.Colour,
            "ownercharacterid" => r.OwnerCharacterId,
            "npcid" => r.NpcId,
            "listingid" => r.ListingId,
            _ => null
        };
}
