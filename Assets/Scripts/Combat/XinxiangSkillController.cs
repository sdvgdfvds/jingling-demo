using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NexusAnima
{
    public enum XinxiangQteGrade
    {
        Perfect = 0,
        Good = 1,
        Miss = 2
    }

    /// <summary>
    /// Owns three independent Xinxiang skill cooldowns and target selection.
    /// </summary>
    public class XinxiangSkillController : MonoBehaviour
    {
        public const string HealBuffComboName = "心象·星辉庇护";

        public static string QteGradeLabel(XinxiangQteGrade grade)
        {
            switch (grade)
            {
                case XinxiangQteGrade.Perfect: return "完美";
                case XinxiangQteGrade.Miss: return "失败";
                default: return "成功";
            }
        }

        public XinxiangSkillDefinition[] Skills { get; private set; }
        public XinxiangSkillId? SelectingSkill { get; private set; }
        public XinxiangSkillId? ComboPartner { get; private set; }
        public bool IsSelecting => SelectingSkill.HasValue;
        public bool IsComboSelecting => ComboPartner.HasValue;
        public bool IsCasting { get; private set; }
        public bool IsPrankCalling { get; private set; }
        /// <summary>挂断搞怪来电后，正在等待玩家点选强化冰伤目标。</summary>
        public bool IsPrankRevengeSelecting => _pendingPrankRevenge;

        readonly float[] _cooldownRemaining = new float[3];
        BattleManager _battle;
        XinxiangCastDirector _director;
        XinxiangPrankCallDirector _prankCall;
        Vector2Int? _aoeHoverCell;
        XinxiangSkillId[] _cooldownOnFinish;
        bool _castWasHealBuffCombo;
        bool _castWasPrankRevenge;
        bool _castWasCheered;
        /// <summary>挂断搞怪来电后，等待玩家自行点选冰伤目标。</summary>
        bool _pendingPrankRevenge;
        const float PrankIceDamageMul = 1.8f;

        public event Action StateChanged;

        public void Init(BattleManager battle, XinxiangCastDirector director, XinxiangPrankCallDirector prankCall = null)
        {
            _battle = battle;
            _director = director;
            _prankCall = prankCall;
            Skills = XinxiangSkillDefinition.CreateDefaults();
            for (int i = 0; i < _cooldownRemaining.Length; i++)
                _cooldownRemaining[i] = 0f;
            SelectingSkill = null;
            ComboPartner = null;
            IsCasting = false;
            IsPrankCalling = false;
            _aoeHoverCell = null;
            _cooldownOnFinish = null;
            _castWasHealBuffCombo = false;
            _castWasPrankRevenge = false;
            _castWasCheered = false;
            _pendingPrankRevenge = false;
            GameAudio.I?.ResetCheerMeter();
            StateChanged?.Invoke();
        }

        public void ResetForBattle()
        {
            if (IsPrankCalling && _prankCall != null)
                _prankCall.Cancel();
            for (int i = 0; i < _cooldownRemaining.Length; i++)
                _cooldownRemaining[i] = 0f;
            SelectingSkill = null;
            ComboPartner = null;
            IsCasting = false;
            IsPrankCalling = false;
            _aoeHoverCell = null;
            _cooldownOnFinish = null;
            _castWasHealBuffCombo = false;
            _castWasPrankRevenge = false;
            _castWasCheered = false;
            _pendingPrankRevenge = false;
            GameAudio.I?.ResetCheerMeter();
            _battle?.SetCombatFrozen(false);
            ClearTargetRangeHighlight();
            // Wipe any Xinxiang VFX that survived a previous Play session / scene save.
            XinxiangVfxLibrary.CleanupStale();
            StateChanged?.Invoke();
        }

        void Update()
        {
            // 选技能 / 施放 / 搞怪来电期间暂停所有心象冷却，避免点中技能时其他技能 CD 仍在跑
            if (!IsSelecting && !IsCasting && !IsPrankCalling)
            {
                bool changed = false;
                for (int i = 0; i < _cooldownRemaining.Length; i++)
                {
                    if (_cooldownRemaining[i] <= 0f) continue;
                    _cooldownRemaining[i] = Mathf.Max(0f, _cooldownRemaining[i] - Time.deltaTime);
                    changed = true;
                }
                if (changed)
                    StateChanged?.Invoke();
            }

            if (IsSelecting && !IsCasting && !IsPrankCalling)
                TickAoEHover();
        }

        public float GetCooldownRemaining(XinxiangSkillId id)
        {
            int idx = (int)id;
            if (idx < 0 || idx >= _cooldownRemaining.Length) return 0f;
            return _cooldownRemaining[idx];
        }

        /// <summary>
        /// Reduce remaining cooldown on every Xinxiang slot (e.g. wall-destroy reward).
        /// </summary>
        public void ReduceAllCooldowns(float seconds)
        {
            if (seconds <= 0f) return;
            bool changed = false;
            for (int i = 0; i < _cooldownRemaining.Length; i++)
            {
                if (_cooldownRemaining[i] <= 0f) continue;
                _cooldownRemaining[i] = Mathf.Max(0f, _cooldownRemaining[i] - seconds);
                changed = true;
            }
            if (changed)
                StateChanged?.Invoke();
        }

        public bool IsReady(XinxiangSkillId id)
        {
            return GetCooldownRemaining(id) <= 0.01f && !IsCasting && !IsPrankCalling;
        }

        public static bool IsBuffIcePrankPair(XinxiangSkillId a, XinxiangSkillId b)
        {
            return (a == XinxiangSkillId.Buff && b == XinxiangSkillId.IceDamage)
                   || (a == XinxiangSkillId.IceDamage && b == XinxiangSkillId.Buff);
        }

        public XinxiangSkillDefinition GetDefinition(XinxiangSkillId id)
        {
            if (Skills == null) return null;
            int idx = (int)id;
            if (idx < 0 || idx >= Skills.Length) return null;
            return Skills[idx];
        }

        public bool IsSlotSelected(XinxiangSkillId id)
        {
            return SelectingSkill == id || ComboPartner == id;
        }

        public static bool IsHealBuffPair(XinxiangSkillId a, XinxiangSkillId b)
        {
            return (a == XinxiangSkillId.Heal && b == XinxiangSkillId.Buff)
                   || (a == XinxiangSkillId.Buff && b == XinxiangSkillId.Heal);
        }

        XinxiangSkillDefinition GetActiveCastDef()
        {
            if (ComboPartner.HasValue
                && (SelectingSkill == XinxiangSkillId.Heal || ComboPartner == XinxiangSkillId.Heal))
                return GetDefinition(XinxiangSkillId.Heal);
            if (!SelectingSkill.HasValue)
                return null;
            return GetDefinition(SelectingSkill.Value);
        }

        public bool BeginSelect(XinxiangSkillId id)
        {
            if (_battle == null || _battle.Phase != GamePhase.Battle) return false;
            if (IsCasting || IsPrankCalling) return false;

            // 挂断复仇：锁定冰伤选目标，禁止再点其他技能 / 再次触发来电
            if (_pendingPrankRevenge)
            {
                if (id == XinxiangSkillId.IceDamage)
                    return true;
                if (_battle != null)
                    _battle.LastEvent = "别再打来了！请选择敌方单位释放强化冰伤";
                return false;
            }

            // Buff ↔ Ice: gag phone call (pure easter egg, no skill cast).
            if (SelectingSkill.HasValue && IsBuffIcePrankPair(SelectingSkill.Value, id))
            {
                if (!IsReady(id)) return false;
                return BeginPrankCall();
            }

            // Already selecting Heal or Buff: clicking the other ready slot forms 星辉庇护.
            if (SelectingSkill.HasValue && IsHealBuffPair(SelectingSkill.Value, id))
            {
                if (!IsReady(id)) return false;
                ComboPartner = id;
                GameAudio.I?.PlayUiSelect();
                _battle?.SetCombatFrozen(true);
                if (_battle != null)
                    _battle.LastEvent = "选择我方单位释放【星辉庇护】（治疗+增益同时释放）";
                _aoeHoverCell = null;
                RefreshTargetRangeHighlight();
                TickAoEHover();
                StateChanged?.Invoke();
                return true;
            }

            if (!IsReady(id)) return false;

            SelectingSkill = id;
            ComboPartner = null;
            var def = GetDefinition(id);
            GameAudio.I?.PlayUiSelect();
            // Pause combat for the whole target-select window; ConfirmCast keeps the hold
            // through the ult animation, then the director releases when the overlay ends.
            _battle?.SetCombatFrozen(true);
            if (_battle != null && def != null)
            {
                if (def.IsArea)
                {
                    _battle.LastEvent = def.TargetRule == XinxiangTargetRule.EnemyUnit
                        ? $"选择敌方单位，对周围 3×3 区域释放【{def.DisplayName}】"
                        : $"选择我方单位，对周围 3×3 区域释放【{def.DisplayName}】";
                }
                else
                {
                    _battle.LastEvent = def.TargetRule == XinxiangTargetRule.EnemyUnit
                        ? $"选择敌方单位释放【{def.DisplayName}】"
                        : $"选择我方单位释放【{def.DisplayName}】";
                }
            }
            _aoeHoverCell = null;
            RefreshTargetRangeHighlight();
            TickAoEHover();
            StateChanged?.Invoke();
            return true;
        }

        public void CancelSelect()
        {
            if (IsPrankCalling)
            {
                _prankCall?.Cancel();
                return;
            }

            if (!SelectingSkill.HasValue) return;

            // 挂断复仇取消：不施放，但仍让冰伤+增益进入冷却
            if (_pendingPrankRevenge)
            {
                _pendingPrankRevenge = false;
                _castWasPrankRevenge = false;
                SelectingSkill = null;
                ComboPartner = null;
                _aoeHoverCell = null;
                ClearTargetRangeHighlight();
                BeginCooldownForPending();
                if (!IsCasting)
                    _battle?.SetCombatFrozen(false);
                if (_battle != null)
                    _battle.LastEvent = "别再打来了";
                StateChanged?.Invoke();
                return;
            }

            SelectingSkill = null;
            ComboPartner = null;
            _aoeHoverCell = null;
            ClearTargetRangeHighlight();
            if (!IsCasting)
                _battle?.SetCombatFrozen(false);
            if (_battle != null)
                _battle.LastEvent = "已取消心象技能选择";
            StateChanged?.Invoke();
        }

        bool BeginPrankCall()
        {
            if (_prankCall == null)
            {
                if (_battle != null)
                    _battle.LastEvent = "搞怪来电导演未就绪";
                return false;
            }

            SelectingSkill = null;
            ComboPartner = null;
            _aoeHoverCell = null;
            ClearTargetRangeHighlight();
            IsPrankCalling = true;
            _battle?.SetCombatFrozen(true);
            if (_battle != null)
                _battle.LastEvent = "琪亚娜打来电话…连点红色按钮挂断！";
            StateChanged?.Invoke();

            if (!_prankCall.Play(OnPrankCallFinished))
            {
                IsPrankCalling = false;
                _battle?.SetCombatFrozen(false);
                StateChanged?.Invoke();
                return false;
            }

            return true;
        }

        void OnPrankCallFinished(bool hungUp)
        {
            IsPrankCalling = false;
            SelectingSkill = null;
            ComboPartner = null;
            _aoeHoverCell = null;
            ClearTargetRangeHighlight();

            if (hungUp)
            {
                BeginPrankRevengeIceSelect();
                return;
            }

            _battle?.SetCombatFrozen(false);
            if (_battle != null)
                _battle.LastEvent = "搞怪来电结束（未挂断）";
            StateChanged?.Invoke();
        }

        /// <summary>
        /// 挂断后进入强化冰伤选目标：玩家自己点敌方单位施放，
        /// 施放后冰伤+增益一起进 CD；取消则不打伤害但仍进 CD。
        /// </summary>
        void BeginPrankRevengeIceSelect()
        {
            var ice = GetDefinition(XinxiangSkillId.IceDamage);
            _cooldownOnFinish = new[] { XinxiangSkillId.IceDamage, XinxiangSkillId.Buff };
            _castWasHealBuffCombo = false;
            _castWasPrankRevenge = true;
            _pendingPrankRevenge = true;

            if (ice == null)
            {
                _pendingPrankRevenge = false;
                _castWasPrankRevenge = false;
                BeginCooldownForPending();
                _battle?.SetCombatFrozen(false);
                if (_battle != null)
                    _battle.LastEvent = "别再打来了";
                StateChanged?.Invoke();
                return;
            }

            SelectingSkill = XinxiangSkillId.IceDamage;
            ComboPartner = null;
            _aoeHoverCell = null;
            IsCasting = false;
            _battle?.SetCombatFrozen(true);
            if (_battle != null)
                _battle.LastEvent = "别再打来了！选择敌方单位释放强化冰伤";
            RefreshTargetRangeHighlight();
            TickAoEHover();
            StateChanged?.Invoke();
        }

        static XinxiangSkillDefinition CloneIceWithBonusDamage(XinxiangSkillDefinition src, float mul)
        {
            if (src == null) return null;
            var c = src.Clone();
            c.Damage = Mathf.Max(1f, src.Damage * Mathf.Max(1f, mul));
            return c;
        }

        public bool TryCastOnUnit(SpiritUnit unit)
        {
            if (!SelectingSkill.HasValue || IsCasting || IsPrankCalling) return false;
            if (unit == null || !unit.IsAlive) return false;

            var id = SelectingSkill.Value;
            bool combo = ComboPartner.HasValue && IsHealBuffPair(id, ComboPartner.Value);
            bool prankRevenge = _pendingPrankRevenge && id == XinxiangSkillId.IceDamage;
            var def = combo ? GetActiveCastDef() : GetDefinition(id);
            if (prankRevenge)
                def = CloneIceWithBonusDamage(GetDefinition(XinxiangSkillId.IceDamage), PrankIceDamageMul);
            if (def == null) return false;

            if (!IsValidTarget(def, unit))
            {
                if (_battle != null)
                {
                    if (combo)
                        _battle.LastEvent = "【星辉庇护】需要选择我方单位";
                    else if (prankRevenge)
                        _battle.LastEvent = "别再打来了！请选择敌方单位作为强化冰伤中心";
                    else
                    {
                        _battle.LastEvent = def.TargetRule == XinxiangTargetRule.EnemyUnit
                            ? (def.IsArea ? "心象·冰伤需要选择敌方单位作为区域中心" : "心象·冰伤需要选择敌方单位")
                            : $"【{def.ShortLabel}】需要选择我方单位";
                    }
                    _battle.NotifyXinxiangTargetInvalid();
                }
                return false;
            }

            var healDef = GetDefinition(XinxiangSkillId.Heal);
            var buffDef = GetDefinition(XinxiangSkillId.Buff);
            _castWasHealBuffCombo = combo;
            if (prankRevenge)
            {
                _pendingPrankRevenge = false;
                _castWasPrankRevenge = true;
                _cooldownOnFinish = new[] { XinxiangSkillId.IceDamage, XinxiangSkillId.Buff };
            }
            else
            {
                _castWasPrankRevenge = false;
                _cooldownOnFinish = combo
                    ? new[] { XinxiangSkillId.Heal, XinxiangSkillId.Buff }
                    : new[] { id };
            }

            SelectingSkill = null;
            ComboPartner = null;
            _aoeHoverCell = null;
            IsCasting = true;
            // Freeze only while the ult animation plays; director releases when the overlay ends.
            _battle?.SetCombatFrozen(true);
            // Keep range grid visible during cast; emphasize the chosen cell.
            ShowCastRangeHighlight(def, unit);
            StateChanged?.Invoke();
            GameAudio.I?.PlayMagicCast();
            if (combo)
                GameAudio.I?.PlayWhoosh();

            bool cheered = GameAudio.I != null && GameAudio.I.NotifySkillCastForCheer();
            _castWasCheered = cheered;
            if (cheered)
            {
                def = XinxiangSkillDefinition.WithCheerBoost(def);
                if (combo)
                {
                    healDef = XinxiangSkillDefinition.WithCheerBoost(healDef);
                    buffDef = XinxiangSkillDefinition.WithCheerBoost(buffDef);
                }
                if (_battle != null)
                    _battle.LastEvent = "观众欢呼！技能效果增强";
            }

            if (_director != null)
            {
                if (combo && healDef != null && buffDef != null)
                    _director.PlaySplit(healDef, buffDef, unit, OnCastFinished);
                else
                    _director.Play(def, unit, OnCastFinished);
            }
            else
            {
                if (combo && healDef != null && buffDef != null)
                    ApplyHealBuffCombo(healDef, buffDef, unit, unit.Board, unit.Cell, spawnVfx: true);
                else
                    ApplyEffect(def, unit, spawnVfx: true);
                BeginCooldownForPending();
                IsCasting = false;
                GameAudio.I?.NotifyCheerCastFinished(_castWasCheered);
                _castWasCheered = false;
                _battle?.SetCombatFrozen(false);
                ClearTargetRangeHighlight();
                StateChanged?.Invoke();
            }

            return true;
        }

        void RefreshTargetRangeHighlight()
        {
            ClearTargetRangeHighlight();
            if (_battle == null || _battle.Board == null || !SelectingSkill.HasValue)
                return;

            var def = GetActiveCastDef();
            if (def == null) return;
            ShowZoneAndUnitRange(def, focusCell: null);
        }

        void ShowCastRangeHighlight(XinxiangSkillDefinition def, SpiritUnit target)
        {
            ClearTargetRangeHighlight();
            if (def == null || _battle == null || _battle.Board == null) return;
            if (def.IsArea && target != null)
            {
                ShowAoERange(def, target.Cell);
                return;
            }
            ShowZoneAndUnitRange(def, focusCell: target != null ? target.Cell : (Vector2Int?)null);
        }

        void TickAoEHover()
        {
            if (!SelectingSkill.HasValue || _battle == null || _battle.Board == null)
                return;

            var def = GetActiveCastDef();
            if (def == null || !def.IsArea)
                return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            if (!TryPickHoverOrigin(out var cell))
                return;

            if (_aoeHoverCell.HasValue && _aoeHoverCell.Value == cell)
                return;

            _aoeHoverCell = cell;
            ShowAoERange(def, cell);
        }

        bool TryPickHoverOrigin(out Vector2Int cell)
        {
            cell = default;
            if (Camera.main == null || _battle == null || _battle.Board == null)
                return false;

            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 200f))
            {
                var unit = hit.collider != null ? hit.collider.GetComponentInParent<SpiritUnit>() : null;
                if (unit != null && unit.IsAlive)
                {
                    cell = unit.Cell;
                    return true;
                }
            }

            var plane = _battle.Board.GetPickPlane();
            if (!plane.Raycast(ray, out float enter))
                return false;

            Vector3 world = ray.GetPoint(enter);
            var near = FindNearestLivingUnit(world, 0.55f);
            if (near != null)
            {
                cell = near.Cell;
                return true;
            }

            return _battle.Board.TryWorldToCell(world, out cell);
        }

        SpiritUnit FindNearestLivingUnit(Vector3 world, float maxDist)
        {
            var board = _battle.Board;
            SpiritUnit best = null;
            float bestSq = maxDist * maxDist;
            for (int x = 0; x < BoardGrid.Size; x++)
            {
                for (int y = 0; y < BoardGrid.Size; y++)
                {
                    var u = board.Get(new Vector2Int(x, y));
                    // Hidden grass ambushers cannot be picked as skill targets.
                    if (u == null || !u.IsAlive || u.IsAmbushing) continue;
                    float sq = (u.transform.position - world).sqrMagnitude;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        best = u;
                    }
                }
            }
            return best;
        }

        void ShowAoERange(XinxiangSkillDefinition def, Vector2Int origin)
        {
            var board = _battle.Board;
            var zoneCells = board.GetChebyshevCells(origin, def.AoERadius);
            var unitCells = new List<Vector2Int>(8);
            Team team = TargetTeam(def);
            for (int i = 0; i < zoneCells.Count; i++)
            {
                var u = board.Get(zoneCells[i]);
                if (u != null && u.IsAlive && u.Team == team)
                    unitCells.Add(zoneCells[i]);
            }

            var center = board.Get(origin);
            bool validCenter = center != null && center.IsAlive && IsValidTarget(def, center);
            bool enemy = def.TargetRule == XinxiangTargetRule.EnemyUnit;
            Color zoneColor = validCenter
                ? (enemy ? new Color(0.15f, 0.65f, 1f, 0.55f) : new Color(0.25f, 0.95f, 0.4f, 0.55f))
                : new Color(1f, 0.4f, 0.3f, 0.4f);
            Color unitColor = enemy
                ? new Color(0.35f, 0.95f, 1f, 0.92f)
                : new Color(0.55f, 1f, 0.55f, 0.92f);
            Color focusColor = validCenter
                ? (enemy ? new Color(1f, 0.95f, 0.35f, 0.98f) : new Color(1f, 1f, 0.45f, 0.98f))
                : new Color(1f, 0.55f, 0.4f, 0.9f);

            board.ShowTargetRangeLayered(zoneCells, zoneColor, unitCells, unitColor, origin, focusColor);
        }

        static Team TargetTeam(XinxiangSkillDefinition def)
        {
            return def != null && def.TargetRule == XinxiangTargetRule.EnemyUnit
                ? Team.Enemy
                : Team.Player;
        }

        /// <summary>
        /// Soft home-zone wash + bright markers on every living valid target,
        /// wherever they currently stand (units leave deploy half mid-fight).
        /// </summary>
        void ShowZoneAndUnitRange(XinxiangSkillDefinition def, Vector2Int? focusCell)
        {
            var board = _battle.Board;
            bool enemy = def.TargetRule == XinxiangTargetRule.EnemyUnit;

            var zoneCells = new System.Collections.Generic.List<Vector2Int>(32);
            var unitCells = new System.Collections.Generic.List<Vector2Int>(8);
            for (int x = 0; x < BoardGrid.Size; x++)
            {
                for (int y = 0; y < BoardGrid.Size; y++)
                {
                    var c = new Vector2Int(x, y);
                    bool inHome = enemy ? board.IsEnemyDeployZone(c) : board.IsPlayerDeployZone(c);
                    if (inHome)
                        zoneCells.Add(c);

                    var u = board.Get(c);
                    if (u != null && u.IsAlive && IsValidTarget(def, u))
                    {
                        unitCells.Add(c);
                        // Unit left home half — still show a soft cell under them.
                        if (!inHome)
                            zoneCells.Add(c);
                    }
                }
            }

            Color zoneColor = enemy
                ? new Color(0.15f, 0.65f, 1f, 0.45f)
                : new Color(0.25f, 0.95f, 0.4f, 0.45f);
            Color unitColor = enemy
                ? new Color(0.35f, 0.95f, 1f, 0.92f)
                : new Color(0.55f, 1f, 0.55f, 0.92f);
            Color focusColor = enemy
                ? new Color(1f, 0.95f, 0.35f, 0.98f)
                : new Color(1f, 1f, 0.45f, 0.98f);

            board.ShowTargetRangeLayered(zoneCells, zoneColor, unitCells, unitColor, focusCell, focusColor);
        }

        void ClearTargetRangeHighlight()
        {
            if (_battle != null && _battle.Board != null)
                _battle.Board.ClearTargetRange();
        }

        void OnCastFinished(XinxiangSkillDefinition def, SpiritUnit target, bool applied)
        {
            if (applied)
                BeginCooldownForPending();
            else if (_castWasPrankRevenge && _cooldownOnFinish != null)
            {
                // Still lock Ice+Buff even if the hit frame somehow missed.
                BeginCooldownForPending();
            }

            bool combo = _castWasHealBuffCombo;
            bool prank = _castWasPrankRevenge;
            _castWasHealBuffCombo = false;
            _castWasPrankRevenge = false;
            GameAudio.I?.NotifyCheerCastFinished(_castWasCheered);
            _castWasCheered = false;
            IsCasting = false;
            _battle?.SetCombatFrozen(false);
            ClearTargetRangeHighlight();
            if (_battle != null)
            {
                if (prank)
                    _battle.LastEvent = "别再打来了";
                else if (def != null)
                    _battle.NotifyXinxiangSkillUsed(def, target, combo ? HealBuffComboName : null);
            }
            StateChanged?.Invoke();
        }

        void BeginCooldownForPending()
        {
            if (_cooldownOnFinish == null || _cooldownOnFinish.Length == 0)
                return;
            for (int i = 0; i < _cooldownOnFinish.Length; i++)
                BeginCooldown(GetDefinition(_cooldownOnFinish[i]));
            _cooldownOnFinish = null;
        }

        void BeginCooldown(XinxiangSkillDefinition def)
        {
            if (def == null) return;
            int idx = (int)def.Id;
            if (idx < 0 || idx >= _cooldownRemaining.Length) return;
            _cooldownRemaining[idx] = Mathf.Max(0.1f, def.Cooldown);
        }

        public static int LastHits { get; private set; }
        public static XinxiangQteGrade LastQteGrade { get; private set; } = XinxiangQteGrade.Good;

        public static bool IsValidTarget(XinxiangSkillDefinition def, SpiritUnit unit)
        {
            if (def == null || unit == null || !unit.IsAlive) return false;
            // Enemy skills cannot lock onto grass units still hidden in ambush.
            if (def.TargetRule == XinxiangTargetRule.EnemyUnit && unit.IsAmbushing)
                return false;
            return def.TargetRule == XinxiangTargetRule.EnemyUnit
                ? unit.Team == Team.Enemy
                : unit.Team == Team.Player;
        }

        public static int ApplyEffect(XinxiangSkillDefinition def, SpiritUnit target, bool spawnVfx = true)
        {
            BoardGrid board = target != null ? target.Board : null;
            Vector2Int cell = target != null ? target.Cell : default;
            return ApplyEffect(def, target, board, cell, spawnVfx);
        }

        public static int ApplyEffect(
            XinxiangSkillDefinition def,
            SpiritUnit focus,
            BoardGrid board,
            Vector2Int origin,
            bool spawnVfx = true)
        {
            if (def == null)
            {
                LastHits = 0;
                return 0;
            }

            var units = CollectTargets(def, focus, board, origin);
            for (int i = 0; i < units.Count; i++)
                ApplyEffectToUnit(def, units[i]);

            if (spawnVfx)
            {
                if (focus != null)
                    XinxiangVfxLibrary.SpawnOn(def, focus);
                else
                {
                    Vector3 pos = board != null
                        ? board.CellToWorld(origin)
                        : Vector3.zero;
                    XinxiangVfxLibrary.SpawnAt(def, pos);
                }
            }

            LastHits = units.Count;
            return units.Count;
        }

        /// <summary>
        /// 星辉庇护：治疗 3×3 内每个我方单位，同时挂上增益。
        /// QTE 三档：完美加强 / 成功原版 / 失败保底弱效果。
        /// </summary>
        public static int ApplyHealBuffCombo(
            XinxiangSkillDefinition healDef,
            XinxiangSkillDefinition buffDef,
            SpiritUnit focus,
            BoardGrid board,
            Vector2Int origin,
            bool spawnVfx = true,
            XinxiangQteGrade qteGrade = XinxiangQteGrade.Good)
        {
            LastQteGrade = qteGrade;

            float healScale = 1f;
            float buffMul = buffDef != null ? buffDef.AtkBuffMul : 1.25f;
            float buffDur = buffDef != null ? buffDef.AtkBuffDuration : 6f;
            switch (qteGrade)
            {
                case XinxiangQteGrade.Perfect:
                    healScale = 1.3f;
                    buffMul = Mathf.Max(buffMul, 1.4f);
                    buffDur = Mathf.Max(buffDur, 8f);
                    break;
                case XinxiangQteGrade.Miss:
                    healScale = 0.6f;
                    buffMul = 1.1f;
                    buffDur = Mathf.Min(buffDur, 4f);
                    break;
            }

            var units = CollectTargets(healDef ?? buffDef, focus, board, origin);
            if (units.Count == 0 && focus != null && focus.IsAlive && IsValidTarget(healDef ?? buffDef, focus))
                units.Add(focus);

            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null || !u.IsAlive) continue;

                if (healDef != null)
                {
                    float heal = u.MaxHp * Mathf.Clamp01(healDef.HealPercentMaxHp) * healScale;
                    u.Heal(heal);
                }

                if (buffDef != null)
                    u.AddAtkBuff(buffMul, buffDur);
            }

            if (units.Count > 0)
            {
                if (healDef != null) GameAudio.I?.PlayHeal();
                if (buffDef != null) GameAudio.I?.PlayBuff();
            }

            if (spawnVfx)
            {
                if (focus != null)
                {
                    XinxiangVfxLibrary.SpawnOn(healDef, focus, cleanupFirst: true);
                    XinxiangVfxLibrary.SpawnOn(buffDef, focus, cleanupFirst: false);
                }
                else
                {
                    Vector3 pos = board != null
                        ? board.CellToWorld(origin)
                        : Vector3.zero;
                    XinxiangVfxLibrary.SpawnAt(healDef, pos, cleanupFirst: true);
                    XinxiangVfxLibrary.SpawnAt(buffDef, pos, cleanupFirst: false);
                }
            }

            LastHits = units.Count;
            return units.Count;
        }

        public static List<SpiritUnit> CollectTargets(
            XinxiangSkillDefinition def,
            SpiritUnit focus,
            BoardGrid board,
            Vector2Int origin)
        {
            var result = new List<SpiritUnit>(8);
            if (def == null) return result;

            if (def.IsArea && board != null)
            {
                var found = board.GetUnitsInChebyshev(origin, def.AoERadius, TargetTeam(def));
                for (int i = 0; i < found.Count; i++)
                {
                    if (found[i] != null && found[i].IsAlive)
                        result.Add(found[i]);
                }
                return result;
            }

            if (focus != null && focus.IsAlive && IsValidTarget(def, focus))
                result.Add(focus);
            return result;
        }

        static void ApplyEffectToUnit(XinxiangSkillDefinition def, SpiritUnit target)
        {
            if (def == null || target == null || !target.IsAlive) return;
            // Offensive skills cannot hit grass units still hidden in ambush.
            if (target.IsAmbushing && def.Id == XinxiangSkillId.IceDamage)
                return;

            switch (def.Id)
            {
                case XinxiangSkillId.IceDamage:
                    float dmg = Mathf.Max(1f, def.Damage - target.Def * 0.35f);
                    target.TakeDamage(dmg, null, true);
                    if (def.StunDuration > 0f && target.IsAlive)
                        target.AddStun(def.StunDuration);
                    break;
                case XinxiangSkillId.Buff:
                    target.AddAtkBuff(def.AtkBuffMul, def.AtkBuffDuration);
                    GameAudio.I?.PlayBuff();
                    break;
                case XinxiangSkillId.Heal:
                    float heal = target.MaxHp * Mathf.Clamp01(def.HealPercentMaxHp);
                    target.Heal(heal);
                    GameAudio.I?.PlayHeal();
                    break;
            }
        }
    }
}
