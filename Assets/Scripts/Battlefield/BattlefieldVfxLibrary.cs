using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// Terrain event FX — prefers Piloto stock prefabs under Resources/VFX/Battlefield/.
    /// Falls back to simple primitive bursts when packs are missing.
    /// </summary>
    public static class BattlefieldVfxLibrary
    {
        const string WallBreakPath = "VFX/Battlefield/WallBreak";
        const string AmbushPath = "VFX/Battlefield/Ambush";
        const float PrefabLifetime = 2.2f;

        public static void SpawnWallBreak(Vector3 worldPos, float cellSize)
        {
            float scale = Mathf.Clamp(cellSize * 1.1f, 0.08f, 0.35f);
            if (TrySpawnPrefab(WallBreakPath, worldPos, scale))
                return;

            SpawnProceduralBurst(worldPos, scale, new Color(0.55f, 0.45f, 0.3f), new Color(0.9f, 0.75f, 0.4f));
        }

        public static void SpawnAmbush(Vector3 worldPos, float cellSize)
        {
            float scale = Mathf.Clamp(cellSize * 1.25f, 0.09f, 0.4f);
            if (TrySpawnPrefab(AmbushPath, worldPos, scale))
            {
                // Still layer a text pop so 奇袭 always reads even with prefab FX.
                return;
            }

            SpawnProceduralBurst(worldPos, scale, new Color(0.25f, 0.9f, 0.35f), new Color(0.7f, 1f, 0.4f));
        }

        /// <summary>
        /// Big world-space "奇袭!" / "出击!" billboard — pop scale + fade.
        /// </summary>
        public static void SpawnAmbushText(Vector3 worldPos, string label, float cellSize)
        {
            if (string.IsNullOrEmpty(label)) label = "奇袭!";

            var root = new GameObject("AmbushTextFX");
            root.transform.position = worldPos + Vector3.up * (cellSize * 0.15f);

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            canvasGo.transform.SetParent(root.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 80;
            var rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(180f, 56f);
            // Concert cells are ~1.3cm; the old 0.22m floor made 奇袭 cover the board.
            // Ancient China keeps the authored billboard size.
            float worldW;
            if (ArenaEnvironment.IsLegacyArchiveScene())
            {
                float baseSize = Mathf.Clamp(cellSize * 1.6f, 0.22f, 0.48f);
                worldW = 180f * 0.01f * baseSize;
            }
            else
            {
                worldW = cellSize * 0.95f;
            }
            rt.localScale = Vector3.one * (worldW / 180f);

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            textGo.transform.SetParent(canvasGo.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var text = textGo.GetComponent<UnityEngine.UI.Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 32;
            // Regular weight — Bold looked too heavy / "thick" on world billboards.
            text.fontStyle = FontStyle.Normal;
            text.color = new Color(0.45f, 1f, 0.55f, 0.95f);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            // Built-in font fallback.
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            // No ground disc / green ring — text pop only.
            var anim = root.AddComponent<AmbushTextAnimator>();
            anim.Duration = 0.95f;
            anim.Rise = cellSize * 0.55f;
            anim.Target = root.transform;
            anim.Text = text;
        }

        /// <summary>Drives ambush text pop / rise / fade without needing an external tween lib.</summary>
        sealed class AmbushTextAnimator : MonoBehaviour
        {
            public float Duration = 1.1f;
            public float Rise = 0.2f;
            public Transform Target;
            public UnityEngine.UI.Text Text;

            float _t;
            Vector3 _start;
            Vector3 _baseScale;

            void Start()
            {
                if (Target == null) Target = transform;
                _start = Target.position;
                _baseScale = Target.localScale;
                Target.localScale = _baseScale * 0.35f;
            }

            void LateUpdate()
            {
                // Billboard toward camera.
                var cam = Camera.main;
                if (cam != null && Target != null)
                {
                    Vector3 toCam = Target.position - cam.transform.position;
                    if (toCam.sqrMagnitude > 0.0001f)
                        Target.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
                }

                _t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(_t / Mathf.Max(0.01f, Duration));
                // Soft pop in, hold, fade out (no huge overshoot).
                float pop = u < 0.18f
                    ? Mathf.SmoothStep(0.55f, 1.08f, u / 0.18f)
                    : u < 0.32f
                        ? Mathf.SmoothStep(1.08f, 1f, (u - 0.18f) / 0.14f)
                        : 1f;
                float fade = u > 0.55f ? 1f - Mathf.SmoothStep(0f, 1f, (u - 0.55f) / 0.45f) : 1f;

                if (Target != null)
                {
                    Target.localScale = _baseScale * pop;
                    Target.position = _start + Vector3.up * (Rise * u);
                }

                if (Text != null)
                {
                    var c = Text.color;
                    c.a = fade;
                    Text.color = c;
                }

                if (u >= 1f)
                    Object.Destroy(gameObject);
            }
        }

        static bool TrySpawnPrefab(string resourcesPath, Vector3 pos, float scale)
        {
            var prefab = Resources.Load<GameObject>(resourcesPath);
            if (prefab == null) return false;

            var fx = Object.Instantiate(prefab, pos, Quaternion.identity);
            fx.name = prefab.name + "_Runtime";
            fx.transform.localScale = Vector3.one * scale;
            SuppressBadRenderers(fx);
            AttackVfxLibrary.PinParticleMotion(fx, AttackVfxLibrary.BoardParticleSpeedCap());
            Object.Destroy(fx, PrefabLifetime);
            return true;
        }

        static void SuppressBadRenderers(GameObject root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    string n = m.name ?? "";
                    if (n.IndexOf("Purple", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Rainbow", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Violet", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        r.enabled = false;
                        break;
                    }
                }
            }
        }

        static void SpawnProceduralBurst(Vector3 pos, float scale, Color core, Color rim)
        {
            var root = new GameObject("TerrainBurst");
            root.transform.position = pos;

            var coreGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            coreGo.name = "Core";
            coreGo.transform.SetParent(root.transform, false);
            coreGo.transform.localScale = Vector3.one * scale;
            Object.Destroy(coreGo.GetComponent<Collider>());
            var coreRend = coreGo.GetComponent<Renderer>();
            if (coreRend != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                                       ?? Shader.Find("Unlit/Color")
                                       ?? Shader.Find("Standard"));
                mat.color = core;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", core);
                coreRend.sharedMaterial = mat;
            }

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localScale = new Vector3(scale * 1.8f, scale * 0.08f, scale * 1.8f);
            Object.Destroy(ring.GetComponent<Collider>());
            var ringRend = ring.GetComponent<Renderer>();
            if (ringRend != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                                       ?? Shader.Find("Unlit/Color")
                                       ?? Shader.Find("Standard"));
                mat.color = rim;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", rim);
                ringRend.sharedMaterial = mat;
            }

            Object.Destroy(root, 0.55f);
        }
    }
}
