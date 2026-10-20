using System.Collections.Generic;
using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// 8×8 terrain overlay: walls (destructible), grass (ambush), river (water root).
    /// Layout is rolled once per match: 1 grass/side front, 1 rock, 2 rivers.
    /// </summary>
    public class BattlefieldMap : MonoBehaviour
    {
        public const float DefaultWallMaxHp = 120f;
        public const float WallCoverDamageMul = 0.5f;
        public const float GrassAmbushDamageMul = 2.0f;
        public const float WallDestroyEnergy = 15f;
        public const float WallDestroyCooldownReduceSeconds = 3f;
        /// <summary>Deprecated: rock-behind no longer boosts attack (kept for asset/compat).</summary>
        public const float WallBehindAtkMul = 1f;
        /// <summary>Unit holding cover behind a rock: defense up (takes less damage).</summary>
        public const float WallBehindDefMul = 1.5f;
        /// <summary>Water spirit rooted in river for the whole fight.</summary>
        public const float WaterRootAtkMul = 1.35f;

        /// <summary>Player front row (deploy y ≤ 3).</summary>
        public const int PlayerFrontRow = 3;
        /// <summary>Enemy front row (deploy y ≥ 4).</summary>
        public const int EnemyFrontRow = 4;

        readonly TerrainType[,] _terrain = new TerrainType[BoardGrid.Size, BoardGrid.Size];
        readonly float[,] _wallHp = new float[BoardGrid.Size, BoardGrid.Size];
        readonly float[,] _wallMaxHp = new float[BoardGrid.Size, BoardGrid.Size];
        readonly Transform[,] _wallVisual = new Transform[BoardGrid.Size, BoardGrid.Size];
        readonly Transform[,] _grassVisual = new Transform[BoardGrid.Size, BoardGrid.Size];
        readonly Transform[,] _riverVisual = new Transform[BoardGrid.Size, BoardGrid.Size];

        BoardGrid _board;
        Transform _root;
        BattleManager _battle;
        float _builtCellSize;

        /// <summary>Player-ordered wall smash target (own-side rocks need an explicit order).</summary>
        Vector2Int? _playerOrderedWall;

        public bool IsReady => _board != null;
        public Vector2Int? PlayerOrderedWall => _playerOrderedWall;

        public void Bind(BoardGrid board, BattleManager battle)
        {
            _board = board;
            _battle = battle;
        }

        public void EnsureBuilt()
        {
            if (_board == null) return;
            if (_root == null)
            {
                var existing = transform.Find("TerrainRoot");
                _root = existing != null
                    ? existing
                    : new GameObject("TerrainRoot").transform;
                _root.SetParent(transform, false);
                _root.localPosition = Vector3.zero;
                _root.localRotation = Quaternion.identity;
                _root.localScale = Vector3.one;
            }

            ClearVisuals();
            ApplyDemoLayout();
            _builtCellSize = _board.CellSize;
            BuildAllVisuals();
        }

        /// <summary>
        /// Re-seat every terrain visual on its cell after DynamicGrid moves/rotates/scales.
        /// Roots stay under TerrainRoot (not under DynamicGrid — negative scale would squash).
        /// </summary>
        public void SnapVisualsToBoard()
        {
            if (_board == null)
                return;

            Quaternion yaw = _board.GridYawRotation;
            float cellSize = _board.CellSize;
            float mul = _builtCellSize > 1e-8f ? cellSize / _builtCellSize : 1f;

            for (int x = 0; x < BoardGrid.Size; x++)
            {
                for (int y = 0; y < BoardGrid.Size; y++)
                {
                    Vector3 pos = _board.CellToWorld(x, y);
                    PlaceVisual(_wallVisual[x, y], pos, yaw, mul, isWall: true, x, y);
                    PlaceVisual(_grassVisual[x, y], pos, yaw, mul, isWall: false, x, y);
                    PlaceVisual(_riverVisual[x, y], pos, yaw, mul, isWall: false, x, y);
                }
            }
        }

        void PlaceVisual(Transform t, Vector3 pos, Quaternion yaw, float mul, bool isWall, int x, int y)
        {
            if (t == null)
                return;
            t.position = pos;
            t.rotation = yaw;
            if (isWall)
            {
                float max = Mathf.Max(1f, _wallMaxHp[x, y]);
                float ratio = Mathf.Clamp01(_wallHp[x, y] / max);
                float hpY = 0.45f + 0.55f * ratio;
                t.localScale = new Vector3(mul, hpY, mul);
            }
            else if (Mathf.Abs(mul - 1f) > 0.001f)
            {
                t.localScale = new Vector3(mul, mul, mul);
            }
        }

        public void ClearForFormation()
        {
            // Keep this match's layout; only refresh wall HP + visuals.
            _playerOrderedWall = null;
            for (int x = 0; x < BoardGrid.Size; x++)
            {
                for (int y = 0; y < BoardGrid.Size; y++)
                {
                    if (_wallMaxHp[x, y] <= 0f) continue;
                    _terrain[x, y] = TerrainType.Wall;
                    _wallHp[x, y] = _wallMaxHp[x, y] > 0f ? _wallMaxHp[x, y] : DefaultWallMaxHp;
                }
            }

            RebuildWallVisualsOnly();
        }

        /// <summary>Re-roll grass / rock / water for a new match.</summary>
        public void RerollLayout()
        {
            _playerOrderedWall = null;
            EnsureBuilt();
        }

        /// <summary>
        /// Player clicks a standing wall during battle to authorize smashing it
        /// (required for own-side rocks; also works for any wall).
        /// </summary>
        public bool TryOrderPlayerWallSmash(Vector2Int cell)
        {
            if (!HasStandingWall(cell)) return false;
            _playerOrderedWall = cell;
            if (_battle != null)
            {
                _battle.LastEvent = $"下令拆墙 @ ({cell.x},{cell.y}) — 己方精灵将优先攻击该岩石";
                _battle.NotifyTerrainEvent();
            }
            return true;
        }

        public void ClearPlayerWallOrder()
        {
            _playerOrderedWall = null;
        }

        /// <summary>Player half is y ≤ 3; enemy half is y ≥ 4.</summary>
        public static bool IsPlayerHalf(Vector2Int cell) => cell.y <= PlayerFrontRow;

        public static bool IsOwnSideCell(Team team, Vector2Int cell) =>
            team == Team.Player ? IsPlayerHalf(cell) : !IsPlayerHalf(cell);

        /// <summary>
        /// Allies never auto-target own-half rocks unless the player ordered that cell.
        /// Enemy AI never auto-targets enemy-half rocks.
        /// </summary>
        public bool CanAutoSmashWall(Team team, Vector2Int wallCell)
        {
            if (!HasStandingWall(wallCell)) return false;
            if (team == Team.Player
                && _playerOrderedWall.HasValue
                && _playerOrderedWall.Value == wallCell)
                return true;
            return !IsOwnSideCell(team, wallCell);
        }

        /// <summary>
        /// True when the unit sits exactly one cell behind a standing rock
        /// on its own side (same column, rock toward the enemy half).
        /// Grants DEF up and default hold-cover AI while true.
        /// </summary>
        public bool IsStandingBehindWall(Vector2Int unitCell, Team team)
        {
            if (!InBounds(unitCell)) return false;

            for (int x = 0; x < BoardGrid.Size; x++)
            {
                for (int y = 0; y < BoardGrid.Size; y++)
                {
                    var wall = new Vector2Int(x, y);
                    if (!HasStandingWall(wall)) continue;
                    // Must be the single cell directly behind the rock (same column).
                    if (wall.x != unitCell.x) continue;

                    if (team == Team.Player)
                    {
                        // Rock is exactly one row in front (higher y, toward enemy).
                        if (wall.y - unitCell.y == 1)
                            return true;
                    }
                    else
                    {
                        // Rock is exactly one row in front (lower y, toward player).
                        if (unitCell.y - wall.y == 1)
                            return true;
                    }
                }
            }

            return false;
        }

        public TerrainType GetTerrain(Vector2Int cell)
        {
            if (!InBounds(cell)) return TerrainType.None;
            return _terrain[cell.x, cell.y];
        }

        public bool IsGrass(Vector2Int cell) => GetTerrain(cell) == TerrainType.Grass;

        public bool IsRiver(Vector2Int cell) => GetTerrain(cell) == TerrainType.River;

        public bool HasStandingWall(Vector2Int cell)
        {
            if (!InBounds(cell)) return false;
            return _terrain[cell.x, cell.y] == TerrainType.Wall && _wallHp[cell.x, cell.y] > 0.01f;
        }

        public bool BlocksMovement(Vector2Int cell) => HasStandingWall(cell);

        public bool BlocksPlacement(Vector2Int cell) => HasStandingWall(cell);

        public float GetWallHp(Vector2Int cell)
        {
            if (!HasStandingWall(cell)) return 0f;
            return _wallHp[cell.x, cell.y];
        }

        public float GetWallMaxHp(Vector2Int cell)
        {
            if (!InBounds(cell) || _terrain[cell.x, cell.y] != TerrainType.Wall) return 0f;
            return _wallMaxHp[cell.x, cell.y];
        }

        /// <summary>
        /// True when a standing wall lies strictly between two cells (Chebyshev LOS sample).
        /// </summary>
        public bool HasCoverBetween(Vector2Int from, Vector2Int to)
        {
            if (!InBounds(from) || !InBounds(to) || from == to) return false;

            int x0 = from.x;
            int y0 = from.y;
            int x1 = to.x;
            int y1 = to.y;
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            int x = x0;
            int y = y0;
            while (true)
            {
                if (!(x == x0 && y == y0) && !(x == x1 && y == y1))
                {
                    if (HasStandingWall(new Vector2Int(x, y)))
                        return true;
                }

                if (x == x1 && y == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y += sy;
                }
            }

            return false;
        }

        /// <summary>
        /// Apply damage to a wall cell. Returns true if the wall was destroyed this hit.
        /// </summary>
        public bool DamageWall(Vector2Int cell, float amount, SpiritUnit attacker)
        {
            if (!HasStandingWall(cell) || amount <= 0f) return false;

            _wallHp[cell.x, cell.y] = Mathf.Max(0f, _wallHp[cell.x, cell.y] - amount);
            UpdateWallVisualHp(cell);

            if (_wallHp[cell.x, cell.y] > 0.01f)
                return false;

            DestroyWall(cell, attacker);
            return true;
        }

        public bool TryFindWallInRange(Vector2Int from, int range, out Vector2Int wallCell)
            => TryFindWallInRange(from, range, null, out wallCell);

        public bool TryFindWallInRange(Vector2Int from, int range, Team? smashTeam, out Vector2Int wallCell)
        {
            wallCell = default;
            if (_board == null || range < 0) return false;

            // Prefer player-ordered wall if it is in range.
            if (smashTeam == Team.Player
                && _playerOrderedWall.HasValue
                && HasStandingWall(_playerOrderedWall.Value))
            {
                float od = _board.Chebyshev(from, _playerOrderedWall.Value);
                if (od <= range + 0.01f)
                {
                    wallCell = _playerOrderedWall.Value;
                    return true;
                }
            }

            float best = float.MaxValue;
            bool found = false;
            for (int x = 0; x < BoardGrid.Size; x++)
            {
                for (int y = 0; y < BoardGrid.Size; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (!HasStandingWall(c)) continue;
                    if (smashTeam.HasValue && !CanAutoSmashWall(smashTeam.Value, c)) continue;
                    float d = _board.Chebyshev(from, c);
                    if (d > range + 0.01f) continue;
                    if (d < best)
                    {
                        best = d;
                        wallCell = c;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Prefer a wall that currently covers the line to the enemy (for smash-through AI).
        /// Respects own-side no-auto-smash rules.
        /// </summary>
        public bool TryFindCoverWallToward(
            Vector2Int from, Vector2Int enemy, int range, Team smashTeam, out Vector2Int wallCell)
        {
            wallCell = default;
            if (_board == null) return false;

            float best = float.MaxValue;
            bool found = false;
            for (int x = 0; x < BoardGrid.Size; x++)
            {
                for (int y = 0; y < BoardGrid.Size; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (!HasStandingWall(c)) continue;
                    if (!CanAutoSmashWall(smashTeam, c)) continue;
                    float d = _board.Chebyshev(from, c);
                    if (d > range + 0.01f) continue;

                    // Wall should sit roughly between attacker and enemy.
                    float toEnemy = _board.Chebyshev(from, enemy);
                    float wallToEnemy = _board.Chebyshev(c, enemy);
                    if (wallToEnemy >= toEnemy) continue;

                    if (d < best)
                    {
                        best = d;
                        wallCell = c;
                        found = true;
                    }
                }
            }

            return found || TryFindWallInRange(from, range, smashTeam, out wallCell);
        }

        void DestroyWall(Vector2Int cell, SpiritUnit attacker)
        {
            _wallHp[cell.x, cell.y] = 0f;
            _terrain[cell.x, cell.y] = TerrainType.None;

            var visual = _wallVisual[cell.x, cell.y];
            if (visual != null)
            {
                SafeDestroy(visual.gameObject);
                _wallVisual[cell.x, cell.y] = null;
            }

            Vector3 pos = _board != null
                ? _board.CellToWorld(cell) + Vector3.up * (_board.CellSize * 0.35f)
                : Vector3.zero;
            BattlefieldVfxLibrary.SpawnWallBreak(pos, _board != null ? _board.CellSize : 0.24f);

            GrantWallDestroyRewards(attacker);

            if (_playerOrderedWall.HasValue && _playerOrderedWall.Value == cell)
                _playerOrderedWall = null;

            if (_battle != null)
            {
                string who = attacker != null && attacker.Definition != null
                    ? attacker.Definition.DisplayName
                    : "单位";
                _battle.LastEvent = $"{who} 击倒岩石！能量+{WallDestroyEnergy:0} · 心象冷却-{WallDestroyCooldownReduceSeconds:0}s";
                _battle.NotifyTerrainEvent();
            }
        }

        void GrantWallDestroyRewards(SpiritUnit attacker)
        {
            if (attacker == null || !attacker.IsAlive) return;

            attacker.AddEnergy(WallDestroyEnergy);

            // Player-side: also nudge Fate Master energy + Xinxiang cooldowns.
            if (attacker.Team == Team.Player && _battle != null)
            {
                _battle.FateMaster?.OnAllyCombatEvent(Team.Player, WallDestroyEnergy * 0.5f);
                _battle.Xinxiang?.ReduceAllCooldowns(WallDestroyCooldownReduceSeconds);
            }
            else if (attacker.Team == Team.Enemy && _battle != null)
            {
                // Enemy smash still shortens player skill pressure slightly less — skip.
            }
        }

        void ApplyDemoLayout()
        {
            for (int x = 0; x < BoardGrid.Size; x++)
            {
                for (int y = 0; y < BoardGrid.Size; y++)
                {
                    _terrain[x, y] = TerrainType.None;
                    _wallHp[x, y] = 0f;
                    _wallMaxHp[x, y] = 0f;
                }
            }

            _playerOrderedWall = null;

            // Grass — exactly one on each front row (player y=3, enemy y=4).
            int playerGrassX = Random.Range(0, BoardGrid.Size);
            int enemyGrassX = Random.Range(0, BoardGrid.Size);
            PlaceGrass(playerGrassX, PlayerFrontRow);
            PlaceGrass(enemyGrassX, EnemyFrontRow);

            // Rock — exactly one random empty cell per match (prefer mid board).
            if (TryPickRandomEmptyCell(1, 6, 1, 6, out var wallCell)
                || TryPickRandomEmptyCell(0, 7, 0, 7, out wallCell))
            {
                PlaceWall(wallCell.x, wallCell.y);
            }

            // Water — exactly two random empty cells per match.
            int riversPlaced = 0;
            for (int attempt = 0; attempt < 48 && riversPlaced < 2; attempt++)
            {
                if (!TryPickRandomEmptyCell(0, 7, 0, 7, out var riverCell))
                    break;
                PlaceRiver(riverCell.x, riverCell.y);
                riversPlaced++;
            }
        }

        bool TryPickRandomEmptyCell(int xMin, int xMax, int yMin, int yMax, out Vector2Int cell)
        {
            cell = default;
            var candidates = new List<Vector2Int>(BoardGrid.Size * BoardGrid.Size);
            int x0 = Mathf.Clamp(xMin, 0, BoardGrid.Size - 1);
            int x1 = Mathf.Clamp(xMax, 0, BoardGrid.Size - 1);
            int y0 = Mathf.Clamp(yMin, 0, BoardGrid.Size - 1);
            int y1 = Mathf.Clamp(yMax, 0, BoardGrid.Size - 1);
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    if (_terrain[x, y] != TerrainType.None) continue;
                    candidates.Add(new Vector2Int(x, y));
                }
            }

            if (candidates.Count == 0) return false;
            cell = candidates[Random.Range(0, candidates.Count)];
            return true;
        }

        void PlaceWall(int x, int y, float hp = DefaultWallMaxHp)
        {
            if (!InBounds(new Vector2Int(x, y))) return;
            _terrain[x, y] = TerrainType.Wall;
            _wallMaxHp[x, y] = hp;
            _wallHp[x, y] = hp;
        }

        void PlaceGrass(int x, int y)
        {
            if (!InBounds(new Vector2Int(x, y))) return;
            if (_terrain[x, y] == TerrainType.Wall) return;
            _terrain[x, y] = TerrainType.Grass;
        }

        void PlaceRiver(int x, int y)
        {
            if (!InBounds(new Vector2Int(x, y))) return;
            if (_terrain[x, y] == TerrainType.Wall) return;
            _terrain[x, y] = TerrainType.River;
        }

        void BuildAllVisuals()
        {
            for (int x = 0; x < BoardGrid.Size; x++)
            {
                for (int y = 0; y < BoardGrid.Size; y++)
                {
                    var cell = new Vector2Int(x, y);
                    switch (_terrain[x, y])
                    {
                        case TerrainType.Wall:
                            SpawnWallVisual(cell);
                            break;
                        case TerrainType.Grass:
                            SpawnGrassVisual(cell);
                            break;
                        case TerrainType.River:
                            SpawnRiverVisual(cell);
                            break;
                    }
                }
            }
        }

        void RebuildWallVisualsOnly()
        {
            for (int x = 0; x < BoardGrid.Size; x++)
            {
                for (int y = 0; y < BoardGrid.Size; y++)
                {
                    if (_wallVisual[x, y] != null)
                    {
                        SafeDestroy(_wallVisual[x, y].gameObject);
                        _wallVisual[x, y] = null;
                    }

                    // Restored walls need terrain type again if previously destroyed.
                    if (_wallMaxHp[x, y] > 0f && _wallHp[x, y] > 0.01f)
                    {
                        _terrain[x, y] = TerrainType.Wall;
                        SpawnWallVisual(new Vector2Int(x, y));
                    }
                }
            }

            // Re-apply original layout walls that may have been cleared to None.
            // Demo layout walls are re-seeded by EnsureBuilt; ClearForFormation only heals.
            // If terrain was set to None on destroy, restore from maxHp table:
            for (int x = 0; x < BoardGrid.Size; x++)
            {
                for (int y = 0; y < BoardGrid.Size; y++)
                {
                    if (_wallMaxHp[x, y] <= 0f) continue;
                    if (_terrain[x, y] == TerrainType.Wall) continue;
                    // Was destroyed — restore as wall for next battle.
                    _terrain[x, y] = TerrainType.Wall;
                    _wallHp[x, y] = _wallMaxHp[x, y];
                    if (_wallVisual[x, y] == null)
                        SpawnWallVisual(new Vector2Int(x, y));
                }
            }
        }

        void ClearVisuals()
        {
            if (_root != null)
            {
                for (int i = _root.childCount - 1; i >= 0; i--)
                    SafeDestroy(_root.GetChild(i).gameObject);
            }

            for (int x = 0; x < BoardGrid.Size; x++)
            {
                for (int y = 0; y < BoardGrid.Size; y++)
                {
                    _wallVisual[x, y] = null;
                    _grassVisual[x, y] = null;
                    _riverVisual[x, y] = null;
                }
            }
        }

        static void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }

        void SpawnWallVisual(Vector2Int cell)
        {
            if (_board == null || _root == null) return;
            float cellSize = _board.CellSize;
            Vector3 pos = _board.CellToWorld(cell);
            float max = Mathf.Max(1f, _wallMaxHp[cell.x, cell.y]);
            float ratio = Mathf.Clamp01(_wallHp[cell.x, cell.y] / max);

            var go = ArenaEnvironment.IsLegacyArchiveScene()
                ? TerrainVisualLibrary.BuildWall(cell, cellSize, _root, ratio)
                : ConcertTerrainVisuals.BuildWall(cell, cellSize, _root, ratio);
            go.transform.position = pos;
            go.transform.rotation = _board.GridYawRotation;
            _wallVisual[cell.x, cell.y] = go.transform;
            UpdateWallVisualHp(cell);
        }

        void UpdateWallVisualHp(Vector2Int cell)
        {
            var t = _wallVisual[cell.x, cell.y];
            if (t == null) return;
            float max = Mathf.Max(1f, _wallMaxHp[cell.x, cell.y]);
            float ratio = Mathf.Clamp01(_wallHp[cell.x, cell.y] / max);
            float cellSize = _board != null ? _board.CellSize : 0.24f;
            TerrainVisualLibrary.ApplyWallHpVisual(t, ratio, cellSize);
        }

        void SpawnGrassVisual(Vector2Int cell)
        {
            if (_board == null || _root == null) return;
            float cellSize = _board.CellSize;
            Vector3 pos = _board.CellToWorld(cell);

            var go = ArenaEnvironment.IsLegacyArchiveScene()
                ? TerrainVisualLibrary.BuildGrass(cell, cellSize, _root)
                : ConcertTerrainVisuals.BuildGrass(cell, cellSize, _root);
            go.transform.position = pos;
            go.transform.rotation = _board.GridYawRotation;
            _grassVisual[cell.x, cell.y] = go.transform;
        }

        void SpawnRiverVisual(Vector2Int cell)
        {
            if (_board == null || _root == null) return;
            float cellSize = _board.CellSize;
            Vector3 pos = _board.CellToWorld(cell);

            var go = ArenaEnvironment.IsLegacyArchiveScene()
                ? TerrainVisualLibrary.BuildRiver(cell, cellSize, _root)
                : ConcertTerrainVisuals.BuildRiver(cell, cellSize, _root);
            go.transform.position = pos;
            go.transform.rotation = _board.GridYawRotation;
            _riverVisual[cell.x, cell.y] = go.transform;
        }

        static bool InBounds(Vector2Int c) =>
            c.x >= 0 && c.y >= 0 && c.x < BoardGrid.Size && c.y < BoardGrid.Size;

        public IEnumerable<Vector2Int> AllCellsOf(TerrainType type)
        {
            for (int x = 0; x < BoardGrid.Size; x++)
                for (int y = 0; y < BoardGrid.Size; y++)
                    if (_terrain[x, y] == type)
                        yield return new Vector2Int(x, y);
        }
    }
}
