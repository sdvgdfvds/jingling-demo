using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NexusAnima
{
    public partial class SpiritUnit : MonoBehaviour
    {public SpiritDefinition Definition { get; private set; }
        public Team Team { get; private set; }
        public Vector2Int Cell { get; private set; }
        public BoardGrid Board => _board;
        public bool IsAlive => CurrentHp > 0f && gameObject.activeInHierarchy;

        public float CurrentHp { get; private set; }
        public float MaxHp { get; private set; }
        public float Atk { get; private set; }
        public float Def { get; private set; }
        public float Energy { get; private set; }
        public const float MaxEnergy = 100f;

        BoardGrid _board;
        BondState _bonds;
        FateMasterController _master;
        BattleManager _battle;
        SpiritUnit _target;
        float _attackCooldown;
        
float _moveCooldown;
        readonly List<StatusEffect> _statuses = new List<StatusEffect>();
        RectTransform _hudRoot;
        Text _label;
        Text _hpValueLabel;
        Image _hpBarFill;
        Image _hpBarLag;
        float _hudAnchorHeight;
                Renderer _bodyRenderer;
        BoxCollider _bodyCollider;
        Transform _bodyTransform;
        Vector3 _bodyBaseLocalPosition;
        Quaternion _bodyBaseLocalRotation;
        float _walkAnimationRemaining;
                float _walkAnimationTime;
        Transform _leftWingRoot;
        Transform _rightWingRoot;
        Quaternion _leftWingBaseRotation;
        Quaternion _rightWingBaseRotation;
        float _wingAnimationTime;
        bool _wingAnimationEnabled;
        readonly List<Transform> _tailRoots = new List<Transform>();
        readonly List<Quaternion> _tailBaseRotations = new List<Quaternion>();
        float _idleAnimationTime;
        float _attackAnimationTime;
        float _attackAnimationDuration;
        Vector3 _attackDirectionLocal;
        float _attackLungeDistance;
        bool _pendingMeleeAttack;
        bool _pendingMeleeSkill;
        int _pendingMeleeHits;
        SpiritUnit _pendingMeleeTarget;
        bool _attackFacingTarget;

        float _displayedHpRatio = 1f;
        float _lagHpRatio = 1f;
        bool _combatEnabled;

        /// <summary>Grass-element unit started the fight on a grass tile — hide until enemy in range.</summary>
        bool _grassAmbushLatent;
        /// <summary>Next attack is the high-damage ambush opener.</summary>
        bool _grassAmbushStrikeReady;
        /// <summary>When set, current attack target is a destructible wall cell (not a unit).</summary>
        Vector2Int? _wallTargetCell;
        /// <summary>
        /// Player click-ordered rock: exclusive target until the rock falls.
        /// Overrides normal enemy chase while set.
        /// </summary>
        Vector2Int? _forcedWallTarget;
        /// <summary>
        /// Water-element unit started the fight on a river tile — rooted all battle, permanent ATK buff.
        /// </summary>
        bool _waterRooted;
        /// <summary>Cache whether we already floated the rock-behind buff cue.</summary>
        bool _behindWallBuffShown;
        /// <summary>
        /// Player clicked this unit during battle to leave rock cover and chase freely.
        /// Resets each fight. Enemies never set this.
        /// </summary>
        bool _leaveCoverOrdered;

        /// <summary>
        /// True while a grass unit is still hidden in ambush (not yet sprung).
        /// Other spirits must not target or damage this unit until it breaks stealth.
        /// </summary>
        public bool IsAmbushing => _grassAmbushLatent && IsAlive;
        /// <summary>Water spirit locked on river for the whole match.</summary>
        public bool IsWaterRooted => _waterRooted && IsAlive;

        /// <summary>
        /// Holding rock cover: stay put, only fight enemies already in attack range,
        /// until the player clicks the unit (or the covering rock falls).
        /// </summary>
        public bool IsHoldingCover =>
            IsAlive && !_leaveCoverOrdered && IsStandingBehindRockNow();

        public void Init(
            SpiritDefinition def,
            Team team,
            Vector2Int cell,
            BoardGrid board,
            BondState bonds,
            FateMasterController master,
            BattleManager battle)
        {
            if (def != null) def.EnsurePresentation();
            Definition = def;
            Team = team;
            Cell = cell;
            _board = board;
            _bonds = bonds;
            _master = master;
            _battle = battle;

            // Bond bonuses apply only to related elements (not whole team).
            BondBonuses scoped = default;
            if (team == Team.Player && bonds != null && def != null)
                scoped = bonds.GetBonusesFor(def.Element);

            float hpMul = 1f + scoped.Hp;
            float atkMul = 1f + scoped.Attack;
            float defMul = 1f + scoped.Defense;

            if (team == Team.Player && master != null && master.Definition.BuffElement == def.Element)
                atkMul += master.Definition.PassiveAtkBonus;

            MaxHp = def.MaxHp * hpMul;
            CurrentHp = MaxHp;
            Atk = def.Atk * atkMul;
            Def = def.Def * defMul;
            Energy = 0f;

            // Always rebuild Body so cell-fit is clean (avoids leftover inner scales
            // from older per-model height hacks on recycled units).
            BuildVisual();

            ApplyDefaultFormationFacing();
            SnapToCell(cell, true);
            EnsureBoardRegistration();
        }

        /// <summary>
        /// Reset root + body to pre-combat formation pose (team yaw, no combat turn).
        /// Combat facing rotates the root via FaceToward*; formation yaws the root
        /// with the grid (+cellY) and keeps the team default yaw on the Body child
        /// (player ~0°, enemy ~180° so they face each other along the cell-Y axis).
        /// </summary>
        public void ApplyDefaultFormationFacing()
        {
            transform.rotation = _board != null ? _board.GridYawRotation : Quaternion.identity;

            if (_bodyTransform != null)
            {
                float yaw = Team == Team.Player ? 0.401f : 180.401f;
                _bodyBaseLocalRotation = Quaternion.Euler(0f, yaw, 0f);
                _bodyTransform.localRotation = _bodyBaseLocalRotation;
                _bodyTransform.localPosition = _bodyBaseLocalPosition;
            }

            _walkAnimationRemaining = 0f;
            _walkAnimationTime = 0f;
            _attackAnimationTime = _attackAnimationDuration;
            _attackFacingTarget = false;
            _pendingMeleeAttack = false;
            _pendingMeleeSkill = false;
            _pendingMeleeTarget = null;
            _pendingMeleeHits = 0;
            _target = null;
        }

        /// <summary>Re-bind this unit to its logical cell so AI targeting can see it.</summary>
        public void EnsureBoardRegistration()
        {
            if (_board == null || !IsAlive) return;
            _board.Set(Cell, this);
        }


public void BuildSelectionPreview(SpiritDefinition def, BoardGrid board, Team previewTeam = Team.Player)
        {
            if (def != null) def.EnsurePresentation();
            Definition = def;
            this.Team = previewTeam;
            Cell = Vector2Int.zero;
            _board = board;
            _bonds = null;
            _master = null;
            _battle = null;
            _combatEnabled = false;
            _idleAnimationTime = 0f;
            _wingAnimationTime = 0f;

            float cellSize = _board != null ? _board.CellSize : 0.24f;
            CreateSpiritModel(cellSize);

            foreach (var collider in GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }


void BuildVisual()
        {
            float cellSize = _board != null ? _board.CellSize : 0.24f;
            float bodyHeight = CreateSpiritModel(cellSize);
            CreateBodyCollider();
            _hudAnchorHeight = bodyHeight + cellSize * 0.58f;
            EnsureScreenHud();
            RefreshLabel();
            UpdateHpBar();
        }

void CreateBodyCollider()
        {
            if (_bodyTransform == null) return;

            var renderers = _bodyTransform.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            var collider = GetComponent<BoxCollider>();
            if (collider == null)
                collider = gameObject.AddComponent<BoxCollider>();

            collider.center = transform.InverseTransformPoint(bounds.center);
            collider.size = bounds.size;
            collider.isTrigger = false;
            _bodyCollider = collider;
        }

void LateUpdate()
        {
            if (!_combatEnabled || _bodyCollider == null || _board == null)
                return;

            foreach (var other in _board.AllUnits())
            {
                if (other == null || other == this || !other.IsAlive || other._bodyCollider == null)
                    continue;
                // Cheap broadphase: only resolve near neighbors.
                if (_board.Chebyshev(Cell, other.Cell) > 1.5f)
                    continue;
                if (GetHashCode() > other.GetHashCode())
                    continue;

                if (!Physics.ComputePenetration(
                    _bodyCollider,
                    transform.position,
                    transform.rotation,
                    other._bodyCollider,
                    other.transform.position,
                    other.transform.rotation,
                    out Vector3 direction,
                    out float distance))
                    continue;

                // Soft push-apart so large meshes don't visibly clip. Cap the correction
            // so units stay near their logical cell centers.
                float pad = _board != null ? _board.CellSize * 0.03f : 0.005f;
                float maxPush = _board != null ? _board.CellSize * 0.12f : 0.02f;
                float push = Mathf.Min(distance + pad, maxPush) * 0.5f;
                Vector3 correction = direction * push;
                correction.y = 0f;
                transform.position += correction;
                other.transform.position -= correction;
                KeepBodyInsideBoard();
                other.KeepBodyInsideBoard();
            }
        }


        void DestroyExistingBodies()
        {
            _bodyTransform = null;
            _bodyRenderer = null;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == null || child.name != "Body")
                    continue;
                // Deactivate first so a same-frame re-Init cannot keep rendering
                // leftover bodies (Destroy is deferred until end of frame).
                child.gameObject.SetActive(false);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        float CreateSpiritModel(float cellSize)
        {
            DestroyExistingBodies();

            var prefab = Resources.Load<GameObject>("Spirits/" + GetSpiritModelName());
            if (prefab != null)
            {
                var model = Instantiate(prefab, transform);
                model.name = "Body";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(0f, Team == Team.Player ? 0.401f : 180.401f, 0f);
                model.transform.localScale = Vector3.one;

                SpiritModelFactory.HideHelperRenderers(model);
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                SpiritModelFactory.ApplySpiritTexture(renderers, GetSpiritModelName());

                if (renderers.Length > 0)
                {
                    float fittedHeight = FitModelToCell(model.transform, cellSize);

                    _bodyTransform = model.transform;
                    _bodyBaseLocalPosition = model.transform.localPosition;
                    _bodyBaseLocalRotation = model.transform.localRotation;
                    _bodyRenderer = renderers[0];
                    SetupWingAnimation(model.transform);
                    return Mathf.Max(fittedHeight, cellSize * 0.35f);
                }

                Destroy(model);
            }

            var fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            fallback.name = "Body";
            fallback.transform.SetParent(transform, false);
            fallback.transform.localScale = new Vector3(cellSize * 0.55f, cellSize * 0.45f, cellSize * 0.55f);
            fallback.transform.localPosition = Vector3.up * (cellSize * 0.45f);
            Destroy(fallback.GetComponent<Collider>());
            _bodyTransform = fallback.transform;
            _bodyBaseLocalPosition = fallback.transform.localPosition;
            _bodyBaseLocalRotation = fallback.transform.localRotation;
            _bodyRenderer = fallback.GetComponent<Renderer>();
            SpiritModelFactory.SetRendererColor(_bodyRenderer, Definition.Color);
            return cellSize;
        }

        /// <summary>
        /// Uniformly scale the model so its world AABB fits inside one board cell
        /// (XZ footprint + height cap), then ground/center it on the unit root.
        /// </summary>
        float FitModelToCell(Transform model, float cellSize)
        {
            if (model == null) return cellSize;

            // Measure at localScale=1 so we get a clean ratio.
            model.localScale = Vector3.one;
            Bounds raw = SpiritModelFactory.GetRenderBounds(model.gameObject);
            float rawX = Mathf.Max(raw.size.x, 1e-5f);
            float rawY = Mathf.Max(raw.size.y, 1e-5f);
            float rawZ = Mathf.Max(raw.size.z, 1e-5f);

            float worldCellX = cellSize;
            float worldCellZ = cellSize;
            if (_board != null)
            {
                worldCellX = _board.CellSize * Mathf.Abs(_board.transform.lossyScale.x);
                worldCellZ = _board.CellSize * Mathf.Abs(_board.transform.lossyScale.z);
            }

            // Stay inside the painted tile with a little margin; height capped near one cell.
            const float footprintFill = 0.86f;
            const float heightFill = 1.05f;
            float maxX = worldCellX * footprintFill;
            float maxZ = worldCellZ * footprintFill;
            float maxY = Mathf.Max(worldCellX, worldCellZ) * heightFill;

            float scale = Mathf.Min(maxX / rawX, maxY / rawY, maxZ / rawZ);
            // Guard against tiny/huge authored meshes.
            scale = Mathf.Clamp(scale, 0.0005f, 50f);
            model.localScale = Vector3.one * scale;

            Bounds scaled = SpiritModelFactory.GetRenderBounds(model.gameObject);
            Vector3 root = transform.position;
            // Birds: clear sky hover (~1.2 cell). Ground units: plant feet on grid surface.
            float flyLift = IsFlyingModel()
                ? Mathf.Min(worldCellX, worldCellZ) * 1.2f
                : 0f;

            // Center on cell XZ and plant feet on the unit root (sky lift for flyers).
            model.position += new Vector3(
                root.x - scaled.center.x,
                (root.y + flyLift) - scaled.min.y,
                root.z - scaled.center.z);

            AlignFlyingModelToCellHorizontal(model);

            // Optional per-model fine-tune on top of algorithmic plant (not absolute Y).
            ApplyBodyLocalYFineTune(model);
            return SpiritModelFactory.GetRenderBounds(model.gameObject).size.y;
        }

        /// <summary>
        /// Small additive Body.localPosition.y tweaks after feet-plant / fly-lift.
        /// Do NOT store absolute Y here — that previously re-buried models under the blue grid.
        /// </summary>
        void ApplyBodyLocalYFineTune(Transform model)
        {
            if (model == null || Definition == null) return;
            float deltaY = Definition.BodyYFineTune;
            if (Mathf.Approximately(deltaY, 0f)) return;

            Vector3 lp = model.localPosition;
            lp.y += deltaY;
            model.localPosition = lp;
        }


void SetupWingAnimation(Transform modelRoot)
        {
            string modelName = GetSpiritModelName();
            _tailRoots.Clear();
            _tailBaseRotations.Clear();

            var transforms = modelRoot.GetComponentsInChildren<Transform>(true);
            foreach (var child in transforms)
            {
                if (child.name == "Shoulder_L") _leftWingRoot = child;
                else if (child.name == "Shoulder_R") _rightWingRoot = child;

                string boneName = child.name.ToLowerInvariant();
                if (boneName.Contains("tail"))
                {
                    _tailRoots.Add(child);
                    _tailBaseRotations.Add(child.localRotation);
                }
            }

            if (Definition == null || !Definition.EnableWingFlap)
                return;

            if (_leftWingRoot == null || _rightWingRoot == null) return;

            _leftWingBaseRotation = _leftWingRoot.localRotation * Quaternion.AngleAxis(60f, Vector3.right);
            _rightWingBaseRotation = _rightWingRoot.localRotation * Quaternion.AngleAxis(60f, Vector3.right);
            _wingAnimationEnabled = true;
        }

        void UpdateFlightAnimation(float dt)
        {
            if (!_wingAnimationEnabled || _leftWingRoot == null || _rightWingRoot == null)
                return;

            _wingAnimationTime += dt;
            float flapAngle = Mathf.Sin(_wingAnimationTime * Mathf.PI * 2f * 2.8f) * 22f;
            Quaternion flap = Quaternion.AngleAxis(flapAngle, Vector3.right);
            _leftWingRoot.localRotation = _leftWingBaseRotation * flap;
            _rightWingRoot.localRotation = _rightWingBaseRotation * flap;
        }

        void UpdateAttackAnimation(float dt)
        {
            if (_bodyTransform == null)
                return;

            bool attackActive = _attackAnimationTime < _attackAnimationDuration;
            if (!attackActive)
            {
                if (_pendingMeleeAttack)
                    ResolvePendingMeleeAttack();

                // Keep facing the enemy after the swing — do not snap root rotation back.
                if (_walkAnimationRemaining <= 0f)
                {
                    _bodyTransform.localPosition = _bodyBaseLocalPosition;
                    _bodyTransform.localRotation = _bodyBaseLocalRotation;
                }
                _attackFacingTarget = false;
                return;
            }

            _attackAnimationTime += dt;
            float progress = Mathf.Clamp01(_attackAnimationTime / Mathf.Max(0.01f, _attackAnimationDuration));
            float pulse = Mathf.Sin(progress * Mathf.PI);
            Vector3 attackOffset = _attackDirectionLocal * (_attackLungeDistance * pulse);
            attackOffset += Vector3.up * (_attackLungeDistance * 0.18f * pulse);
            float attackPitch = -_attackDirectionLocal.z * 10f * pulse;
            float attackRoll = -_attackDirectionLocal.x * 12f * pulse;

            if (_pendingMeleeAttack && _attackAnimationTime >= _attackAnimationDuration * 0.52f)
                ResolvePendingMeleeAttack();

            _bodyTransform.localPosition = _bodyBaseLocalPosition + attackOffset;
            _bodyTransform.localRotation = _bodyBaseLocalRotation
                * Quaternion.Euler(attackPitch, 0f, attackRoll);
        }

        void PlayAttackAnimation(SpiritUnit target)
        {
            if (target == null)
                return;

            Vector3 direction = target.transform.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                direction = DefaultTeamForward();
            direction.Normalize();

            bool melee = GetTargetingMode() != TargetingMode.RangedNearest;
            FaceTowardImmediate(direction);
            _attackFacingTarget = true;

            _attackDirectionLocal = transform.InverseTransformDirection(direction);
            _attackDirectionLocal.y = 0f;
            if (_attackDirectionLocal.sqrMagnitude > 0.0001f)
                _attackDirectionLocal.Normalize();

            _attackAnimationTime = 0f;
            _attackAnimationDuration = melee ? 0.36f : 0.28f;
            // Keep melee lunge short so models do not clip into the target.
            _attackLungeDistance = _board != null
                ? Mathf.Clamp(_board.CellSize * (melee ? 0.08f : 0.03f), 0.004f, 0.02f)
                : (melee ? 0.012f : 0.006f);
        }

        void FaceTowardImmediate(Vector3 flatDirection)
        {
            flatDirection.y = 0f;
            if (flatDirection.sqrMagnitude < 0.0001f)
                return;
            flatDirection.Normalize();

            Vector3 currentForward = _bodyTransform != null ? _bodyTransform.forward : transform.forward;
            currentForward.y = 0f;
            if (currentForward.sqrMagnitude < 0.0001f)
                currentForward = transform.forward;
            currentForward.Normalize();

            transform.rotation = Quaternion.FromToRotation(currentForward, flatDirection) * transform.rotation;
        }

        void FaceTowardSmooth(Vector3 worldPoint, float dt)
        {
            Vector3 direction = worldPoint - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                return;
            direction.Normalize();

            Vector3 currentForward = _bodyTransform != null ? _bodyTransform.forward : transform.forward;
            currentForward.y = 0f;
            if (currentForward.sqrMagnitude < 0.0001f)
                return;
            currentForward.Normalize();

            Vector3 blended = Vector3.Slerp(currentForward, direction, Mathf.Clamp01(dt * 10f));
            if (blended.sqrMagnitude < 0.0001f)
                return;
            transform.rotation = Quaternion.FromToRotation(currentForward, blended.normalized) * transform.rotation;
        }





        string GetSpiritModelName()
        {
            if (Definition == null) return "Art_KS_1502_00";
            Definition.EnsurePresentation();
            return string.IsNullOrEmpty(Definition.ModelName) ? "Art_KS_1502_00" : Definition.ModelName;
        }


public void SetCombatEnabled(bool enabled)
        {
            _combatEnabled = enabled;
            _attackCooldown = 0.2f;
            if (enabled)
                EnsureBoardRegistration();
        }

        /// <summary>
        /// Called once when battle starts.
        /// Grass on grass → ambush; water on river → root + permanent ATK buff.
        /// </summary>
        public void PrepareTerrainCombatState()
        {
            _grassAmbushLatent = false;
            _grassAmbushStrikeReady = false;
            _wallTargetCell = null;
            _forcedWallTarget = null;
            _waterRooted = false;
            _behindWallBuffShown = false;
            _leaveCoverOrdered = false;

            var map = GetBattlefield();
            if (map == null || Definition == null) return;

            if (Definition.Element == ElementType.Grass && map.IsGrass(Cell))
            {
                _grassAmbushLatent = true;
                _grassAmbushStrikeReady = true;
                ApplyAmbushVisual(true);
                SpawnFloatText(GetDamageTextPosition(), "伏击", new Color(0.4f, 1f, 0.45f));
            }

            if (Definition.Element == ElementType.Water && map.IsRiver(Cell))
            {
                _waterRooted = true;
                SpawnFloatText(GetDamageTextPosition(), "扎根水域", new Color(0.35f, 0.75f, 1f));
                SpawnFloatText(
                    GetDamageTextPosition() + Vector3.up * (_board != null ? _board.CellSize * 0.35f : 0.05f),
                    $"攻×{BattlefieldMap.WaterRootAtkMul:0.00}",
                    new Color(0.45f, 0.9f, 1f));
            }
        }

        /// <summary>
        /// True if this unit can currently take a player rock-smash order.
        /// Excludes ambushers and water-rooted units that are out of attack range
        /// (rooted spirits cannot walk to the rock).
        /// </summary>
        public bool CanAcceptWallSmashOrder(Vector2Int wallCell)
        {
            if (!IsAlive || !_combatEnabled) return false;
            if (IsAmbushing) return false;
            if (_board == null) return false;

            var map = GetBattlefield();
            if (map == null || !map.HasStandingWall(wallCell)) return false;

            // Water-rooted: can only smash if already in attack range.
            if (_waterRooted)
            {
                int range = GetCombatAttackRange();
                return _board.Chebyshev(Cell, wallCell) <= range + 0.01f;
            }

            return true;
        }

        /// <summary>
        /// Player clicked this unit in battle: leave rock cover and chase enemies normally.
        /// </summary>
        public bool OrderLeaveCover()
        {
            if (!IsAlive || !_combatEnabled) return false;
            if (Team != Team.Player) return false;
            if (!IsStandingBehindRockNow() && !_leaveCoverOrdered)
                return false;

            bool wasHolding = IsHoldingCover || IsStandingBehindRockNow();
            _leaveCoverOrdered = true;
            ClearForcedWallTarget();
            _moveCooldown = 0f;
            _attackCooldown = 0f;

            if (wasHolding)
            {
                SpawnFloatText(GetDamageTextPosition(), "出击!", new Color(1f, 0.85f, 0.35f));
                if (_battle != null)
                    _battle.LastEvent = $"{Definition?.DisplayName ?? "精灵"} 离开岩后，主动出击";
            }

            return true;
        }

        /// <summary>
        /// Player clicked a rock: drop current enemy target and smash that rock immediately.
        /// Callers should filter with <see cref="CanAcceptWallSmashOrder"/> first.
        /// </summary>
        public void OrderSmashWall(Vector2Int wallCell)
        {
            if (!CanAcceptWallSmashOrder(wallCell)) return;
            if (_board == null) return;

            // Leaving cover to execute a smash order.
            _leaveCoverOrdered = true;

            // Hard retarget — ignore whoever we were fighting.
            _forcedWallTarget = wallCell;
            _wallTargetCell = wallCell;
            _target = null;
            _pendingMeleeAttack = false;
            _pendingMeleeSkill = false;
            _pendingMeleeTarget = null;
            _pendingMeleeHits = 0;
            // Cancel current swing / walk so we can act on the rock this frame.
            _attackAnimationTime = _attackAnimationDuration;
            _attackFacingTarget = false;
            _moveCooldown = 0f;
            _attackCooldown = 0f;
            _walkAnimationRemaining = 0f;

            Vector3 wallWorld = _board.CellToWorld(wallCell);
            FaceTowardImmediate(wallWorld - transform.position);

            int range = GetCombatAttackRange();
            float dist = _board.Chebyshev(Cell, wallCell);
            if (dist <= range + 0.01f)
            {
                PerformWallAttackCycle(wallCell);
                _attackCooldown = 1f / Mathf.Max(0.2f, Definition != null ? Definition.AttackSpeed : 1f);
            }
            else if (!_waterRooted)
            {
                TryMoveToward(wallCell);
                _moveCooldown = GetCombatMoveInterval();
            }
        }

        void ClearForcedWallTarget()
        {
            _forcedWallTarget = null;
            if (_wallTargetCell.HasValue)
                _wallTargetCell = null;
        }

        BattlefieldMap GetBattlefield()
        {
            if (_board != null && _board.Battlefield != null)
                return _board.Battlefield;
            return _battle != null ? _battle.GetBattlefield() : null;
        }

        public void AddEnergy(float amount)
        {
            if (!IsAlive || amount <= 0f) return;
            float before = Energy;
            Energy = Mathf.Min(MaxEnergy, Energy + amount);
            float gained = Energy - before;
            if (gained <= 0.01f) return;
            SpawnFloatText(GetDamageTextPosition(), $"+{gained:0}能", new Color(1f, 0.92f, 0.35f));
        }

        void ApplyAmbushVisual(bool hiding)
        {
            // Dim root scale cue so ambush reads even on opaque toon mats.
            transform.localScale = hiding ? Vector3.one * 0.92f : Vector3.one;
        }


void Update()
        {
            // Xinxiang cast: freeze combat motion on the board (cinematic hold).
            if (_battle != null && _battle.CombatFrozen)
            {
                UpdateOverheadUi();
                AnimateHpBar(Time.unscaledDeltaTime);
                return;
            }

            UpdateMovementAnimation(Time.deltaTime);
            UpdateFlightAnimation(Time.deltaTime);
            UpdateAttackAnimation(Time.deltaTime);
            AnimateHpBar(Time.deltaTime);
            UpdateOverheadUi();

            if (!_combatEnabled || !IsAlive || _battle == null || _battle.Phase != GamePhase.Battle)
                return;

            float dt = Time.deltaTime;
            TickStatuses(dt);
            if (IsStunned()) return;

            // --- Grass ambush: hold still until spring conditions ---
            if (_grassAmbushLatent)
            {
                bool forceSortie = ShouldForceGrassAmbushSortie();
                _target = FindNearestEnemy();

                bool inRange = false;
                if (_target != null && _target.IsAlive)
                {
                    if (_attackAnimationTime >= _attackAnimationDuration)
                        FaceTowardSmooth(_target.transform.position, dt);

                    int ambushRange = GetCombatAttackRange();
                    float ambushDist = _board.Chebyshev(Cell, _target.Cell);
                    inRange = ambushDist <= ambushRange + 0.01f
                              || IsPhysicallyNearTarget(_target);
                }

                // Spring when enemy enters range, OR when only grass remains (must fight).
                if (!forceSortie && !inRange)
                    return;

                _grassAmbushLatent = false;
                ApplyAmbushVisual(false);
                _wallTargetCell = null;
                _attackCooldown = 0f;
                string ambushLabel = forceSortie && !inRange ? "出击!" : "奇袭!";
                Vector3 ambushPos = transform.position + Vector3.up * (_board.CellSize * 0.35f);
                BattlefieldVfxLibrary.SpawnAmbush(ambushPos, _board.CellSize);
                BattlefieldVfxLibrary.SpawnAmbushText(ambushPos, ambushLabel, _board.CellSize);
                SpawnFloatText(GetDamageTextPosition(), ambushLabel, new Color(0.35f, 1f, 0.4f));
                // Fall through into normal combat this frame.
            }

            // Rock-behind stance cue (once when first gained).
            RefreshBehindWallCue();

            // Player ordered a rock: exclusive target until it falls — no enemy retarget.
            // Ambush / water-root out of range never hold this flag (filtered at order time).
            if (_forcedWallTarget.HasValue)
            {
                var mapForced = GetBattlefield();
                if (mapForced == null || !mapForced.HasStandingWall(_forcedWallTarget.Value))
                {
                    ClearForcedWallTarget();
                }
                else if (_waterRooted
                         && _board.Chebyshev(Cell, _forcedWallTarget.Value) > GetCombatAttackRange() + 0.01f)
                {
                    // Rooted and no longer in range — drop the order, resume normal combat.
                    ClearForcedWallTarget();
                }
                else
                {
                    _wallTargetCell = _forcedWallTarget;
                    _target = null;
                    // Fall through to wall attack / move block below.
                }
            }

            if (!_forcedWallTarget.HasValue)
            {
                // Ranged units re-evaluate every frame so an enemy entering range becomes the nearest in-range target.
                if (_target == null || !_target.IsAlive || GetTargetingMode() == TargetingMode.RangedNearest)
                    _target = FindNearestEnemy();

                ResolveCombatTarget(out SpiritUnit unitTarget, out Vector2Int? wallTarget);
                _target = unitTarget;
                _wallTargetCell = wallTarget;
            }

            bool holdingCover = IsHoldingCover;

            if (_target == null && !_wallTargetCell.HasValue)
                return;

            Vector3 facePos = _wallTargetCell.HasValue && _board != null
                ? _board.CellToWorld(_wallTargetCell.Value)
                : _target.transform.position;

            // Always turn to face the current target while fighting.
            if (_attackAnimationTime >= _attackAnimationDuration)
                FaceTowardSmooth(facePos, dt);

            int attackRange = GetCombatAttackRange();
            bool ranged = GetTargetingMode() == TargetingMode.RangedNearest;

            if (_wallTargetCell.HasValue)
            {
                float wallDist = _board.Chebyshev(Cell, _wallTargetCell.Value);
                if (wallDist <= attackRange + 0.01f)
                {
                    _attackCooldown -= dt;
                    if (_attackCooldown <= 0f)
                    {
                        PerformWallAttackCycle(_wallTargetCell.Value);
                        _attackCooldown = 1f / Mathf.Max(0.2f, Definition.AttackSpeed);
                    }
                }
                // Hold-cover units never walk out to smash rocks unless ordered (_forcedWallTarget).
                else if (!_waterRooted && !holdingCover)
                {
                    _moveCooldown -= dt;
                    if (_moveCooldown <= 0f)
                    {
                        TryMoveToward(_wallTargetCell.Value);
                        _moveCooldown = GetCombatMoveInterval();
                    }
                }
                return;
            }

            float dist = _board.Chebyshev(Cell, _target.Cell);

            if (ranged)
            {
                // Hold-cover: never kite or close distance — only shoot if already in range.
                // Water-rooted units never leave their river tile.
                if (!holdingCover && !_waterRooted && !Mathf.Approximately(dist, attackRange))
                {
                    _moveCooldown -= dt;
                    if (_moveCooldown <= 0f)
                    {
                        _moveCooldown = GetCombatMoveInterval();
                        if (dist < attackRange)
                        {
                            if (TryMoveAwayFrom(_target.Cell, attackRange))
                                return;
                        }
                        else if (TryMoveTowardRange(_target.Cell, attackRange))
                        {
                            return;
                        }
                    }
                }

                if (dist <= attackRange)
                {
                    _attackCooldown -= dt;
                    if (_attackCooldown <= 0f)
                    {
                        PerformAttackCycle();
                        _attackCooldown = 1f / Mathf.Max(0.2f, Definition.AttackSpeed);
                    }
                }
                return;
            }

            bool physicalRange = IsPhysicallyNearTarget(_target);
            if (dist <= attackRange || physicalRange)
            {
                _attackCooldown -= dt;
                if (_attackCooldown <= 0f)
                {
                    PerformAttackCycle();
                    _attackCooldown = 1f / Mathf.Max(0.2f, Definition.AttackSpeed);
                }
            }
            else if (!_waterRooted && !holdingCover)
            {
                _moveCooldown -= dt;
                if (_moveCooldown <= 0f)
                {
                    TryMoveToward(_target.Cell);
                    _moveCooldown = GetCombatMoveInterval();
                }
            }
        }

        bool IsStandingBehindRockNow()
        {
            var map = GetBattlefield();
            return map != null && map.IsStandingBehindWall(Cell, Team);
        }

        void RefreshBehindWallCue()
        {
            var map = GetBattlefield();
            if (map == null) return;
            bool behind = map.IsStandingBehindWall(Cell, Team);
            if (behind && !_behindWallBuffShown)
            {
                _behindWallBuffShown = true;
                string label = _leaveCoverOrdered ? "岩后·增防" : "岩后·固守增防";
                SpawnFloatText(GetDamageTextPosition(), label, new Color(0.55f, 0.85f, 1f));
            }
            else if (!behind)
            {
                _behindWallBuffShown = false;
            }
        }

        void ResolveCombatTarget(out SpiritUnit unitTarget, out Vector2Int? wallTarget)
        {
            unitTarget = _target != null && _target.IsAlive ? _target : FindNearestEnemy();
            wallTarget = null;
            var map = GetBattlefield();
            if (map == null || _board == null)
                return;

            int range = GetCombatAttackRange();

            // Player-ordered rock: only if this unit can actually act on it
            // (not ambushing; water-rooted must already be in range).
            if (Team == Team.Player
                && map.PlayerOrderedWall.HasValue
                && CanAcceptWallSmashOrder(map.PlayerOrderedWall.Value))
            {
                wallTarget = map.PlayerOrderedWall.Value;
                return;
            }

            // Holding rock cover: only engage enemies already inside attack range.
            // Do not auto-walk out or auto-smash the covering rock.
            if (IsHoldingCover)
            {
                unitTarget = FindNearestEnemyInAttackRange(range);
                wallTarget = null;
                return;
            }

            // If an enemy is in range and not fully covered, prefer the enemy.
            if (unitTarget != null && unitTarget.IsAlive)
            {
                float d = _board.Chebyshev(Cell, unitTarget.Cell);
                bool covered = map.HasCoverBetween(Cell, unitTarget.Cell);
                if (d <= range + 0.01f && !covered)
                    return;

                // Covered: only smash walls we are allowed to auto-target (not own-side unless ordered).
                if (covered
                    && map.TryFindCoverWallToward(Cell, unitTarget.Cell, range, Team, out var coverWall))
                {
                    wallTarget = coverWall;
                    return;
                }

                if (d > range + 0.01f
                    && map.TryFindWallInRange(Cell, range, Team, out var nearWall)
                    && map.HasCoverBetween(Cell, unitTarget.Cell))
                {
                    wallTarget = nearWall;
                    return;
                }

                return;
            }

            // No living enemy — smash non-own walls in range (or ordered own wall).
            if (map.TryFindWallInRange(Cell, range, Team, out var anyWall))
                wallTarget = anyWall;
        }

        SpiritUnit FindNearestEnemyInAttackRange(int range)
        {
            if (_board == null) return null;

            SpiritUnit best = null;
            float bestDist = float.MaxValue;
            foreach (var other in _board.AllUnits())
            {
                if (other == null || !other.IsAlive || other.Team == Team)
                    continue;
                if (other.IsAmbushing)
                    continue;

                float d = _board.Chebyshev(Cell, other.Cell);
                if (d > range + 0.01f)
                    continue;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = other;
                }
            }

            return best;
        }

        void PerformAttackCycle()
        {
            if (_target == null || !_target.IsAlive)
                return;

            // Unit active skills (including heal) are temporarily disabled — basic attacks only.
            QueueAttackPayload(_target, fromSkill: false);

            float gain = Definition.EnergyPerAttack;
            if (Team == Team.Player && _bonds != null && Definition != null)
                gain *= 1f + _bonds.GetBonusesFor(Definition.Element).Energy;
            Energy = Mathf.Min(MaxEnergy, Energy + gain);
        }

        void PerformWallAttackCycle(Vector2Int wallCell)
        {
            var map = GetBattlefield();
            if (map == null || !map.HasStandingWall(wallCell))
            {
                _wallTargetCell = null;
                if (_forcedWallTarget.HasValue && _forcedWallTarget.Value == wallCell)
                    _forcedWallTarget = null;
                return;
            }

            PlayAttackAnimationToward(_board.CellToWorld(wallCell));

            float dmg = Mathf.Max(1f, GetEffectiveAtk());
            // Ambush opener can also dump into a wall if that is the first swing.
            if (ConsumeAmbushMultiplier(out float ambushMul))
                dmg *= ambushMul;

            bool destroyed = map.DamageWall(wallCell, dmg, this);
            SpawnFloatText(
                _board.CellToWorld(wallCell) + Vector3.up * (_board.CellSize * 0.6f),
                destroyed ? "破墙!" : $"-{dmg:0}",
                destroyed ? new Color(1f, 0.85f, 0.3f) : new Color(0.85f, 0.8f, 0.7f));

            float gain = Definition.EnergyPerAttack * 0.5f;
            if (Team == Team.Player && _bonds != null && Definition != null)
                gain *= 1f + _bonds.GetBonusesFor(Definition.Element).Energy;
            Energy = Mathf.Min(MaxEnergy, Energy + gain);

            if (destroyed)
            {
                _wallTargetCell = null;
                if (_forcedWallTarget.HasValue && _forcedWallTarget.Value == wallCell)
                    _forcedWallTarget = null;
            }
        }

        bool ConsumeAmbushMultiplier(out float mul)
        {
            mul = 1f;
            if (!_grassAmbushStrikeReady) return false;
            _grassAmbushStrikeReady = false;
            mul = BattlefieldMap.GrassAmbushDamageMul;
            return true;
        }

        void DealBasicAttack(SpiritUnit target)
        {
            if (target == null || !target.IsAlive || target.IsAmbushing) return;
            // Bond AttackBonus already baked into Atk during Init — do not re-apply here.
            float elementMul = DamageCalculator.GetElementMultiplier(
                Definition.Element, target.Definition.Element);
            float dmg = DamageCalculator.ComputeDamage(
                GetEffectiveAtk(), 1f,
                0f,
                elementMul,
                target.GetEffectiveDef());

            if (ConsumeAmbushMultiplier(out float ambushMul))
            {
                dmg *= ambushMul;
                SpawnFloatText(GetDamageTextPosition(), "奇袭×2", new Color(0.45f, 1f, 0.4f));
                BattlefieldVfxLibrary.SpawnAmbushText(
                    transform.position + Vector3.up * (_board != null ? _board.CellSize * 0.4f : 0.08f),
                    "奇袭×2",
                    _board != null ? _board.CellSize : 0.24f);
            }

            // Wall cover: LOS through a standing wall reduces damage (separate from behind-rock stance).
            var map = GetBattlefield();
            if (map != null && map.HasCoverBetween(Cell, target.Cell))
                dmg *= BattlefieldMap.WallCoverDamageMul;

            // Fire bond burn: only fire spirits with fire×4 tier.
            if (Team == Team.Player && _bonds != null && Definition != null
                && _bonds.GetBonusesFor(Definition.Element).FireBurn)
                target.AddBurn(2f, GetEffectiveAtk() * 0.15f);

            target.TakeDamage(dmg, this, false);
            _master?.OnAllyCombatEvent(Team, 3f);
        }

        void PlayAttackAnimationToward(Vector3 worldPos)
        {
            Vector3 direction = worldPos - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                direction = DefaultTeamForward();
            direction.Normalize();

            bool melee = GetTargetingMode() != TargetingMode.RangedNearest;
            FaceTowardImmediate(direction);
            _attackFacingTarget = true;

            _attackDirectionLocal = transform.InverseTransformDirection(direction);
            _attackDirectionLocal.y = 0f;
            if (_attackDirectionLocal.sqrMagnitude > 0.0001f)
                _attackDirectionLocal.Normalize();

            _attackAnimationTime = 0f;
            _attackAnimationDuration = melee ? 0.36f : 0.28f;
            _attackLungeDistance = _board != null
                ? Mathf.Clamp(_board.CellSize * (melee ? 0.08f : 0.03f), 0.004f, 0.02f)
                : (melee ? 0.012f : 0.006f);
        }

        float GetEffectiveAtk()
        {
            float mul = 1f;
            for (int i = 0; i < _statuses.Count; i++)
            {
                float m = _statuses[i].AtkMul;
                if (m > 0f && !Mathf.Approximately(m, 1f))
                    mul *= m;
            }

            // Water root: permanent ATK buff for the whole fight.
            if (_waterRooted)
                mul *= BattlefieldMap.WaterRootAtkMul;

            return Atk * mul;
        }

        /// <summary>DEF after rock-behind bonus (higher DEF = take less damage while in cover).</summary>
        public float GetEffectiveDef()
        {
            float mul = 1f;
            // Defense bonus while physically behind a standing rock (even after leave-cover order,
            // until the unit actually steps away from the cover cell).
            var map = GetBattlefield();
            if (map != null && map.IsStandingBehindWall(Cell, Team))
                mul *= BattlefieldMap.WallBehindDefMul;
            return Def * mul;
        }

        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f) return;
            float before = CurrentHp;
            CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
            float gained = CurrentHp - before;
            if (gained <= 0.01f) return;
            UpdateHpBar();
            SpawnFloatText(GetDamageTextPosition(), $"+{gained:0}", new Color(0.35f, 1f, 0.55f));
        }

        public void AddAtkBuff(float mul, float duration)
        {
            if (!IsAlive || duration <= 0f) return;
            mul = Mathf.Max(0.01f, mul);
            _statuses.Add(new StatusEffect
            {
                Id = "xinxiang_atk_buff",
                Remaining = duration,
                AtkMul = mul
            });
            SpawnFloatText(GetDamageTextPosition(), $"攻×{mul:0.00}", new Color(1f, 0.85f, 0.3f));
        }

        void QueueAttackPayload(SpiritUnit target, bool fromSkill)
        {
            if (target == null || !target.IsAlive)
                return;
            // Cannot attack grass units still hidden in ambush.
            if (target.IsAmbushing)
                return;

            PlayAttackAnimation(target);

            if (GetTargetingMode() == TargetingMode.RangedNearest)
            {
                int hits = fromSkill && Definition.ActiveSkillKind != SkillKind.Control
                    ? Mathf.Max(1, Definition.SkillHits)
                    : 1;
                for (int i = 0; i < hits; i++)
                    LaunchEnergyProjectile(target, fromSkill, i * 0.055f);

                if (fromSkill)
                {
                    SpawnFloatText(transform.position + Vector3.up * 1.8f, Definition.ActiveSkillName, Color.yellow);
                    _master?.OnAllyCombatEvent(Team, 5f);
                }
                return;
            }

            _pendingMeleeAttack = true;
            _pendingMeleeSkill = fromSkill;
            _pendingMeleeHits = fromSkill && Definition.ActiveSkillKind != SkillKind.Control
                ? Mathf.Max(1, Definition.SkillHits)
                : 1;
            _pendingMeleeTarget = target;
            // Spawn claw immediately so the melee tell is never lost if the swing is interrupted.
            SpawnClawSlash(target);
        }

        void ResolvePendingMeleeAttack()
        {
            if (!_pendingMeleeAttack)
                return;

            bool fromSkill = _pendingMeleeSkill;
            int hits = Mathf.Max(1, _pendingMeleeHits);
            SpiritUnit target = _pendingMeleeTarget;
            _pendingMeleeAttack = false;
            _pendingMeleeSkill = false;
            _pendingMeleeHits = 0;
            _pendingMeleeTarget = null;

            if (target == null || !target.IsAlive || target.IsAmbushing)
                return;

            if (fromSkill)
            {
                for (int i = 0; i < hits; i++)
                    ApplySkillHit(target);
                SpawnFloatText(transform.position + Vector3.up * 1.8f, Definition.ActiveSkillName, Color.yellow);
                _master?.OnAllyCombatEvent(Team, 5f);
            }
            else
            {
                DealBasicAttack(target);
            }
        }

        void SpawnClawSlash(SpiritUnit target)
        {
            if (target == null || Definition == null) return;

            float cellSize = _board != null ? _board.CellSize : 0.24f;
            Vector3 from = transform.position + Vector3.up * (cellSize * 0.42f);
            Vector3 to = target.transform.position + Vector3.up * (cellSize * 0.42f);
            AttackVfxLibrary.SpawnMeleeHit(Definition.Element, from, to, cellSize, Team);
        }

        void ApplySkillHit(SpiritUnit target)
        {
            if (target == null || !target.IsAlive || target.IsAmbushing)
                return;

            float dmg = DamageCalculator.ComputeDamage(
                GetEffectiveAtk(), Definition.SkillMultiplier, 0f,
                DamageCalculator.GetElementMultiplier(Definition.Element, target.Definition.Element),
                target.GetEffectiveDef());

            if (ConsumeAmbushMultiplier(out float ambushMul))
                dmg *= ambushMul;

            var map = GetBattlefield();
            if (map != null && map.HasCoverBetween(Cell, target.Cell))
                dmg *= BattlefieldMap.WallCoverDamageMul;

            if (Team == Team.Player && _bonds != null && Definition != null
                && _bonds.GetBonusesFor(Definition.Element).FireBurn)
                target.AddBurn(2f, GetEffectiveAtk() * 0.15f);

            target.TakeDamage(dmg, this, true);
            if (Definition.ActiveSkillKind == SkillKind.Control && target.IsAlive)
            {
                target.AddStun(Definition.SkillDuration);
                SpawnFloatText(target.GetDamageTextPosition(), "眩晕", Color.cyan);
            }
        }

        public void ApplyProjectileHit(SpiritUnit target, bool fromSkill)
        {
            if (!IsAlive || target == null || !target.IsAlive || target.Team == Team)
                return;
            // Projectile must not reveal or damage a still-hidden grass ambusher.
            if (target.IsAmbushing)
                return;

            if (fromSkill)
                ApplySkillHit(target);
            else
                DealBasicAttack(target);
        }

