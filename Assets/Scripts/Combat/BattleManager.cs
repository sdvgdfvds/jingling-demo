using System.Collections.Generic;
using UnityEngine;

namespace NexusAnima
{
    public class BattleManager : MonoBehaviour
    {
        public GamePhase Phase { get; private set; } = GamePhase.Formation;
        public BoardGrid Board { get; private set; }

        public IReadOnlyList<SpiritUnit> GetSpawnedUnits() => _spawned;
        public BondState PlayerBonds { get; private set; } = new BondState();
        public FateMasterController FateMaster { get; private set; }
        public XinxiangSkillController Xinxiang { get; private set; }
        public string ResultText { get; private set; } = "";
        public string LastEvent { get; set; } = "从精灵池选一只，点蓝区放我方、点红区放敌方";

        /// <summary>
        /// When true, units stop moving/attacking and projectiles freeze in place
        /// (used during Xinxiang cast presentation).
        /// </summary>
        public bool CombatFrozen { get; private set; }

        readonly List<SpiritUnit> _spawned = new List<SpiritUnit>();

        readonly Dictionary<string, Vector2Int> _enemyPlacement = new Dictionary<string, Vector2Int>();
        readonly Dictionary<string, Vector2Int> _playerPlacement = new Dictionary<string, Vector2Int>();
        List<SpiritDefinition> _roster;
        List<SpiritDefinition> _enemyRoster;
        List<SpiritDefinition> _spiritPool;
        FateMasterDefinition _masterDef;
        DemoUI _ui;
        SpiritUnit _spiritPrefab;
        SpiritProjectile _projectilePrefab;

        public SpiritProjectile ProjectilePrefab => _projectilePrefab;

        public void SetSpiritPrefab(SpiritUnit prefab) => _spiritPrefab = prefab;
        public void SetProjectilePrefab(SpiritProjectile prefab) => _projectilePrefab = prefab;

        public IReadOnlyDictionary<string, Vector2Int> PlayerPlacement => _playerPlacement;

        public IReadOnlyList<SpiritDefinition> EnemyRoster => _enemyRoster;
        public IReadOnlyDictionary<string, Vector2Int> EnemyPlacement => _enemyPlacement;
        public IReadOnlyList<SpiritDefinition> Roster => _roster;
        /// <summary>Unified formation pool — place onto either side by board zone.</summary>
        public IReadOnlyList<SpiritDefinition> SpiritPool => _spiritPool;

        public void SetXinxiang(XinxiangSkillController xinxiang) => Xinxiang = xinxiang;

        public void SetCombatFrozen(bool frozen)
        {
            CombatFrozen = frozen;
        }

        public void Setup(
            BoardGrid board,
            FateMasterController master,
            List<SpiritDefinition> roster,
            List<SpiritDefinition> enemies,
            FateMasterDefinition masterDef,
            DemoUI ui)
        {
            Board = board;
            FateMaster = master;
            _roster = roster ?? new List<SpiritDefinition>();
            _enemyRoster = enemies ?? new List<SpiritDefinition>();
            _spiritPool = BuildSpiritPool(_roster, _enemyRoster);
            _masterDef = masterDef;
            _ui = ui;

            EnsureBattlefieldMap();

            _playerPlacement.Clear();
            RestoreDefaultEnemyPlacement();

            Phase = GamePhase.Formation;
            RefreshBondsPreview();
        }

        void EnsureBattlefieldMap()
        {
            if (Board == null) return;

            var map = Board.GetComponent<BattlefieldMap>();
            if (map == null)
                map = Board.gameObject.AddComponent<BattlefieldMap>();
            map.Bind(Board, this);
            Board.SetBattlefield(map);
            map.EnsureBuilt();
        }

        public BattlefieldMap GetBattlefield() => Board != null ? Board.Battlefield : null;

        public void NotifyTerrainEvent()
        {
            _ui?.RefreshAll();
        }

        static List<SpiritDefinition> BuildSpiritPool(
            List<SpiritDefinition> roster,
            List<SpiritDefinition> enemies)
        {
            var pool = new List<SpiritDefinition>();
            var seen = new HashSet<string>();
            // Prefer the player catalog as the single selectable pool.
            if (roster != null)
            {
                foreach (var def in roster)
                {
                    if (def == null || string.IsNullOrEmpty(def.Id) || !seen.Add(def.Id))
                        continue;
                    pool.Add(def);
                }
            }

            // Keep any enemy-only defs available for lookups / legacy saves,
            // but do not duplicate near-identical "敌·" cards in the UI pool.
            if (enemies != null)
            {
                foreach (var def in enemies)
                {
                    if (def == null || string.IsNullOrEmpty(def.Id) || seen.Contains(def.Id))
                        continue;
                    // Skip enemy clones that mirror a pool spirit by display name.
                    string bare = StripEnemyPrefix(def.DisplayName);
                    bool mirrored = pool.Exists(p => p != null && p.DisplayName == bare);
                    if (mirrored)
                        continue;
                    seen.Add(def.Id);
                    pool.Add(def);
                }
            }

            return pool;
        }

