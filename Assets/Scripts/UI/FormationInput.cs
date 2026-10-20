using UnityEngine;

namespace NexusAnima
{
    public class FormationInput : MonoBehaviour
    {
        BattleManager _battle;
        BoardGrid _board;

        SpiritDefinition _selected;
        Team _ghostTeam = Team.Player;
        GameObject _ghost;
        SpiritUnit _movingUnit;

        public void Init(BattleManager battle, BoardGrid board)
        {
            _battle = battle;
            _board = board;
        }

        public void SetSelected(SpiritDefinition def)
        {
            SetSelected(def, Team.Player);
        }

        public void SetSelected(SpiritDefinition def, Team team)
        {
            if (_movingUnit != null)
            {
                _movingUnit.gameObject.SetActive(true);
                _movingUnit = null;
            }

            _selected = def;
            _ghostTeam = team;

            if (_ghost != null)
            {
                var preview = _ghost.GetComponent<SpiritUnit>();
                if (preview == null || preview.Definition != _selected)
                {
                    Destroy(_ghost);
                    _ghost = null;
                }
            }

            if (_board != null && _battle != null && _battle.Phase == GamePhase.Formation)
            {
                _board.SetVisualVisible(true);
                if (_selected == null)
                    _board.SetHoveredCell(default, false);
            }
            else
            {
                _board?.SetHoveredCell(default, false);
                _board?.SetVisualVisible(false);
            }
            EnsureGhost();
        }

        void EnsureGhost()
        {
            if (_selected == null || _board == null)
            {
                if (_ghost != null) _ghost.SetActive(false);
                return;
            }

            if (_ghost != null)
            {
                var preview = _ghost.GetComponent<SpiritUnit>();
                if (preview == null
                    || preview.Definition != _selected
                    || preview.Team != _ghostTeam)
                {
                    Destroy(_ghost);
                    _ghost = null;
                }
            }

            if (_ghost == null)
            {
                _ghost = new GameObject("PlacementGhost");
                var preview = _ghost.AddComponent<SpiritUnit>();
                preview.BuildSelectionPreview(_selected, _board, _ghostTeam);
            }

            _ghost.SetActive(_battle != null && _battle.Phase == GamePhase.Formation);
        }

        void ClearSelection()
        {
            _selected = null;
            _ghostTeam = Team.Player;

            if (_movingUnit != null)
            {
                var moving = _movingUnit;
                _movingUnit = null;
                if (moving != null) moving.gameObject.SetActive(true);
            }

            if (_ghost != null) _ghost.SetActive(false);
            _board?.SetHoveredCell(default, false);
            // Grid stays on during Formation; Update() keeps SetVisualVisible(true).
        }

        void BeginMovingUnit(SpiritUnit unit)
        {
            if (_battle == null || _board == null || unit == null)
                return;

            if (_movingUnit != null && _movingUnit != unit)
                _movingUnit.gameObject.SetActive(true);

            _movingUnit = unit;
            _selected = unit.Definition;
            _ghostTeam = unit.Team;
            unit.gameObject.SetActive(false);
            _board.SetVisualVisible(true);
            EnsureGhost();
        }

        static bool TryResolveTeam(BoardGrid board, Vector2Int cell, out Team team)
        {
            if (board.IsPlayerDeployZone(cell))
            {
                team = Team.Player;
                return true;
            }

            if (board.IsEnemyDeployZone(cell))
            {
                team = Team.Enemy;
                return true;
            }

            team = Team.Player;
            return false;
        }