void LaunchEnergyProjectile(SpiritUnit target, bool fromSkill, float delay)
        {
            if (Definition == null || target == null) return;

            Vector3 start = GetProjectileStartPosition(target);
            float size = _board != null
                ? Mathf.Clamp(_board.CellSize * 0.22f, 0.02f, 0.045f)
                : 0.028f;

            var projectileObject = AttackVfxLibrary.SpawnRangedProjectileVisual(
                Definition.Element, start, size);
            projectileObject.name = "EnergyProjectile";

            var projectile = projectileObject.GetComponent<SpiritProjectile>();
            if (projectile == null)
                projectile = projectileObject.AddComponent<SpiritProjectile>();

            float distance = Vector3.Distance(start, target.transform.position);
            float cellSize = _board != null ? _board.CellSize : 0.24f;
            projectile.Initialize(
                this,
                target,
                fromSkill,
                Mathf.Clamp(distance / 0.45f, 0.12f, 0.55f),
                delay,
                Definition.Element,
                cellSize);
        }

        Vector3 GetProjectileStartPosition(SpiritUnit target)
        {
            Vector3 direction = target != null
                ? target.transform.position - transform.position
                : transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                direction = transform.forward;
            direction.Normalize();

            float cellSize = _board != null ? _board.CellSize : 0.24f;
            return transform.position
                + Vector3.up * (cellSize * 0.38f)
                + direction * (cellSize * 0.18f);
        }

        Vector3 GetDamageTextPosition()
        {
            Vector3 anchor = GetOverheadWorldAnchor();
            Vector3 right = Camera.main != null ? Camera.main.transform.right : Vector3.right;
            float offset = _board != null ? _board.CellSize * 0.48f : 0.06f;
            return anchor + right * offset;
        }


        public void TakeDamage(float amount, SpiritUnit source, bool fromSkill)
        {
            if (!IsAlive) return;

            // Ambushing grass units cannot be hit by anyone until they spring the trap
            // (other spirits, AoE/skills with null source, etc.). Self-damage still applies.
            if (IsAmbushing && source != this)
            {
                SpawnFloatText(GetDamageTextPosition(), "隐匿", new Color(0.55f, 0.95f, 0.55f));
                return;
            }

            // Grass bond damage reduce: only grass spirits with grass×4 tier.
            if (Team == Team.Player && _bonds != null && Definition != null
                && _bonds.GetBonusesFor(Definition.Element).GrassDamageReduce
                && Random.value < 0.2f)
            {
                amount *= 0.5f;
                SpawnFloatText(GetDamageTextPosition(), "减伤", Color.white);
            }

            CurrentHp = Mathf.Max(0f, CurrentHp - amount);
            UpdateHpBar();
            SpawnFloatText(GetDamageTextPosition(), $"-{amount:0}", fromSkill ? new Color(1f, 0.6f, 0.2f) : Color.white);
            FlashHit();
            GameAudio.I?.PlayHit();

            _master?.OnAllyCombatEvent(Team, 2f);

            if (CurrentHp <= 0f)
                Die();
        }

        void Die()
        {
            _board.Clear(Cell);
            _combatEnabled = false;
            gameObject.SetActive(false);
            _battle?.NotifyUnitDied(this);
        }