        static string StripEnemyPrefix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name.StartsWith("敌·")) return name.Substring(2);
            if (name.StartsWith("敌方")) return name.Substring(2);
            return name;
        }

        public SpiritDefinition FindDefinition(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_spiritPool != null)
            {
                for (int i = 0; i < _spiritPool.Count; i++)
                    if (_spiritPool[i] != null && _spiritPool[i].Id == id)
                        return _spiritPool[i];
            }
            if (_roster != null)
            {
                for (int i = 0; i < _roster.Count; i++)
                    if (_roster[i] != null && _roster[i].Id == id)
                        return _roster[i];
            }
            if (_enemyRoster != null)
            {
                for (int i = 0; i < _enemyRoster.Count; i++)
                    if (_enemyRoster[i] != null && _enemyRoster[i].Id == id)
                        return _enemyRoster[i];
            }
            return null;
        }

        public void RestoreDefaultEnemyPlacement()
        {
            _enemyPlacement.Clear();
            var slots = DemoDataFactory.DefaultEnemySlots();
            // Default enemy lineup comes from the shared pool (not a separate enemy catalog).
            var defaults = DemoDataFactory.DefaultEnemyPoolIds();
            if (defaults == null || slots == null)
            {
                LastEvent = "默认敌阵数据缺失";
                return;
            }

            for (int i = 0; i < defaults.Length && i < slots.Length; i++)
            {
                var def = FindDefinition(defaults[i]);
                if (def == null && _spiritPool != null && i < _spiritPool.Count)
                    def = _spiritPool[i];
                if (def == null)
                {
                    Debug.LogWarning($"[BattleManager] default enemy id missing: {defaults[i]}");
                    continue;
                }
                _enemyPlacement[def.Id] = slots[i];
            }

            LastEvent = _enemyPlacement.Count > 0
                ? $"从精灵池选一只，点蓝区放我方、点红区放敌方（默认敌阵 {_enemyPlacement.Count} 只）"
                : "从精灵池选一只，点蓝区放我方、点红区放敌方（默认敌阵未生成）";
            _ui?.RefreshAll();
        }

        /// <summary>
        /// Place from the shared pool. Team is decided by the cell zone.
        /// When relocating an existing unit, it is removed from its old side first.
        /// </summary>
        public bool TryPlaceFromPool(SpiritDefinition def, Vector2Int cell, SpiritUnit movingUnit = null)
        {
            if (Phase != GamePhase.Formation || def == null || Board == null)
                return false;

            Team team;
            if (Board.IsPlayerDeployZone(cell)) team = Team.Player;
            else if (Board.IsEnemyDeployZone(cell)) team = Team.Enemy;
            else return false;

            if (movingUnit != null && movingUnit.Definition != null)
            {
                if (movingUnit.Team == Team.Player)
                    RemoveSpirit(movingUnit.Definition.Id, refreshUi: false);
                else
                    RemoveEnemySpirit(movingUnit.Definition.Id, refreshUi: false);
            }

            return team == Team.Player
                ? TryPlaceSpirit(def, cell)
                : TryPlaceEnemySpirit(def, cell);
        }

        public bool TryPlaceSpirit(SpiritDefinition def, Vector2Int cell)
        {
            if (Phase != GamePhase.Formation || def == null) return false;
            if (!Board.IsPlayerDeployZone(cell)) return false;
            if (!Board.IsPlaceable(cell))
            {
                LastEvent = "墙体格不可放置精灵";
                _ui?.RefreshAll();
                return false;
            }
            if (_playerPlacement.Count >= 6 && !_playerPlacement.ContainsKey(def.Id)) return false;

            if (_playerPlacement.TryGetValue(def.Id, out var old))
            {
                Board.Clear(old);
                RemoveSpawnedAt(old);
                _playerPlacement.Remove(def.Id);
            }

            if (!Board.IsEmpty(cell))
            {
                var occ = Board.Get(cell);
                if (occ == null || occ.Team != Team.Player)
                    return false;

                _playerPlacement.Remove(occ.Definition.Id);
                Board.Clear(cell);
                occ.gameObject.SetActive(false);
            }

            _playerPlacement[def.Id] = cell;
            SpawnPreviewUnit(def, Team.Player, cell);
            RefreshBondsPreview();
            LastEvent = $"已放置我方 {def.DisplayName} @ ({cell.x},{cell.y})";
            _ui?.RefreshAll();
            return true;
        }

        public bool TryPlaceEnemySpirit(SpiritDefinition def, Vector2Int cell)
        {
            if (Phase != GamePhase.Formation || def == null) return false;
            if (!Board.IsEnemyDeployZone(cell)) return false;
            if (!Board.IsPlaceable(cell))
            {
                LastEvent = "墙体格不可放置精灵";
                _ui?.RefreshAll();
                return false;
            }
            if (_enemyPlacement.Count >= 6 && !_enemyPlacement.ContainsKey(def.Id)) return false;

            if (_enemyPlacement.TryGetValue(def.Id, out var old))
            {
                Board.Clear(old);
                RemoveSpawnedAt(old);
                _enemyPlacement.Remove(def.Id);
            }

            if (!Board.IsEmpty(cell))
            {
                var occ = Board.Get(cell);
                if (occ == null || occ.Team != Team.Enemy)
                    return false;

                _enemyPlacement.Remove(occ.Definition.Id);
                Board.Clear(cell);
                occ.gameObject.SetActive(false);
            }

            _enemyPlacement[def.Id] = cell;
            SpawnPreviewUnit(def, Team.Enemy, cell);
            LastEvent = $"已放置敌方 {def.DisplayName} @ ({cell.x},{cell.y})";
            _ui?.RefreshAll();
            return true;
        }


        public void RemoveSpirit(string id) => RemoveSpirit(id, refreshUi: true);

        public void RemoveSpirit(string id, bool refreshUi)
        {
            if (Phase != GamePhase.Formation) return;
            if (!_playerPlacement.TryGetValue(id, out var cell)) return;
            Board.Clear(cell);
            RemoveSpawnedAt(cell);
            _playerPlacement.Remove(id);
            RefreshBondsPreview();
            if (refreshUi) _ui?.RefreshAll();
        }

        public void RemoveEnemySpirit(string id) => RemoveEnemySpirit(id, refreshUi: true);

        public void RemoveEnemySpirit(string id, bool refreshUi)
        {
            if (Phase != GamePhase.Formation) return;
            if (!_enemyPlacement.TryGetValue(id, out var cell)) return;

            Board.Clear(cell);
            RemoveSpawnedAt(cell);
            _enemyPlacement.Remove(id);
            if (refreshUi) _ui?.RefreshAll();
        }

        /// <summary>
        /// One-click clear: remove every placed spirit from both sides during formation.
        /// </summary>
        public void ClearBoardSpirits()
        {
            if (Phase != GamePhase.Formation) return;

            _playerPlacement.Clear();
            _enemyPlacement.Clear();
            ClearAllUnits();
            RefreshBondsPreview();
            LastEvent = "已清空棋盘精灵（双方布阵已重置）";
            _ui?.RefreshAll();
        }

        public void StartBattle()
        {
            if (Phase != GamePhase.Formation) return;
            if (_playerPlacement.Count < 1)
            {
                LastEvent = "至少上阵 1 只精灵";
                _ui?.RefreshAll();
                return;
            }

            // Never start a battle with an empty enemy side after edits / hot-reload.
            if (_enemyPlacement.Count == 0)
                RestoreDefaultEnemyPlacement();

            ClearAllUnits();
            RefreshBondsPreview();

            foreach (var kv in _playerPlacement)
            {
                var def = FindDefinition(kv.Key);
                if (def == null) continue;
                SpawnCombatUnit(def, Team.Player, kv.Value);
            }

            foreach (var kv in _enemyPlacement)
            {
                var def = FindDefinition(kv.Key);
                if (def == null) continue;
                SpawnCombatUnit(def, Team.Enemy, kv.Value);
            }

            FateMaster.Init(_masterDef, Board, this);
            Xinxiang?.ResetForBattle();
            GetBattlefield()?.ClearForFormation();
            // Walls were healed above; rebuild once more if map was missing.
            if (GetBattlefield() == null)
                EnsureBattlefieldMap();

            Phase = GamePhase.Battle;
            LastEvent = "战斗开始！岩后固守增防(点精灵出击) / 点岩石拆墙 / 草伏击 / 水扎根";

            foreach (var u in _spawned)
            {
                if (u == null) continue;
                u.EnsureBoardRegistration();
                u.PrepareTerrainCombatState();
                u.SetCombatEnabled(true);
            }

            GameAudio.Ensure();
            GameAudio.I?.PlayBattleStart();
            GameAudio.I?.PlayBattleBgm();
            _ui?.RefreshAll();
        }

        public void NotifyUnitDied(SpiritUnit unit)
        {
            if (Phase != GamePhase.Battle) return;
            CheckEnd();
        }

        public void NotifyMasterSkillUsed(Vector2Int cell, int hits)
        {
            LastEvent = $"星理技能命中 {hits} 个目标 @ ({cell.x},{cell.y})";
            _ui?.RefreshAll();
        }

        public void NotifyMasterSkillTargetInvalid()
        {
            LastEvent = "星理技能需要覆盖至少一个敌方目标";
            _ui?.RefreshAll();
        }

        public void NotifyXinxiangSkillUsed(XinxiangSkillDefinition def, SpiritUnit target, string overrideName = null)
        {
            string targetName = target != null && target.Definition != null
                ? target.Definition.DisplayName
                : "目标";
            string skillName = !string.IsNullOrEmpty(overrideName)
                ? overrideName
                : (def != null ? def.DisplayName : "心象技能");
            int hits = XinxiangSkillController.LastHits;
            bool combo = overrideName == XinxiangSkillController.HealBuffComboName;
            bool area = (def != null && def.IsArea) || combo;
            string qteTag = combo
                ? "【" + XinxiangSkillController.QteGradeLabel(XinxiangSkillController.LastQteGrade) + "】"
                : "";
            if (area)
                LastEvent = $"{skillName}{qteTag} → {targetName} 周围 3×3（命中 {hits} 个）";
            else
                LastEvent = $"{skillName} → {targetName}";
            _ui?.RefreshAll();
        }

        public void NotifyXinxiangTargetInvalid()
        {
            _ui?.RefreshAll();
        }

        void CheckEnd()
        {
            int playerAlive = Board.GetTeam(Team.Player).Count;
            int enemyAlive = Board.GetTeam(Team.Enemy).Count;
            if (playerAlive > 0 && enemyAlive > 0) return;

            Phase = GamePhase.Result;
            foreach (var u in _spawned)
                if (u != null) u.SetCombatEnabled(false);

            if (enemyAlive <= 0 && playerAlive > 0)
            {
                ResultText = "胜利！";
                LastEvent = "敌方全灭，我方胜利";
                GameAudio.I?.PlayVictory();
            }
            else if (playerAlive <= 0 && enemyAlive > 0)
            {
                ResultText = "失败";
                LastEvent = "我方全灭，战斗失败";
                GameAudio.I?.PlayDefeat();
            }
            else
            {
                ResultText = "同归于尽";
                LastEvent = "双方同时全灭";
                GameAudio.I?.PlayDefeat();
            }
            _ui?.RefreshAll();
        }

        public void RestartToFormation()
        {
            ClearAllUnits();
            Board.ClearAll();
            // Keep both sides' placement cells so units return to formation spots.
            if (_enemyPlacement.Count == 0)
                RestoreDefaultEnemyPlacement();
            Phase = GamePhase.Formation;
            ResultText = "";
            LastEvent = "重新布阵：岩后固守增防；草伏击；水扎根；开战点精灵可出击、点岩石可拆墙";
            GameAudio.I?.PlayFormationBgm();
            FateMaster.Init(_masterDef, Board, this);
            // New match → re-roll 1 grass/side, 1 rock, 2 rivers.
            GetBattlefield()?.RerollLayout();
            RefreshBondsPreview();
            SpawnFormationPreviews();
            _ui?.RefreshAll();
        }

        /// <summary>Respawn player + enemy preview units on their last formation cells.</summary>
        public void SpawnFormationPreviews()
        {
            foreach (var kv in _playerPlacement)
            {
                var def = FindDefinition(kv.Key);
                if (def != null)
                    SpawnPreviewUnit(def, Team.Player, kv.Value);
            }
            SpawnEnemyPreviews();
        }

        public void SpawnEnemyPreviews()
        {
            foreach (var kv in _enemyPlacement)
            {
                var def = FindDefinition(kv.Key);
                if (def != null)
                    SpawnPreviewUnit(def, Team.Enemy, kv.Value);
            }
        }

        void RefreshBondsPreview()
        {
            var defs = new List<SpiritDefinition>();
            foreach (var kv in _playerPlacement)
            {
                var d = FindDefinition(kv.Key);
                if (d != null) defs.Add(d);
            }
            PlayerBonds.Recalculate(defs);
            // Stats are baked at Init — refresh surviving player previews after roster/selection changes.
            ReapplyPlayerBondBonuses();
        }

        /// <summary>
        /// Formation-only: player toggles an eligible bond (max BondState.MaxSelected).
        /// Enemy never uses bonds — SpawnCombatUnit passes an empty BondState.
        /// </summary>
        public bool TryTogglePlayerBond(string key)
        {
            if (Phase != GamePhase.Formation) return false;
            if (!PlayerBonds.TryToggle(key)) return false;
            ReapplyPlayerBondBonuses();
            return true;
        }

        void ReapplyPlayerBondBonuses()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                var u = _spawned[i];
                if (u == null || !u.gameObject.activeInHierarchy) continue;
                if (u.Team != Team.Player || u.Definition == null) continue;
                u.Init(u.Definition, Team.Player, u.Cell, Board, PlayerBonds, FateMaster, this);
                u.SetCombatEnabled(false);
            }
        }

