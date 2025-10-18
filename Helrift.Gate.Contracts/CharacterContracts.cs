using System;
using System.Collections.Generic;

namespace Helrift.Gate.Contracts
{
    // ---- primitives for Unity vectors ----
    public struct Vec3 { public float x { get; set; } public float y { get; set; } public float z { get; set; } }
    public struct Vec2 { public float x { get; set; } public float y { get; set; } }

    // ---- enums (int-wired). Unknown=0 lets us accept any int values coming from Unity/DB. ----
    public enum Gender : int { Unknown = 0 }
    public enum EquipmentSlot : int { Unknown = 0 }
    public enum ItemQuality : int { Unknown = 0 }
    public enum ItemStatType : int { Unknown = 0 }
    public enum MaterialType : int { Unknown = 0 }

    // ---- Character root ----
    [Serializable]
    public class CharacterData
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string CharacterName { get; set; }
        public string MapId { get; set; }
        public Vec3 Position { get; set; }
        public Vec3 Rotation { get; set; }
        public int Side { get; set; }
        public int SideStatus { get; set; }
        public Gender Gender { get; set; }

        public HumanAppearance Appearance { get; set; }

        public int Hp { get; set; }
        public int Mp { get; set; }
        public int Sp { get; set; }
        public int Level { get; set; }
        public int Strength { get; set; }
        public int Dexterity { get; set; }
        public int Vitality { get; set; }
        public int Magic { get; set; }
        public int Intelligence { get; set; }
        public int Finesse { get; set; }
        public long Experience { get; set; }
        public int Criticals { get; set; }
        public int EnemyKillPoints { get; set; }
        public int MajesticPoints { get; set; }

        public CharacterItemData[] Inventory { get; set; }
        public CharacterItemData[] Warehouse { get; set; }
        public Dictionary<string, CharacterSkillData> Skills { get; set; } = new();

        public CharacterQuestData Quests { get; set; }
        public CharacterTitleData Titles { get; set; }
        public CharacterGuildData Guild { get; set; }
        public CharacterBeastiaryData Beastiary { get; set; }
        public CharacterResearchData Research { get; set; }
        public CharacterSpellsData Spells { get; set; }
        public CharacterCosmeticData Cosmetics { get; set; }

