using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// Concert-only replacements for grass / river / wall tiles.
    /// Ancient China keeps TerrainVisualLibrary nature meshes — never call this
    /// when ArenaEnvironment.IsLegacyArchiveScene() is true.
    /// </summary>
    public static class ConcertTerrainVisuals
    {
        const string ResourceFolder = "Terrain/Concert/";
        const string KitFolder = "Assets/TirgamesAssets/PBRStageEquipment/Prefabs/";

        static readonly string[] WallPrefabs =
        {
            "Speakers01",
            "Speakers02",
            "Speakers03",
            "Speakers04",
            "FlightCase01"
        };

        static readonly string[] GrassPrefabs =
        {
            "DJControl01_1",
            "DJControl02",
            "Headphones01",
            "Microphone01"
        };

        static readonly string[] RiverPrefabs =
        {
            "SmokeMachine01"
        };

        public static GameObject BuildWall(Vector2Int cell, float cellSize, Transform parent, float hpRatio = 1f)
        {
            hpRatio = Mathf.Clamp01(hpRatio);
            var root = NewRoot($"Wall_{cell.x}_{cell.y}", parent);
            var rng = new System.Random(cell.x * 73856093 ^ cell.y * 19349663);

            string name = WallPrefabs[rng.Next(WallPrefabs.Length)];
            var prop = SpawnKit(name, root.transform);
            if (prop != null)
            {
                float h = 0.42f + (float)rng.NextDouble() * 0.14f;
                FitToCell(prop, cellSize, h * Mathf.Lerp(0.55f, 1f, hpRatio), 0.78f);
                Yaw(prop, rng);
            }
            else
            {
                MakeFallbackBox(root.transform, cellSize, 0.48f * hpRatio,
                    new Color(0.16f, 0.16f, 0.18f, 1f));
            }

            return root;
        }

        public static GameObject BuildGrass(Vector2Int cell, float cellSize, Transform parent)
        {
            var root = NewRoot($"Grass_{cell.x}_{cell.y}", parent);
            var rng = new System.Random(unchecked(cell.x * 83492791 ^ cell.y * 19349663));

            // Dance-floor patch instead of meadow.
            MakeNeonPad(root.transform, cellSize, new Color(0.10f, 0.04f, 0.16f, 1f),
                new Color(0.95f, 0.18f, 0.72f, 1f), 0.16f);

            string name = GrassPrefabs[rng.Next(GrassPrefabs.Length)];
            var prop = SpawnKit(name, root.transform);
            if (prop != null)
            {
                FitToCell(prop, cellSize, 0.22f + (float)rng.NextDouble() * 0.10f, 0.62f);
                Yaw(prop, rng);
            }

            return root;
        }

        public static GameObject BuildRiver(Vector2Int cell, float cellSize, Transform parent)
        {
            var root = NewRoot($"River_{cell.x}_{cell.y}", parent);
            var rng = new System.Random(cell.x * 50331653 ^ cell.y * 12582917);

            string name = RiverPrefabs[rng.Next(RiverPrefabs.Length)];
            var prop = SpawnKit(name, root.transform, keepParticles: true);
            if (prop != null)
            {
                FitToCell(prop, cellSize, 0.30f + (float)rng.NextDouble() * 0.08f, 0.58f);
                // Keep the crate square on the cell; only the jet aims up.
                prop.transform.localRotation = Quaternion.identity;
                PinRiverSmoke(prop);
            }

            return root;
        }

        static GameObject NewRoot(string name, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            return root;
        }

        static GameObject SpawnKit(string prefabName, Transform parent, bool keepParticles = false)
        {
            var prefab = LoadKit(prefabName);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent);
            go.name = prefabName;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            Sanitize(go, keepParticles);
            return go;
        }

        static GameObject LoadKit(string prefabName)
        {
            var fromResources = Resources.Load<GameObject>(ResourceFolder + prefabName);
            if (fromResources != null) return fromResources;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                KitFolder + prefabName + ".prefab");
#else
            return null;