void SpawnPreviewUnit(SpiritDefinition def, Team team, Vector2Int cell)
        {
            var unit = SpawnCombatUnit(def, team, cell);
            if (unit != null)
                unit.SetCombatEnabled(false);
        }

SpiritUnit SpawnCombatUnit(SpiritDefinition def, Team team, Vector2Int cell)
        {
            string sceneName = $"{team}_{def.Id}";
            SpiritUnit unit = null;
            var candidates = FindObjectsByType<SpiritUnit>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (var candidate in candidates)
            {
                if (candidate != null && candidate.gameObject.name == sceneName)
                {
                    unit = candidate;
                    break;
                }
            }

            if (unit == null)
            {
                if (_spiritPrefab != null)
                {
                    unit = Instantiate(_spiritPrefab, transform);
                    unit.gameObject.name = sceneName;
                }
                else
                {
                    var go = new GameObject(sceneName);
                    go.transform.SetParent(transform, false);
                    unit = go.AddComponent<SpiritUnit>();
                }
            }

            unit.gameObject.SetActive(true);
            unit.Init(def, team, cell, Board,
                team == Team.Player ? PlayerBonds : new BondState(),
                FateMaster, this);

            if (!_spawned.Contains(unit))
                _spawned.Add(unit);
            return unit;
        }