void TryMoveToward(Vector2Int targetCell)
        {
            // Water spirits that opened on a river never leave that tile.
            if (_waterRooted) return;
            // Rock-cover holders stay put until the player orders leave-cover / smash.
            if (IsHoldingCover) return;

            targetCell = new Vector2Int(
                Mathf.Clamp(targetCell.x, 0, BoardGrid.Size - 1),
                Mathf.Clamp(targetCell.y, 0, BoardGrid.Size - 1));

            if (_board == null || Cell == targetCell)
                return;

            Vector2Int best = Cell;
            int bestScore = int.MaxValue;
            int bestTie = int.MaxValue;

            // Try every adjacent cell. The old path step could choose a blocked
            // cell first and then abort without considering the other seven.
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    Vector2Int candidate = new Vector2Int(Cell.x + dx, Cell.y + dy);
                    if (!CanOccupyCell(candidate))
                        continue;

                    int score = Mathf.Abs(candidate.x - targetCell.x)
                        + Mathf.Abs(candidate.y - targetCell.y);
                    int tie = Mathf.RoundToInt(_board.Chebyshev(candidate, targetCell));
                    if (score < bestScore || (score == bestScore && tie < bestTie))
                    {
                        best = candidate;
                        bestScore = score;
                        bestTie = tie;
                    }
                }
            }

            if (best == Cell)
                return;

            _board.Clear(Cell);
            Cell = best;
            _board.Set(Cell, this);
            SnapToCell(Cell, false);

            if (Definition == null || !Definition.SkipWalkBob)
            {
                _walkAnimationRemaining = Mathf.Max(_walkAnimationRemaining, 0.26f);
                _walkAnimationTime = 0f;
            }
        }

