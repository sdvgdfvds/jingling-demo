using UnityEngine;
using UnityEngine.Video;

namespace NexusAnima
{
    public enum XinxiangSkillId
    {
        IceDamage = 0,
        Buff = 1,
        Heal = 2
    }

    public enum XinxiangTargetRule
    {
        EnemyUnit,
        AllyUnit
    }

    /// <summary>
    /// Runtime definition for one Xinxiang (心象) skill slot.
    /// </summary>
    public class XinxiangSkillDefinition
    {
        public XinxiangSkillId Id;
        public string DisplayName;
        public string ShortLabel;
        public Color FrameColor = Color.white;
        public XinxiangTargetRule TargetRule;
        public float Cooldown = 10f;

        public float Damage = 0f;
        public float StunDuration = 0f;
        public float HealPercentMaxHp = 0f;
        public float AtkBuffMul = 1f;
        public float AtkBuffDuration = 0f;
        /// <summary>Chebyshev radius in cells. 0 = single unit; 1 = 3×3 around the clicked unit.</summary>
        public int AoERadius = 0;

        public bool IsArea => AoERadius > 0;

        public string VideoResourcePath;
        public string VideoFileName;
        public string IconResourcePath;
        public float VisualEndTime = 2f;
        public float HitNormalizedTime = 0.7f;
        public string VfxResourcePath;
        /// <summary>Optional second stock prefab (e.g. Ice Ground_Burst after Frozen_Dome).</summary>
        public string VfxSecondaryResourcePath;
        public float VfxScale = 0.45f;
        public float VfxDuration = 0.9f;
        public float VfxSecondaryDelay = 0.35f;

        public float GetVfxLifeSeconds() => Mathf.Clamp(VfxDuration, 0.55f, 2.2f);

        public XinxiangSkillDefinition Clone()
        {
            return new XinxiangSkillDefinition
            {
                Id = Id,
                DisplayName = DisplayName,
                ShortLabel = ShortLabel,
                FrameColor = FrameColor,
                TargetRule = TargetRule,
                Cooldown = Cooldown,
                Damage = Damage,
                StunDuration = StunDuration,
                HealPercentMaxHp = HealPercentMaxHp,
                AtkBuffMul = AtkBuffMul,
                AtkBuffDuration = AtkBuffDuration,
                AoERadius = AoERadius,
                VideoResourcePath = VideoResourcePath,
                VideoFileName = VideoFileName,
                IconResourcePath = IconResourcePath,
                VisualEndTime = VisualEndTime,
                HitNormalizedTime = HitNormalizedTime,
                VfxResourcePath = VfxResourcePath,
                VfxSecondaryResourcePath = VfxSecondaryResourcePath,
                VfxScale = VfxScale,
                VfxDuration = VfxDuration,
                VfxSecondaryDelay = VfxSecondaryDelay
            };
        }

        /// <summary>
        /// Concert cheer window: stronger numbers + slightly bigger VFX.
        /// Ice 1.5× dmg / 1.3× stun, heal 1.5×, buff bonus 1.8×, duration 1.4×.
        /// </summary>
        public static XinxiangSkillDefinition WithCheerBoost(XinxiangSkillDefinition src)
        {
            if (src == null) return null;
            var c = src.Clone();
            if (c.Damage > 0f)
                c.Damage *= 1.5f;
            if (c.StunDuration > 0f)
                c.StunDuration *= 1.3f;
            if (c.HealPercentMaxHp > 0f)
                c.HealPercentMaxHp = Mathf.Min(1f, c.HealPercentMaxHp * 1.5f);
            if (c.AtkBuffMul > 1f)
                c.AtkBuffMul = 1f + (c.AtkBuffMul - 1f) * 1.8f;
            if (c.AtkBuffDuration > 0f)
                c.AtkBuffDuration *= 1.4f;
            c.VfxScale *= 1.25f;
            return c;
        }