void RemoveSpawnedAt(Vector2Int cell)
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                var u = _spawned[i];
                if (u == null)
                {
                    _spawned.RemoveAt(i);
                    continue;
                }
                if (u.Cell == cell)
                    u.gameObject.SetActive(false);
            }
        }

void ClearAllUnits()
        {
            foreach (var u in _spawned)
                if (u != null) u.gameObject.SetActive(false);
            _spawned.Clear();
            Board.ClearAll();
        }

        void Update()
        {
            if (Phase != GamePhase.Battle) return;

            if (Xinxiang != null && Xinxiang.IsSelecting && Input.GetMouseButtonDown(0))
            {
                if (UnityEngine.EventSystems.EventSystem.current != null &&
                    UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                    return;
                if (Camera.main == null) return;

                var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                SpiritUnit unit = null;
                if (Physics.Raycast(ray, out var hit, 200f))
                    unit = hit.collider != null ? hit.collider.GetComponentInParent<SpiritUnit>() : null;

                if (unit == null)
                {
                    var plane = new Plane(Vector3.up, Board != null ? Board.Origin : Vector3.zero);
                    if (plane.Raycast(ray, out float enter))
                        unit = FindNearestLivingUnit(ray.GetPoint(enter), 0.55f);
                }

                if (unit != null)
                {
                    Xinxiang.TryCastOnUnit(unit);
                    _ui?.RefreshAll();
                }
                else
                {
                    LastEvent = "请点击有效的目标单位";
                    _ui?.RefreshAll();
                }
            }
            else if (Input.GetMouseButtonDown(0)
                     && (Xinxiang == null || !Xinxiang.IsSelecting))
            {
                // Click a player unit behind rock → order leave-cover / chase.
                // Otherwise click a rock → smash order.
                if (!TryHandleLeaveCoverClick())
                    TryHandleWallSmashOrderClick();
            }

            if (Input.GetKeyDown(KeyCode.Escape) && Xinxiang != null)
            {
                Xinxiang.CancelSelect();
                _ui?.RefreshAll();
            }
        }

        /// <summary>
        /// Battle click on a player spirit that is holding rock cover → leave cover and chase.
        /// Returns true when a unit was ordered (so rock-smash should not also fire).
        /// </summary>
        bool TryHandleLeaveCoverClick()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return false;
            if (Camera.main == null || Board == null) return false;

            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            SpiritUnit unit = null;
            if (Physics.Raycast(ray, out var hit, 200f))
                unit = hit.collider != null ? hit.collider.GetComponentInParent<SpiritUnit>() : null;

            if (unit == null)
            {
                var plane = Board.GetPickPlane();
                if (plane.Raycast(ray, out float enter))
                    unit = FindNearestLivingUnit(ray.GetPoint(enter), 0.55f);
            }

            if (unit == null || !unit.IsAlive || unit.Team != Team.Player)
                return false;

            // Only consume the click when the unit is actually holding cover
            // (or still standing behind a rock and can be ordered out).
            if (!unit.IsHoldingCover)
                return false;

            if (!unit.OrderLeaveCover())
                return false;

            LastEvent = $"{unit.Definition?.DisplayName ?? "精灵"} 离开岩后出击";
            _ui?.RefreshAll();
            return true;
        }

        void TryHandleWallSmashOrderClick()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return;
            if (Camera.main == null || Board == null) return;

            var map = GetBattlefield();
            if (map == null) return;

            // Rocks have no colliders — pick the board cell under the cursor, then
            // snap to the nearest standing wall within ~1 cell so clicks are forgiving.
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            var plane = Board.GetPickPlane();
            if (!plane.Raycast(ray, out float enter))
                return;

            Vector3 hitPoint = ray.GetPoint(enter);
            if (!Board.TryWorldToCell(hitPoint, out var clicked))
                return;

            if (!TryFindWallNearClick(map, clicked, hitPoint, out var wallCell))
                return;

            if (!map.TryOrderPlayerWallSmash(wallCell))
                return;

            // Fighting player spirits retarget the rock now.
            // Skip ambushers and water-rooted units that cannot reach the rock.
            int ordered = 0;
            for (int i = 0; i < _spawned.Count; i++)
            {
                var u = _spawned[i];
                if (u == null || !u.IsAlive || u.Team != Team.Player) continue;
                if (!u.CanAcceptWallSmashOrder(wallCell)) continue;
                u.OrderSmashWall(wallCell);
                ordered++;
            }

            if (ordered > 0)
                LastEvent = $"改目标！{ordered} 只精灵立刻砸岩石 ({wallCell.x},{wallCell.y})";
            else
                LastEvent = "没有可立刻砸岩的精灵（伏击中 / 水中够不到除外）";
            _ui?.RefreshAll();
        }

        /// <summary>
        /// Accept a direct wall cell, or the nearest standing wall within 1 Chebyshev
        /// of the clicked cell / world point (click forgiveness).
        /// </summary>
        static bool TryFindWallNearClick(
            BattlefieldMap map, Vector2Int clicked, Vector3 worldHit, out Vector2Int wallCell)
        {
            wallCell = default;
            if (map.HasStandingWall(clicked))
            {
                wallCell = clicked;
                return true;
            }

            float best = float.MaxValue;
            bool found = false;
            for (int x = 0; x < BoardGrid.Size; x++)
            {
                for (int y = 0; y < BoardGrid.Size; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (!map.HasStandingWall(c)) continue;
                    int cheb = Mathf.Max(Mathf.Abs(c.x - clicked.x), Mathf.Abs(c.y - clicked.y));
                    if (cheb > 1) continue;
                    // Prefer closer world distance among 1-cell neighbors.
                    float wx = c.x - clicked.x;
                    float wy = c.y - clicked.y;
                    float score = cheb * 10f + (wx * wx + wy * wy);
                    if (score < best)
                    {
                        best = score;
                        wallCell = c;
                        found = true;
                    }
                }
            }

            return found;
        }

        SpiritUnit FindNearestLivingUnit(Vector3 worldPos, float maxDist)
        {
            SpiritUnit best = null;
            float bestSq = maxDist * maxDist;
            for (int i = 0; i < _spawned.Count; i++)
            {
                var u = _spawned[i];
                if (u == null || !u.IsAlive || u.IsAmbushing) continue;
                float sq = (u.transform.position - worldPos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = u;
                }
            }
            return best;
        }
    }
}
