using System.Collections.Generic;
using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// Runtime FX for ParallelStage (Blender "场景配布 stage【改】"):
    /// - Recreates Blender spot/area lights (FBX did not keep them)
    /// - Pulses emissive mats (亮闪闪 / shinning / lian / 材质) — Blender material
    ///   actions do not survive FBX, so we approximate the stage flicker here
    /// - Optional Stage BGM (Resources/Audio/Bgm/Stage.wav — drop Gimme×Gimme here)
    /// </summary>
    [DisallowMultipleComponent]
    public class ParallelStageFx : MonoBehaviour
    {
        const string LightsRootName = "StageLights";
        const string StageBgmKey = "Stage";

        [Header("Emission Flicker")]
        [SerializeField] bool flickerEmission = true;
        [SerializeField] float flickerSpeed = 2.4f;
        [SerializeField] float flickerMin = 0.35f;
        [SerializeField] float flickerMax = 1.15f;

        [Header("Lights")]
        [SerializeField] bool spawnLights = true;
        [SerializeField, Range(0.0005f, 0.05f)] float blenderEnergyToUnity = 0.0045f;
        [SerializeField] float lightRangeMultiplier = 1.15f;

        [Header("Audio")]
        [SerializeField] bool playStageBgm = true;

        struct EmitTarget
        {
            public Material mat;
            public Color baseEmission;
            public float seed;
        }

        readonly List<EmitTarget> _emits = new List<EmitTarget>();
        readonly List<Material> _owned = new List<Material>();
        bool _ready;
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        // Blender world (Z-up) light defs from Stage【改】.blend — converted to Unity FBX space.
        struct LightDef
        {
            public string name;
            public LightType type;
            public float energy;
            public Color color;
            public Vector3 blenderLoc;
            public Vector3 blenderEulerRad;
            public float spotSizeRad;
            public float areaSize;
        }

        static readonly LightDef[] StageLights =
        {
            new LightDef
            {
                name = "ZHEN.001", type = LightType.Spot, energy = 900f,
                color = new Color(1f, 0.30f, 0.30f),
                blenderLoc = new Vector3(-4.786f, -4.054f, 9.319f),
                blenderEulerRad = new Vector3(-0.447f, -0.236f, 1.812f),
                spotSizeRad = 1.002f
            },
            new LightDef
            {
                name = "聚光", type = LightType.Spot, energy = 1000f,
                color = new Color(1f, 0.52f, 0.93f),
                blenderLoc = new Vector3(-6.083f, -8.156f, -0.417f),
                blenderEulerRad = new Vector3(1.694f, -0.032f, -0.664f),
                spotSizeRad = 0.451f
            },
            new LightDef
            {
                name = "聚光.003", type = LightType.Spot, energy = 2200f, // capped from 5333
                color = Color.white,
                blenderLoc = new Vector3(0f, 0f, 17.918f),
                blenderEulerRad = Vector3.zero,
                spotSizeRad = 1.408f
            },
            new LightDef
            {
                name = "面光.002", type = LightType.Point, energy = 14f,
                color = new Color(0.86f, 1f, 0.94f),
                blenderLoc = new Vector3(0.142f, -1.136f, 0.171f),
                blenderEulerRad = new Vector3(2.252f, 0f, 0f),
                areaSize = 1f
            },
            new LightDef
            {
                name = "面光.003", type = LightType.Point, energy = 70f,
                color = new Color(1f, 0.86f, 0.87f),
                blenderLoc = new Vector3(0.354f, -6.341f, 1.725f),
                blenderEulerRad = new Vector3(-1.571f, 0f, -3.142f),
                areaSize = 8f
            },
            new LightDef
            {
                name = "面光.004", type = LightType.Point, energy = 16f,
                color = new Color(1f, 0.82f, 0.84f),
                blenderLoc = new Vector3(0.057f, -2.240f, -0.397f),
                blenderEulerRad = new Vector3(-1.571f, 0f, -3.142f),
                areaSize = 1f
            },
        };

        public static ParallelStageFx Ensure(GameObject stageRoot)
        {
            if (stageRoot == null) return null;
            var fx = stageRoot.GetComponent<ParallelStageFx>();
            if (fx == null)
                fx = stageRoot.AddComponent<ParallelStageFx>();
            fx.Rebuild();
            return fx;
        }

        void OnEnable()
        {
            if (Application.isPlaying)
                Rebuild();
        }

        void OnDestroy()
        {
            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i] != null)
                    Destroy(_owned[i]);
            }
            _owned.Clear();
        }

        public void Rebuild()
        {
            if (spawnLights)
                EnsureLights();

            // Material instances + BGM only at runtime (avoid dirtying shared mats in edit mode).
            if (Application.isPlaying)
            {
                if (flickerEmission)
                    CollectEmitters();
                _ready = true;
                if (playStageBgm)
                    TryPlayStageBgm();
            }
            else
            {
                _ready = false;
            }
        }

        public static void TryPlayStageBgm()
        {
            // Prefer dedicated Stage clip; fall back quietly to Formation.
            var audio = GameAudio.Ensure();
            if (audio == null) return;
            if (Resources.Load<AudioClip>("Audio/Bgm/" + StageBgmKey) != null)
                audio.PlayStageBgm();
            else
                Debug.Log("[ParallelStageFx] Resources/Audio/Bgm/Stage.wav missing — keep Formation BGM. Drop Gimme×Gimme.wav there as Stage.wav when ready.");
        }

        void EnsureLights()
        {
            var root = transform.Find(LightsRootName);
            if (root != null)
                return; // already built (edit or previous play)

            var go = new GameObject(LightsRootName);
            go.transform.SetParent(transform, false);
            root = go.transform;

            for (int i = 0; i < StageLights.Length; i++)
            {
                var def = StageLights[i];
                var lightGo = new GameObject(def.name);
                lightGo.transform.SetParent(root, false);
                lightGo.transform.localPosition = BlenderToUnityPos(def.blenderLoc);
                lightGo.transform.localRotation = BlenderToUnityRot(def.blenderEulerRad);

                var light = lightGo.AddComponent<Light>();
                light.type = def.type;
                light.color = NormalizeLightColor(def.color);
                light.intensity = Mathf.Clamp(def.energy * blenderEnergyToUnity, 0.2f, 12f);
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.35f;

                if (def.type == LightType.Spot)
                {
                    light.spotAngle = Mathf.Clamp(def.spotSizeRad * Mathf.Rad2Deg, 12f, 120f);
                    light.innerSpotAngle = light.spotAngle * 0.55f;
                    light.range = Mathf.Clamp(22f * lightRangeMultiplier, 6f, 40f);
                }
                else
                {
                    light.range = Mathf.Clamp((6f + def.areaSize) * lightRangeMultiplier, 4f, 28f);
                }
            }
        }

        void CollectEmitters()
        {
            _emits.Clear();
            var renderers = GetComponentsInChildren<Renderer>(true);
            var seen = new HashSet<Material>();

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var shared = r.sharedMaterials;
                if (shared == null) continue;

                Material[] instanced = null;
                for (int m = 0; m < shared.Length; m++)
                {
                    var src = shared[m];
                    if (src == null || !IsFlickerMaterial(src.name))
                        continue;
                    if (!src.HasProperty(EmissionColorId))
                        continue;

                    // Instance so we don't mutate the FBX shared mat across sessions.
                    if (instanced == null)
                    {
                        instanced = r.materials; // creates instances
                        for (int k = 0; k < instanced.Length; k++)
                        {
                            if (instanced[k] != null)
                                _owned.Add(instanced[k]);
                        }
                    }

                    var mat = instanced[m];
                    if (mat == null || seen.Contains(mat))
                        continue;
                    seen.Add(mat);

                    mat.EnableKeyword("_EMISSION");
                    Color baseCol = mat.GetColor(EmissionColorId);
                    if (baseCol.maxColorComponent < 0.05f)
                    {
                        // shinning imported with black emission — give it a stage glow.
                        baseCol = new Color(0.55f, 1.6f, 3.2f, 1f);
                        mat.SetColor(EmissionColorId, baseCol);
                    }

                    _emits.Add(new EmitTarget
                    {
                        mat = mat,
                        baseEmission = baseCol,
                        seed = (src.name.GetHashCode() & 0xffff) * 0.017f
                    });
                }
            }
        }

        static bool IsFlickerMaterial(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            // Strip " (Instance)"
            if (name.EndsWith(" (Instance)"))
                name = name.Substring(0, name.Length - " (Instance)".Length);

            if (name == "亮闪闪" || name == "shinning" || name == "shinning.001")
                return true;
            if (name.StartsWith("lian."))
                return true;
            if (name.StartsWith("材质.") || name == "材质")
                return true;
            if (name == "红" || name.Contains("光"))
                return true;
            return false;
        }

        void Update()
        {
            if (!_ready || !flickerEmission || _emits.Count == 0)
                return;

            float t = Time.time * flickerSpeed;
            for (int i = 0; i < _emits.Count; i++)
            {
                var e = _emits[i];
                if (e.mat == null) continue;

                // Soft stage pulse: slow sine + faster sparkle (Perlin).
                float slow = 0.5f + 0.5f * Mathf.Sin(t * 1.15f + e.seed);
                float spark = Mathf.PerlinNoise(t * 1.8f + e.seed, e.seed * 3.1f);
                float mul = Mathf.Lerp(flickerMin, flickerMax, 0.55f * slow + 0.45f * spark);
                e.mat.SetColor(EmissionColorId, e.baseEmission * mul);
            }

            // Mild intensity flicker on stage spots so "聚光" feels alive.
            var lightsRoot = transform.Find(LightsRootName);
            if (lightsRoot == null) return;
            for (int i = 0; i < lightsRoot.childCount; i++)
            {
                var light = lightsRoot.GetChild(i).GetComponent<Light>();
                if (light == null || light.type != LightType.Spot) continue;
                float baseIntensity = light.intensity; // already set; modulate via color alpha-ish
                // Store original in name hash seed — use a gentle relative pulse via range of ±18%.
                float pulse = 0.82f + 0.18f * Mathf.PerlinNoise(t * 0.9f + i * 1.7f, i * 0.37f);
                // Avoid drift: bake resting intensity into light.cullingMask unused — instead re-derive from defs.
                float rest = StageLights[Mathf.Clamp(i, 0, StageLights.Length - 1)].energy * blenderEnergyToUnity;
                light.intensity = Mathf.Clamp(rest * pulse, 0.15f, 14f);
            }
        }

        /// <summary>Blender Z-up world → Unity FBX local (same as ModelImporter axis conversion).</summary>
        static Vector3 BlenderToUnityPos(Vector3 b) => new Vector3(b.x, b.z, -b.y);

        static Quaternion BlenderToUnityRot(Vector3 eulerRad)
        {
            // Approximate: convert Blender XYZ euler (Z-up) into Unity rotation.
            var qBlender = Quaternion.Euler(eulerRad * Mathf.Rad2Deg);
            // Axis basis change: Blender (X,Y,Z) → Unity (X,Z,-Y)
            var basis = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            // Simpler practical mapping used by Blender→Unity FBX for lights:
            return Quaternion.Euler(
                eulerRad.x * Mathf.Rad2Deg,
                eulerRad.z * Mathf.Rad2Deg,
                -eulerRad.y * Mathf.Rad2Deg);
        }

        static Color NormalizeLightColor(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (max > 1.01f)
                c /= max;
            c.a = 1f;
            return c;
        }
    }
}