        public static XinxiangSkillDefinition[] CreateDefaults()
        {
            return new[]
            {
                new XinxiangSkillDefinition
                {
                    Id = XinxiangSkillId.IceDamage,
                    DisplayName = "心象·冰伤",
                    ShortLabel = "冰伤",
                    FrameColor = new Color(0.35f, 0.7f, 1f, 1f),
                    TargetRule = XinxiangTargetRule.EnemyUnit,
                    Cooldown = 12f,
                    Damage = 200f,
                    StunDuration = 1.2f,
                    AoERadius = 1,
                    VideoResourcePath = "Xinxiang/Videos/Xinxiang_Ice",
                    VideoFileName = "Xinxiang_Ice.mp4",
                    IconResourcePath = "Xinxiang/Icons/Ice",
                    VisualEndTime = 2.0f,
                    HitNormalizedTime = 0.72f,
                    // Stock: DeathIce_Ground_Burst then DeathIce_Hit.
                    VfxResourcePath = "VFX/Xinxiang/Ice",
                    VfxSecondaryResourcePath = "VFX/Xinxiang/IceBurst",
                    VfxSecondaryDelay = 0.28f,
                    // VfxScale multiplies the base (3× normal attack impact). 1 = exact 3×.
                    VfxScale = 1f,
                    VfxDuration = 1.6f
                },
                new XinxiangSkillDefinition
                {
                    Id = XinxiangSkillId.Buff,
                    DisplayName = "心象·增益",
                    ShortLabel = "增益",
                    FrameColor = new Color(1f, 0.82f, 0.35f, 1f),
                    TargetRule = XinxiangTargetRule.AllyUnit,
                    Cooldown = 10f,
                    AtkBuffMul = 1.25f,
                    AtkBuffDuration = 6f,
                    VideoResourcePath = "Xinxiang/Videos/Xinxiang_Buff",
                    VideoFileName = "Xinxiang_Buff.mp4",
                    IconResourcePath = "Xinxiang/Icons/Buff",
                    VisualEndTime = 1.5f,
                    HitNormalizedTime = 0.65f,
                    // Stock: Holy_Aura — wrap the unit mesh (~0.05 XY), not 3× attack size.
                    VfxResourcePath = "VFX/Xinxiang/BuffAura",
                    VfxSecondaryResourcePath = null,
                    VfxScale = 0.07f,
                    VfxDuration = 1.5f
                },
                new XinxiangSkillDefinition
                {
                    Id = XinxiangSkillId.Heal,
                    DisplayName = "心象·治疗",
                    ShortLabel = "治疗",
                    FrameColor = new Color(0.45f, 1f, 0.55f, 1f),
                    TargetRule = XinxiangTargetRule.AllyUnit,
                    Cooldown = 10f,
                    HealPercentMaxHp = 0.3f,
                    AoERadius = 1,
                    VideoResourcePath = "Xinxiang/Videos/Xinxiang_Heal",
                    VideoFileName = "Xinxiang_Heal.mp4",
                    IconResourcePath = "Xinxiang/Icons/Heal",
                    VisualEndTime = 2.0f,
                    HitNormalizedTime = 0.7f,
                    // Stock: Water_Healing_Rain (authored rain is huge — 0.4 ≈ ice visual size).
                    VfxResourcePath = "VFX/Xinxiang/Heal",
                    VfxScale = 0.4f,
                    VfxDuration = 1.5f
                }
            };
        }

        public VideoClip TryLoadVideoClip()
        {
            if (string.IsNullOrEmpty(VideoResourcePath))
                return null;
            return Resources.Load<VideoClip>(VideoResourcePath);
        }

        public Sprite TryLoadIcon()
        {
            if (string.IsNullOrEmpty(IconResourcePath))
                return null;
            return Resources.Load<Sprite>(IconResourcePath);
        }

        public string ResolveVideoUrl()
        {
            // Prefer Art folder copy (always present as raw file in Editor).
            string art = System.IO.Path.Combine(Application.dataPath, "Art", "Xinxiang", "Videos", VideoFileName);
            if (System.IO.File.Exists(art))
                return "file:///" + art.Replace("\\", "/");

            string res = System.IO.Path.Combine(Application.dataPath, "Resources", "Xinxiang", "Videos", VideoFileName);
            if (System.IO.File.Exists(res))
                return "file:///" + res.Replace("\\", "/");

            return null;
        }
    }
}