#endif
        }

        static void Sanitize(GameObject go, bool keepParticles)
        {
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    Object.DestroyImmediate(cols[i]);
            }

            var lights = go.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                if (light == null) continue;
                light.enabled = false;
                light.intensity = 0f;
                light.range = 0.05f;
                light.spotAngle = 1f;
                light.cookie = null;
                light.shadows = LightShadows.None;
                light.cullingMask = 0;
            }

            if (!keepParticles)
            {
                var particles = go.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < particles.Length; i++)
                {
                    if (particles[i] != null)
                        particles[i].gameObject.SetActive(false);
                }
            }

            var anims = go.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < anims.Length; i++)
            {
                if (anims[i] != null)
                    anims[i].enabled = false;
            }

            var lines = go.GetComponentsInChildren<LineRenderer>(true);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != null)
                    lines[i].enabled = false;
            }

            var audios = go.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audios.Length; i++)
            {
                if (audios[i] != null)
                    audios[i].enabled = false;
            }
        }

        static void FitToCell(GameObject go, float cellSize, float heightFrac, float xzFrac)
        {
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one;

            if (!TryWorldBounds(go, out Bounds bounds))
                return;

            float h = Mathf.Max(1e-5f, bounds.size.y);
            float xz = Mathf.Max(1e-5f, Mathf.Max(bounds.size.x, bounds.size.z));
            float s = Mathf.Min((cellSize * heightFrac) / h, (cellSize * xzFrac) / xz);
            go.transform.localScale = Vector3.one * s;

            if (!TryWorldBounds(go, out bounds))
                return;
            Vector3 parentPos = go.transform.parent != null
                ? go.transform.parent.position
                : Vector3.zero;
            go.transform.position += new Vector3(0f, parentPos.y - bounds.min.y, 0f);
        }

        static void Yaw(GameObject go, System.Random rng)
        {
            go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        }

        /// <summary>
        /// Crate stays upright. Cone (+Z) is aimed world-up from the lid.
        /// Rate / size / speed / alpha are cut so the jet stays a light haze.
        /// </summary>
        static void PinRiverSmoke(GameObject go)
        {
            Vector3 lid = go.transform.position;
            if (TryWorldBounds(go, out Bounds box))
                lid = new Vector3(box.center.x, box.max.y, box.center.z);

            var particles = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                var ps = particles[i];
                if (ps == null) continue;
                ps.gameObject.SetActive(true);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                ps.transform.position = lid;
                ps.transform.rotation = Quaternion.LookRotation(Vector3.up);

                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.loop = true;
                main.playOnAwake = true;
                main.startSpeed = 1.1f;
                main.startLifetime = 0.45f;
                main.startSizeMultiplier = 0.32f;
                main.maxParticles = 28;
                Color tint = main.startColor.color;
                tint.a = 0.28f;
                main.startColor = tint;
                main.gravityModifier = 0f;

                var emission = ps.emission;
                emission.rateOverTime = 22f;
                emission.rateOverDistance = 0f;

                var shape = ps.shape;
                shape.angle = 8f;
                shape.radius = 0.006f;

                ps.Play(true);
            }
        }

        static bool TryWorldBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null || !r.enabled) continue;
                if (r is ParticleSystemRenderer) continue;
                if (!r.gameObject.activeInHierarchy) continue;
                if (!any)
                {
                    bounds = r.bounds;
                    any = true;
                }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        static GameObject MakeNeonPad(Transform parent, float cellSize, Color albedo, Color emission,
            float emissionStrength, bool transparent = false)
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = transparent ? "NeonPool" : "NeonPad";
            pad.transform.SetParent(parent, false);
            Object.DestroyImmediate(pad.GetComponent<Collider>());
            pad.transform.localPosition = new Vector3(0f, cellSize * 0.012f, 0f);
            pad.transform.localScale = new Vector3(cellSize * 0.94f, cellSize * 0.02f, cellSize * 0.94f);

            var rend = pad.GetComponent<Renderer>();
            if (rend != null)
            {
                var mat = new Material(FindLit()) { name = "Concert_NeonPad" };
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", albedo);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", albedo);
                mat.color = albedo;
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.15f);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.72f);
                if (mat.HasProperty("_EmissionColor"))
                {
                    Color e = emission * emissionStrength;
                    mat.SetColor("_EmissionColor", e);
                    mat.EnableKeyword("_EMISSION");
                }
                if (transparent)
                {
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                    if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
                    if (mat.HasProperty("_SrcBlend"))
                        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    if (mat.HasProperty("_DstBlend"))
                        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = 3000;
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }
                rend.material = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            return pad;
        }

        static void MakeFallbackBox(Transform parent, float cellSize, float heightFrac, Color tint)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "FallbackCrate";
            box.transform.SetParent(parent, false);
            Object.DestroyImmediate(box.GetComponent<Collider>());
            float h = cellSize * heightFrac;
            box.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            box.transform.localScale = new Vector3(cellSize * 0.72f, h, cellSize * 0.72f);
            var rend = box.GetComponent<Renderer>();
            if (rend != null)
            {
                var mat = new Material(FindLit()) { name = "Concert_Fallback" };
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
                mat.color = tint;
                rend.material = mat;
            }
        }

        static Shader FindLit() =>
            Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Diffuse");
    }
}