        void Update()
        {
            if (_battle == null || _board == null || _battle.Phase != GamePhase.Formation)
            {
                if (_ghost != null) _ghost.SetActive(false);
                _board?.SetHoveredCell(default, false);
                if (GridPlayAdjust.IsDriving)
                    _board?.SetVisualVisible(true);
                else
                    _board?.SetVisualVisible(false);
                return;
            }

            // Keep the formation grid visible for the whole Formation phase
            // (not only while a roster spirit is selected).
            _board.SetVisualVisible(true);

            // First-person camera posing owns WASD / RMB while F2 fly is on.
            // Grid adjust owns LMB / wheel while 摆网格 is on.
            if (BattleFlyCamera.IsDriving || GridPlayAdjust.IsDriving || PropPlayAdjust.IsDriving)
                return;

            // Keyboard actions must not depend on mouse / UI hit-tests.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ClearSelection();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Delete))
            {
                TryDeleteCurrentOrHovered(null);
                return;
            }

            var camera = Camera.main;
            if (camera == null)
                return;

            var ray = camera.ScreenPointToRay(Input.mousePosition);
            bool overUi = UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

            // Right-click delete: allow even when pointer is over UI (roster/HUD often covers the board edge).
            // Prefer moving unit → model raycast → board cell.
            if (Input.GetMouseButtonDown(1))
            {
                TryDeleteCurrentOrHovered(ray);
                return;
            }

            // Check the model before projecting onto the board plane. Raised/flying models
            // may be clicked where the ray does not intersect the board surface.
            if (!overUi && Input.GetMouseButtonDown(0) && _selected == null)
            {
                var clickedUnit = FindUnitAlongRay(ray) ?? FindUnitUnderPointer(Input.mousePosition);
                if (clickedUnit != null)
                {
                    BeginMovingUnit(clickedUnit);
                    return;
                }
            }

            // Pick on the SAME Y as painted grid tiles — using Board.Origin caused
            // rear-rank parallax (click tile A, land on cell B), worse for flying models.
            var plane = _board.GetPickPlane();
            bool hitBoardPlane = plane.Raycast(ray, out float enter);
            bool inBoard = false;
            Vector2Int cell = default;

            if (hitBoardPlane)
            {
                var world = ray.GetPoint(enter);
                inBoard = _board.TryWorldToCell(world, out cell);
            }

            // Team follows the drop zone (blue = player, red = enemy).
            Team hoverTeam = Team.Player;
            bool inAllowedZone = inBoard && TryResolveTeam(_board, cell, out hoverTeam);
            if (inAllowedZone && hoverTeam != _ghostTeam)
            {
                _ghostTeam = hoverTeam;
                EnsureGhost();
            }

            // Grid hover preview while selecting / relocating a spirit.
            if (_selected != null)
                _board.SetVisualVisible(true);
            bool showHover = _selected != null && inBoard;
            _board.SetHoveredCell(cell, showHover);

            EnsureGhost();
            if (_selected != null && inAllowedZone && _ghost != null)
            {
                // Ghost root must sit on the logical cell center (same as SnapToCell)
                // and yaw with the painted grid so preview facing matches live units.
                _ghost.transform.position = _board.CellToWorld(cell);
                _ghost.transform.rotation = _board.GridYawRotation;
                _ghost.SetActive(true);
            }
            else if (_ghost != null)
            {
                _ghost.SetActive(false);
            }

            if (overUi) return;

            if (Input.GetMouseButtonDown(0) && inBoard)
            {
                var occupant = _board.Get(cell);

                if (_selected != null && inAllowedZone)
                {
                    if (occupant != null && occupant != _movingUnit)
                        return;

                    bool placed = _battle.TryPlaceFromPool(_selected, cell, _movingUnit);
                    if (placed)
                    {
                        _movingUnit = null;
                        ClearSelection();
                    }
                }
                else if (_selected == null && occupant != null)
                {
                    BeginMovingUnit(occupant);
                }
            }
        }

