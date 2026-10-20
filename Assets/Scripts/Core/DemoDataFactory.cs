using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NexusAnima
{
    public static class DemoDataFactory
    {
        const string PlayerResourcePath = "Data/Spirits/Player";
        const string EnemyResourcePath = "Data/Spirits/Enemy";
        const string MasterResourcePath = "Data/Masters/FateMaster_Fire";

        public static List<SpiritDefinition> CreatePlayerRoster()
        {
            var loaded = LoadRoster(PlayerResourcePath);
            if (loaded.Count > 0) return loaded;
            return CreatePlayerRosterFallback();
        }

        public static List<SpiritDefinition> CreateEnemyRoster()
        {
            var loaded = LoadRoster(EnemyResourcePath);
            if (loaded.Count > 0) return loaded;
            return CreateEnemyRosterFallback();
        }

        public static FateMasterDefinition CreateFateMaster()
        {
            var loaded = Resources.Load<FateMasterDefinition>(MasterResourcePath);
            if (loaded != null) return loaded.Clone();

            var created = ScriptableObject.CreateInstance<FateMasterDefinition>();
            created.name = "master_fire_runtime";
            return created;
        }

        public static Vector2Int[] DefaultEnemySlots()
        {
            return new[]
            {
                new Vector2Int(1, 5),
                new Vector2Int(3, 5),
                new Vector2Int(0, 6),
                new Vector2Int(4, 6),
                new Vector2Int(2, 7),
            };
        }

        /// <summary>
        /// Default enemy lineup ids drawn from the shared spirit pool
        /// (same catalog as player — team is decided by drop zone).
        /// </summary>
        public static string[] DefaultEnemyPoolIds()
        {
            return new[]
            {
                "p_fire_tank",
                "p_fire_dps",
                "p_competition_dps",
                "p_grass_dps",
                "p_beauty_dps",
            };
        }

        static List<SpiritDefinition> LoadRoster(string resourcePath)
        {
            var assets = Resources.LoadAll<SpiritDefinition>(resourcePath);
            if (assets == null || assets.Length == 0)
                return new List<SpiritDefinition>();

            return assets
                .Where(a => a != null)
                .OrderBy(a => a.Id)
                .Select(a =>
                {
                    a.EnsurePresentation();
                    return a.Clone();
                })
                .ToList();
        }

        public static List<SpiritDefinition> CreatePlayerRosterFallback()
        {
            return new List<SpiritDefinition>
            {
                WithProfile(Make("p_fire_tank", "火枪女王", ElementType.Fire, TraitType.Gladiator,
                    800, 40, 30, 1.0f, 3f, 15f, new Color(0.95f, 0.35f, 0.2f)),
                    "战争", "指挥官", "后排物理射手", "火枪兽"),
                WithProfile(Make("p_fire_dps", "宵狐妮", ElementType.Fire, TraitType.Gladiator,
                    500, 70, 15, 1.2f, 3f, 12f, new Color(1f, 0.45f, 0.15f)),
                    "黑暗", "小脾气", "后排输出 / 功能控制", "狐"),
                WithProfile(Make("p_water_tank", "皎鹿", ElementType.Water, TraitType.Gladiator,
                    850, 38, 32, 0.95f, 3f, 14f, new Color(0.25f, 0.55f, 0.95f)),
                    "光明", "小天使", "后排法术输出", "鹿"),
                WithProfile(Make("p_water_sup", "爱巧驼", ElementType.Water, TraitType.Vanguard,
                    450, 35, 12, 1.3f, 1f, 10f, new Color(0.35f, 0.75f, 1f), physical: false),
                    "饱足", "小脾气", "前排功能坦 / 持续输出", "羊驼"),
                WithProfile(Make("p_grass_tank", "安布蕊娜", ElementType.Grass, TraitType.Explorer,
                    820, 42, 28, 1.0f, 3f, 15f, new Color(0.3f, 0.75f, 0.35f)),
                    "美丽", "吉祥物", "后排功能辅助", "伞灵"),
                WithProfile(Make("p_grass_dps", "小墨驹", ElementType.Grass, TraitType.Gladiator,
                    480, 68, 14, 1.25f, 1f, 12f, new Color(0.45f, 0.9f, 0.3f)),
                    "铭记", "角斗士", "前排物理输出", "墨驹"),
                WithProfile(Make("p_light_dps", "阿波罗斯", ElementType.Light, TraitType.Vanguard,
                    520, 72, 16, 1.2f, 1f, 12f, new Color(1f, 0.92f, 0.45f)),
                    "光明", "保护者", "前排坦克（守护）", "圣兽"),
                WithProfile(Make("p_light_sup", "墨角兽", ElementType.Light, TraitType.Vanguard,
                    460, 36, 13, 1.3f, 1f, 10f, new Color(1f, 0.98f, 0.7f), physical: false),
                    "铭记", "探险家", "前排坦克 / 半输出", "独角兽"),
                WithProfile(Make("p_dark_dps", "夜影", ElementType.Fire, TraitType.Gladiator,
                    560, 62, 18, 1.15f, 1f, 12f, new Color(0.22f, 0.12f, 0.3f)),
                    "黑暗", "角斗士", "前排物理输出", "影兽"),
                WithProfile(Make("p_beauty_dps", "凤翎", ElementType.Grass, TraitType.Explorer,
                    440, 45, 12, 1.2f, 3f, 10f, new Color(0.95f, 0.35f, 0.2f), physical: false),
                    "美丽", "小天使", "后排法术辅助输出", "凤鸟"),
                WithProfile(Make("p_competition_dps", "芒星鹫", ElementType.Water, TraitType.Gladiator,
                    520, 65, 14, 1.2f, 3f, 12f, new Color(0.15f, 0.3f, 0.7f)),
                    "竞争", "角斗士", "高机动远程物理输出", "星鹫"),
            };
        }

        public static List<SpiritDefinition> CreateEnemyRosterFallback()
        {
            return new List<SpiritDefinition>
            {
                WithProfile(Make("e_grass_tank1", "敌·火枪女王", ElementType.Grass, TraitType.Vanguard,
                    780, 38, 28, 1.0f, 3f, 14f, new Color(0.2f, 0.45f, 0.2f)),
                    "", "", "", "火枪兽"),
                WithProfile(Make("e_grass_tank2", "敌·宵狐妮", ElementType.Grass, TraitType.Vanguard,
                    780, 38, 28, 1.0f, 3f, 14f, new Color(0.25f, 0.5f, 0.22f)),
                    "", "", "", "狐"),
                WithProfile(Make("e_water_dps1", "敌·芒星鹫", ElementType.Water, TraitType.Gladiator,
                    480, 65, 14, 1.2f, 3f, 12f, new Color(0.15f, 0.3f, 0.7f)),
                    "", "", "", "星鹫"),
                WithProfile(Make("e_water_dps2", "敌·浪刃乙", ElementType.Water, TraitType.Gladiator,
                    480, 65, 14, 1.2f, 3f, 12f, new Color(0.18f, 0.35f, 0.75f)),
                    "", "", "", "浪刃兽"),
                WithProfile(Make("e_fire_sup", "敌·凤翎", ElementType.Fire, TraitType.Explorer,
                    430, 34, 12, 1.25f, 3f, 10f, new Color(0.55f, 0.2f, 0.15f), physical: false),
                    "", "", "", "凤鸟"),
            };
        }

        static SpiritDefinition Make(
            string id, string displayName, ElementType element, TraitType trait,
            float hp, float atk, float defense, float aspd, float range, float energy,
            Color color, bool physical = true)
        {
            var spirit = ScriptableObject.CreateInstance<SpiritDefinition>();
            spirit.name = id;
            spirit.Id = id;
            spirit.DisplayName = displayName;
            spirit.Element = element;
            spirit.Trait = trait;
            spirit.MaxHp = hp;
            spirit.Atk = atk;
            spirit.Def = defense;
            spirit.AttackSpeed = aspd;
            spirit.AttackRange = range;
            spirit.EnergyPerAttack = energy;
            spirit.Color = color;
            // Unit skills temporarily disabled for all spirits.
            spirit.ActiveSkillName = "";
            spirit.ActiveSkillKind = SkillKind.BasicAttack;
            spirit.SkillMultiplier = 1f;
            spirit.SkillDuration = 0f;
            spirit.SkillHits = 1;
            spirit.HealAmount = 0f;
            spirit.IsPhysical = physical;
            spirit.MoveSpeed = trait == TraitType.Vanguard ? 3.2f : 3.8f;
            SpiritPresentationCatalog.ApplyDefaults(spirit);
            return spirit;
        }

        static SpiritDefinition WithProfile(
            SpiritDefinition definition,
            string principle,
            string trait,
            string role,
            string species = "")
        {
            definition.PrincipleDisplayName = principle;
            definition.TraitDisplayName = trait;
            definition.RoleDisplayName = role;
            definition.SpeciesDisplayName = species;
            return definition;
        }

        public static string ElementLabel(ElementType e)
        {
            switch (e)
            {
                case ElementType.Fire: return "火";
                case ElementType.Water: return "水";
                case ElementType.Grass: return "草";
                case ElementType.Light: return "光";
                default: return "?";
            }
        }

        public static string SpeciesLabel(SpiritDefinition definition)
        {
            if (definition != null && !string.IsNullOrEmpty(definition.SpeciesDisplayName))
                return definition.SpeciesDisplayName;
            return "未知";
        }

        public static string PrincipleLabel(SpiritDefinition definition)
        {
            if (definition != null && !string.IsNullOrEmpty(definition.PrincipleDisplayName))
                return definition.PrincipleDisplayName;
            return definition != null ? ElementLabel(definition.Element) : "?";
        }

        public static string TraitLabel(SpiritDefinition definition)
        {
            if (definition != null && !string.IsNullOrEmpty(definition.TraitDisplayName))
                return definition.TraitDisplayName;
            return definition != null ? TraitLabel(definition.Trait) : "?";
        }

        public static string TraitLabel(TraitType t)
        {
            switch (t)
            {
                case TraitType.Vanguard: return "冲锋员";
                case TraitType.Gladiator: return "角斗士";
                case TraitType.Explorer: return "探险家";
                default: return "?";
            }
        }

        /// <summary>Uses SpiritDefinition.Targeting (catalog-backed).</summary>
        public static bool IsRangedCombatant(SpiritDefinition definition)
        {
            if (definition == null) return false;
            definition.EnsurePresentation();
            return definition.IsRanged || definition.AttackRange >= 2.5f;
        }

        public static Color ElementVfxColor(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return new Color(1f, 0.45f, 0.12f, 1f);
                case ElementType.Water: return new Color(0.25f, 0.7f, 1f, 1f);
                case ElementType.Grass: return new Color(0.35f, 0.95f, 0.35f, 1f);
                case ElementType.Light: return new Color(1f, 0.95f, 0.55f, 1f);
                default: return Color.white;
            }
        }
    }
}
