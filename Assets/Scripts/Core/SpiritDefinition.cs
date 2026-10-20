using UnityEngine;

namespace NexusAnima
{
    [CreateAssetMenu(menuName = "NexusAnima/Spirit Definition", fileName = "SpiritDefinition")]
    public class SpiritDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public string PrincipleDisplayName;
        public string TraitDisplayName;
        public string RoleDisplayName;
        /// <summary>Creature species shown on roster cards, e.g. 狐 / 鹿 / 星鹫.</summary>
        public string SpeciesDisplayName;
        public ElementType Element;
        public TraitType Trait;
        public Color Color = Color.white;

        public float MaxHp = 500f;
        public float Atk = 50f;
        public float Def = 15f;
        public float AttackSpeed = 1f;
        public float AttackRange = 1f;
        public float MoveSpeed = 3.5f;
        public float EnergyPerAttack = 12f;

        public string ActiveSkillName = "";
        public SkillKind ActiveSkillKind = SkillKind.BasicAttack;
        public float SkillMultiplier = 1f;
        public float SkillDuration = 0f;
        public int SkillHits = 1;
        /// <summary>Deprecated: unit heal skills are disabled. Kept for asset compatibility.</summary>
        public float HealAmount = 0f;
        public bool IsPhysical = true;

        [Header("Presentation / Combat Style")]
        [Tooltip("Resources/Spirits/{ModelName} prefab name, e.g. Art_KS_1605_00")]
        public string ModelName = "";
        public TargetingMode Targeting = TargetingMode.Nearest;
        public bool IsFlying;
        [Tooltip("Additive Body.localPosition.y after feet-plant / fly-lift.")]
        public float BodyYFineTune;
        [Tooltip("Multiplier on auto-battle move interval (lower = relocates faster).")]
        public float MoveIntervalMultiplier = 1f;
        [Tooltip("Flying units often skip ground walk bob.")]
        public bool SkipWalkBob;
        [Tooltip("Enable procedural wing flap for bird models.")]
        public bool EnableWingFlap;
        public IdleAnimProfile IdleProfile = IdleAnimProfile.Default;

        public bool IsRanged => Targeting == TargetingMode.RangedNearest;

        public int CombatRangeCells => Mathf.Max(1, Mathf.RoundToInt(AttackRange));

        /// <summary>Ensure presentation fields are populated (catalog fallback for older assets).</summary>
        public void EnsurePresentation()
        {
            // Non-destructive: only fills when ModelName is still empty.
            SpiritPresentationCatalog.ApplyDefaults(this, force: false);
        }

        public SpiritDefinition Clone()
        {
            var copy = CreateInstance<SpiritDefinition>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(this), copy);
            copy.name = string.IsNullOrEmpty(Id) ? name + "_runtime" : Id + "_runtime";
            copy.EnsurePresentation();
            return copy;
        }
    }
}
