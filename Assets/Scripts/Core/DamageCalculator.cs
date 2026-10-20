using UnityEngine;

namespace NexusAnima
{
    public static class DamageCalculator
    {
        public const float DefenseFactor = 0.5f;

        public static float GetElementMultiplier(ElementType attacker, ElementType defender)
        {
            if (Beats(attacker, defender)) return 1.25f;
            if (Beats(defender, attacker)) return 0.75f;
            return 1f;
        }

        public static bool Beats(ElementType a, ElementType b)
        {
            return (a == ElementType.Fire && b == ElementType.Grass)
                || (a == ElementType.Grass && b == ElementType.Water)
                || (a == ElementType.Water && b == ElementType.Fire);
        }

        public static float ComputeDamage(
            float atk,
            float skillMultiplier,
            float bondDamageBonus,
            float elementMultiplier,
            float defense)
        {
            float raw = atk * skillMultiplier * (1f + bondDamageBonus) * elementMultiplier;
            float finalDmg = Mathf.Max(1f, raw - defense * DefenseFactor);
            return finalDmg;
        }
    }
}
