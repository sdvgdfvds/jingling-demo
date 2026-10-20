using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NexusAnima
{
    /// <summary>
    /// Formation board. DynamicGrid can be moved/scaled freely in the editor.
    /// On Play, Board logic (cells / spirits) reads DynamicGrid's world footprint —
    /// it does NOT rebuild or reset DynamicGrid while you drag it.
    /// </summary>
    public class BoardGrid : MonoBehaviour
    {
        public const int Size = 8;
        // The 8x8 board fits inside the red carpet instead of covering the whole island.
        public float CellSize = 0.24f;

        [Header("Formation Grid Visual")]
        [Tooltip("格子相对 Board 原点抬高。手工校准后地毯表面约 Board.y+0.0144。")]
        [SerializeField] float gridHeightOffset = 0.0144f;

        [Header("Authored DynamicGrid Pose")]
        [Tooltip("Play 校准后的 DynamicGrid 本地坐标（Inspector 锁定值）。")]
        [SerializeField] Vector3 authoredGridLocalPosition = new Vector3(0f, 0.0109f, 0.0065f);
        [SerializeField] Vector3 authoredGridLocalScale = new Vector3(-0.062f, 0.15147f, 0.0415f);
        [SerializeField] Vector3 authoredGridLocalEuler = Vector3.zero;
        [Tooltip("格子填充不透明度 0~1")]
        [SerializeField, Range(0.05f, 1f)] float gridCellAlpha = 0.85f;
        [Tooltip("鼠标悬停格不透明度 0~1")]
        [SerializeField, Range(0.05f, 1f)] float gridHoverAlpha = 0.95f;

        /// <summary>World Y of the visible DynamicGrid tiles (pick plane / overlays must match this).</summary>
        public float GridSurfaceY
        {
            get
            {
                if (_tiles[0, 0] == null)
                    BindPaintedTiles();
                if (_tiles[0, 0] != null)
                    return _tiles[0, 0].position.y;
                var dg = _visualRoot != null ? _visualRoot : transform.Find("DynamicGrid");
                if (dg != null && TryMeasureDynamicGridWorldBounds(dg, out Bounds bounds))
                    return bounds.center.y;
                return Origin.y + GridLift;
            }
        }

        /// <summary>
        /// Hairline lift so hover / skill overlays sit on DynamicGrid without z-fight.
        /// Concert cells are ~1.3cm — metre-scale lifts (0.006) float a half-cell above the board.
        /// </summary>
        float CellOverlayLift(float layer = 1f)
        {
            float eps = Mathf.Clamp(CellSize * 0.012f, 0.00008f, 0.00035f);
            return eps * Mathf.Max(0.5f, layer);
        }

        Vector3 CellOverlayPosition(Vector2Int cell, float layer = 1f)
        {
            Vector3 p = CellToWorld(cell);
            return new Vector3(p.x, p.y + CellOverlayLift(layer), p.z);
        }

        float GridLift => Mathf.Clamp(gridHeightOffset, 0.008f, 0.12f);

        readonly SpiritUnit[,] _cells = new SpiritUnit[Size, Size];
        readonly Transform[,] _tiles = new Transform[Size, Size];
        readonly List<GameObject> _rangeMarkers = new List<GameObject>();
        Transform _visualRoot;
        Transform _rangeRoot;
        GameObject _hoverTile;
        Renderer _hoverRenderer;
        bool _hoverVisible;
        BattlefieldMap _battlefield;
        Vector3 _lastDgWorldPos;
        float _lastDgYaw;
        Vector3 _lastDgLossyScale;
        bool _hasDgPose;
        bool _snappingOccupants;

        public Vector3 Origin => transform.position;
        public BattlefieldMap Battlefield => _battlefield;

        public void SetBattlefield(BattlefieldMap map) => _battlefield = map;

        public void Configure(Vector3 origin, float cellSize)
        {
            transform.position = origin;
            CellSize = cellSize;
        }

        /// <summary>
        /// World AABB of the 8×8 playable board (cell footprints on the pick plane).
        /// Prefer measuring DynamicGrid visuals when present so authored scale wins.
        /// </summary>
        public Bounds GetWorldBounds()
        {
            var dg = transform.Find("DynamicGrid");
            if (dg != null && TryMeasureDynamicGridWorldBounds(dg, out Bounds visualBounds))
                return visualBounds;

            Vector3 c0 = CellToWorld(0, 0);
            Vector3 c1 = CellToWorld(Size - 1, Size - 1);
            float cellWx = Mathf.Abs(CellSize * transform.lossyScale.x);
            float cellWz = Mathf.Abs(CellSize * transform.lossyScale.z);
            Vector3 min = new Vector3(
                Mathf.Min(c0.x, c1.x) - cellWx * 0.5f,
                GridSurfaceY,
                Mathf.Min(c0.z, c1.z) - cellWz * 0.5f);
            Vector3 max = new Vector3(
                Mathf.Max(c0.x, c1.x) + cellWx * 0.5f,
                GridSurfaceY + 0.02f,
                Mathf.Max(c0.z, c1.z) + cellWz * 0.5f);
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        /// <summary>
        /// Read DynamicGrid's current world footprint and update Board logic to match.
        /// Does not destroy or reset DynamicGrid unless rebuildVisual is explicitly true.
        /// </summary>
        public bool SyncFromDynamicGrid(bool rebuildVisual = false)
        {
            var dg = transform.Find("DynamicGrid");
            if (dg == null)
                return false;
            if (!TryMeasureDynamicGridWorldBounds(dg, out Bounds bounds))
                return false;

            // DynamicGrid is a child of Board — unpin it in world space while we
            // rebake Board pose/scale, otherwise moving Board drags the authored grid away.
            Vector3 dgWorldPos = dg.position;
            Quaternion dgWorldRot = dg.rotation;
            dg.SetParent(null, true);

            float worldW = Mathf.Max(0.05f, bounds.size.x);
            float worldD = Mathf.Max(0.05f, bounds.size.z);

            // Encode aspect in Board scale; CellSize drives world X cell size 1:1.
            CellSize = worldW / Size;
            float scaleZ = (worldD / Size) / Mathf.Max(1e-6f, CellSize);
            transform.localScale = new Vector3(1f, 1f, scaleZ);

            float surfaceY = bounds.center.y;
            transform.position = new Vector3(
                bounds.center.x,
                surfaceY - GridLift,
                bounds.center.z);

            dg.SetParent(transform, true);
            dg.position = dgWorldPos;
            dg.rotation = dgWorldRot;
            // World-stay reparent already recomputed localScale for the new Board
            // lossy Z. Writing the pre-unparent localScale back stretches the painted
            // tiles away from CellToWorld (spirits land left of the dance floor).

            if (rebuildVisual)
                BuildVisual(forceRebuild: true);

            return true;
        }

        /// <summary>
        /// Re-apply the Inspector-calibrated DynamicGrid pose after a visual rebuild.
        /// BuildVisual(forceRebuild) otherwise resets localPosition to zero.
        /// </summary>
        public void ApplyAuthoredDynamicGridPose()
        {
            var dg = transform.Find("DynamicGrid");
            if (dg == null)
                return;
            dg.localPosition = authoredGridLocalPosition;
            dg.localRotation = Quaternion.Euler(authoredGridLocalEuler);
            dg.localScale = authoredGridLocalScale;
            dg.gameObject.tag = "Untagged";
            dg.gameObject.layer = 0;
            UnlockDynamicGridStatic();
        }

        /// <summary>
        /// Persist the current DynamicGrid local pose into Inspector fields
        /// so a later bake / rebuild does not snap back to stale numbers.
        /// </summary>
        public void RememberAuthoredDynamicGridPose()
        {
            var dg = transform.Find("DynamicGrid");
            if (dg == null)
                return;
            authoredGridLocalPosition = dg.localPosition;
            authoredGridLocalEuler = dg.localEulerAngles;
            authoredGridLocalScale = dg.localScale;
        }

        /// <summary>
        /// Flattened +cellX / +cellY on the pick plane, from painted Cell_* tiles.
        /// Survives DynamicGrid yaw/scale (including negative X).
        /// </summary>
        public bool TryGetCellAxes(out Vector3 axisX, out Vector3 axisY)
        {
            if (_tiles[0, 0] == null)
                BindPaintedTiles();

            var t00 = _tiles[0, 0];
            var t10 = _tiles[1, 0];
            var t01 = _tiles[0, 1];
            if (t00 != null && t10 != null && t01 != null)
            {
                axisX = t10.position - t00.position;
                axisY = t01.position - t00.position;
                axisX.y = 0f;
                axisY.y = 0f;
                if (axisX.sqrMagnitude > 1e-12f && axisY.sqrMagnitude > 1e-12f)
                {
                    axisX.Normalize();
                    axisY.Normalize();
                    return true;
                }
            }

            axisX = Vector3.right;
            axisY = Vector3.forward;
            return false;
        }

        /// <summary>+cellY on the floor: player default facing, enemy faces the opposite.</summary>
        public Vector3 GridForward
        {
            get
            {
                if (TryGetCellAxes(out _, out Vector3 axisY))
                    return axisY;
                return Vector3.forward;
            }
        }

        public Quaternion GridYawRotation
        {
            get
            {
                Vector3 fwd = GridForward;
                if (fwd.sqrMagnitude < 1e-8f)
                    return Quaternion.identity;
                return Quaternion.LookRotation(fwd, Vector3.up);
            }
        }

        public Vector3 TeamForward(Team team)
        {
            Vector3 fwd = GridForward;
            return team == Team.Player ? fwd : -fwd;
        }

        /// <summary>World rotation of a floor quad that matches painted Cell_* tiles.</summary>
        public Quaternion CellQuadWorldRotation()
        {
            if (_tiles[0, 0] == null)
                BindPaintedTiles();
            var painted = _tiles[0, 0];
            if (painted != null)
                return painted.rotation;
            return Quaternion.AngleAxis(
                Mathf.Atan2(GridForward.x, GridForward.z) * Mathf.Rad2Deg,
                Vector3.up) * Quaternion.Euler(-90f, 0f, 0f);
        }

        /// <summary>
        /// Keep spirits and terrain glued to painted cells after DynamicGrid
        /// moves / rotates / scales. Does not parent them under the grid
        /// (non-uniform / negative scale would squash meshes).
        /// </summary>
        public void SnapOccupantsToGrid()
        {
            if (_snappingOccupants)
                return;
            _snappingOccupants = true;
            try
            {
                var scene = gameObject.scene;
                var units = Object.FindObjectsByType<SpiritUnit>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < units.Length; i++)
                {
                    var u = units[i];
                    if (u == null || u.gameObject.scene != scene)
                        continue;
                    if (u.name == "PlacementGhost")
                        continue;
                    u.SnapToCell(u.Cell, true);
                }

                if (_battlefield != null)
                    _battlefield.SnapVisualsToBoard();
            }
            finally
            {
                _snappingOccupants = false;
            }
        }

        /// <summary>
        /// Static / Batching Static freezes mesh transforms at Play start.
        /// Grid must stay dynamic so Game-view drag and Inspector edits actually move it.
        /// </summary>
        public void UnlockDynamicGridStatic()
        {
            var dg = transform.Find("DynamicGrid");
            if (dg == null)
                return;
            ClearStaticRecursive(dg.gameObject);
        }

        public static void ClearStaticRecursive(GameObject go)
        {
            if (go == null)
                return;
            go.isStatic = false;
            var t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                ClearStaticRecursive(t.GetChild(i).gameObject);
        }

        /// <summary>
        /// One-shot: sync Board from DynamicGrid, optionally fit arena + frame camera.
        /// Safe to call from Play boot; does not run continuously in edit mode.
        /// </summary>
        public bool ApplyDynamicGridAuthority(bool fitArena = true, bool frameCamera = true)
        {
            if (!SyncFromDynamicGrid(rebuildVisual: false))
                return false;

            if (fitArena)
            {
                var arena = GameObject.Find(ArenaEnvironment.ParallelArenaName);
                if (arena == null)
                {
                    foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                    {
                        if (t == null || !t.gameObject.scene.IsValid()) continue;
                        if (t.name == ArenaEnvironment.ParallelArenaName ||
                            t.name.StartsWith(ArenaEnvironment.LegacyArenaName))
                        {
                            arena = t.gameObject;
                            break;
                        }
                    }
                }
                if (arena != null && arena.activeInHierarchy)
                    ArenaEnvironment.FitArenaToBoard(arena);
            }

            if (frameCamera)
            {
                var focus = FindFirstObjectByType<BattleCameraFocus>();
                if (focus != null)
                    focus.FrameToBoard(this);
                else
                    BattleCameraFocus.FrameMainCameraToBoard(this);
            }

            return true;
        }

        public static bool TryMeasureDynamicGridWorldBounds(Transform dg, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            for (int i = 0; i < dg.childCount; i++)
            {
                var child = dg.GetChild(i);
                if (child == null || !child.name.StartsWith("Cell_"))
                    continue;
                var renderer = child.GetComponent<Renderer>();
                if (renderer == null)
                    continue;
                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                    bounds.Encapsulate(renderer.bounds);
            }

            if (!any)
            {
                var renderers = dg.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (renderer == null)
                        continue;
                    if (renderer.name == "GridHover" || renderer.name.StartsWith("Range_"))
                        continue;
                    if (!any)
                    {
                        bounds = renderer.bounds;
                        any = true;
                    }
                    else
                        bounds.Encapsulate(renderer.bounds);
                }
            }

            return any && bounds.size.x > 0.001f && bounds.size.z > 0.001f;
        }

        void Awake()
        {
            // Visual is rebuilt by GameBootstrap.BuildWorld → Sync/BuildVisual().
        }

        void LateUpdate()
        {
            var dg = _visualRoot != null ? _visualRoot : transform.Find("DynamicGrid");
            if (dg == null)
                return;

            Vector3 p = dg.position;
            float yaw = dg.eulerAngles.y;
            Vector3 s = dg.lossyScale;
            if (_hasDgPose
                && (p - _lastDgWorldPos).sqrMagnitude < 1e-10f
                && Mathf.Abs(Mathf.DeltaAngle(_lastDgYaw, yaw)) < 0.02f
                && (s - _lastDgLossyScale).sqrMagnitude < 1e-10f)
                return;

            _lastDgWorldPos = p;
            _lastDgYaw = yaw;
            _lastDgLossyScale = s;
            _hasDgPose = true;

            if (_snappingOccupants)
                return;

            SyncFromDynamicGrid(false);
            SnapOccupantsToGrid();
            RememberAuthoredDynamicGridPose();

            _lastDgWorldPos = dg.position;
            _lastDgYaw = dg.eulerAngles.y;
            _lastDgLossyScale = dg.lossyScale;
        }

        /// <summary>
        /// Ensure formation grid visuals exist. By default REUSES a scene-saved DynamicGrid
        /// (including its local Y) so manual height tweaks survive Play Mode.
        /// Pass forceRebuild=true only when cell layout / board scale must be regenerated.
        /// </summary>
        public void BuildVisual(bool forceRebuild = false)
        {
            // Legacy scenes sometimes stored ~0.003 which sinks under the arena carpet mesh.
            // Calibrated carpet height is ~0.0144 (was previously forced up to 0.045).
            if (gridHeightOffset < 0.008f)
                gridHeightOffset = 0.0144f;

            var existing = transform.Find("DynamicGrid");
            if (existing != null && !forceRebuild)
            {
                BindExistingVisual(existing);
                return;
            }

            DestroyAllNamedChildren("DynamicGrid");
            DestroyAllNamedChildren("GridHover");
            _visualRoot = null;
            _hoverTile = null;
            _hoverRenderer = null;
            _hoverVisible = false;

            _visualRoot = new GameObject("DynamicGrid").transform;
            // Keep under Board so you can find it in Hierarchy: GameRoot → Board → DynamicGrid
            // Counteract Board non-uniform scale so world cell size stays correct.
            _visualRoot.SetParent(transform, false);
            float invX = 1f / Mathf.Max(0.0001f, Mathf.Abs(transform.localScale.x));
            float invZ = 1f / Mathf.Max(0.0001f, Mathf.Abs(transform.localScale.z));
            _visualRoot.localPosition = Vector3.zero;
            _visualRoot.localRotation = Quaternion.identity;
            _visualRoot.localScale = new Vector3(invX, 1f, invZ);

            float scaleX = Mathf.Abs(transform.lossyScale.x);
            float scaleZ = Mathf.Abs(transform.lossyScale.z);
            float cellWorldX = CellSize * scaleX;
            float cellWorldZ = CellSize * scaleZ;

            float aHi = gridCellAlpha;
            float aLo = gridCellAlpha * 0.65f;
            Color playerA = new Color(0.15f, 0.85f, 1f, aHi);
            Color playerB = new Color(0.10f, 0.60f, 0.95f, aLo);
            Color enemyA = new Color(1f, 0.40f, 0.22f, aHi);
            Color enemyB = new Color(0.90f, 0.25f, 0.12f, aLo);

            for (int x = 0; x < Size; x++)
            {
                for (int y = 0; y < Size; y++)
                {
                    bool playerZone = y <= 3;
                    bool checker = ((x + y) & 1) == 0;
                    Color color = playerZone
                        ? (checker ? playerA : playerB)
                        : (checker ? enemyA : enemyB);

                    // CellToWorld already returns GridSurfaceY — do not add lift again.
                    var tile = CreateCellQuad(
                        "Cell_" + x + "_" + y,
                        CellToWorld(x, y),
                        cellWorldX * 0.94f,
                        cellWorldZ * 0.94f,
                        color);
                    tile.transform.SetParent(_visualRoot, true);
                }
            }

            // Zone split bar across the middle of the board — same Y as painted tiles.
            float midY = CellToWorld(0, 3).y + CellOverlayLift(1f);
            float midZ = (CellToWorld(0, 3).z + CellToWorld(0, 4).z) * 0.5f;
            // Soft divider — avoid full-bright yellow which blooms into a center glare.
            var split = CreateCellQuad(
                "ZoneSplit",
                new Vector3(Origin.x, midY, midZ),
                Size * cellWorldX * 0.98f,
                Mathf.Max(0.006f, cellWorldZ * 0.08f),
                new Color(0.55f, 0.48f, 0.12f, Mathf.Clamp01(gridCellAlpha * 0.55f)));
            split.transform.SetParent(_visualRoot, true);

            _hoverTile = CreateCellQuad(
                "GridHover",
                CellOverlayPosition(new Vector2Int(0, 0), 1f),
                cellWorldX * 0.98f,
                cellWorldZ * 0.98f,
                new Color(1f, 1f, 0.3f, gridHoverAlpha));
            // Parent under counter-scaled DynamicGrid so hover size matches cell tiles.
            _hoverTile.transform.SetParent(_visualRoot, true);
            _hoverRenderer = _hoverTile.GetComponent<Renderer>();

            _hoverTile.SetActive(false);
            _hoverVisible = false;
            BindPaintedTiles();
            // Keep DynamicGrid local Y at 0 — height is authored via gridHeightOffset
            // so pick plane / cells / hover never drift apart.
            _visualRoot.localPosition = Vector3.zero;
            // Leave DynamicGrid active so scene-saved grids stay visible for editing;
            // formation UI still toggles via SetVisualVisible.
            _visualRoot.gameObject.SetActive(true);
        }

        void BindExistingVisual(Transform existing)
        {
            _visualRoot = existing;
            // Preserve authored DynamicGrid localPosition / localScale so Scene dragging
            // is not wiped on Play. Board logic syncs from its world bounds instead.
            UnlockDynamicGridStatic();

            BindPaintedTiles();

            Transform hover = _visualRoot.Find("GridHover");
            if (hover == null)
            {
                for (int i = 0; i < _visualRoot.childCount; i++)
                {
                    var c = _visualRoot.GetChild(i);
                    if (c != null && c.name == "GridHover")
                    {
                        hover = c;
                        break;
                    }
                }
            }

            _hoverTile = hover != null ? hover.gameObject : null;
            _hoverRenderer = _hoverTile != null ? _hoverTile.GetComponent<Renderer>() : null;
            _hoverVisible = false;
            if (_hoverTile != null)
                _hoverTile.SetActive(false);
        }

        GameObject CreateCellQuad(string name, Vector3 worldPos, float sizeX, float sizeZ, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = worldPos;
            // Quad default normal is +Z; -90 X makes it face +Y (visible from above).
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            go.transform.localScale = new Vector3(sizeX, sizeZ, 1f);

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateUnlitTransparent(color);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        void DestroyAllNamedChildren(string childName)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child != null && child.name == childName)
                    DestroyImmediate(child.gameObject);
            }

            if (_visualRoot != null && _visualRoot.name == childName)
            {
                DestroyImmediate(_visualRoot.gameObject);
                _visualRoot = null;
            }

            if (_hoverTile != null && _hoverTile.name == childName)
            {
                DestroyImmediate(_hoverTile);
                _hoverTile = null;
            }

            // Clear world-rooted leftovers from earlier builds (parent == null).
            var found = Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                var t = found[i];
                if (t == null || t.name != childName || t.parent != null)
                    continue;
                DestroyImmediate(t.gameObject);
            }
        }

        void OnDestroy()
        {
            // Do NOT destroy DynamicGrid — it is a scene-saved object whose height
            // the designer tunes by hand. Only clear runtime refs.
            _visualRoot = null;
            _hoverTile = null;
            _hoverRenderer = null;
        }

        public void SetVisualVisible(bool visible)
        {
            if (_visualRoot == null || _hoverTile == null)
                BuildVisual();

            if (_visualRoot != null)
                _visualRoot.gameObject.SetActive(visible);
            if (!visible)
            {
                if (_hoverTile != null)
                    _hoverTile.SetActive(false);
                _hoverVisible = false;
            }
        }

        public void SetHoveredCell(Vector2Int cell, bool visible)
        {
            if (_hoverTile == null)
                BuildVisual();

            _hoverVisible = visible && InBounds(cell);
            if (_hoverTile == null)
                return;

            _hoverTile.SetActive(_hoverVisible);
            if (!_hoverVisible)
                return;

            float scaleX = Mathf.Abs(transform.lossyScale.x);
            float scaleZ = Mathf.Abs(transform.lossyScale.z);
            float cellWorldX = CellSize * scaleX;
            float cellWorldZ = CellSize * scaleZ;

            Vector3 cellPos = CellOverlayPosition(cell, 1f);
            _hoverTile.transform.position = cellPos;
            var painted = InBounds(cell) ? _tiles[cell.x, cell.y] : null;
            _hoverTile.transform.rotation = painted != null ? painted.rotation : CellQuadWorldRotation();
            if (painted != null)
                _hoverTile.transform.localScale = painted.localScale;
            else
                _hoverTile.transform.localScale = new Vector3(cellWorldX * 0.98f, cellWorldZ * 0.98f, 1f);

            if (_hoverRenderer != null)
            {
                bool playerZone = IsPlayerDeployZone(cell);
                float pulse = Mathf.Clamp01(gridHoverAlpha + Mathf.Sin(Time.unscaledTime * 7f) * 0.12f);
                _hoverRenderer.material.color = playerZone
                    ? new Color(0.35f, 1f, 1f, pulse)
                    : new Color(1f, 0.55f, 0.25f, pulse);
            }
        }

        /// <summary>
        /// Highlight valid Xinxiang target cells on the formation grid.
        /// </summary>
        public void ShowTargetRange(IList<Vector2Int> cells, Color color)
        {
            ShowTargetRangeLayered(cells, color, null, color, null, color);
        }

        /// <summary>
        /// Layered range: whole zone + living unit cells + optional focus cell.
        /// </summary>
        public void ShowTargetRangeLayered(
            IList<Vector2Int> zoneCells,
            Color zoneColor,
            IList<Vector2Int> unitCells,
            Color unitColor,
            Vector2Int? focusCell,
            Color focusColor)
        {
            ClearTargetRange();
            EnsureRangeRoot();

            float scaleX = Mathf.Abs(transform.lossyScale.x);
            float scaleZ = Mathf.Abs(transform.lossyScale.z);
            float cellWorldX = CellSize * scaleX;
            float cellWorldZ = CellSize * scaleZ;

            if (zoneCells != null)
            {
                for (int i = 0; i < zoneCells.Count; i++)
                    AddRangeMarker(zoneCells[i], zoneColor, cellWorldX, cellWorldZ, 1f, pulse: true);
            }

            if (unitCells != null)
            {
                for (int i = 0; i < unitCells.Count; i++)
                {
                    var cell = unitCells[i];
                    if (focusCell.HasValue && cell == focusCell.Value)
                        continue;
                    AddRangeMarker(cell, unitColor, cellWorldX, cellWorldZ, 1.6f, pulse: true);
                }
            }

            if (focusCell.HasValue)
                AddRangeMarker(focusCell.Value, focusColor, cellWorldX, cellWorldZ, 2.2f, pulse: true);
        }

        void EnsureRangeRoot()
        {
            // IMPORTANT: do NOT parent under DynamicGrid / _visualRoot.
            // FormationInput hides DynamicGrid during battle, which would also hide range.
            if (_rangeRoot != null)
            {
                if (!_rangeRoot.gameObject.activeSelf)
                    _rangeRoot.gameObject.SetActive(true);
                // Migrate out of a hidden DynamicGrid if an older build left it there.
                if (_visualRoot != null && _rangeRoot.IsChildOf(_visualRoot))
                    _rangeRoot.SetParent(transform, true);
                return;
            }

            var go = new GameObject("XinxiangRange");
            _rangeRoot = go.transform;
            _rangeRoot.SetParent(transform, false);
            _rangeRoot.localPosition = Vector3.zero;
            _rangeRoot.localRotation = Quaternion.identity;
            // Counter Board non-uniform scale so world cell sizes stay correct
            // (same trick DynamicGrid uses).
            float invX = 1f / Mathf.Max(0.0001f, Mathf.Abs(transform.localScale.x));
            float invZ = 1f / Mathf.Max(0.0001f, Mathf.Abs(transform.localScale.z));
            _rangeRoot.localScale = new Vector3(invX, 1f, invZ);
            _rangeRoot.gameObject.SetActive(true);
        }

        void AddRangeMarker(Vector2Int cell, Color color, float cellWorldX, float cellWorldZ, float layer, bool pulse)
        {
            if (!InBounds(cell)) return;

            Vector3 cellPos = CellOverlayPosition(cell, layer);
            Color hi = color;
            hi.a = Mathf.Clamp01(Mathf.Max(color.a, 0.45f));
            var marker = CreateCellQuad(
                "Range_" + cell.x + "_" + cell.y,
                cellPos,
                cellWorldX * 0.98f,
                cellWorldZ * 0.98f,
                hi);
            marker.transform.SetParent(_rangeRoot, true);
            var painted = _tiles[cell.x, cell.y];
            marker.transform.rotation = painted != null ? painted.rotation : CellQuadWorldRotation();
            if (pulse)
            {
                var pulseCmp = marker.AddComponent<XinxiangRangePulse>();
                pulseCmp.Configure(hi);
            }
            _rangeMarkers.Add(marker);
        }

        public void ClearTargetRange()
        {
            for (int i = 0; i < _rangeMarkers.Count; i++)
            {
                if (_rangeMarkers[i] != null)
                    Destroy(_rangeMarkers[i]);
            }
            _rangeMarkers.Clear();

            if (_rangeRoot != null)
            {
                for (int i = _rangeRoot.childCount - 1; i >= 0; i--)
                    Destroy(_rangeRoot.GetChild(i).gameObject);
                // Keep root alive & active for next show; just empty it.
                if (!_rangeRoot.gameObject.activeSelf)
                    _rangeRoot.gameObject.SetActive(true);
            }
        }

        static Material CreateUnlitTransparent(Color color)
        {
            // Sprites/Default without a mainTexture samples as fully transparent → grid "vanishes".
            var shader = Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Transparent")
                ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
                ?? Shader.Find("Unlit/Color");
            var material = new Material(shader);
            material.mainTexture = Texture2D.whiteTexture;
            material.color = color;
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_RendererColor"))
                material.SetColor("_RendererColor", color);
            if (material.HasProperty("_ZTest"))
                material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            if (material.HasProperty("_ZWrite"))
                material.SetInt("_ZWrite", 0);
            if (material.HasProperty("_SrcBlend"))
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.renderQueue = 3100;
            return material;
        }

        public Vector3 CellToWorld(int x, int y)
        {
            if ((uint)x < Size && (uint)y < Size)
            {
                var painted = _tiles[x, y];
                if (painted == null)
                {
                    BindPaintedTiles();
                    painted = _tiles[x, y];
                }
                if (painted != null)
                    return painted.position;
            }

            float offsetX = (Size - 1) * CellSize * transform.lossyScale.x * 0.5f;
            float offsetZ = (Size - 1) * CellSize * transform.lossyScale.z * 0.5f;
            // Y must match the painted blue/red grid (GridSurfaceY). Using Origin.y
            // buried spirit meshes under the tiles by GridLift (~0.0144).
            return Origin + new Vector3(
                x * CellSize * transform.lossyScale.x - offsetX,
                GridLift,
                y * CellSize * transform.lossyScale.z - offsetZ);
        }

        public Vector3 CellToWorld(Vector2Int cell) => CellToWorld(cell.x, cell.y);

        void BindPaintedTiles()
        {
            for (int x = 0; x < Size; x++)
            {
                for (int y = 0; y < Size; y++)
                    _tiles[x, y] = null;
            }

            var dg = _visualRoot != null ? _visualRoot : transform.Find("DynamicGrid");
            if (dg == null)
                return;
            _visualRoot = dg;
            for (int x = 0; x < Size; x++)
            {
                for (int y = 0; y < Size; y++)
                    _tiles[x, y] = dg.Find("Cell_" + x + "_" + y);
            }
        }

        public bool TryWorldToCell(Vector3 world, out Vector2Int cell)
        {
            if (_tiles[0, 0] == null)
                BindPaintedTiles();

            var t00 = _tiles[0, 0];
            var t10 = _tiles[1, 0];
            var t01 = _tiles[0, 1];
            if (t00 != null && t10 != null && t01 != null)
            {
                Vector3 origin = t00.position;
                float axx = t10.position.x - origin.x;
                float axz = t10.position.z - origin.z;
                float azx = t01.position.x - origin.x;
                float azz = t01.position.z - origin.z;
                float det = axx * azz - axz * azx;
                if (Mathf.Abs(det) > 1e-12f)
                {
                    float dx = world.x - origin.x;
                    float dz = world.z - origin.z;
                    float u = (dx * azz - dz * azx) / det;
                    float v = (axx * dz - axz * dx) / det;
                    int paintedX = Mathf.FloorToInt(u + 0.5f);
                    int paintedY = Mathf.FloorToInt(v + 0.5f);
                    if (paintedX >= 0 && paintedY >= 0 && paintedX < Size && paintedY < Size)
                    {
                        cell = new Vector2Int(paintedX, paintedY);
                        return true;
                    }
                }

                cell = default;
                return false;
            }

            // Floor against full board extents so clicking a painted tile maps to that tile
            // (Round-to-center + raised-grid parallax was sending rear clicks to wrong cells).
            float cellWorldX = CellSize * transform.lossyScale.x;
            float cellWorldZ = CellSize * transform.lossyScale.z;
            if (Mathf.Abs(cellWorldX) < 1e-6f || Mathf.Abs(cellWorldZ) < 1e-6f)
            {
                cell = default;
                return false;
            }

            float halfBoardX = Size * cellWorldX * 0.5f;
            float halfBoardZ = Size * cellWorldZ * 0.5f;
            float fx = (world.x - Origin.x + halfBoardX) / cellWorldX;
            float fz = (world.z - Origin.z + halfBoardZ) / cellWorldZ;
            int x = Mathf.FloorToInt(fx);
            int y = Mathf.FloorToInt(fz);
            if (x < 0 || y < 0 || x >= Size || y >= Size)
            {
                cell = default;
                return false;
            }
            cell = new Vector2Int(x, y);
            return true;
        }

        /// <summary>
        /// Plane used for formation mouse picking — must match GridSurfaceY.
        /// IMPORTANT: use the (normal, point) overload. Unity's (normal, float distance)
        /// stores d in n·p + d = 0, so new Plane(up, y) puts the plane at Y = -y and
        /// causes severe rear-rank / flying parallax (click A, land on B).
        /// </summary>
        public Plane GetPickPlane() => new Plane(Vector3.up, new Vector3(0f, GridSurfaceY, 0f));

        public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Size && c.y < Size;

        public bool IsPlayerDeployZone(Vector2Int c) => InBounds(c) && c.y <= 3;
        public bool IsEnemyDeployZone(Vector2Int c) => InBounds(c) && c.y >= 4;

        public bool IsEmpty(Vector2Int c) => InBounds(c) && _cells[c.x, c.y] == null;

        /// <summary>Empty of units and not blocked by a standing wall.</summary>
        public bool IsWalkable(Vector2Int c)
        {
            if (!InBounds(c) || !IsEmpty(c)) return false;
            if (_battlefield != null && _battlefield.BlocksMovement(c)) return false;
            return true;
        }

        public bool IsPlaceable(Vector2Int c)
        {
            if (!InBounds(c)) return false;
            if (_battlefield != null && _battlefield.BlocksPlacement(c)) return false;
            return true;
        }

        public SpiritUnit Get(Vector2Int c) => InBounds(c) ? _cells[c.x, c.y] : null;

        public void Set(Vector2Int c, SpiritUnit unit)
        {
            if (!InBounds(c)) return;
            _cells[c.x, c.y] = unit;
        }

        public void Clear(Vector2Int c)
        {
            if (!InBounds(c)) return;
            _cells[c.x, c.y] = null;
        }

        public void ClearAll()
        {
            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                    _cells[x, y] = null;
        }

        public IEnumerable<SpiritUnit> AllUnits()
        {
            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                    if (_cells[x, y] != null)
                        yield return _cells[x, y];
        }

        public List<SpiritUnit> GetTeam(Team team)
        {
            var list = new List<SpiritUnit>();
            foreach (var u in AllUnits())
                if (u != null && u.Team == team && u.IsAlive)
                    list.Add(u);
            return list;
        }

        public float Chebyshev(Vector2Int a, Vector2Int b)
        {
            return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        public bool TryFindPathStep(Vector2Int from, Vector2Int to, out Vector2Int next)
        {
            next = from;
            if (from == to) return false;

            int bestScore = int.MaxValue;
            Vector2Int best = from;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var cand = new Vector2Int(from.x + dx, from.y + dy);
                    if (!InBounds(cand)) continue;
                    if ((!IsEmpty(cand) && cand != to) || (_battlefield != null && _battlefield.BlocksMovement(cand) && cand != to))
                        continue;
                    int score = Mathf.Abs(cand.x - to.x) + Mathf.Abs(cand.y - to.y);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = cand;
                    }
                }
            }

            if (best == from) return false;
            next = best;
            return true;
        }

        public List<SpiritUnit> GetUnitsInAoE(Vector2Int origin, int size, Team targetTeam)
        {
            var result = new List<SpiritUnit>();
            for (int x = origin.x; x < origin.x + size; x++)
            {
                for (int y = origin.y; y < origin.y + size; y++)
                {
                    var c = new Vector2Int(x, y);
                    var u = Get(c);
                    // Grass ambushers stay untargetable until they spring the trap.
                    if (u != null && u.IsAlive && u.Team == targetTeam && !u.IsAmbushing)
                        result.Add(u);
                }
            }
            return result;
        }

        /// <summary>Cells within Chebyshev radius (1 = 3×3 including origin).</summary>
        public List<Vector2Int> GetChebyshevCells(Vector2Int origin, int radius)
        {
            var result = new List<Vector2Int>();
            if (radius < 0) return result;
            for (int x = origin.x - radius; x <= origin.x + radius; x++)
            {
                for (int y = origin.y - radius; y <= origin.y + radius; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (InBounds(c))
                        result.Add(c);
                }
            }
            return result;
        }

        public List<SpiritUnit> GetUnitsInChebyshev(Vector2Int origin, int radius, Team targetTeam)
        {
            var result = new List<SpiritUnit>();
            var cells = GetChebyshevCells(origin, radius);
            for (int i = 0; i < cells.Count; i++)
            {
                var u = Get(cells[i]);
                if (u != null && u.IsAlive && u.Team == targetTeam && !u.IsAmbushing)
                    result.Add(u);
            }
            return result;
        }
    }

    /// <summary>Soft alpha pulse for Xinxiang range tiles.</summary>
    public sealed class XinxiangRangePulse : MonoBehaviour
    {
        Color _base = Color.cyan;
        Renderer _rend;

        public void Configure(Color color)
        {
            _base = color;
            _rend = GetComponent<Renderer>();
        }

        void Update()
        {
            if (_rend == null) return;
            float pulse = 0.55f + Mathf.Sin(Time.unscaledTime * 6f) * 0.25f;
            var c = _base;
            c.a = Mathf.Clamp01(_base.a * pulse);
            if (_rend.material != null)
                _rend.material.color = c;
        }
    }
}