        void TryDeleteCurrentOrHovered(Ray? optionalRay)
        {
            SpiritUnit unit = null;

            // 1) Currently relocating — delete that unit even if mouse is elsewhere.
            if (_movingUnit != null)
                unit = _movingUnit;

            // 2) Clicked model (works for flying units whose feet miss the board cell).
            if (unit == null && optionalRay.HasValue)
                unit = FindUnitAlongRay(optionalRay.Value) ?? FindUnitUnderPointer(Input.mousePosition);

            // 3) Board cell under cursor.
            if (unit == null && optionalRay.HasValue && Camera.main != null)
            {
                var plane = _board.GetPickPlane();
                if (plane.Raycast(optionalRay.Value, out float enter))
                {
                    var world = optionalRay.Value.GetPoint(enter);
                    if (_board.TryWorldToCell(world, out var cell))
                        unit = _board.Get(cell);
                }
            }

            // 4) Delete key with pool selection but no live unit —
            // remove only when the spirit is uniquely placed on one side.
            if (unit == null && _selected != null)
            {
                string id = _selected.Id;
                bool onPlayer = _battle.PlayerPlacement.ContainsKey(id);
                bool onEnemy = _battle.EnemyPlacement.ContainsKey(id);
                if (onPlayer ^ onEnemy)
                {
                    if (onPlayer) _battle.RemoveSpirit(id);
                    else _battle.RemoveEnemySpirit(id);
                    ClearSelection();
                    return;
                }
            }

            if (unit == null)
                return;

            if (unit.Team == Team.Player)
                _battle.RemoveSpirit(unit.Definition.Id);
            else
                _battle.RemoveEnemySpirit(unit.Definition.Id);

            _movingUnit = null;
            ClearSelection();
        }

        SpiritUnit FindUnitAlongRay(Ray ray)
        {
            var hits = Physics.RaycastAll(ray, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            SpiritUnit best = null;
            float bestDistance = float.MaxValue;

            foreach (var hit in hits)
            {
                var unit = hit.collider != null
                    ? hit.collider.GetComponentInParent<SpiritUnit>()
                    : null;
                if (unit == null || !unit.IsAlive || hit.distance >= bestDistance)
                    continue;

                // Ignore placement ghost previews.
                if (unit.gameObject.name == "PlacementGhost" || !unit.gameObject.activeInHierarchy)
                    continue;

                best = unit;
                bestDistance = hit.distance;
            }

            return best;
        }

        SpiritUnit FindUnitUnderPointer(Vector2 screenPosition)
        {
            var camera = Camera.main;
            if (camera == null)
                return null;

            SpiritUnit best = null;
            float bestDepth = float.MaxValue;

            foreach (var unit in FindObjectsOfType<SpiritUnit>())
            {
                if (unit == null || !unit.IsAlive)
                    continue;
                if (unit.gameObject.name == "PlacementGhost" || !unit.gameObject.activeInHierarchy)
                    continue;

                var renderers = unit.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                    continue;

                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                Vector3 center = bounds.center;
                Vector3 extents = bounds.extents;
                Vector3[] corners =
                {
                    center + new Vector3(-extents.x, -extents.y, -extents.z),
                    center + new Vector3(-extents.x, -extents.y, extents.z),
                    center + new Vector3(-extents.x, extents.y, -extents.z),
                    center + new Vector3(-extents.x, extents.y, extents.z),
                    center + new Vector3(extents.x, -extents.y, -extents.z),
                    center + new Vector3(extents.x, -extents.y, extents.z),
                    center + new Vector3(extents.x, extents.y, -extents.z),
                    center + new Vector3(extents.x, extents.y, extents.z)
                };

                float minX = float.MaxValue;
                float minY = float.MaxValue;
                float maxX = float.MinValue;
                float maxY = float.MinValue;
                bool hasVisibleCorner = false;

                foreach (var corner in corners)
                {
                    Vector3 screen = camera.WorldToScreenPoint(corner);
                    if (screen.z <= 0f)
                        continue;

                    hasVisibleCorner = true;
                    minX = Mathf.Min(minX, screen.x);
                    minY = Mathf.Min(minY, screen.y);
                    maxX = Mathf.Max(maxX, screen.x);
                    maxY = Mathf.Max(maxY, screen.y);
                }

                if (!hasVisibleCorner
                    || screenPosition.x < minX || screenPosition.x > maxX
                    || screenPosition.y < minY || screenPosition.y > maxY)
                    continue;

                float depth = camera.WorldToScreenPoint(center).z;
                if (depth > 0f && depth < bestDepth)
                {
                    best = unit;
                    bestDepth = depth;
                }
            }

            return best;
        }
    }
}
