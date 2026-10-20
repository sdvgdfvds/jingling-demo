using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// Canonical presentation/combat defaults keyed by SpiritDefinition.Id.
    /// Used when baking assets and as a runtime fallback for older ScriptableObjects.
    /// </summary>
    public static class SpiritPresentationCatalog
    {
        public readonly struct Entry
        {
            public readonly string ModelName;
            public readonly int CombatRangeCells;
            public readonly TargetingMode Targeting;
            public readonly bool IsFlying;
            public readonly float BodyYFineTune;
            public readonly float MoveIntervalMultiplier;
            public readonly bool SkipWalkBob;
            public readonly IdleAnimProfile Idle;

            public Entry(
                string modelName,
                int combatRangeCells,
                TargetingMode targeting,
                bool isFlying = false,
                float bodyYFineTune = 0f,
                float moveIntervalMultiplier = 1f,
                bool skipWalkBob = false,
                IdleAnimProfile? idle = null)
            {
                ModelName = modelName;
                CombatRangeCells = combatRangeCells;
                Targeting = targeting;
                IsFlying = isFlying;
                BodyYFineTune = bodyYFineTune;
                MoveIntervalMultiplier = moveIntervalMultiplier;
                SkipWalkBob = skipWalkBob;
                Idle = idle ?? IdleAnimProfile.Default;
            }
        }

        public static bool TryGet(string id, out Entry entry)
        {
            entry = default;
            if (string.IsNullOrEmpty(id)) return false;

            switch (id)
            {
                case "p_fire_tank":
                    entry = new Entry("Art_KS_1605_01", 3, TargetingMode.RangedNearest, bodyYFineTune: 0.0008f,
                        idle: Idle("Art_KS_1605_01"));
                    return true;
                case "p_fire_dps":
                    entry = new Entry("Art_KS_1605_00", 3, TargetingMode.RangedNearest, bodyYFineTune: 0.0008f,
                        idle: Idle("Art_KS_1605_00"));
                    return true;
                case "p_water_tank":
                    entry = new Entry("Art_KS_1506_00", 3, TargetingMode.RangedNearest, bodyYFineTune: 0.0010f,
                        idle: Idle("Art_KS_1506_00"));
                    return true;
                case "p_water_sup":
                    entry = new Entry("Art_KS_2705_00", 1, TargetingMode.Nearest, bodyYFineTune: 0.0010f,
                        idle: Idle("Art_KS_2705_00"));
                    return true;
                case "p_grass_tank":
                    entry = new Entry("Art_KS_2304_00", 3, TargetingMode.RangedNearest, bodyYFineTune: 0.0010f,
                        idle: Idle("Art_KS_2304_00"));
                    return true;
                case "p_grass_dps":
                    entry = new Entry("Art_KS_2001_00", 1, TargetingMode.Nearest, bodyYFineTune: 0.0008f,
                        idle: Idle("Art_KS_2001_00"));
                    return true;
                case "p_light_dps":
                    entry = new Entry("Art_KS_1502_00", 1, TargetingMode.Nearest, bodyYFineTune: 0.0008f,
                        idle: Idle("Art_KS_1502_00"));
                    return true;
                case "p_light_sup":
                    entry = new Entry("Art_KS_2002_00", 1, TargetingMode.Nearest, bodyYFineTune: 0.0010f,
                        idle: Idle("Art_KS_2002_00"));
                    return true;
                case "p_dark_dps":
                    entry = new Entry("Art_KS_1602_00", 1, TargetingMode.Nearest, bodyYFineTune: 0.0015f,
                        idle: Idle("Art_KS_1602_00"));
                    return true;
                case "p_beauty_dps":
                    entry = new Entry("Art_KS_2302_00", 3, TargetingMode.RangedNearest,
                        isFlying: true, bodyYFineTune: 0f, skipWalkBob: true,
                        idle: Idle("Art_KS_2302_00"));
                    return true;
                case "p_competition_dps":
                    entry = new Entry("Art_KS_3304_00", 3, TargetingMode.RangedNearest,
                        isFlying: true, bodyYFineTune: 0f, moveIntervalMultiplier: 0.72f, skipWalkBob: true,
                        idle: Idle("Art_KS_3304_00"));
                    return true;

                case "e_grass_tank1":
                    entry = new Entry("Art_KS_1605_01", 3, TargetingMode.RangedNearest, bodyYFineTune: 0.0008f,
                        idle: Idle("Art_KS_1605_01"));
                    return true;
                case "e_grass_tank2":
                    entry = new Entry("Art_KS_1605_00", 3, TargetingMode.RangedNearest, bodyYFineTune: 0.0008f,
                        idle: Idle("Art_KS_1605_00"));
                    return true;
                case "e_water_dps1":
                    entry = new Entry("Art_KS_3304_00", 3, TargetingMode.RangedNearest,
                        isFlying: true, bodyYFineTune: 0f, moveIntervalMultiplier: 0.72f, skipWalkBob: true,
                        idle: Idle("Art_KS_3304_00"));
                    return true;
                case "e_water_dps2":
                    entry = new Entry("Art_KS_6701_00", 3, TargetingMode.RangedNearest, bodyYFineTune: 0.0012f,
                        idle: Idle("Art_KS_6701_00"));
                    return true;
                case "e_fire_sup":
                    entry = new Entry("Art_KS_2302_00", 3, TargetingMode.RangedNearest,
                        isFlying: true, bodyYFineTune: 0f, skipWalkBob: true,
                        idle: Idle("Art_KS_2302_00"));
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Fill presentation fields from the catalog.
        /// When <paramref name="force"/> is false, only fills missing ModelName / zeroed idle.
        /// </summary>
        public static void ApplyDefaults(SpiritDefinition def, bool force = true)
        {
            if (def == null) return;
            if (!TryGet(def.Id, out var entry))
            {
                if (string.IsNullOrEmpty(def.ModelName))
                    def.ModelName = "Art_KS_1502_00";
                if (def.MoveIntervalMultiplier <= 0f)
                    def.MoveIntervalMultiplier = 1f;
                if (ApproximatelyDefaultIdle(def.IdleProfile))
                    def.IdleProfile = IdleAnimProfile.Default;
                return;
            }

            bool missingModel = string.IsNullOrEmpty(def.ModelName);
            if (!force && !missingModel)
                return;

            def.ModelName = entry.ModelName;
            def.AttackRange = entry.CombatRangeCells;
            def.Targeting = entry.Targeting;
            def.IsFlying = entry.IsFlying;
            def.BodyYFineTune = entry.BodyYFineTune;
            def.MoveIntervalMultiplier = entry.MoveIntervalMultiplier <= 0f ? 1f : entry.MoveIntervalMultiplier;
            def.SkipWalkBob = entry.SkipWalkBob;
            def.IdleProfile = entry.Idle;
            def.EnableWingFlap = entry.IsFlying
                && (entry.ModelName == "Art_KS_3304_00" || entry.ModelName == "Art_KS_2302_00");
        }

        static bool ApproximatelyDefaultIdle(IdleAnimProfile profile)
        {
            return Mathf.Approximately(profile.BodyFrequency, 0f)
                && Mathf.Approximately(profile.BobAmplitude, 0f);
        }

        static IdleAnimProfile Idle(string modelName)
        {
            var p = IdleAnimProfile.Default;
            switch (modelName)
            {
                case "Art_KS_1502_00":
                    p.BodyFrequency = 0.43f; p.PhaseOffset = 0.35f; break;
                case "Art_KS_1506_00":
                    p.BodyFrequency = 0.39f; p.BobAmplitude = 0.0038f;
                    p.TailFrequency = 0.26f; p.TailAmplitude = 0.85f; p.PhaseOffset = 1.1f; break;
                case "Art_KS_1602_00":
                    p.BodyFrequency = 0.47f; p.BobAmplitude = 0.0048f; p.SwayAmplitude = 0.22f;
                    p.TailFrequency = 0.31f; p.TailAmplitude = 1.4f; p.PhaseOffset = 2.0f; break;
                case "Art_KS_1605_00":
                    p.BodyFrequency = 0.50f; p.BobAmplitude = 0.0050f; p.SwayAmplitude = 0.24f;
                    p.TailFrequency = 0.33f; p.TailAmplitude = 1.5f; p.PhaseOffset = 2.7f; break;
                case "Art_KS_1605_01":
                    p.BodyFrequency = 0.42f; p.BobAmplitude = 0.0036f; p.SwayAmplitude = 0.15f;
                    p.TailFrequency = 0.24f; p.TailAmplitude = 0.65f; p.PhaseOffset = 0.7f; break;
                case "Art_KS_2000_00":
                    p.BodyFrequency = 0.45f; p.BobAmplitude = 0.0040f; p.SwayAmplitude = 0.17f;
                    p.TailFrequency = 0.27f; p.TailAmplitude = 0.9f; p.PhaseOffset = 1.9f; break;
                case "Art_KS_2001_00":
                    p.BodyFrequency = 0.37f; p.BobAmplitude = 0.0038f; p.SwayAmplitude = 0.16f;
                    p.TailFrequency = 0.27f; p.TailAmplitude = 1.0f; p.PhaseOffset = 1.5f; break;
                case "Art_KS_2002_00":
                    p.BodyFrequency = 0.41f; p.BobAmplitude = 0.0040f; p.SwayAmplitude = 0.18f;
                    p.TailFrequency = 0.28f; p.TailAmplitude = 1.1f; p.PhaseOffset = 2.4f; break;
                case "Art_KS_2302_00":
                    p.BodyFrequency = 0.30f; p.BobAmplitude = 0.0030f; p.SwayAmplitude = 0.10f;
                    p.TailFrequency = 0.21f; p.TailAmplitude = 0.70f; p.PhaseOffset = 0.9f; break;
                case "Art_KS_2304_00":
                    p.BodyFrequency = 0.44f; p.BobAmplitude = 0.0039f; p.SwayAmplitude = 0.17f;
                    p.TailFrequency = 0.29f; p.TailAmplitude = 1.0f; p.PhaseOffset = 1.8f; break;
                case "Art_KS_2705_00":
                    p.BodyFrequency = 0.34f; p.BobAmplitude = 0.0032f; p.SwayAmplitude = 0.14f;
                    p.TailFrequency = 0.23f; p.TailAmplitude = 0.80f; p.PhaseOffset = 0.4f; break;
                case "Art_KS_3304_00":
                    p.BodyFrequency = 0.28f; p.BobAmplitude = 0.0028f; p.SwayAmplitude = 0.09f;
                    p.TailFrequency = 0.19f; p.TailAmplitude = 0.60f; p.PhaseOffset = 2.2f; break;
                case "Art_KS_6701_00":
                    p.BodyFrequency = 0.33f; p.BobAmplitude = 0.0032f; p.SwayAmplitude = 0.11f;
                    p.TailFrequency = 0.22f; p.TailAmplitude = 0.70f; p.PhaseOffset = 1.3f; break;
            }
            return p;
        }
    }
}