bool TryMoveAwayFrom(Vector2Int targetCell, int desiredRange)
        {
            if (_waterRooted) return false;
            if (IsHoldingCover) return false;

            int currentDistance = Mathf.RoundToInt(_board.Chebyshev(Cell, targetCell));
            Vector2Int best = Cell;
            int bestDistance = currentDistance;
            int bestScore = int.MaxValue;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    var candidate = new Vector2Int(Cell.x + dx, Cell.y + dy);
                    if (!CanOccupyCell(candidate)) continue;

                    int candidateDistance = Mathf.RoundToInt(_board.Chebyshev(candidate, targetCell));
                    if (candidateDistance <= currentDistance || candidateDistance > desiredRange) continue;

                    // Prefer landing exactly on max attack range.
                    int score = Mathf.Abs(candidateDistance - desiredRange);
                    if (candidateDistance > bestDistance
                        || (candidateDistance == bestDistance && score < bestScore))
                    {
                        best = candidate;
                        bestDistance = candidateDistance;
                        bestScore = score;
                    }
                }
            }

            if (best == Cell) return false;

            ApplyCellMove(best);
            return true;
        }

        bool TryMoveTowardRange(Vector2Int targetCell, int desiredRange)
        {
            if (_waterRooted) return false;
            if (IsHoldingCover) return false;
            if (_board == null || Cell == targetCell)
                return false;

            int currentDistance = Mathf.RoundToInt(_board.Chebyshev(Cell, targetCell));
            Vector2Int best = Cell;
            int bestScore = int.MaxValue;
            int bestDistance = currentDistance;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    var candidate = new Vector2Int(Cell.x + dx, Cell.y + dy);
                    if (!CanOccupyCell(candidate))
                        continue;

                    int candidateDistance = Mathf.RoundToInt(_board.Chebyshev(candidate, targetCell));
                    // Never step closer than max attack range while closing in.
                    if (candidateDistance < desiredRange)
                        continue;

                    int score = Mathf.Abs(candidateDistance - desiredRange);
                    if (candidateDistance > currentDistance)
                        score += 8;

                    if (score < bestScore
                        || (score == bestScore && candidateDistance < bestDistance))
                    {
                        best = candidate;
                        bestScore = score;
                        bestDistance = candidateDistance;
                    }
                }
            }

            if (best == Cell)
                return false;

            ApplyCellMove(best);
            return true;
        }

        void ApplyCellMove(Vector2Int next)
        {
            _board.Clear(Cell);
            Cell = next;
            _board.Set(Cell, this);
            SnapToCell(Cell, false);

            if (Definition == null || !Definition.SkipWalkBob)
            {
                _walkAnimationRemaining = Mathf.Max(_walkAnimationRemaining, 0.26f);
                _walkAnimationTime = 0f;
            }
        }

