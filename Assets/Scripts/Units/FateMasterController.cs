using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// Fate Master passive holder. The old 星理 AoE ultimate was replaced by Xinxiang skills.
    /// Energy tracking remains for UI/debug compatibility but no longer gates a board skill.
    /// </summary>
    public class FateMasterController : MonoBehaviour
    {
        public FateMasterDefinition Definition { get; private set; }
        public float Energy { get; private set; }
        public bool SkillReady => false;
        public bool SelectingTarget => false;

        BoardGrid _board;
        BattleManager _battle;

        public void Init(FateMasterDefinition def, BoardGrid board, BattleManager battle)
        {
            Definition = def;
            _board = board;
            _battle = battle;
            Energy = def != null ? def.StartEnergy : 0f;
        }

        public void OnAllyCombatEvent(Team team, float amount)
        {
            if (team != Team.Player) return;
            if (Energy >= 100f) return;
            Energy = Mathf.Min(100f, Energy + amount);
        }

        [System.Obsolete("星理技能已移除，请用心象技能 XinxiangSkillController。")]
        public void BeginTargetSelect() { }

        [System.Obsolete("星理技能已移除。")]
        public void CancelTargetSelect() { }

        [System.Obsolete("星理技能已移除。")]
        public bool TryCastAtWorld(Vector3 worldPos) => false;

        [System.Obsolete("星理技能已移除。")]
        public bool TryCastAtCell(Vector2Int cell) => false;
    }
}