        public DateTime? LastLoggedIn { get; set; }
    }

    [Serializable]
    public class HumanAppearance
    {
        public HumanBodyData BodyData { get; set; }
        public string HairStyle { get; set; }
        public string FacialHairStyle { get; set; }
        public string HairColour { get; set; }
        public int SkinColour { get; set; }
        public int EyeColour { get; set; }
        public string EyebrowStyle { get; set; }
    }

    [Serializable]
    public class CharacterItemData
    {
        public string UniqueId { get; set; }
        public string ItemId { get; set; }
        public int Quantity { get; set; }
        public string SkinId { get; set; }
        public string GuildId { get; set; }
        public bool IsEquipped { get; set; }
        public EquipmentSlot EquipmentSlot { get; set; }
        public bool IsDualWield { get; set; }
        public string Colour { get; set; }
        public int Endurance { get; set; }
        public float Weight { get; set; }
        public ItemQuality Quality { get; set; }
        public int UpgradeLevel { get; set; }
        public bool IsCrafted { get; set; }
        public CharacterItemStatData[] Stats { get; set; }
        public CharacterItemPropData[] Props { get; set; }
        public CharacterItemContentsData[] Contents { get; set; }
        public CharacterItemCoatingData Coating { get; set; }
        public Vec2 BagPosition { get; set; }
    }

    [Serializable]
    public class CharacterItemContentsData
    {
        public string UniqueId { get; set; }
        public string ItemId { get; set; }
        public int Quantity { get; set; }
        public string SkinId { get; set; }
        public string Colour { get; set; }
        public int Endurance { get; set; }
        public float Weight { get; set; }
        public ItemQuality Quality { get; set; }
        public int UpgradeLevel { get; set; }
        public bool IsCrafted { get; set; }
        public string CraftedBy { get; set; }
        public CharacterItemStatData[] Stats { get; set; }
        public CharacterItemPropData[] Props { get; set; }
        public CharacterItemCoatingData Coating { get; set; }
    }

    [Serializable]
    public class CharacterItemCoatingData
    {
        public string CoatingItemId { get; set; }
        public long ExpiresAtUnixUtc { get; set; }
        public bool Consumed { get; set; }
    }

    [Serializable]
    public class CharacterItemStatData
    {
        public ItemStatType Type { get; set; }
        public int Value { get; set; }
    }

    [Serializable]
    public class CharacterItemPropData
    {
        public string k { get; set; }
        public string v { get; set; }
    }

    // Note: these property names come from Unity and are intentionally snake_case / lower-case
    [Serializable]
    public class HumanBodyData
    {
        public float age { get; set; }
        public float face_shape { get; set; }
        public float brows_height { get; set; }
        public float brows_angle { get; set; }
        public float cheek_volume { get; set; }
        public float cheek_height { get; set; }
        public float jaw_extrusion { get; set; }
        public float chin_height { get; set; }
        public float chin_width { get; set; }
        public float chin_angle { get; set; }
        public float eye_distance { get; set; }
        public float eye_shape { get; set; }
        public float eyefold_position { get; set; }
        public float eyefold_volume { get; set; }
        public float outereye_angle { get; set; }
        public float innereye_angle { get; set; }
        public float epicanthus { get; set; }
        public float nose_length { get; set; }
        public float nose_depth { get; set; }
        public float nose_curve { get; set; }
        public float nose_angle { get; set; }
        public float nose_width { get; set; }
        public float nose_bridge { get; set; }
        public float nose_position { get; set; }
        public float nose_compression { get; set; }
        public float nostrils { get; set; }
        public float mouth_depth { get; set; }
        public float mouth_width { get; set; }
        public float dimples { get; set; }
        public float laugh_lines { get; set; }
        public float lip_size { get; set; }
        public float lowerlip_volume { get; set; }
        public float lowerlip_shape { get; set; }
        public float lowerlip_height { get; set; }
        public float upperlip_volume { get; set; }
        public float upperlip_shape { get; set; }
        public float upperlip_height { get; set; }
        public float philtrum { get; set; }
    }

    [Serializable]
    public class CharacterRecipeData
    {
        public string recipeId { get; set; }
        public DateTime unlockedTime { get; set; }
        public int timesCrafted { get; set; }
        public bool isFavourite { get; set; }
    }

    [Serializable]
    public class CharacterSkillData
    {
        public double totalXp { get; set; }
        public Dictionary<string, double> attributes { get; set; } = new();
    }

    [Serializable]
    public class CharacterQuestData
    {
        public CharacterQuestItemData[] activeQuests { get; set; }
        public string[] completedQuests { get; set; }
    }

    [Serializable]
    public class CharacterQuestItemData
    {
        public string questId { get; set; }
        public bool isTracked { get; set; }
        public int[] stepProgress { get; set; }
    }

    [Serializable]
    public class CharacterGuildData
    {
        public string guildId { get; set; }
        public string guildName { get; set; }
    }

    [Serializable]
    public class CharacterTitleData
    {
        public string activeTitle { get; set; }
        public CharacterTitleItemData[] titles { get;set; }
    }

    [Serializable]
    public class CharacterTitleItemData
    {
        public string titleId { get; set; }
        public int amount { get; set; }
        public int lastLevel { get; set; }
        public DateTime lastUpdated { get; set; }
    }

    [Serializable]
    public class CharacterBeastiaryData
    {
        public CharacterBeastiaryEntryData[] entries { get; set; }
    }

    [Serializable]
    public class CharacterBeastiaryEntryData
    {
        public string npcId { get; set; }
        public bool encountered { get; set; }
        public CharacterBeastiaryEntryProgressData[] progress { get; set; }
    }

    [Serializable]
    public class CharacterBeastiaryEntryProgressData
    {
        public string targetId { get; set; }
        public int quantity { get; set; }
        public bool fulfilled { get; set; }
    }

    [Serializable]
    public class CharacterResearchData
    {
        public Dictionary<MaterialType, CharacterResearchMaterialData> materialInsights { get; set; }
        public Dictionary<string, ItemQuality> recipes { get; set; }
    }

    [Serializable]
    public class CharacterResearchMaterialData
    {
        public int mi { get; set; }
    }

    [Serializable]
    public class CharacterSpellsData
    {
        public string spells { get; set; } // base64 – 1 bit per spellId
    }

    [Serializable]
    public class CharacterCosmeticData
    {
        public string activeCastEffect { get; set; }
        public string[] unlockedCastEffects { get; set; }
        public string[] unlockedSkins { get; set; }
    }
}
