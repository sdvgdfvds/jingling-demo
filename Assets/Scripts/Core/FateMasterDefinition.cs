using UnityEngine;

namespace NexusAnima
{
    [CreateAssetMenu(menuName = "NexusAnima/Fate Master Definition", fileName = "FateMasterDefinition")]
    public class FateMasterDefinition : ScriptableObject
    {
        public string Id = "master_fire";
        public string DisplayName = "炎律因缘师";
        public ElementType BuffElement = ElementType.Fire;
        public float PassiveAtkBonus = 0.15f;
        public float SkillDamage = 200f;
        public float StunDuration = 1.5f;
        public int AoESize = 2;
        public float StartEnergy = 50f;
        public float EnergyOnAllyHit = 3f;

        public FateMasterDefinition Clone()
        {
            var copy = CreateInstance<FateMasterDefinition>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(this), copy);
            copy.name = string.IsNullOrEmpty(Id) ? name + "_runtime" : Id + "_runtime";
            return copy;
        }
    }
}
