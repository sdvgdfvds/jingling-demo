using System.Collections.Generic;
using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// Loads bond definitions from Resources/Data/Bonds, with code fallbacks for Demo.
    /// </summary>
    public static class BondCatalog
    {
        static List<BondDefinition> _cached;
        static bool _usedFallback;

        public static IReadOnlyList<BondDefinition> All
        {
            get
            {
                EnsureLoaded();
                return _cached;
            }
        }

        public static bool UsedFallback
        {
            get
            {
                EnsureLoaded();
                return _usedFallback;
            }
        }

        public static void EnsureLoaded()
        {
            if (_cached != null) return;

            _cached = new List<BondDefinition>();
            var loaded = Resources.LoadAll<BondDefinition>("Data/Bonds");
            if (loaded != null && loaded.Length > 0)
            {
                _cached.AddRange(loaded);
                _usedFallback = false;
            }
            else
            {
                _cached.AddRange(CreateDefaultElementPairs());
                _usedFallback = true;
            }

            _cached.Sort((a, b) =>
            {
                int p = b.Priority.CompareTo(a.Priority);
                if (p != 0) return p;
                return string.CompareOrdinal(a.Id, b.Id);
            });
        }

        public static void Reload()
        {
            _cached = null;
            _usedFallback = false;
            EnsureLoaded();
        }

        /// <summary>MVP complementary / rivalry chains (ElementPair only).</summary>
        public static List<BondDefinition> CreateDefaultElementPairs()
        {
            return new List<BondDefinition>
            {
                MakePair("bond_fire_water", "水火相济", ElementType.Fire, ElementType.Water,
                    atk: 0.08f, hp: 0.08f, def: 0f, energy: 0f, priority: 20,
                    desc: "火与水同场，攻防调和。"),
                MakePair("bond_grass_water", "草木逢春", ElementType.Grass, ElementType.Water,
                    atk: 0f, hp: 0.06f, def: 0.06f, energy: 0f, priority: 18,
                    desc: "草木得水，生机盎然。"),
                MakePair("bond_fire_grass", "焚林之势", ElementType.Fire, ElementType.Grass,
                    atk: 0.12f, hp: 0f, def: 0f, energy: 0f, priority: 18,
                    desc: "火借草木之势，攻势更猛。"),
                MakePair("bond_light_water", "圣潮守御", ElementType.Light, ElementType.Water,
                    atk: 0f, hp: 0f, def: 0.10f, energy: 0.10f, priority: 16,
                    desc: "光潮交织，守御充盈。"),
            };
        }

        static BondDefinition MakePair(
            string id,
            string name,
            ElementType a,
            ElementType b,
            float atk,
            float hp,
            float def,
            float energy,
            int priority,
            string desc)
        {
            var bond = ScriptableObject.CreateInstance<BondDefinition>();
            bond.name = id;
            bond.Id = id;
            bond.DisplayName = name;
            bond.Description = desc;
            bond.MatchKind = BondMatchKind.ElementPair;
            bond.ElementA = a;
            bond.ElementB = b;
            bond.AttackBonus = atk;
            bond.HpBonus = hp;
            bond.DefenseBonus = def;
            bond.EnergyBonus = energy;
            bond.Priority = priority;
            bond.Level = 1;
            return bond;
        }
    }
}
