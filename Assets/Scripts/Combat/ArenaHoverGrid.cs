using UnityEngine;
using UnityEngine.Rendering;

namespace NexusAnima
{
    public sealed class ArenaHoverGrid : MonoBehaviour
    {
        public const int GridSize = 8;

        [SerializeField] Renderer arenaRenderer;
        [SerializeField, Range(0.5f, 1f)] float widthRatio = 0.8f;
        [SerializeField, Range(0.5f, 1f)] float depthRatio = 0.84f;
        [SerializeField] float surfaceOffset = 0.003f;

        Transform visualRoot;
        Transform hoverTile;
        Vector2 boardSize;
        Vector3 boardCenter;
        bool built;

        public Vector2 CellSize => boardSize / GridSize;

        void Start()
        {
            Build();
        }

        void Update()
        {
            if (!built) Build();
            if (!built || Camera.main == null) return;

            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(transform.up, transform.TransformPoint(boardCenter));
            if (!plane.Raycast(ray, out float distance))
            {
                SetVisible(false);
                return;
            }

            Vector3 point = ray.GetPoint(distance);
            if (!TryGetCell(point, out Vector2Int cell))
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            hoverTile.localPosition = CellToLocal(cell) + Vector3.up * 0.001f;
            float pulse = 0.72f + Mathf.Sin(Time.unscaledTime * 5f) * 0.12f;
            hoverTile.GetComponent<Renderer>().material.color = new Color(0.92f, 0.68f, 0.18f, pulse);
        }

        public bool TryGetCell(Vector3 worldPosition, out Vector2Int cell)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition) - boardCenter;
            float normalizedX = local.x / boardSize.x + 0.5f;
            float normalizedZ = local.z / boardSize.y + 0.5f;
            int x = Mathf.FloorToInt(normalizedX * GridSize);
            int z = Mathf.FloorToInt(normalizedZ * GridSize);
            if (x < 0 || x >= GridSize || z < 0 || z >= GridSize)
            {
                cell = default;
                return false;
            }

            cell = new Vector2Int(x, z);
            return true;
        }

        public Vector3 CellToWorld(Vector2Int cell)
        {
            return transform.TransformPoint(CellToLocal(cell));
        }

        void Build()
        {
            if (arenaRenderer == null)
                arenaRenderer = GetComponentInParent<Renderer>();
            if (arenaRenderer == null) return;

            Bounds bounds = arenaRenderer.bounds;
            float boardSpan = Mathf.Min(bounds.size.x * widthRatio, bounds.size.z * depthRatio);
            boardSize = Vector2.one * boardSpan;
            boardCenter = transform.InverseTransformPoint(new Vector3(
                bounds.center.x,
                bounds.max.y + surfaceOffset,
                bounds.center.z));

            visualRoot = new GameObject("HoverVisuals").transform;
            visualRoot.SetParent(transform, false);
            CreateGridLines();
            hoverTile = CreateHoverTile();
            SetVisible(false);
            built = true;
        }

        void CreateGridLines()
        {
            Vector2 cell = CellSize;
            float halfWidth = boardSize.x * 0.5f;
            float halfDepth = boardSize.y * 0.5f;
            Color lineColor = new Color(0.98f, 0.72f, 0.24f, 0.35f);

            for (int i = 0; i <= GridSize; i++)
            {
                float x = -halfWidth + i * cell.x;
                CreateLine("Column_" + i, new Vector3(x, 0f, -halfDepth), new Vector3(x, 0f, halfDepth), lineColor);
                float z = -halfDepth + i * cell.y;
                CreateLine("Row_" + i, new Vector3(-halfWidth, 0f, z), new Vector3(halfWidth, 0f, z), lineColor);
            }
        }

        void CreateLine(string lineName, Vector3 from, Vector3 to, Color color)
        {
            var lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(visualRoot, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.startWidth = 0.008f;
            line.endWidth = 0.008f;
            line.numCapVertices = 2;
            line.material = CreateColorMaterial(color);
            line.startColor = color;
            line.endColor = color;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.SetPosition(0, boardCenter + from);
            line.SetPosition(1, boardCenter + to);
        }

        Transform CreateHoverTile()
        {
            var tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
            tile.name = "HoverTile";
            tile.transform.SetParent(visualRoot, false);
            tile.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            tile.transform.localScale = new Vector3(CellSize.x * 0.9f, CellSize.y * 0.9f, 1f);
            Destroy(tile.GetComponent<Collider>());
            var renderer = tile.GetComponent<Renderer>();
            renderer.material = CreateColorMaterial(new Color(0.92f, 0.68f, 0.18f, 0.72f));
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return tile.transform;
        }

        Vector3 CellToLocal(Vector2Int cell)
        {
            float x = -boardSize.x * 0.5f + (cell.x + 0.5f) * CellSize.x;
            float z = -boardSize.y * 0.5f + (cell.y + 0.5f) * CellSize.y;
            return boardCenter + new Vector3(x, 0f, z);
        }

        void SetVisible(bool visible)
        {
            if (visualRoot != null && visualRoot.gameObject.activeSelf != visible)
                visualRoot.gameObject.SetActive(visible);
        }

        static Material CreateColorMaterial(Color color)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { color = color };
            return material;
        }
    }
}
