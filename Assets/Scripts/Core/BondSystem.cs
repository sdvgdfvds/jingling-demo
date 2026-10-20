using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NexusAnima
{
    /// <summary>Per-spirit bond bonuses (scoped to related elements only).</summary>
    public struct BondBonuses
    {
        public float Attack;
        public float Hp;
        public float Defense;
        public float Energy;
        public bool FireBurn;
        public bool GrassDamageReduce;
        public bool LightCritHint;
    }

    public class BondState
    {
        public const int MaxSelected = 3;

        public readonly Dictionary<ElementType, int> Counts = new Dictionary<ElementType, int>();
        /// <summary>Eligible element-tier levels (count met), whether or not selected.</summary>
        public readonly Dictionary<ElementType, int> EligibleLevels = new Dictionary<ElementType, int>();
        /// <summary>Eligible complementary pair defs (condition met).</summary>
        public readonly List<BondDefinition> EligiblePairs = new List<BondDefinition>();
        /// <summary>Player-chosen bond keys (tier_Fire / bond id). Cap = MaxSelected.</summary>
        public readonly HashSet<string> SelectedKeys = new HashSet<string>();

        // Applied (selected only) — UI totals + combat via GetBonusesFor.
        public readonly Dictionary<ElementType, int> Levels = new Dictionary<ElementType, int>();
        public readonly List<BondDefinition> ActiveBonds = new List<BondDefinition>();

        public float AttackBonus;
        public float HpBonus;
        public float DefenseBonus;
        public float EnergyBonus;
        public bool FireBurn;
        public bool GrassDamageReduce;
        public bool LightCritHint;

        const float CapAttack = 0.40f;
        const float CapHp = 0.40f;
        const float CapDefense = 0.40f;
        const float CapEnergy = 0.50f;

        public static string TierKey(ElementType element) => "tier_" + element;

        public void Recalculate(IEnumerable<SpiritDefinition> spirits)
        {
            Counts.Clear();
            EligibleLevels.Clear();
            EligiblePairs.Clear();

            foreach (var s in spirits)
            {
                if (s == null) continue;
                if (!Counts.ContainsKey(s.Element)) Counts[s.Element] = 0;
                Counts[s.Element]++;
            }

            foreach (var kv in Counts)
            {
                int level = kv.Value >= 4 ? 2 : (kv.Value >= 2 ? 1 : 0);
                if (level > 0) EligibleLevels[kv.Key] = level;
            }

            BondCatalog.EnsureLoaded();
            foreach (var def in BondCatalog.All)
            {
                if (def == null || def.MatchKind != BondMatchKind.ElementPair) continue;
                if (!def.Matches(Counts)) continue;
                EligiblePairs.Add(def);
            }

            // Drop selections that are no longer eligible.
            SelectedKeys.RemoveWhere(key => !IsEligible(key));

            RebuildAppliedBonuses();
        }

        public bool IsEligible(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (key.StartsWith("tier_"))
            {
                if (!TryParseTierKey(key, out var el)) return false;
                return EligibleLevels.ContainsKey(el);
            }
            foreach (var def in EligiblePairs)
            {
                if (def != null && def.Id == key) return true;
            }
            return false;
        }

        public bool IsSelected(string key) =>
            !string.IsNullOrEmpty(key) && SelectedKeys.Contains(key);

        public int SelectedCount => SelectedKeys.Count;

        /// <summary>
        /// Toggle a bond on/off. Returns false if ineligible or already at MaxSelected when turning on.
        /// </summary>
        public bool TryToggle(string key)
        {
            if (!IsEligible(key)) return false;

            if (SelectedKeys.Contains(key))
            {
                SelectedKeys.Remove(key);
                RebuildAppliedBonuses();
                return true;
            }

            if (SelectedKeys.Count >= MaxSelected)
                return false;

            SelectedKeys.Add(key);
            RebuildAppliedBonuses();
            return true;
        }

        void RebuildAppliedBonuses()
        {
            Levels.Clear();
            ActiveBonds.Clear();
            AttackBonus = HpBonus = DefenseBonus = EnergyBonus = 0f;
            FireBurn = GrassDamageReduce = LightCritHint = false;

            foreach (var kv in EligibleLevels)
            {
                string key = TierKey(kv.Key);
                if (!SelectedKeys.Contains(key)) continue;
                Levels[kv.Key] = kv.Value;
                ApplyLegacyElementTotals(kv.Key, kv.Value);
            }

            foreach (var def in EligiblePairs)
            {
                if (def == null || !SelectedKeys.Contains(def.Id)) continue;
                ActiveBonds.Add(def);
                AttackBonus += def.AttackBonus;
                HpBonus += def.HpBonus;
                DefenseBonus += def.DefenseBonus;
                EnergyBonus += def.EnergyBonus;
            }

            AttackBonus = Mathf.Min(AttackBonus, CapAttack);
            HpBonus = Mathf.Min(HpBonus, CapHp);
            DefenseBonus = Mathf.Min(DefenseBonus, CapDefense);
            EnergyBonus = Mathf.Min(EnergyBonus, CapEnergy);
        }

        /// <summary>
        /// Bonuses for a spirit of the given element (selected bonds only, related elements only).
        /// </summary>
        public BondBonuses GetBonusesFor(ElementType element)
        {
            var b = new BondBonuses();

            if (Levels.TryGetValue(element, out int level) && level > 0)
                ApplyLegacyTo(ref b, element, level);

            foreach (var def in ActiveBonds)
            {
                if (def == null) continue;
                if (!AffectsElement(def, element)) continue;
                b.Attack += def.AttackBonus;
                b.Hp += def.HpBonus;
                b.Defense += def.DefenseBonus;
                b.Energy += def.EnergyBonus;
            }

            b.Attack = Mathf.Min(b.Attack, CapAttack);
            b.Hp = Mathf.Min(b.Hp, CapHp);
            b.Defense = Mathf.Min(b.Defense, CapDefense);
            b.Energy = Mathf.Min(b.Energy, CapEnergy);
            return b;
        }

        static bool AffectsElement(BondDefinition def, ElementType element)
        {
            if (def.MatchKind == BondMatchKind.ElementCount)
                return def.Element == element;
            return def.ElementA == element || def.ElementB == element;
        }

        static bool TryParseTierKey(string key, out ElementType element)
        {
            element = default;
            if (string.IsNullOrEmpty(key) || !key.StartsWith("tier_")) return false;
            string name = key.Substring(5);
            return System.Enum.TryParse(name, out element);
        }

        void ApplyLegacyElementTotals(ElementType element, int level)
        {
            if (level <= 0) return;
            var tmp = new BondBonuses();
            ApplyLegacyTo(ref tmp, element, level);
            AttackBonus += tmp.Attack;
            HpBonus += tmp.Hp;
            DefenseBonus += tmp.Defense;
            EnergyBonus += tmp.Energy;
            if (tmp.FireBurn) FireBurn = true;
            if (tmp.GrassDamageReduce) GrassDamageReduce = true;
            if (tmp.LightCritHint) LightCritHint = true;
        }

        static void ApplyLegacyTo(ref BondBonuses b, ElementType element, int level)
        {
            if (level <= 0) return;
            switch (element)
            {
                case ElementType.Fire:
                    b.Attack += level == 1 ? 0.10f : 0.20f;
                    b.FireBurn = level >= 2;
                    break;
                case ElementType.Water:
                    b.Hp += level == 1 ? 0.10f : 0.20f;
                    break;
                case ElementType.Grass:
                    b.Defense += level == 1 ? 0.10f : 0.20f;
                    b.GrassDamageReduce = level >= 2;
                    break;
                case ElementType.Light:
                    b.Energy += level == 1 ? 0.15f : 0.30f;
                    b.LightCritHint = level >= 2;
                    break;
            }
        }

        public string BuildSummary()
        {
            bool hasAny = Levels.Count > 0 || ActiveBonds.Count > 0;
            if (!hasAny)
                return $"未启用羁绊（最多选{MaxSelected}条）\n满足条件后点击列表勾选";

            var sb = new StringBuilder();
            sb.AppendLine($"✨ 已启用 {SelectedCount}/{MaxSelected}（仅相关元素）");

            foreach (var kv in Levels)
                sb.AppendLine($"· {DemoDataFactory.ElementLabel(kv.Key)}共鸣 {kv.Value}级");

            foreach (var bond in ActiveBonds)
            {
                string a = DemoDataFactory.ElementLabel(bond.ElementA);
                string b = DemoDataFactory.ElementLabel(bond.ElementB);
                sb.AppendLine($"· {bond.DisplayName}（{a}+{b}）");
            }

            sb.AppendLine("——");
            if (AttackBonus > 0f) sb.AppendLine($"攻击 +{AttackBonus * 100f:0}%");
            if (HpBonus > 0f) sb.AppendLine($"生命 +{HpBonus * 100f:0}%");
            if (DefenseBonus > 0f) sb.AppendLine($"防御 +{DefenseBonus * 100f:0}%");
            if (EnergyBonus > 0f) sb.AppendLine($"回能 +{EnergyBonus * 100f:0}%");
            if (FireBurn) sb.AppendLine("火·灼烧：火系普攻附带持续伤害");
            if (GrassDamageReduce) sb.AppendLine("草·减伤：草系受击有概率减免");
            if (LightCritHint) sb.AppendLine("光·充能：光系能量获取提升");
            return sb.ToString().TrimEnd();
        }
    }
}