Bounds GetBodyRenderBounds()
        {
            if (_bodyTransform == null)
                return new Bounds(transform.position, Vector3.zero);

            var renderers = _bodyTransform.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(transform.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        /// <summary>
        /// World point on the visible mesh (not the unit root). Concert cells are ~1cm,
        /// so a hardcoded +0.06m lift floats VFX several body-heights above the sprite.
        /// </summary>
        public Vector3 GetVfxWorldPosition()
        {
            if (_bodyTransform != null)
            {
                var renderers = _bodyTransform.GetComponentsInChildren<Renderer>(true);
                bool any = false;
                Bounds bounds = default;
                for (int i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                        continue;
                    if (SpiritModelFactory.IsHelperRenderer(renderer))
                        continue;
                    if (!any)
                    {
                        bounds = renderer.bounds;
                        any = true;
                    }
                    else
                        bounds.Encapsulate(renderer.bounds);
                }

                if (any && bounds.size.sqrMagnitude > 1e-12f)
                    return bounds.center;
            }

            float lift = 0.008f;
            if (_board != null && _board.CellSize > 1e-5f)
                lift = _board.CellSize * 0.45f;
            return transform.position + Vector3.up * lift;
        }

        Bounds GetBodyBoundsAtCell(Vector2Int cell)
        {
            Bounds bounds = GetBodyRenderBounds();
            bounds.center += _board.CellToWorld(cell) - transform.position;
            return bounds;
        }

        bool CanOccupyCell(Vector2Int cell)
        {
            // Grid occupancy is authoritative. Standing walls block movement.
            // Visual anti-clip is handled in LateUpdate via collider penetration.
            if (_board == null || !_board.InBounds(cell) || !_board.IsEmpty(cell))
                return false;
            if (_board.Battlefield != null && _board.Battlefield.BlocksMovement(cell))
                return false;
            return true;
        }

        bool IsPhysicallyNearTarget(SpiritUnit target)
        {
            if (target == null || _board == null)
                return false;

            // Melee can strike from Chebyshev distance 1 (adjacent / diagonal).
            return _board.Chebyshev(Cell, target.Cell) <= 1.01f;
        }


        public void SnapToCell(Vector2Int cell, bool instant)
        {
            cell = new Vector2Int(
                Mathf.Clamp(cell.x, 0, BoardGrid.Size - 1),
                Mathf.Clamp(cell.y, 0, BoardGrid.Size - 1));

            Cell = cell;
            var world = _board.CellToWorld(cell);
            if (instant) transform.position = world;
            else transform.position = Vector3.Lerp(transform.position, world, 1f);
            transform.position = world;

            // Keep mesh centered on the logical cell (flying models had large authored XZ offsets).
            if (_bodyTransform != null && IsFlyingModel())
                AlignFlyingModelToCellHorizontal(_bodyTransform);

            // During formation, never shove the root off the cell — grid ↔ spirit must match.
            // Also refresh team facing so a rotated/scaled grid turns the models with it.
            if (_battle != null && _battle.Phase == GamePhase.Formation)
            {
                ApplyDefaultFormationFacing();
                return;
            }

            KeepBodyInsideBoard();
        }

        Vector3 DefaultTeamForward()
        {
            if (_board != null)
                return _board.TeamForward(Team);
            return Team == Team.Player ? Vector3.forward : Vector3.back;
        }

void UpdateMovementAnimation(float dt)
        {
            if (_bodyTransform == null) return;

            if (_walkAnimationRemaining > 0f)
            {
                _walkAnimationRemaining -= dt;
                _walkAnimationTime += dt;
                float phase = _walkAnimationTime * 18f;
                float bob = Mathf.Abs(Mathf.Sin(phase)) * (_board != null ? _board.CellSize * 0.08f : 0.008f);
                float sway = Mathf.Sin(phase) * 4f;
                _bodyTransform.localPosition = _bodyBaseLocalPosition + Vector3.up * bob;
                _bodyTransform.localRotation = _bodyBaseLocalRotation * Quaternion.Euler(0f, 0f, sway);

                for (int i = 0; i < _tailRoots.Count; i++)
                    _tailRoots[i].localRotation = _tailBaseRotations[i];
                return;
            }

            _idleAnimationTime += dt;
            GetIdleAnimationProfile(
                out float bodyFrequency,
                out float bobAmplitude,
                out float swayAmplitude,
                out float tailFrequency,
                out float tailAmplitude,
                out float phaseOffset);

            float idlePhase = _idleAnimationTime * (Mathf.PI * 2f * bodyFrequency) + phaseOffset;
            float idleBob = Mathf.Sin(idlePhase) * (_board != null ? _board.CellSize * bobAmplitude : bobAmplitude * 0.12f);
            float idleSway = Mathf.Sin(idlePhase * 0.79f + 0.4f) * swayAmplitude;
            _bodyTransform.localPosition = _bodyBaseLocalPosition + Vector3.up * idleBob;
            _bodyTransform.localRotation = _bodyBaseLocalRotation * Quaternion.Euler(0f, 0f, idleSway);

            for (int i = 0; i < _tailRoots.Count; i++)
            {
                float segment = _tailRoots.Count <= 1 ? 1f : (float)i / (_tailRoots.Count - 1);
                float tailPhase = _idleAnimationTime * (Mathf.PI * 2f * tailFrequency)
                    + phaseOffset + i * 0.09f;
                float wagAngle = Mathf.Sin(tailPhase)
                    * Mathf.Lerp(tailAmplitude * 0.45f, tailAmplitude, segment);
                _tailRoots[i].localRotation =
                    _tailBaseRotations[i] * Quaternion.Euler(0f, wagAngle, 0f);
            }
        }

void GetIdleAnimationProfile(
            out float bodyFrequency,
            out float bobAmplitude,
            out float swayAmplitude,
            out float tailFrequency,
            out float tailAmplitude,
            out float phaseOffset)
        {
            var profile = IdleAnimProfile.Default;
            if (Definition != null)
            {
                Definition.EnsurePresentation();
                profile = Definition.IdleProfile;
            }

            bodyFrequency = profile.BodyFrequency;
            bobAmplitude = profile.BobAmplitude;
            swayAmplitude = profile.SwayAmplitude;
            tailFrequency = profile.TailFrequency;
            tailAmplitude = profile.TailAmplitude;
            phaseOffset = profile.PhaseOffset;
        }



        void KeepBodyInsideBoard()
        {
            // Flying models stay anchored to their logical grid cell on X/Z.
            if (IsFlyingModel())
                return;
            if (_board == null || _bodyTransform == null) return;

            var renderers = _bodyTransform.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Bounds bodyBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bodyBounds.Encapsulate(renderers[i].bounds);

            float halfCellX = _board.CellSize * Mathf.Abs(_board.transform.lossyScale.x) * 0.5f;
            float halfCellZ = _board.CellSize * Mathf.Abs(_board.transform.lossyScale.z) * 0.5f;
            Vector3 cell00 = _board.CellToWorld(0, 0);
            Vector3 cell70 = _board.CellToWorld(BoardGrid.Size - 1, 0);
            Vector3 cell07 = _board.CellToWorld(0, BoardGrid.Size - 1);
            float boardMinX = Mathf.Min(cell00.x, cell70.x) - halfCellX;
            float boardMaxX = Mathf.Max(cell00.x, cell70.x) + halfCellX;
            float boardMinZ = Mathf.Min(cell00.z, cell07.z) - halfCellZ;
            float boardMaxZ = Mathf.Max(cell00.z, cell07.z) + halfCellZ;

            float correctionX = 0f;
            float correctionZ = 0f;
            if (bodyBounds.min.x < boardMinX) correctionX = boardMinX - bodyBounds.min.x;
            if (bodyBounds.max.x > boardMaxX) correctionX = boardMaxX - bodyBounds.max.x;
            if (bodyBounds.min.z < boardMinZ) correctionZ = boardMinZ - bodyBounds.min.z;
            if (bodyBounds.max.z > boardMaxZ) correctionZ = boardMaxZ - bodyBounds.max.z;

            transform.position += new Vector3(correctionX, 0f, correctionZ);
        }


        int GetCombatAttackRange()
        {
            if (Definition == null) return 1;
            Definition.EnsurePresentation();
            return Definition.CombatRangeCells;
        }

        TargetingMode GetTargetingMode()
        {
            if (Definition == null) return TargetingMode.Nearest;
            Definition.EnsurePresentation();
            return Definition.Targeting;
        }

        float GetCombatMoveInterval()
        {
            float interval = 0.28f / Mathf.Max(0.5f, Definition.MoveSpeed / 3.5f);
            float mul = Definition != null && Definition.MoveIntervalMultiplier > 0f
                ? Definition.MoveIntervalMultiplier
                : 1f;
            return interval * mul;
        }


        /// <summary>
        /// When only grass-type allies remain (or the board has no targetable enemies left
        /// because everyone is still ambushing), break stealth and fight so the match can end.
        /// </summary>
        bool ShouldForceGrassAmbushSortie()
        {
            if (_board == null) return false;

            bool anyAlly = false;
            bool anyNonGrassAlly = false;
            bool anyEnemy = false;
            bool anyTargetableEnemy = false;

            foreach (var u in _board.AllUnits())
            {
                if (u == null || !u.IsAlive) continue;

                if (u.Team == Team)
                {
                    anyAlly = true;
                    if (u.Definition != null && u.Definition.Element != ElementType.Grass)
                        anyNonGrassAlly = true;
                }
                else
                {
                    anyEnemy = true;
                    if (!u.IsAmbushing)
                        anyTargetableEnemy = true;
                }
            }

            // "只剩下草" — every living ally is grass; they must sortie.
            if (anyAlly && !anyNonGrassAlly)
                return true;

            // Mutual ambush stalemate: enemies exist but none are targetable.
            if (anyEnemy && !anyTargetableEnemy)
                return true;

            return false;
        }

        SpiritUnit FindNearestEnemy()
        {
            SpiritUnit best = null;
            float bestDist = float.MaxValue;
            int bestPriority = int.MaxValue;
            TargetingMode mode = GetTargetingMode();
            foreach (var u in _board.AllUnits())
            {
                if (u == null || !u.IsAlive || u.Team == Team) continue;
                // Hidden grass ambushers are invisible to enemy targeting.
                if (u.IsAmbushing) continue;
                float d = _board.Chebyshev(Cell, u.Cell);
                int priority = mode == TargetingMode.RangedNearest
                    ? (d <= GetCombatAttackRange() ? 0 : 1)
                    : 0;

                if (priority < bestPriority || (priority == bestPriority && d < bestDist))
                {
                    bestPriority = priority;
                    bestDist = d;
                    best = u;
                }
            }
            return best;
        }

        public void AddStun(float duration)
        {
            _statuses.Add(new StatusEffect { Id = "stun", Remaining = duration, Stun = true });
        }

        public void AddBurn(float duration, float dps)
        {
            _statuses.Add(new StatusEffect { Id = "burn", Remaining = duration, Burn = true, BurnDps = dps });
        }

        bool IsStunned()
        {
            for (int i = 0; i < _statuses.Count; i++)
                if (_statuses[i].Stun) return true;
            return false;
        }

        void TickStatuses(float dt)
        {
            for (int i = _statuses.Count - 1; i >= 0; i--)
            {
                var s = _statuses[i];
                if (s.Burn)
                    CurrentHp = Mathf.Max(0f, CurrentHp - s.BurnDps * dt);
                if (!s.Tick(dt))
                    _statuses.RemoveAt(i);
            }
            UpdateHpBar();
            if (CurrentHp <= 0f) Die();
        }


        void FlashHit()
        {
            if (_bodyRenderer == null) return;
            // Brief bright flash; never restore to Definition.Color (that is UI/fallback tint only).
            SpiritModelFactory.SetRendererColor(_bodyRenderer, new Color(2f, 2f, 2f, 1f));
            CancelInvoke(nameof(RestoreColor));
            Invoke(nameof(RestoreColor), 0.08f);
        }

        void RestoreColor()
        {
            if (_bodyRenderer == null) return;
            // Textured models must stay white so albedo shows correctly.
            // Capsule fallback (no mainTexture) can keep the roster Color.
            var mat = _bodyRenderer.material;
            bool hasTexture = mat != null && (mat.mainTexture != null
                || (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null));
            SpiritModelFactory.SetRendererColor(_bodyRenderer, hasTexture || Definition == null
                ? Color.white
                : Definition.Color);
        }

        void ResetBodyMaterialTint()
        {
            if (_bodyTransform == null) return;
            foreach (var renderer in _bodyTransform.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.material == null) continue;
                var mat = renderer.material;
                bool hasTexture = mat.mainTexture != null
                    || (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null);
                if (hasTexture)
                    SpiritModelFactory.SetRendererColor(renderer, Color.white);
            }
        }

    
bool IsFlyingModel()
        {
            if (Definition == null) return false;
            Definition.EnsurePresentation();
            return Definition.IsFlying;
        }


void AlignFlyingModelToCellHorizontal(Transform model)
        {
            if (!IsFlyingModel() || model == null)
                return;

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            Vector3 offset = bounds.center - transform.position;
            model.position -= new Vector3(offset.x, 0f, offset.z);
        }


        void BindExistingVisual()
        {
            _bodyTransform = transform.Find("Body");
            if (_bodyTransform == null)
                return;

            SpiritModelFactory.HideHelperRenderers(_bodyTransform.gameObject);
            var renderers = _bodyTransform.GetComponentsInChildren<Renderer>(true);
            _bodyRenderer = renderers.Length > 0 ? renderers[0] : null;
            _bodyCollider = GetComponent<BoxCollider>();

            // Scene units may retain a previous Definition.Color tint — clear it and reapply albedo.
            SpiritModelFactory.ApplySpiritTexture(renderers, GetSpiritModelName());
            ResetBodyMaterialTint();

            // Strip legacy world-space TextMesh/Cube HUD so Scene view stays clean.
            DestroyWorldHudLeftovers();

            float cellSize = _board != null ? _board.CellSize : 0.24f;
            if (_bodyTransform != null)
            {
                float height = FitModelToCell(_bodyTransform, cellSize);
                _bodyBaseLocalPosition = _bodyTransform.localPosition;
                _bodyBaseLocalRotation = _bodyTransform.localRotation;
                _hudAnchorHeight = height + cellSize * 0.16f;
            }
            else
            {
                _bodyBaseLocalPosition = Vector3.zero;
                _bodyBaseLocalRotation = Quaternion.identity;
                _hudAnchorHeight = cellSize * 0.9f;
            }

            EnsureScreenHud();
            _wingAnimationEnabled = false;
            SetupWingAnimation(_bodyTransform);
            RefreshLabel();
            UpdateHpBar();
        }


        void OnEnable()
        {
            if (_hudRoot != null)
                _hudRoot.gameObject.SetActive(true);
        }

        void OnDisable()
        {
            if (_hudRoot != null)
                _hudRoot.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_hudRoot != null)
                Destroy(_hudRoot.gameObject);
        }
    }
}
