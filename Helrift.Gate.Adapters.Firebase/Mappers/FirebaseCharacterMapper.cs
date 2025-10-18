using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Adapters.Firebase
{
    internal static class FirebaseCharacterMapper
    {
        // ---------------- READ (JsonElement -> CharacterData) ----------------
        public static CharacterData FromFirebase(string accountId, string charId, JsonElement el)
        {
            var c = new CharacterData
            {
                Id = charId,
                Username = J.Str(el, "username") ?? accountId,
                CharacterName = J.Str(el, "character_name", "characterName"),
                MapId = J.Str(el, "map_id", "mapId"),
                Position = J.Vec3(el, "position", "pos"),
                Rotation = J.Vec3(el, "rotation", "rot"),
                Side = J.Int(el, "side"),
                SideStatus = J.Int(el, "side_status", "sideStatus"),
                Gender = (Gender)J.Int(el, "gender"),
                Appearance = ReadAppearance(el, "appearance"),
                Hp = J.Int(el, "hp", "HP"),
                Mp = J.Int(el, "mp", "MP"),
                Sp = J.Int(el, "sp", "SP"),
                Level = J.Int(el, "level"),
                Strength = J.Int(el, "strength"),
                Dexterity = J.Int(el, "dexterity"),
                Vitality = J.Int(el, "vitality"),
                Magic = J.Int(el, "magic"),
                Intelligence = J.Int(el, "intelligence"),
                Finesse = J.Int(el, "finesse"),
                Experience = J.Long(el, "experience"),
                Criticals = J.Int(el, "criticals"),
                EnemyKillPoints = J.Int(el, "enemy_kill_points", "enemyKillPoints"),
                MajesticPoints = J.Int(el, "majestic_points", "majesticPoints"),
                Inventory = ReadInventory(el, "inventory"),
                Warehouse = ReadInventory(el, "warehouse"),
                Skills = ReadSkills(el, "skills"),
                Quests = ReadQuests(el, "quests"),
                Titles = ReadTitles(el, "titles"),
                Guild = ReadGuild(el, "guild"),
                Beastiary = ReadBeastiary(el, "beastiary"),
                Research = ReadResearch(el, "research"),
                Spells = ReadSpells(el, "spells"),
                Cosmetics = ReadCosmetics(el, "cosmetics"),
                LastLoggedIn = J.Date(el, "last_logged_in", "lastLoggedIn")
            };
            return c;
        }

        // ---------------- WRITE (CharacterData -> snake_case dict) ----------------
        public static Dictionary<string, object> ToFirebaseDict(CharacterData c)
        {
            var dict = new Dictionary<string, object>(64)
            {
                ["id"] = c.Id ?? "",
                ["username"] = c.Username ?? "",
                ["character_name"] = c.CharacterName ?? "",
                ["map_id"] = c.MapId ?? "",
                ["position"] = Obj(c.Position),
                ["rotation"] = Obj(c.Rotation),
                ["side"] = c.Side,
                ["side_status"] = c.SideStatus,
                ["gender"] = (int)c.Gender,
                ["appearance"] = Obj(c.Appearance),
                ["hp"] = c.Hp,
                ["mp"] = c.Mp,
                ["sp"] = c.Sp,
                ["level"] = c.Level,
                ["strength"] = c.Strength,
                ["dexterity"] = c.Dexterity,
                ["vitality"] = c.Vitality,
                ["magic"] = c.Magic,
                ["intelligence"] = c.Intelligence,
                ["finesse"] = c.Finesse,
                ["experience"] = c.Experience,
                ["criticals"] = c.Criticals,
                ["enemy_kill_points"] = c.EnemyKillPoints,
                ["majestic_points"] = c.MajesticPoints,
                ["inventory"] = Arr(c.Inventory, ItemObj),
                ["warehouse"] = Arr(c.Warehouse, ItemObj),
                ["skills"] = Obj(c.Skills, SkillObj),
                ["quests"] = Obj(c.Quests),
                ["titles"] = Obj(c.Titles),
                ["guild"] = Obj(c.Guild),
                ["beastiary"] = Obj(c.Beastiary),
                ["research"] = Obj(c.Research),
                ["spells"] = Obj(c.Spells),
                ["cosmetics"] = Obj(c.Cosmetics),
            };

            if (c.LastLoggedIn.HasValue)
                dict["last_logged_in"] = c.LastLoggedIn.Value.ToUniversalTime();

            return dict;
        }

        // ---------------- nested READ helpers ----------------
        private static HumanAppearance ReadAppearance(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;
            return new HumanAppearance
            {
                BodyData = ReadBodyData(el, "body_data", "bodyData"),
                HairStyle = J.Str(el, "hair_style", "hairStyle"),
                FacialHairStyle = J.Str(el, "facial_hair_style", "facialHairStyle"),
                HairColour = J.Str(el, "hair_colour", "hairColor", "hairColour"),
                SkinColour = J.Int(el, "skin_colour", "skinColor", "skinColour"),
                EyeColour = J.Int(el, "eye_colour", "eyeColor", "eyeColour"),
                EyebrowStyle = J.Str(el, "eyebrow_style", "eyebrowStyle")
            };
        }

        private static HumanBodyData ReadBodyData(JsonElement root, params string[] keys)
        {
            var el = J.Find(root, keys);
            if (el.ValueKind != JsonValueKind.Object) return null;

            var b = new HumanBodyData();
            foreach (var p in el.EnumerateObject())
            {
                float f = J.Float(el, p.Name);
                switch (p.Name)
                {
                    case "age": b.age = f; break;
                    case "face_shape": b.face_shape = f; break;
                    case "brows_height": b.brows_height = f; break;
                    case "brows_angle": b.brows_angle = f; break;
                    case "cheek_volume": b.cheek_volume = f; break;
                    case "cheek_height": b.cheek_height = f; break;
                    case "jaw_extrusion": b.jaw_extrusion = f; break;
                    case "chin_height": b.chin_height = f; break;
                    case "chin_width": b.chin_width = f; break;
                    case "chin_angle": b.chin_angle = f; break;
                    case "eye_distance": b.eye_distance = f; break;
                    case "eye_shape": b.eye_shape = f; break;
                    case "eyefold_position": b.eyefold_position = f; break;
                    case "eyefold_volume": b.eyefold_volume = f; break;
                    case "outereye_angle": b.outereye_angle = f; break;
                    case "innereye_angle": b.innereye_angle = f; break;
                    case "epicanthus": b.epicanthus = f; break;
                    case "nose_length": b.nose_length = f; break;
                    case "nose_depth": b.nose_depth = f; break;
                    case "nose_curve": b.nose_curve = f; break;
                    case "nose_angle": b.nose_angle = f; break;
                    case "nose_width": b.nose_width = f; break;
                    case "nose_bridge": b.nose_bridge = f; break;
                    case "nose_position": b.nose_position = f; break;
                    case "nose_compression": b.nose_compression = f; break;
                    case "nostrils": b.nostrils = f; break;
                    case "mouth_depth": b.mouth_depth = f; break;
                    case "mouth_width": b.mouth_width = f; break;
                    case "dimples": b.dimples = f; break;
                    case "laugh_lines": b.laugh_lines = f; break;
                    case "lip_size": b.lip_size = f; break;
                    case "lowerlip_volume": b.lowerlip_volume = f; break;
                    case "lowerlip_shape": b.lowerlip_shape = f; break;
                    case "lowerlip_height": b.lowerlip_height = f; break;
                    case "upperlip_volume": b.upperlip_volume = f; break;
                    case "upperlip_shape": b.upperlip_shape = f; break;
                    case "upperlip_height": b.upperlip_height = f; break;
                    case "philtrum": b.philtrum = f; break;
                }
            }
            return b;
        }

        private static CharacterItemData[] ReadInventory(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Array) return Array.Empty<CharacterItemData>();
            var list = new List<CharacterItemData>();
            foreach (var it in el.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Object) continue;
                list.Add(ReadItem(it));
            }
            return list.ToArray();
        }

        private static CharacterItemData ReadItem(JsonElement it)
        {
            return new CharacterItemData
            {
                UniqueId = J.Str(it, "unique_id", "uniqueId"),
                ItemId = J.Str(it, "item_id", "itemId"),
                Quantity = J.Int(it, "quantity"),
                SkinId = J.Str(it, "skin_id", "skinId"),
                GuildId = J.Str(it, "guild_id", "guildId"),
                IsEquipped = J.Bool(it, "is_equipped", "isEquipped"),
                EquipmentSlot = (EquipmentSlot)J.Int(it, "equipment_slot", "equipmentSlot"),
                IsDualWield = J.Bool(it, "is_dual_wield", "isDualWield"),
                Colour = J.Str(it, "colour", "color"),
                Endurance = J.Int(it, "endurance"),
                Weight = J.Float(it, "weight"),
                Quality = (ItemQuality)J.Int(it, "quality"),
                UpgradeLevel = J.Int(it, "upgrade_level", "upgradeLevel"),
                IsCrafted = J.Bool(it, "is_crafted", "isCrafted"),
                Stats = ReadStats(it, "stats"),
                Props = ReadProps(it, "props"),
                Contents = ReadContents(it, "contents"),
                Coating = ReadCoating(it, "coating"),
                BagPosition = J.Vec2(it, "bag_position", "bagPosition")
            };
        }

        private static CharacterItemContentsData[] ReadContents(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Array) return Array.Empty<CharacterItemContentsData>();
            var list = new List<CharacterItemContentsData>();
            foreach (var it in el.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Object) continue;
                list.Add(new CharacterItemContentsData
                {
                    UniqueId = J.Str(it, "unique_id", "uniqueId"),
                    ItemId = J.Str(it, "item_id", "itemId"),
                    Quantity = J.Int(it, "quantity"),
                    SkinId = J.Str(it, "skin_id", "skinId"),
                    Colour = J.Str(it, "colour", "color"),
                    Endurance = J.Int(it, "endurance"),
                    Weight = J.Float(it, "weight"),
                    Quality = (ItemQuality)J.Int(it, "quality"),
                    UpgradeLevel = J.Int(it, "upgrade_level", "upgradeLevel"),
                    IsCrafted = J.Bool(it, "is_crafted", "isCrafted"),
                    CraftedBy = J.Str(it, "crafted_by", "craftedBy"),
                    Stats = ReadStats(it, "stats"),
                    Props = ReadProps(it, "props"),
                    Coating = ReadCoating(it, "coating")
                });
            }
            return list.ToArray();
        }

        private static CharacterItemCoatingData ReadCoating(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;
            return new CharacterItemCoatingData
            {
                CoatingItemId = J.Str(el, "coating_item_id", "coatingItemId"),
                ExpiresAtUnixUtc = J.Long(el, "expires_at_unix_utc", "expiresAtUnixUtc"),
                Consumed = J.Bool(el, "consumed")
            };
        }

        private static CharacterItemStatData[] ReadStats(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el)) return Array.Empty<CharacterItemStatData>();
            var list = new List<CharacterItemStatData>();

            if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in el.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object) continue;
                    list.Add(new CharacterItemStatData
                    {
                        Type = (ItemStatType)J.Int(s, "type"),
                        Value = J.Int(s, "value")
                    });
                }
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                // legacy flat map: { "Absorption": 5 }
                foreach (var kv in el.EnumerateObject())
                    list.Add(new CharacterItemStatData { Type = (ItemStatType)J.Int(el, kv.Name), Value = J.Int(el, kv.Name) });
            }

            return list.ToArray();
        }

        private static CharacterItemPropData[] ReadProps(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el)) return Array.Empty<CharacterItemPropData>();
            var list = new List<CharacterItemPropData>();

            if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in el.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Object) continue;
                    list.Add(new CharacterItemPropData { k = J.Str(p, "k", "K"), v = J.Str(p, "v", "V") });
                }
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var kv in el.EnumerateObject())
                    list.Add(new CharacterItemPropData { k = kv.Name, v = kv.Value.GetRawText().Trim('"') });
            }

            return list.ToArray();
        }

        private static Dictionary<string, CharacterSkillData> ReadSkills(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return new();
            var d = new Dictionary<string, CharacterSkillData>(StringComparer.Ordinal);
            foreach (var kv in el.EnumerateObject())
            {
                var v = kv.Value;
                var cs = new CharacterSkillData
                {
                    totalXp = J.Double(v, "total_xp", "totalXp"),
                    attributes = ReadDoubleMap(v, "attributes")
                };
                d[kv.Name] = cs;
            }
            return d;
        }

        private static Dictionary<string, double> ReadDoubleMap(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return new();
            var d = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var kv in el.EnumerateObject())
                d[kv.Name] = kv.Value.ValueKind == JsonValueKind.Number ? kv.Value.GetDouble() :
                             double.TryParse(kv.Value.GetString(), out var dd) ? dd : 0d;
            return d;
        }

        private static CharacterQuestData ReadQuests(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;

            var active = new List<CharacterQuestItemData>();
            if (el.TryGetProperty("active_quests", out var aq) || el.TryGetProperty("activeQuests", out aq))
            {
                if (aq.ValueKind == JsonValueKind.Array)
                {
                    foreach (var q in aq.EnumerateArray())
                    {
                        if (q.ValueKind != JsonValueKind.Object) continue;
                        active.Add(new CharacterQuestItemData
                        {
                            questId = J.Str(q, "quest_id", "questId"),
                            isTracked = J.Bool(q, "is_tracked", "isTracked"),
                            stepProgress = J.IntArray(q, "step_progress", "stepProgress")
                        });
                    }
                }
            }

            string[] completed = Array.Empty<string>();
            if (el.TryGetProperty("completed_quests", out var cq) || el.TryGetProperty("completedQuests", out cq))
            {
                completed = J.StringArrayFromAny(cq);
            }

            return new CharacterQuestData { activeQuests = active.ToArray(), completedQuests = completed };
        }

        private static CharacterTitleData ReadTitles(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;

            var titles = new List<CharacterTitleItemData>();
            if (el.TryGetProperty("titles", out var ts) && ts.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in ts.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.Object) continue;
                    titles.Add(new CharacterTitleItemData
                    {
                        titleId = J.Str(t, "title_id", "titleId"),
                        amount = J.Int(t, "amount"),
                        lastLevel = J.Int(t, "last_level", "lastLevel"),
                        lastUpdated = J.Date(t, "last_updated", "lastUpdated") ?? default
                    });
                }
            }

            return new CharacterTitleData
            {
                activeTitle = J.Str(el, "active_title", "activeTitle"),
                titles = titles.ToArray()
            };
        }

        private static CharacterGuildData ReadGuild(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;
            return new CharacterGuildData
            {
                guildId = J.Str(el, "guild_id", "guildId"),
                guildName = J.Str(el, "guild_name", "guildName")
            };
        }

        private static CharacterBeastiaryData ReadBeastiary(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;

            var list = new List<CharacterBeastiaryEntryData>();
            if (el.TryGetProperty("entries", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in arr.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;

                    var prog = new List<CharacterBeastiaryEntryProgressData>();
                    if (e.TryGetProperty("progress", out var pr) && pr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in pr.EnumerateArray())
                        {
                            if (p.ValueKind != JsonValueKind.Object) continue;
                            prog.Add(new CharacterBeastiaryEntryProgressData
                            {
                                targetId = J.Str(p, "target_id", "targetId"),
                                quantity = J.Int(p, "quantity"),
                                fulfilled = J.Bool(p, "fulfilled")
                            });
                        }
                    }

                    list.Add(new CharacterBeastiaryEntryData
                    {
                        npcId = J.Str(e, "npc_id", "npcId"),
                        encountered = J.Bool(e, "encountered"),
                        progress = prog.ToArray()
                    });
                }
            }

            return new CharacterBeastiaryData { entries = list.ToArray() };
        }

        private static CharacterResearchData ReadResearch(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;

            var r = new CharacterResearchData();

            if (el.TryGetProperty("material_insights", out var mi) || el.TryGetProperty("materialInsights", out mi))
            {
                if (mi.ValueKind == JsonValueKind.Object)
                {
                    foreach (var kv in mi.EnumerateObject())
                    {
                        if (kv.Value.ValueKind != JsonValueKind.Object) continue;
                        var mm = new CharacterResearchMaterialData { mi = J.Int(kv.Value, "mi") };
                        // MaterialType is int-wired; key may be name or number; we accept number string best-effort.
                        if (int.TryParse(kv.Name, out var intKey))
                            r.materialInsights[(MaterialType)intKey] = mm;
                    }
                }
            }

            if (el.TryGetProperty("recipes", out var rc) && rc.ValueKind == JsonValueKind.Object)
            {
                foreach (var kv in rc.EnumerateObject())
                {
                    var q = kv.Value.ValueKind == JsonValueKind.Number ? (int)kv.Value.GetDouble() :
                            int.TryParse(kv.Value.GetString(), out var t) ? t : 0;
                    r.recipes[kv.Name] = (ItemQuality)q;
                }
            }

            return r;
        }

        private static CharacterSpellsData ReadSpells(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;
            return new CharacterSpellsData { spells = J.Str(el, "spells") ?? "" };
        }

        private static CharacterCosmeticData ReadCosmetics(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;
            return new CharacterCosmeticData
            {
                activeCastEffect = J.Str(el, "active_cast_effect", "activeCastEffect"),
                unlockedCastEffects = J.StringArray(el, "unlocked_cast_effects", "unlockedCastEffects"),
                unlockedSkins = J.StringArray(el, "unlocked_skins", "unlockedSkins")
            };
        }

        // ---------------- nested WRITE helpers ----------------
        private static object Obj(Vec3 v) => new Dictionary<string, object> { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };
        private static object Obj(Vec2 v) => new Dictionary<string, object> { ["x"] = v.x, ["y"] = v.y };

        private static object Obj(HumanAppearance a)
            => a == null ? null : new Dictionary<string, object>
            {
                ["body_data"] = Obj(a.BodyData),
                ["hair_style"] = a.HairStyle,
                ["facial_hair_style"] = a.FacialHairStyle,
                ["hair_colour"] = a.HairColour,
                ["skin_colour"] = a.SkinColour,
                ["eye_colour"] = a.EyeColour,
                ["eyebrow_style"] = a.EyebrowStyle
            };

        private static object Obj(HumanBodyData b)
        {
            if (b == null) return null;
            var d = new Dictionary<string, object>();
            void F(string k, float v) => d[k] = v;
            F("age", b.age); F("face_shape", b.face_shape); F("brows_height", b.brows_height); F("brows_angle", b.brows_angle);
            F("cheek_volume", b.cheek_volume); F("cheek_height", b.cheek_height); F("jaw_extrusion", b.jaw_extrusion);
            F("chin_height", b.chin_height); F("chin_width", b.chin_width); F("chin_angle", b.chin_angle);
            F("eye_distance", b.eye_distance); F("eye_shape", b.eye_shape); F("eyefold_position", b.eyefold_position);
            F("eyefold_volume", b.eyefold_volume); F("outereye_angle", b.outereye_angle); F("innereye_angle", b.innereye_angle);
            F("epicanthus", b.epicanthus); F("nose_length", b.nose_length); F("nose_depth", b.nose_depth);
            F("nose_curve", b.nose_curve); F("nose_angle", b.nose_angle); F("nose_width", b.nose_width);
            F("nose_bridge", b.nose_bridge); F("nose_position", b.nose_position); F("nose_compression", b.nose_compression);
            F("nostrils", b.nostrils); F("mouth_depth", b.mouth_depth); F("mouth_width", b.mouth_width);
            F("dimples", b.dimples); F("laugh_lines", b.laugh_lines); F("lip_size", b.lip_size);
            F("lowerlip_volume", b.lowerlip_volume); F("lowerlip_shape", b.lowerlip_shape); F("lowerlip_height", b.lowerlip_height);
            F("upperlip_volume", b.upperlip_volume); F("upperlip_shape", b.upperlip_shape); F("upperlip_height", b.upperlip_height);
            F("philtrum", b.philtrum);
            return d;
        }

        private static object Arr<T>(IEnumerable<T> arr, Func<T, object> map)
            => arr == null ? Array.Empty<object>() : arr.Select(map).ToArray();

        private static object Obj(Dictionary<string, CharacterSkillData> skills, Func<KeyValuePair<string, CharacterSkillData>, KeyValuePair<string, object>> projector)
        {
            if (skills == null) return new Dictionary<string, object>();
            var d = new Dictionary<string, object>(skills.Count, StringComparer.Ordinal);
            foreach (var kv in skills.Select(projector))
                d[kv.Key] = kv.Value;
            return d;
        }

        private static KeyValuePair<string, object> SkillObj(KeyValuePair<string, CharacterSkillData> kv)
        {
            var v = kv.Value ?? new CharacterSkillData();
            return new KeyValuePair<string, object>(kv.Key, new Dictionary<string, object>
            {
                ["total_xp"] = v.totalXp,
                ["attributes"] = v.attributes ?? new Dictionary<string, double>()
            });
        }

        private static object Obj(CharacterQuestData q)
            => q == null ? null : new Dictionary<string, object>
            {
                ["active_quests"] = Arr(q.activeQuests ?? Array.Empty<CharacterQuestItemData>(), x =>
                    new Dictionary<string, object>
                    {
                        ["quest_id"] = x.questId,
                        ["is_tracked"] = x.isTracked,
                        ["step_progress"] = x.stepProgress ?? Array.Empty<int>()
                    }),
                ["completed_quests"] = q.completedQuests ?? Array.Empty<string>()
            };

        private static object Obj(CharacterTitleData t)
            => t == null ? null : new Dictionary<string, object>
            {
                ["active_title"] = t.activeTitle,
                ["titles"] = Arr(t.titles ?? Array.Empty<CharacterTitleItemData>(), x =>
                    new Dictionary<string, object>
                    {
                        ["title_id"] = x.titleId,
                        ["amount"] = x.amount,
                        ["last_level"] = x.lastLevel,
                        ["last_updated"] = x.lastUpdated
                    })
            };

        private static object Obj(CharacterGuildData g)
            => g == null ? null : new Dictionary<string, object>
            {
                ["guild_id"] = g.guildId,
                ["guild_name"] = g.guildName
            };

        private static object Obj(CharacterBeastiaryData b)
            => b == null ? null : new Dictionary<string, object>
            {
                ["entries"] = Arr(b.entries ?? Array.Empty<CharacterBeastiaryEntryData>(), e =>
                    new Dictionary<string, object>
                    {
                        ["npc_id"] = e.npcId,
                        ["encountered"] = e.encountered,
                        ["progress"] = Arr(e.progress ?? Array.Empty<CharacterBeastiaryEntryProgressData>(), p =>
                            new Dictionary<string, object>
                            {
                                ["target_id"] = p.targetId,
                                ["quantity"] = p.quantity,
                                ["fulfilled"] = p.fulfilled
                            })
                    })
            };

        private static object Obj(CharacterResearchData r)
        {
            if (r == null) return null;
            var mi = new Dictionary<string, object>();
            foreach (var kv in r.materialInsights ?? new Dictionary<MaterialType, CharacterResearchMaterialData>())
                mi[((int)kv.Key).ToString()] = new Dictionary<string, object> { ["mi"] = kv.Value?.mi ?? 0 };

            var rc = new Dictionary<string, int>();
            foreach (var kv in r.recipes ?? new Dictionary<string, ItemQuality>())
                rc[kv.Key] = (int)kv.Value;

            return new Dictionary<string, object>
            {
                ["material_insights"] = mi,
                ["recipes"] = rc
            };
        }

        private static object Obj(CharacterSpellsData s)
            => s == null ? null : new Dictionary<string, object> { ["spells"] = s.spells ?? "" };

        private static object Obj(CharacterCosmeticData c)
            => c == null ? null : new Dictionary<string, object>
            {
                ["active_cast_effect"] = c.activeCastEffect ?? "",
                ["unlocked_cast_effects"] = c.unlockedCastEffects ?? Array.Empty<string>(),
                ["unlocked_skins"] = c.unlockedSkins ?? Array.Empty<string>()
            };

        private static object ItemObj(CharacterItemData it)
            => new Dictionary<string, object>
            {
                ["unique_id"] = it.UniqueId,
                ["item_id"] = it.ItemId,
                ["quantity"] = it.Quantity,
                ["skin_id"] = it.SkinId,
                ["guild_id"] = it.GuildId,
                ["is_equipped"] = it.IsEquipped,
                ["equipment_slot"] = (int)it.EquipmentSlot,
                ["is_dual_wield"] = it.IsDualWield,
                ["colour"] = it.Colour,
                ["endurance"] = it.Endurance,
                ["weight"] = it.Weight,
                ["quality"] = (int)it.Quality,
                ["upgrade_level"] = it.UpgradeLevel,
                ["is_crafted"] = it.IsCrafted,
                ["stats"] = Arr(it.Stats ?? Array.Empty<CharacterItemStatData>(), s => new Dictionary<string, object> { ["type"] = (int)s.Type, ["value"] = s.Value }),
                ["props"] = Arr(it.Props ?? Array.Empty<CharacterItemPropData>(), p => new Dictionary<string, object> { ["k"] = p.k, ["v"] = p.v }),
                ["contents"] = Arr(it.Contents ?? Array.Empty<CharacterItemContentsData>(), ContentObj),
                ["coating"] = Obj(it.Coating),
                ["bag_position"] = Obj(it.BagPosition)
            };

        private static object ContentObj(CharacterItemContentsData it)
            => new Dictionary<string, object>
            {
                ["unique_id"] = it.UniqueId,
                ["item_id"] = it.ItemId,
                ["quantity"] = it.Quantity,
                ["skin_id"] = it.SkinId,
                ["colour"] = it.Colour,
                ["endurance"] = it.Endurance,
                ["weight"] = it.Weight,
                ["quality"] = (int)it.Quality,
                ["upgrade_level"] = it.UpgradeLevel,
                ["is_crafted"] = it.IsCrafted,
                ["crafted_by"] = it.CraftedBy,
                ["stats"] = Arr(it.Stats ?? Array.Empty<CharacterItemStatData>(), s => new Dictionary<string, object> { ["type"] = (int)s.Type, ["value"] = s.Value }),
                ["props"] = Arr(it.Props ?? Array.Empty<CharacterItemPropData>(), p => new Dictionary<string, object> { ["k"] = p.k, ["v"] = p.v }),
                ["coating"] = Obj(it.Coating)
            };

        private static object Obj(CharacterItemCoatingData c)
            => c == null ? null : new Dictionary<string, object>
            {
                ["coating_item_id"] = c.CoatingItemId,
                ["expires_at_unix_utc"] = c.ExpiresAtUnixUtc,
                ["consumed"] = c.Consumed
            };

        // ---------------- JSON access helpers ----------------
        private static class J
        {
            public static JsonElement Find(JsonElement root, params string[] keys)
            {
                foreach (var k in keys)
                    if (root.TryGetProperty(k, out var el)) return el;
                return default;
            }

            public static string Str(JsonElement obj, params string[] names)
            {
                foreach (var n in names)
                    if (obj.TryGetProperty(n, out var el))
                    {
                        if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? "";
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

            public static long Long(JsonElement obj, params string[] names)
            {
                foreach (var n in names)
                    if (obj.TryGetProperty(n, out var el))
                    {
                        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var i)) return i;
                        if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out var s)) return s;
                    }
                return 0L;
            }

            public static double Double(JsonElement obj, params string[] names)
            {
                foreach (var n in names)
                    if (obj.TryGetProperty(n, out var el))
                    {
                        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d)) return d;
                        if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), out var s)) return s;
                    }
                return 0d;
            }

            public static float Float(JsonElement obj, params string[] names)
            {
                foreach (var n in names)
                    if (obj.TryGetProperty(n, out var el))
                    {
                        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d)) return (float)d;
                        if (el.ValueKind == JsonValueKind.String && float.TryParse(el.GetString(), out var s)) return s;
                    }
                return 0f;
            }

            public static bool Bool(JsonElement obj, params string[] names)
            {
                foreach (var n in names)
                    if (obj.TryGetProperty(n, out var el))
                    {
                        if (el.ValueKind == JsonValueKind.True) return true;
                        if (el.ValueKind == JsonValueKind.False) return false;
                        if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var b)) return b;
                    }
                return false;
            }

            public static Vec3 Vec3(JsonElement obj, params string[] names)
            {
                foreach (var n in names)
                    if (obj.TryGetProperty(n, out var el))
                    {
                        if (el.ValueKind == JsonValueKind.Object)
                            return new Vec3 { x = Float(el, "x"), y = Float(el, "y"), z = Float(el, "z") };
                        if (el.ValueKind == JsonValueKind.Array)
                        {
                            var a = el.EnumerateArray().Select(e =>
                                e.ValueKind == JsonValueKind.Number ? (float)e.GetDouble() :
                                e.ValueKind == JsonValueKind.String && float.TryParse(e.GetString(), out var f) ? f : 0f).ToArray();
                            return new Vec3 { x = a.ElementAtOrDefault(0), y = a.ElementAtOrDefault(1), z = a.ElementAtOrDefault(2) };
                        }
                    }
                return new Vec3();
            }

            public static Vec2 Vec2(JsonElement obj, params string[] names)
            {
                foreach (var n in names)
                    if (obj.TryGetProperty(n, out var el))
                    {
                        if (el.ValueKind == JsonValueKind.Object)
                            return new Vec2 { x = Float(el, "x"), y = Float(el, "y") };
                        if (el.ValueKind == JsonValueKind.Array)
                        {
                            var a = el.EnumerateArray().Select(e =>
                                e.ValueKind == JsonValueKind.Number ? (float)e.GetDouble() :
                                e.ValueKind == JsonValueKind.String && float.TryParse(e.GetString(), out var f) ? f : 0f).ToArray();
                            return new Vec2 { x = a.ElementAtOrDefault(0), y = a.ElementAtOrDefault(1) };
                        }
                    }
                return new Vec2();
            }

            public static int[] IntArray(JsonElement obj, params string[] names)
            {
                foreach (var n in names)
                    if (obj.TryGetProperty(n, out var el) && el.ValueKind == JsonValueKind.Array)
                        return el.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var i) ? i :
                                                               int.TryParse(e.GetString(), out var s) ? s : 0).ToArray();
                return Array.Empty<int>();
            }

            public static string[] StringArray(JsonElement obj, params string[] names)
            {
                foreach (var n in names)
                    if (obj.TryGetProperty(n, out var el)) return StringArrayFromAny(el);
                return Array.Empty<string>();
            }

            public static string[] StringArrayFromAny(JsonElement el)
            {
                if (el.ValueKind == JsonValueKind.Array)
                    return el.EnumerateArray().Select(e => e.GetRawText().Trim('"')).ToArray();
                if (el.ValueKind == JsonValueKind.Object)
                    return el.EnumerateObject().Select(p => p.Value.GetRawText().Trim('"')).ToArray();
                return Array.Empty<string>();
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
}
