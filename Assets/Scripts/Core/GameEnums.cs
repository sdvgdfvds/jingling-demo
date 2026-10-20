namespace NexusAnima
{
    public enum ElementType
    {
        Fire,
        Water,
        Grass,
        Light
    }

    /// <summary>Per-cell battlefield terrain (8×8 board overlay).</summary>
    public enum TerrainType
    {
        None = 0,
        Wall = 1,
        Grass = 2,
        River = 3
    }

    public enum TraitType
    {
        Vanguard,   // 冲锋员 - 坦克
        Gladiator,  // 角斗士 - 输出
        Explorer    // 探险家 - 辅助
    }

    public enum Team
    {
        Player,
        Enemy
    }

    public enum GamePhase
    {
        Formation,
        Battle,
        Result
    }

    public enum SkillKind
    {
        BasicAttack,
        ActiveSkill,
        /// <summary>Deprecated — heal skills removed. Value kept to preserve asset serialization.</summary>
        Heal,
        Control,
        Buff
    }

    /// <summary>How a bond / synergy rule matches the current formation.</summary>
    public enum BondMatchKind
    {
        /// <summary>Same element count threshold (existing fire/water/grass/light tiers).</summary>
        ElementCount = 0,
        /// <summary>Two different elements present at once (complement / rivalry chain).</summary>
        ElementPair = 1
    }

    /// <summary>Combat targeting style for auto-battlers.</summary>
    public enum TargetingMode
    {
        /// <summary>Chase nearest enemy and fight in melee range.</summary>
        Nearest = 0,
        /// <summary>Prefer enemies already in attack range; kite to hold max range.</summary>
        RangedNearest = 1
    }
}
