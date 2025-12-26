using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;
using Helrift.Gate.Contracts;
using System.ComponentModel.DataAnnotations;

public enum MerchantQuerySort
{
    PriceAsc = 0,
    PriceDesc = 1,
    RarityDesc = 2,
    ListedAtDesc = 3
}

public enum ItemType
{
    Weapon,
    Armour,
    Offhand,
    Consumable,
    Gatherable,
    Necklace,
    Ring,
    Fish,
    Coating
}

public sealed class StatRangeFilter
{
    public int Type { get; set; }              // ItemStatType as int
    public float Min { get; set; }
    public float Max { get; set; } = float.MaxValue;
}

public sealed class MerchantQuery
{
    public int Page { get; set; } = 1;
    [Range(1, 200)]
    public int PageSize { get; set; } = 30;
    public bool IncludeExpired { get; set; } = false;
    public MerchantQuerySort Sort { get; set; } = MerchantQuerySort.PriceAsc;

    public ItemType? ItemType { get; set; }
    public float MinStatValue { get; set; } = 0f;
    public float MaxStatValue { get; set; } = float.MaxValue;

    public StatRangeFilter[]? StatRanges { get; set; }
}

[Serializable]
public sealed class MerchantPageResult
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }

    public List<MerchantItemRow> Items { get; set; } = new List<MerchantItemRow>();
}

[Serializable]
public class MerchantItemRow
{
    public string NpcId { get; set; }                // which NPC stocks it
    public string ListingId { get; set; }
    public string ItemUniqueId { get; set; }
    public string ItemId { get; set; }
    public string OwnerCharacterId { get; set; }      // seller
    public string ItemName { get; set; }              // display name
    public string Colour { get; set; }
    public int Quantity { get; set; }
    public ItemType ItemType { get; set; }
    public ItemQuality Quality { get; set; }                  // 1..4 as per your ItemQuality
    public ItemRarity Rarity { get; set; }
    public bool IsCrafted { get; set; }

    public int CurrentEndurance { get; set; }
    public float Weight { get; set; }
    public long ListedAtUnix { get; set; }            // seconds
    public long ExpireAtUnix { get; set; }            // seconds

    // Denormalized facets to search/sort quickly (backed by indexes)
    public ItemStat[] Stats { get; set; } // e.g. {"Absorption": 5, "HitRatio": 12}
    public ItemProp[] Props { get; set; }

    // Valuation snapshot at listing time (endurance-aware)
    public long BaseValue { get; set; }               // at max endurance
    public long ResaleValueNow { get; set; }          // BaseValue - RepairCostMissing
    public long BuyPriceWithoutTax { get; set; }      // equals ResaleValueNow (snapshot)
    public long SellPriceToVendor { get; set; }       // floor(ResaleValueNow * 0.5)

    // Filled per-query server-side (reflect current tax)
    public long FinalBuyPrice { get; set; }
}

[Serializable]
public class ItemStat
{
    public ItemStatType statType { get; set; }
    public int statValue { get; set; }
    public int bonusPerQuality { get; set; }
}

[Serializable]
public struct ItemProp
{
    public string k { get; set; }
    public string v { get; set; }
}

public sealed class MerchantSellEvent
{
    public required string EventId { get; init; }
    public required string NpcId { get; init; }
    public required string SellerCharacterId { get; init; }
    public long CreatedAtUnix { get; init; }
    public required MerchantItemRow[] Rows { get; init; }
}