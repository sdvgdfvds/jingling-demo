using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// Terrain visuals sized for the tiny 0.24 board cells.
    /// Rocks = low rubble piles (not giant boulders). Grass = short tufts.
    /// Water = shallow pool with scrolled normals (no foam blobs / VFX meshes).
    /// </summary>
    public static class TerrainVisualLibrary
    {
        static bool _loaded;
        static Material _wallMat;
        static Material _brickMat;
        static Material _grassPadMat;
        static Material _grassBladeMat;
        static Material _plantMat;
        static Material _riverMat;
        static Material _riverDeepMat;
        static Material _sandMat;
        static Material _pebbleMat;

        static Mesh[] _rockMeshes;
        static Mesh[] _brickMeshes;
        static Mesh _plantMesh;

        /// <summary>Force re-read Resources textures/meshes.</summary>
        public static void Reload()
        {
            _loaded = false;
            _wallMat = _brickMat = _grassPadMat = _grassBladeMat = _plantMat = null;
            _riverMat = _riverDeepMat = _sandMat = _pebbleMat = null;
            _rockMeshes = _brickMeshes = null;
            _plantMesh = null;
            EnsureLoaded();
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            _rockMeshes = LoadMeshSet("Terrain/Meshes/Rocks");
            _brickMeshes = LoadMeshSet("Terrain/Meshes/Bricks");
            _plantMesh = LoadSingleMesh("Terrain/Meshes/Plant");

            // --- wall / rock: prefer small-stone / path textures ---
            _wallMat = BuildLitMat(
                "wall",
                FirstTex(
                    "Terrain/Textures/rocks_ground_diff",
                    "Terrain/Textures/rock06_diff",
                    "Terrain/Textures/pebble_diff",
                    "Terrain/Textures/aerial_rocks02_diff",
                    "Terrain/Textures/rockwall_diff",
                    "Terrain/Textures/rock_diff",
                    "Terrain/Textures/wall_diff"),
                FirstTex(
                    "Terrain/Textures/rocks_ground_nor",
                    "Terrain/Textures/rock06_nor",
                    "Terrain/Textures/aerial_rocks02_nor",
                    "Terrain/Textures/rockwall_nor",
                    "Terrain/Textures/wall_nor"),
                null,
                new Color(0.70f, 0.66f, 0.60f, 1f),
                metallic: 0.04f,
                smoothness: 0.22f);

            _brickMat = BuildLitMat(
                "brick",
                FirstTex(
                    "Terrain/Textures/brick_diff",
                    "Terrain/Textures/brick_plain_diff",
                    "Terrain/Textures/pebble_diff"),
                FirstTex("Terrain/Textures/brick_nor", "Terrain/Textures/brick_plain_nor"),
                null,
                new Color(0.76f, 0.54f, 0.42f, 1f),
                metallic: 0.02f,
                smoothness: 0.18f);

            _pebbleMat = BuildLitMat(
                "pebble",
                FirstTex(
                    "Terrain/Textures/pebble_diff",
                    "Terrain/Textures/rocks_ground_diff",
                    "Terrain/Textures/rock06_diff",
                    "Terrain/Textures/rock_diff"),
                FirstTex("Terrain/Textures/rocks_ground_nor", "Terrain/Textures/rock06_nor"),
                null,
                new Color(0.62f, 0.60f, 0.56f, 1f),
                metallic: 0.03f,
                smoothness: 0.2f);

            // --- grass pad: short meadow look ---
            _grassPadMat = BuildLitMat(
                "grassPad",
                FirstTex(
                    "Terrain/Textures/grasslight",
                    "Terrain/Textures/leafy_grass_diff",
                    "Terrain/Textures/grass_ph_diff",
                    "Terrain/Textures/aerial_grass_rock_diff",
                    "Terrain/Textures/moss_diff",
                    "Terrain/Textures/grass_path_diff",
                    "Terrain/Textures/grass_diff",
                    "Terrain/Textures/grass_pad_diff"),
                FirstTex(
                    "Terrain/Textures/grasslight_nm",
                    "Terrain/Textures/grass_ph_nor",
                    "Terrain/Textures/aerial_grass_rock_nor",
                    "Terrain/Textures/moss_nor",
                    "Terrain/Textures/grass_path_nor",
                    "Terrain/Textures/grass_pad_nor"),
                null,
                new Color(0.38f, 0.70f, 0.30f, 1f),
                metallic: 0f,
                smoothness: 0.14f);

            _grassBladeMat = BuildUnlitTransparent(
                "grassBlade",
                FirstTex(
                    "Terrain/Textures/grasslight",
                    "Terrain/Textures/grass_ph_diff",
                    "Terrain/Textures/grass_diff",
                    "Terrain/Textures/leaves_diff"),
                new Color(0.30f, 0.78f, 0.26f, 0.95f));

            _plantMat = BuildLitMat(
                "plant",
                FirstTex(
                    "Terrain/Textures/leafy_grass_diff",
                    "Terrain/Textures/leaves_diff",
                    "Terrain/Textures/grasslight",
                    "Terrain/Textures/grass_diff"),
                null,
                null,
                new Color(0.26f, 0.62f, 0.20f, 1f),
                metallic: 0f,
                smoothness: 0.28f);

            // --- water: normals + soft blue tint (no bubble/foam textures) ---
            Texture waterNor = FirstTex(
                "Terrain/Textures/water_normals",
                "Terrain/Textures/water_normals2",
                "Terrain/Textures/water_n_drei",
                "Terrain/Textures/acg_water_nor",
                "Terrain/Textures/water_nor",
                "Terrain/Textures/water_noise");
            Texture waterAlb = FirstTex(
                "Terrain/Textures/acg_water_diff",
                "Terrain/Textures/water_diff",
                "Terrain/Textures/water01_diff",
                "Terrain/Textures/lake_diff",
                "Terrain/Textures/water_noise",
                "Terrain/Textures/water_splash");

            // Soft cyan shallow water
            _riverMat = BuildTransparentLit(
                "river",
                waterAlb,
                waterNor,
                new Color(0.22f, 0.62f, 0.88f, 0.55f),
                smoothness: 0.92f,
                metallic: 0.0f);

            // Slightly deeper center tint (still transparent)
            _riverDeepMat = BuildTransparentLit(
                "riverDeep",
                waterAlb,
                waterNor,
                new Color(0.10f, 0.42f, 0.72f, 0.62f),
                smoothness: 0.95f,
                metallic: 0.0f);

            _sandMat = BuildLitMat(
                "sand",
                FirstTex(
                    "Terrain/Textures/coast_sand_diff",
                    "Terrain/Textures/river_rock_diff",
                    "Terrain/Textures/river_stones_diff",
                    "Terrain/Textures/sand_diff",
                    "Terrain/Textures/sand_rocks_diff",
                    "Terrain/Textures/ground_diff"),
                FirstTex("Terrain/Textures/river_stones_nor"),
                null,
                new Color(0.78f, 0.70f, 0.50f, 1f),
                metallic: 0f,
                smoothness: 0.12f);
        }

        // ── WALL: compact rubble, not towering boulders ───────────────────────

        public static GameObject BuildWall(Vector2Int cell, float cellSize, Transform parent, float hpRatio = 1f)
        {
            EnsureLoaded();
            var root = new GameObject($"Wall_{cell.x}_{cell.y}");
            root.transform.SetParent(parent, false);

            var rng = new System.Random(cell.x * 73856093 ^ cell.y * 19349663);
            hpRatio = Mathf.Clamp01(hpRatio);

            // Low base slab so the wall reads as a short barrier.
            {
                var baseGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                baseGo.name = "Base";
                baseGo.transform.SetParent(root.transform, false);
                Object.DestroyImmediate(baseGo.GetComponent<Collider>());
                float baseH = cellSize * 0.22f * hpRatio;
                baseGo.transform.localPosition = new Vector3(0f, baseH * 0.5f, 0f);
                baseGo.transform.localScale = new Vector3(cellSize * 0.82f, baseH, cellSize * 0.82f);
                var br = baseGo.GetComponent<Renderer>();
                if (br != null) br.sharedMaterial = _wallMat;
            }

            // 2~3 small rubble chunks on top (kept under ~0.55 cell total height).
            int chunks = 2 + rng.Next(0, 2);
            for (int i = 0; i < chunks; i++)
            {
                var piece = new GameObject($"Rock_{i}");
                piece.transform.SetParent(root.transform, false);
                var mf = piece.AddComponent<MeshFilter>();
                var mr = piece.AddComponent<MeshRenderer>();

                // Prefer simple cube/sphere over huge Piloto rocks — more controllable size.
                bool useMesh = _rockMeshes != null && _rockMeshes.Length > 0 && rng.NextDouble() > 0.35;
                if (useMesh)
                {
                    mf.sharedMesh = PickRockMesh(rng);
                }
                else
                {
                    var tmp = GameObject.CreatePrimitive(
                        rng.NextDouble() > 0.5 ? PrimitiveType.Cube : PrimitiveType.Sphere);
                    mf.sharedMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                    Object.DestroyImmediate(tmp);
                }

                mr.sharedMaterial = rng.NextDouble() > 0.5
                    ? (_pebbleMat != null ? _pebbleMat : _wallMat)
                    : (_brickMat != null ? _brickMat : _wallMat);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

                // Target height: ~0.28–0.45 of a cell (was 0.85–1.3 — far too tall).
                float targetH = cellSize * (0.28f + (float)rng.NextDouble() * 0.17f) * Mathf.Lerp(0.55f, 1f, hpRatio);
                float meshH = mf.sharedMesh != null
                    ? Mathf.Max(0.01f, mf.sharedMesh.bounds.size.y)
                    : 1f;
                // Also clamp by XZ so wide rocks don't spill out of the cell.
                float meshXZ = mf.sharedMesh != null
                    ? Mathf.Max(0.01f, Mathf.Max(mf.sharedMesh.bounds.size.x, mf.sharedMesh.bounds.size.z))
                    : 1f;
                float sH = targetH / meshH;
                float sXZ = (cellSize * 0.38f) / meshXZ;
                float s = Mathf.Min(sH, sXZ);

                float ox = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.32f;
                float oz = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.32f;
                float oy = cellSize * (0.18f + i * 0.10f) * Mathf.Lerp(0.6f, 1f, hpRatio);
                piece.transform.localPosition = new Vector3(ox, oy, oz);
                piece.transform.localRotation = Quaternion.Euler(
                    (float)rng.NextDouble() * 25f,
                    (float)rng.NextDouble() * 360f,
                    (float)rng.NextDouble() * 20f);
                piece.transform.localScale = new Vector3(
                    s * (0.85f + (float)rng.NextDouble() * 0.25f),
                    s * (0.75f + (float)rng.NextDouble() * 0.3f),
                    s * (0.85f + (float)rng.NextDouble() * 0.25f));
            }

            // Tiny brick chips only (optional, very small).
            if (_brickMeshes != null && _brickMeshes.Length > 0 && rng.NextDouble() > 0.4)
            {
                int bricks = 1 + rng.Next(0, 2);
                for (int i = 0; i < bricks; i++)
                {
                    var b = new GameObject($"Brick_{i}");
                    b.transform.SetParent(root.transform, false);
                    var mf = b.AddComponent<MeshFilter>();
                    var mr = b.AddComponent<MeshRenderer>();
                    var bm = _brickMeshes[rng.Next(_brickMeshes.Length)];
                    mf.sharedMesh = bm;
                    mr.sharedMaterial = _brickMat;
                    float meshH = bm != null ? Mathf.Max(0.01f, bm.bounds.size.y) : 1f;
                    float s = (cellSize * 0.16f) / meshH;
                    b.transform.localPosition = new Vector3(
                        ((float)rng.NextDouble() - 0.5f) * cellSize * 0.35f,
                        cellSize * 0.12f * hpRatio,
                        ((float)rng.NextDouble() - 0.5f) * cellSize * 0.35f);
                    b.transform.localRotation = Quaternion.Euler(
                        (float)rng.NextDouble() * 50f,
                        (float)rng.NextDouble() * 360f,
                        (float)rng.NextDouble() * 40f);
                    b.transform.localScale = Vector3.one * s;
                }
            }

            root.transform.localScale = Vector3.one;
            return root;
        }

        public static void ApplyWallHpVisual(Transform wallRoot, float hpRatio, float cellSize)
        {
            if (wallRoot == null) return;
            hpRatio = Mathf.Clamp01(hpRatio);
            wallRoot.localScale = new Vector3(1f, 0.45f + 0.55f * hpRatio, 1f);

            var rends = wallRoot.GetComponentsInChildren<Renderer>();
            Color healthy = Color.white;
            Color damaged = new Color(1f, 0.55f, 0.45f, 1f);
            Color tint = Color.Lerp(damaged, healthy, hpRatio);
            foreach (var r in rends)
            {
                if (r == null) continue;
                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                r.SetPropertyBlock(block);
            }
        }

        // ── GRASS: short meadow, not tall bushes ──────────────────────────────

        public static GameObject BuildGrass(Vector2Int cell, float cellSize, Transform parent)
        {
            EnsureLoaded();
            var root = new GameObject($"Grass_{cell.x}_{cell.y}");
            root.transform.SetParent(parent, false);

            var rng = new System.Random(unchecked(cell.x * 83492791 ^ cell.y * 19349663));

            // Ground pad (flat)
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Pad";
            pad.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(pad.GetComponent<Collider>());
            pad.transform.localPosition = new Vector3(0f, cellSize * 0.012f, 0f);
            pad.transform.localScale = new Vector3(cellSize * 0.96f, cellSize * 0.022f, cellSize * 0.96f);
            var padR = pad.GetComponent<Renderer>();
            if (padR != null)
            {
                padR.sharedMaterial = _grassPadMat;
                padR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Short blade tufts — height ~0.18–0.32 cell (was 0.55–1.1).
            int tufts = 8 + rng.Next(0, 5);
            for (int i = 0; i < tufts; i++)
            {
                var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blade.name = $"Blade_{i}";
                blade.transform.SetParent(root.transform, false);
                Object.DestroyImmediate(blade.GetComponent<Collider>());
                float h = cellSize * (0.16f + (float)rng.NextDouble() * 0.14f);
                float w = cellSize * (0.045f + (float)rng.NextDouble() * 0.04f);
                blade.transform.localPosition = new Vector3(
                    ((float)rng.NextDouble() - 0.5f) * cellSize * 0.62f,
                    h * 0.5f,
                    ((float)rng.NextDouble() - 0.5f) * cellSize * 0.62f);
                blade.transform.localRotation = Quaternion.Euler(
                    ((float)rng.NextDouble() - 0.5f) * 16f,
                    (float)rng.NextDouble() * 360f,
                    ((float)rng.NextDouble() - 0.5f) * 16f);
                blade.transform.localScale = new Vector3(w, h, w * 0.4f);
                var br = blade.GetComponent<Renderer>();
                if (br != null)
                {
                    br.sharedMaterial = _grassBladeMat != null ? _grassBladeMat : _grassPadMat;
                    br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            // Tiny low bush spheres (not tall plant FBX).
            int bushes = 2 + rng.Next(0, 2);
            for (int i = 0; i < bushes; i++)
            {
                var bush = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bush.name = $"Bush_{i}";
                bush.transform.SetParent(root.transform, false);
                Object.DestroyImmediate(bush.GetComponent<Collider>());
                float s = cellSize * (0.14f + (float)rng.NextDouble() * 0.10f);
                bush.transform.localPosition = new Vector3(
                    ((float)rng.NextDouble() - 0.5f) * cellSize * 0.4f,
                    s * 0.4f,
                    ((float)rng.NextDouble() - 0.5f) * cellSize * 0.4f);
                bush.transform.localScale = new Vector3(s * 1.15f, s * 0.7f, s * 1.15f);
                var br = bush.GetComponent<Renderer>();
                if (br != null)
                {
                    br.sharedMaterial = _plantMat != null ? _plantMat : _grassPadMat;
                    br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            // Optional plant mesh — heavily downscaled (max ~0.32 cell).
            if (_plantMesh != null && rng.NextDouble() > 0.55)
            {
                var p = new GameObject("Plant_0");
                p.transform.SetParent(root.transform, false);
                var mf = p.AddComponent<MeshFilter>();
                var mr = p.AddComponent<MeshRenderer>();
                mf.sharedMesh = _plantMesh;
                mr.sharedMaterial = _plantMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                float plantH = Mathf.Max(0.01f, _plantMesh.bounds.size.y);
                float targetH = cellSize * (0.22f + (float)rng.NextDouble() * 0.10f);
                float s = targetH / plantH;
                p.transform.localPosition = new Vector3(
                    ((float)rng.NextDouble() - 0.5f) * cellSize * 0.25f,
                    0f,
                    ((float)rng.NextDouble() - 0.5f) * cellSize * 0.25f);
                p.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                p.transform.localScale = Vector3.one * s;
            }

            return root;
        }

        // ── RIVER: clean shallow pool ─────────────────────────────────────────

        public static GameObject BuildRiver(Vector2Int cell, float cellSize, Transform parent)
        {
            EnsureLoaded();
            var root = new GameObject($"River_{cell.x}_{cell.y}");
            root.transform.SetParent(parent, false);

            var rng = new System.Random(cell.x * 50331653 ^ cell.y * 12582917);

            // Sandy / pebble bed
            var bank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bank.name = "Bed";
            bank.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(bank.GetComponent<Collider>());
            bank.transform.localPosition = new Vector3(0f, cellSize * 0.008f, 0f);
            bank.transform.localScale = new Vector3(cellSize * 0.98f, cellSize * 0.018f, cellSize * 0.98f);
            var bankR = bank.GetComponent<Renderer>();
            if (bankR != null)
            {
                bankR.sharedMaterial = _sandMat;
                bankR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // A few tiny bed pebbles under the water
            int pebbles = 2 + rng.Next(0, 2);
            for (int i = 0; i < pebbles; i++)
            {
                var pebble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pebble.name = $"Pebble_{i}";
                pebble.transform.SetParent(root.transform, false);
                Object.DestroyImmediate(pebble.GetComponent<Collider>());
                float s = cellSize * (0.08f + (float)rng.NextDouble() * 0.07f);
                pebble.transform.localPosition = new Vector3(
                    ((float)rng.NextDouble() - 0.5f) * cellSize * 0.5f,
                    cellSize * 0.02f,
                    ((float)rng.NextDouble() - 0.5f) * cellSize * 0.5f);
                pebble.transform.localScale = new Vector3(s, s * 0.45f, s);
                var pr = pebble.GetComponent<Renderer>();
                if (pr != null)
                {
                    pr.sharedMaterial = _pebbleMat != null ? _pebbleMat : _sandMat;
                    pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            // Main water surface — thin disk, soft blue, UV-scrolled normals.
            var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            water.name = "Water";
            water.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.transform.localPosition = new Vector3(0f, cellSize * 0.035f, 0f);
            water.transform.localScale = new Vector3(cellSize * 0.92f, cellSize * 0.028f, cellSize * 0.92f);
            var wr = water.GetComponent<Renderer>();
            if (wr != null)
            {
                wr.sharedMaterial = _riverMat;
                wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var scroll = water.AddComponent<TerrainUvScroll>();
            scroll.scrollSpeed = new Vector2(0.045f + (float)rng.NextDouble() * 0.03f, 0.02f);
            scroll.secondarySpeed = new Vector2(-0.02f, 0.035f);

            // Slightly deeper inner disc for depth read (no glow lights).
            var deep = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            deep.name = "Deep";
            deep.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(deep.GetComponent<Collider>());
            deep.transform.localPosition = new Vector3(0f, cellSize * 0.038f, 0f);
            deep.transform.localScale = new Vector3(cellSize * 0.55f, cellSize * 0.02f, cellSize * 0.55f);
            var dr = deep.GetComponent<Renderer>();
            if (dr != null)
            {
                dr.sharedMaterial = _riverDeepMat != null ? _riverDeepMat : _riverMat;
                dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var deepScroll = deep.AddComponent<TerrainUvScroll>();
            deepScroll.scrollSpeed = new Vector2(-0.03f, 0.05f);

            // Soft rim ring (thin, low alpha) — not bright foam bubbles.
            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "Rim";
            rim.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(rim.GetComponent<Collider>());
            rim.transform.localPosition = new Vector3(0f, cellSize * 0.04f, 0f);
            rim.transform.localScale = new Vector3(cellSize * 0.94f, cellSize * 0.008f, cellSize * 0.94f);
            var rr = rim.GetComponent<Renderer>();
            if (rr != null)
            {
                // Reuse deep mat with a property block tint toward white-blue edge.
                rr.sharedMaterial = _riverMat;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", new Color(0.55f, 0.82f, 0.95f, 0.28f));
                block.SetColor("_Color", new Color(0.55f, 0.82f, 0.95f, 0.28f));
                rr.SetPropertyBlock(block);
            }

            return root;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        static Texture2D FirstTex(params string[] paths)
        {
            if (paths == null) return null;
            for (int i = 0; i < paths.Length; i++)
            {
                var t = LoadTex(paths[i]);
                if (t != null) return t;
            }
            return null;
        }

        static Mesh PickRockMesh(System.Random rng)
        {
            if (_rockMeshes == null || _rockMeshes.Length == 0) return null;
            return _rockMeshes[rng.Next(_rockMeshes.Length)];
        }

        static Mesh[] LoadMeshSet(string resourcesFolder)
        {
            var all = Resources.LoadAll<Mesh>(resourcesFolder);
            if (all != null && all.Length > 0) return all;
            var gos = Resources.LoadAll<GameObject>(resourcesFolder);
            if (gos == null || gos.Length == 0) return null;
            var list = new System.Collections.Generic.List<Mesh>();
            foreach (var go in gos)
            {
                if (go == null) continue;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) list.Add(mf.sharedMesh);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        static Mesh LoadSingleMesh(string resourcesPath)
        {
            var m = Resources.Load<Mesh>(resourcesPath);
            if (m != null) return m;
            var go = Resources.Load<GameObject>(resourcesPath);
            if (go != null)
            {
                var mf = go.GetComponentInChildren<MeshFilter>();
                if (mf != null) return mf.sharedMesh;
            }
            return null;
        }

        static Texture2D LoadTex(string path) => Resources.Load<Texture2D>(path);

        static Shader FindLit() =>
            Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Diffuse");

        static Shader FindUnlit() =>
            Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Transparent")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default")
            ?? FindLit();

        static Material BuildLitMat(string name, Texture albedo, Texture normal, Texture rough,
            Color tint, float metallic, float smoothness)
        {
            var mat = new Material(FindLit()) { name = $"Terrain_{name}" };
            if (albedo != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", albedo);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", albedo);
            }
            mat.color = tint;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);

            if (normal != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
                if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 1f);
            }
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            return mat;
        }

        static Material BuildTransparentLit(string name, Texture albedo, Texture normal, Color tint,
            float smoothness, float metallic)
        {
            var mat = BuildLitMat(name, albedo, normal, null, tint, metallic, smoothness);
            EnableTransparent(mat);
            return mat;
        }

        static Material BuildUnlitTransparent(string name, Texture tex, Color tint)
        {
            var mat = new Material(FindUnlit()) { name = $"Terrain_{name}" };
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }
            mat.color = tint;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            EnableTransparent(mat);
            return mat;
        }

        static void EnableTransparent(Material mat)
        {
            if (mat == null) return;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        }
    }

    /// <summary>Simple UV scroll for river water.</summary>
    public class TerrainUvScroll : MonoBehaviour
    {
        public Vector2 scrollSpeed = new Vector2(0.1f, 0.05f);
        public Vector2 secondarySpeed = Vector2.zero;
        Renderer _rend;
        Vector2 _offset;

        void Awake()
        {
            _rend = GetComponent<Renderer>();
        }

        void LateUpdate()
        {
            if (_rend == null) return;
            _offset += scrollSpeed * Time.deltaTime;
            var mat = _rend.material; // few river cells — instance OK
            if (mat.HasProperty("_BaseMap"))
                mat.SetTextureOffset("_BaseMap", _offset);
            if (mat.HasProperty("_MainTex"))
                mat.SetTextureOffset("_MainTex", _offset);
            if (mat.HasProperty("_BumpMap"))
                mat.SetTextureOffset("_BumpMap", _offset * 0.7f);
        }
    }
}
