using UnityEngine;

namespace NexusAnima
{
    [CreateAssetMenu(menuName = "NexusAnima/Bond Definition", fileName = "BondDefinition")]
    public class BondDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        [TextArea] public string Description;
        public BondMatchKind MatchKind = BondMatchKind.ElementPair;

        [Header("Element Count")]
        public ElementType Element;
        [Tooltip("Required count for ElementCount bonds (e.g. 2 or 4).")]
        public int RequiredCount = 2;
        [Tooltip("Display tier for ElementCount bonds.")]
        public int Level = 1;

        [Header("Element Pair (unordered)")]
        public ElementType ElementA = ElementType.Fire;
        public ElementType ElementB = ElementType.Water;

        [Header("Bonuses (multiplicative addends, 0.08 = +8%)")]
        public float AttackBonus;
        public float HpBonus;
        public float DefenseBonus;
        public float EnergyBonus;

        [Tooltip("Higher priority listed first in the summary.")]
        public int Priority = 0;

        public bool Matches(System.Collections.Generic.IReadOnlyDictionary<ElementType, int> counts)
        {
            if (counts == null) return false;
            switch (MatchKind)
            {
                case BondMatchKind.ElementCount:
                    return counts.TryGetValue(Element, out int n) && n >= Mathf.Max(1, RequiredCount);
                case BondMatchKind.ElementPair:
                    if (ElementA == ElementB)
                        return counts.TryGetValue(ElementA, out int same) && same >= 2;
                    return counts.TryGetValue(ElementA, out int a) && a >= 1
                           && counts.TryGetValue(ElementB, out int b) && b >= 1;
                default:
                    return false;
            }
        }
    }
}
