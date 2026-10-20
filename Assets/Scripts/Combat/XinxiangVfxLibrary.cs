using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// Xinxiang hit VFX. Particle-first burst (no leftover opaque "blue balls"),
    /// optional Piloto Impact prefab on top, guaranteed auto-cleanup.
    /// </summary>
    public static class XinxiangVfxLibrary
    {
        public static Vector3 AnchorOn(SpiritUnit unit)
        {
            if (unit == null)
                return Vector3.zero;
            var board = unit.Board;
            if (board != null)
                return GridCellAnchor(board, unit.Cell, unit.transform.position);
            return GridAnchorFromWorld(unit.transform.position);
        }

        static Vector3 GridCellAnchor(BoardGrid board, Vector2Int cell, Vector3 fallback)
        {
            if (board == null)
                return GridAnchorFromWorld(fallback);
            Vector3 p = board.CellToWorld(cell);
            float lift = Mathf.Clamp(board.CellSize * 0.02f, 0.00008f, 0.0004f);
            return new Vector3(p.x, p.y + lift, p.z);
        }

        static Vector3 GridAnchorFromWorld(Vector3 world)
        {
            var board = Object.FindAnyObjectByType<BoardGrid>();
            if (board == null)
                return world;
            float lift = Mathf.Clamp(board.CellSize * 0.02f, 0.00008f, 0.0004f);
            return new Vector3(world.x, board.GridSurfaceY + lift, world.z);
        }

        public static GameObject Spawn(XinxiangSkillDefinition def, Transform target)
        {
            if (def == null || target == null)
                return null;
            var unit = target.GetComponentInParent<SpiritUnit>();
            if (unit != null)
                return SpawnOn(def, unit);
            return SpawnAt(def, GridAnchorFromWorld(target.position));
        }

        public static GameObject SpawnOn(
            XinxiangSkillDefinition def,
            SpiritUnit unit,
            bool cleanupFirst = true)
        {
            Vector3 pos = unit != null ? AnchorOn(unit) : Vector3.zero;
            return SpawnOn(def, unit, pos, cleanupFirst);
        }

        public static GameObject SpawnOn(
            XinxiangSkillDefinition def,
            SpiritUnit unit,
            Vector3 fallbackPos,
            bool cleanupFirst = true)
        {
            Vector3 pos = fallbackPos;
            if (unit != null)
            {
                try { pos = AnchorOn(unit); }
                catch { pos = GridAnchorFromWorld(fallbackPos); }
            }
            else
            {
                pos = GridAnchorFromWorld(fallbackPos);
            }

            var root = SpawnAt(def, pos, cleanupFirst);
            if (root != null && unit != null)
            {
                var follow = root.GetComponent<XinxiangVfxFollow>();
                if (follow == null)
                    follow = root.AddComponent<XinxiangVfxFollow>();
                follow.Bind(unit, pos);
            }
            return root;
        }

        public static GameObject SpawnAt(XinxiangSkillDefinition def, Vector3 worldPos, bool cleanupFirst = true)
        {
            if (def == null)
                return null;

            // Never leave previous cast debris (PulseCore / HitLight) on the board.
            if (cleanupFirst)
                CleanupStale();

            float cellWorld = ResolveCellWorld();
            float burstRadius = Mathf.Clamp(cellWorld * 2.4f, cellWorld * 1.1f, cellWorld * 6f);

            var root = new GameObject("XinxiangVfx_" + def.Id);
            // Never serialize into the scene if Play Mode exits mid-cast.
            root.hideFlags = HideFlags.DontSave;
            root.transform.position = worldPos;

            // Prefer stock Resources/VFX/Xinxiang prefabs (CopyAsset only — no particle edits).
            // Ice: stock only (no procedural). Buff/Heal: prefab first, light procedural fallback.
            bool prefabOk = TrySpawnImpactPrefab(def, root.transform, worldPos, cellWorld);
            if (def.Id != XinxiangSkillId.IceDamage && !prefabOk)
                CreateProceduralBurst(def, root.transform, burstRadius);

            // Safety: strip any opaque Sphere meshes (old PulseCore look).
            StripSolidSpheres(root);
            MarkTransient(root);
            AttackVfxLibrary.PinParticleMotion(root, AttackVfxLibrary.BoardParticleSpeedCap());
            // Prefab instances already PlantOnGrid inside PreparePrefabFx.
            // Do not bounds-snap: IceBurst/Heal AABBs bury or lift the real emitters.

            float life = Mathf.Clamp(def.GetVfxLifeSeconds(), 0.7f, def.Id == XinxiangSkillId.IceDamage ? 2.0f : 1.6f);
            if (Application.isPlaying)
                Object.Destroy(root, life);

            Debug.Log($"[Xinxiang] VFX spawned id={def.Id} prefabOk={prefabOk} pos={worldPos} cellWorld={cellWorld:F3} life={life:F2}");
            return root;
        }

        /// <summary>
        /// Kept for callers. Placement is the prefab pivot on DynamicGrid plus
        /// <see cref="PlantOnGrid"/> — never renderer AABB (that hid Heal / lifted IceBurst).
        /// </summary>
        public static void SnapVisualCenterTo(GameObject root, Vector3 worldAnchor)
        {
            if (root == null)
                return;
            root.transform.position = new Vector3(worldAnchor.x, worldAnchor.y, worldAnchor.z);
            PlantOnGrid(root);
        }

        /// <summary>
        /// Runtime instance only: keep the pivot on the cell, pull metre-scale child /
        /// shape Y down to about one cell so concert tiles are not empty air.
        /// Does not Stop/Clear particles (0.05s one-shots would vanish).
        /// </summary>
        public static void PlantOnGrid(GameObject fx)
        {
            if (fx == null)
                return;

            fx.transform.localPosition = Vector3.zero;

            float cell = 0.013f;
            var board = Object.FindAnyObjectByType<BoardGrid>();
            if (board != null && board.CellSize > 1e-5f)
                cell = board.CellSize;

            float lossy = Mathf.Max(1e-6f, Mathf.Abs(fx.transform.lossyScale.y));
            float maxWorld = cell * 2.2f;
            float keepWorld = cell * 0.35f;
            float keepLocal = keepWorld / lossy;

            var transforms = fx.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null || t == fx.transform)
                    continue;
                Vector3 lp = t.localPosition;
                if (Mathf.Abs(lp.y) * lossy <= maxWorld)
                    continue;
                lp.y = Mathf.Sign(lp.y) * keepLocal;
                t.localPosition = lp;
            }

            var systems = fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                if (ps == null)
                    continue;
                var shape = ps.shape;
                if (!shape.enabled)
                    continue;
                Vector3 sp = shape.position;
                if (Mathf.Abs(sp.y) * lossy <= maxWorld)
                    continue;
                sp.y = Mathf.Sign(sp.y) * keepLocal;
                shape.position = sp;
            }
        }

        /// <summary>Destroy any leftover XinxiangVfx_* roots / orphan pulse meshes still hanging in the scene.</summary>
        public static void CleanupStale()
        {
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;
                string n = t.name;

                // Full previous-cast trees.
                if (n.StartsWith("XinxiangVfx_"))
                {
                    DestroyNow(t.gameObject);
                    continue;
                }

                // Orphan debris from older builds (PulseCore opaque spheres etc.).
                if (n == "XinxiangPulseCore" || n == "XinxiangHitLight"
                    || n == "PulseCore" || n == "HitLight"
                    || n == "CoreFlash" || n == "ShockShell" || n == "ShockRing"
                    || n == "IceCoreFlash" || n == "IceShockRing" || n == "IceShard"
                    || n == "VfxVisibilityMarker")
                {
                    DestroyNow(t.gameObject);
                    continue;
                }

                // Catch unnamed leftover ice-blue spheres that got parented oddly.
                if (IsLeftoverBlueSphere(t))
                    DestroyNow(t.gameObject);
            }
        }

        static void DestroyNow(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying)
                Object.Destroy(go);
            else
                Object.DestroyImmediate(go);
        }

        static bool IsLeftoverBlueSphere(Transform t)
        {
            if (t == null) return false;
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.name.Contains("Sphere"))
                return false;
            // Only treat as Xinxiang debris when near board / small / ice-blue.
            if (t.lossyScale.x > 0.5f) return false;
            var rend = t.GetComponent<Renderer>();
            if (rend == null || rend.sharedMaterial == null) return false;
            var mat = rend.sharedMaterial;
            Color c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                : (mat.HasProperty("_Color") ? mat.GetColor("_Color") : mat.color);
            return c.b > 0.55f && c.r < 0.65f && c.g > 0.35f && c.a > 0.5f;
        }

        static void StripSolidSpheres(GameObject root)
        {
            if (root == null) return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (!mf.sharedMesh.name.Contains("Sphere")) continue;
                // Keep particle systems; kill solid sphere meshes that read as "blue balls".
                if (mf.GetComponent<ParticleSystem>() != null) continue;
                Object.Destroy(mf.gameObject);
            }
        }

        static void MarkTransient(GameObject root)
        {
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != null)
                    t.gameObject.hideFlags = HideFlags.DontSave;
            }
        }

        static float ResolveCellWorld()
        {
            var board = Object.FindAnyObjectByType<BoardGrid>();
            if (board == null)
                return 0.06f;

            float wx = board.CellSize * Mathf.Abs(board.transform.lossyScale.x);
            float wz = board.CellSize * Mathf.Abs(board.transform.lossyScale.z);
            return Mathf.Max(board.CellSize * 0.5f, 0.5f * (wx + wz));
        }

        static bool TrySpawnImpactPrefab(
            XinxiangSkillDefinition def,
            Transform parent,
            Vector3 worldPos,
            float cellWorld)
        {
            ElementType element = ElementFor(def.Id);
            GameObject prefab = null;
            bool stockXinxiang = !string.IsNullOrEmpty(def.VfxResourcePath)
                && def.VfxResourcePath.StartsWith("VFX/Xinxiang/");

            if (!string.IsNullOrEmpty(def.VfxResourcePath))
                prefab = Resources.Load<GameObject>(def.VfxResourcePath);
            if (prefab == null)
                prefab = Resources.Load<GameObject>("VFX/Impact/" + ElementFolder(element));
            // Legacy Ice Impact/Ice copies can lose materials — only fall back when not on stock path.
            if (!stockXinxiang
                && def.Id == XinxiangSkillId.IceDamage
                && (prefab == null || LooksBroken(prefab) || HasMissingMats(prefab)))
                prefab = Resources.Load<GameObject>("VFX/Impact/Water");
            if (prefab == null)
                return false;
            // Stock Xinxiang: keep original even if some mats are missing (no hand edits / swaps).
            if (!stockXinxiang && LooksBroken(prefab))
                return false;

            // Same cell metric as AttackVfxLibrary (BoardGrid.CellSize), then ×3 vs normal impact.
            float scale = ResolveXinxiangScale(def);

            var fx = Object.Instantiate(prefab, worldPos, Quaternion.identity, parent);
            // Stock DeathIce / Pickups / Healing_Rain: play as authored (no ice recolor).
            PreparePrefabFx(fx, scale, forceIceTint: false, def: def);
            TrySpawnSecondaryPrefab(def, parent, worldPos, scale);
            return true;
        }

        /// <summary>
        /// Normal attack impact uses <c>CellSize * 0.38</c>. Xinxiang should read ~3× that.
        /// </summary>
        const float AttackImpactScaleMul = 0.38f;
        const float XinxiangVsAttackMul = 3f;

        static float ResolveXinxiangScale(XinxiangSkillDefinition def)
        {
            float cellSize = 0.24f;
            var board = Object.FindAnyObjectByType<BoardGrid>();
            if (board != null && board.CellSize > 0.001f)
                cellSize = board.CellSize;

            float scale = cellSize * AttackImpactScaleMul * XinxiangVsAttackMul; // CellSize * 1.14
            // Optional mild per-skill tweak via VfxScale (1 = exact 3× attack impact).
            if (def != null && def.VfxScale > 0.01f)
                scale *= def.VfxScale;

            // Floors must follow the current cell, otherwise a 1.3cm board still
            // instantiates Ice at 8cm and its authored 5–12 m/s bursts hit the ceiling.
            float minScale = cellSize * AttackImpactScaleMul * 0.35f;
            if (def != null && def.Id == XinxiangSkillId.Heal)
                minScale = cellSize * 0.4f;
            else if (def != null && def.Id == XinxiangSkillId.Buff)
                minScale = cellSize * 0.05f;
            return Mathf.Clamp(scale, minScale, Mathf.Max(0.12f, cellSize * 8f));
        }

        static void TrySpawnSecondaryPrefab(
            XinxiangSkillDefinition def,
            Transform parent,
            Vector3 worldPos,
            float scale)
        {
            if (def == null || string.IsNullOrEmpty(def.VfxSecondaryResourcePath))
                return;

            var secondary = Resources.Load<GameObject>(def.VfxSecondaryResourcePath);
            if (secondary == null)
                return;

            float delay = Mathf.Max(0f, def.VfxSecondaryDelay);
            if (delay <= 0.001f)
            {
                var fx = Object.Instantiate(secondary, worldPos, Quaternion.identity, parent);
                PreparePrefabFx(fx, scale, forceIceTint: false, def: def);
                return;
            }

            var host = parent != null ? parent.gameObject : null;
            if (host == null)
                return;

            var runner = host.GetComponent<XinxiangDelayedVfxSpawn>();
            if (runner == null)
                runner = host.AddComponent<XinxiangDelayedVfxSpawn>();
            runner.Schedule(secondary, worldPos, scale, delay);
        }

        /// <summary>Used by delayed secondary spawn (IceBurst).</summary>
        public static void PrepareDelayed(GameObject fx, float worldScale)
        {
            PreparePrefabFx(fx, worldScale, forceIceTint: false, def: null);
        }

        static void PreparePrefabFx(
            GameObject fx,
            float worldScale,
            bool forceIceTint = false,
            XinxiangSkillDefinition def = null)
        {
            if (fx == null) return;
            fx.transform.localPosition = Vector3.zero;
            fx.transform.localRotation = Quaternion.identity;
            fx.transform.localScale = Vector3.one * worldScale;
            PlantOnGrid(fx);

            foreach (var col in fx.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            if (forceIceTint)
                SanitizeIceTint(fx);

            SuppressMagentaRenderers(fx);
            SuppressPurpleAndHdLeftovers(fx);
            AttackVfxLibrary.PinParticleMotion(fx, AttackVfxLibrary.BoardParticleSpeedCap());

            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!ps.gameObject.activeInHierarchy)
                    continue;

                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                // Do not re-enable layers SuppressPurpleAndHdLeftovers just turned off.
                if (renderer != null && !renderer.enabled)
                    continue;

                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.playOnAwake = true;
                main.useUnscaledTime = true;

                if (renderer != null)
                {
                    // Healing_Rain startSize is 13–45; tighter viewport cap so it doesn't flood the board.
                    bool heal = def != null && def.Id == XinxiangSkillId.Heal;
                    float minP = heal ? 0.08f : 0.14f;
                    float maxP = heal ? 0.16f : 0.32f;
                    renderer.maxParticleSize = Mathf.Clamp(renderer.maxParticleSize, minP, maxP);
                    renderer.minParticleSize = 0f;
                    renderer.allowRoll = true;
                }

                var emission = ps.emission;
                if (!emission.enabled)
                    emission.enabled = true;

                ps.Clear(true);
                ps.Play(true);
                // Force a visible first sample even if the frame is hitching.
                ps.Simulate(0.08f, true, true, true);
                ps.Play(true);
            }
        }

        /// <summary>
        /// Disable only renderers whose materials are null / purple / rainbow / magenta.
        /// Never deactivate transforms by name: Piloto nests real emitters under
        /// parents named DefaultHDParticleMaterial / FullRainbow*, so name-kills
        /// wipe Buff and Heal entirely (Ice keeps mesh layers so it still looked fine).
        /// </summary>
        static void SuppressPurpleAndHdLeftovers(GameObject root)
        {
            if (root == null) return;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                if (mats == null) continue;
                bool kill = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) { kill = true; break; }
                    string mn = m.name ?? "";
                    if (mn.IndexOf("Purple", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || mn.IndexOf("Rainbow", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || mn.IndexOf("Violet", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || mn.IndexOf("Magenta", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || mn.IndexOf("DefaultHDParticle", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        kill = true;
                        break;
                    }
                }

                if (!kill) continue;
                renderer.enabled = false;
                var ps = renderer.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    var emission = ps.emission;
                    emission.enabled = false;
                }
            }
        }

        /// <summary>
        /// Kill rainbow/purple addons and push particle colors toward ice-blue.
        /// </summary>
        static void SanitizeIceTint(GameObject root)
        {
            if (root == null) return;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.gameObject == root) continue;
                string n = t.name;
                if (n.IndexOf("Rainbow", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("FullRainbow", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("DiagonalFlare_ConalSpliff", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Distorsion", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Distort", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    t.gameObject.SetActive(false);
                }
            }

            Color ice = new Color(0.4f, 0.85f, 1f, 1f);
            Color iceHot = new Color(0.75f, 0.95f, 1f, 1f);
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                var main = ps.main;
                var g = main.startColor;
                if (g.mode == ParticleSystemGradientMode.Color)
                {
                    if (LooksPurple(g.color))
                        main.startColor = ice;
                }
                else if (g.mode == ParticleSystemGradientMode.TwoColors)
                {
                    if (LooksPurple(g.colorMin) || LooksPurple(g.colorMax))
                        main.startColor = new ParticleSystem.MinMaxGradient(iceHot, ice);
                }
            }
        }

        static bool LooksPurple(Color c)
        {
            if (c.r > 0.85f && c.b > 0.85f && c.g < 0.25f) return true; // magenta
            return c.r > 0.45f && c.b > 0.55f && c.g < 0.4f && c.b > c.g + 0.15f;
        }

        /// <summary>
        /// Guaranteed-visible ice hit: opaque emissive shards (not solid blue balls)
        /// + reliable particle burst. Transparent URP materials were often invisible.
        /// </summary>
        static void CreateIceHitBurst(Transform parent, float radius)
        {
            Color ice = new Color(0.45f, 0.88f, 1f, 1f);
            Color iceHot = new Color(0.85f, 0.97f, 1f, 1f);
            float r = Mathf.Max(0.22f, radius);

            // Bright core disc (flat cylinder — flash plate, not a solid ball).
            CreateEmissiveDisc(parent, "IceCoreFlash", Vector3.up * (r * 0.02f), r * 0.72f, iceHot, 0.4f, 3.6f);

            // Expanding ground ring.
            CreateEmissiveDisc(parent, "IceShockRing", Vector3.down * (r * 0.04f), r * 0.42f, ice, 0.7f, 2.4f, expandMul: 4.2f, thin: true);

            // Ice crystal shards flying outward (cubes/capsules — StripSolidSpheres ignores these).
            int shards = 12;
            for (int i = 0; i < shards; i++)
            {
                float ang = (i / (float)shards) * Mathf.PI * 2f + 0.15f;
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0.45f + (i % 3) * 0.15f, Mathf.Sin(ang)).normalized;
                CreateIceShard(parent, dir, r, ice, iceHot, i);
            }

            // Dense particle sparkles — Sprites/Default always renders.
            CreateReliableParticles(parent, "IceSparkBurst", ice, r, count: 56, sizeMul: 1.25f);
            CreateReliableParticles(parent, "IceSparkInner", iceHot, r * 0.6f, count: 28, sizeMul: 0.9f);

            CreateFadingLight(parent, ice, r);
        }

        static void CreateIceShard(Transform parent, Vector3 dir, float radius, Color ice, Color hot, int index)
        {
            bool tall = (index % 2) == 0;
            var go = GameObject.CreatePrimitive(tall ? PrimitiveType.Capsule : PrimitiveType.Cube);
            go.name = "IceShard";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = dir * (radius * 0.08f);
            go.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up)
                * Quaternion.Euler(90f, index * 37f, index * 19f);

            float len = radius * (tall ? 0.72f : 0.5f);
            float thick = radius * (tall ? 0.1f : 0.14f);
            go.transform.localScale = tall
                ? new Vector3(thick, len * 0.5f, thick)
                : new Vector3(thick, thick * 0.55f, len);

            ApplyEmissiveOpaque(go, Color.Lerp(ice, hot, (index % 3) * 0.2f), 2.8f);
            var pulse = go.AddComponent<XinxiangIceShardFly>();
            pulse.Configure(dir, radius * (0.55f + (index % 4) * 0.12f), 0.55f + (index % 3) * 0.08f);
        }

        static void CreateEmissiveDisc(
            Transform parent,
            string name,
            Vector3 localPos,
            float radius,
            Color color,
            float duration,
            float emission,
            float expandMul = 2.1f,
            bool thin = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            float h = thin ? radius * 0.02f : radius * 0.045f;
            go.transform.localScale = new Vector3(radius, h, radius);
            ApplyEmissiveOpaque(go, color, emission);
            var pulse = go.AddComponent<XinxiangBurstPulse>();
            pulse.Configure(duration, expandMul: expandMul, fade: true);
        }

        static void CreateReliableParticles(
            Transform parent,
            string name,
            Color color,
            float radius,
            int count = 36,
            float sizeMul = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.55f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.6f, radius * 3.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.28f * sizeMul, radius * 0.7f * sizeMul);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Color.Lerp(color, Color.white, 0.6f),
                color);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = 0f;
            main.maxParticles = Mathf.Max(96, count * 2);
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.useUnscaledTime = true;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius * 0.12f;

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(color, 0.25f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.4f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLife.color = grad;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.08f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.maxParticleSize = 0.55f;
            renderer.minParticleSize = 0f;
            renderer.sharedMaterial = MakeReliableParticleMat(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            ps.Play(true);
            ps.Simulate(0.05f, true, true, true);
            ps.Play(true);
        }

        static Material MakeReliableParticleMat(Color color)
        {
            // Sprites/Default is the most reliable across RP setups.
            var shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Legacy Shaders/Particles/Additive");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            mat.color = color;
            mat.renderQueue = 3000;
            return mat;
        }

        static void ApplyEmissiveOpaque(GameObject go, Color color, float emission)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend == null) return;

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            Color c = color;
            c.a = 1f;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            mat.color = c;
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * emission);
            }
            // Keep opaque — transparent URP Unlit was rendering invisible on this project.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
            mat.renderQueue = 2000;
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }

        static void CreateProceduralBurst(XinxiangSkillDefinition def, Transform parent, float radius)
        {
            Color color = def.FrameColor;
            color.a = 1f;
            Color hot = Color.Lerp(color, Color.white, 0.55f);

            // Soft flash discs — emissive opaque (reliable). NOT solid spheres.
            CreateEmissiveDisc(parent, "CoreFlash", Vector3.zero, radius * 1.15f, hot, 0.4f, 2.8f);
            CreateEmissiveDisc(parent, "ShockShell", Vector3.up * (radius * 0.05f), radius * 1.7f, color, 0.55f, 2.0f, expandMul: 2.4f, thin: true);

            CreateGroundRing(parent, color, radius);

            CreateReliableParticles(parent, "SparkBurst", color, radius);
            CreateReliableParticles(parent, "SparkBurstInner", hot, radius * 0.55f, count: 18, sizeMul: 0.7f);

            CreateFadingLight(parent, color, radius);
        }

        static void CreateAdditiveFlash(
            Transform parent,
            string name,
            Vector3 localPos,
            float size,
            Color color,
            float duration)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * Mathf.Max(0.05f, size);
            // Face camera-ish upward tilt so it reads from the board cam.
            go.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);

            ApplyAdditive(go, color, 2.8f);
            var pulse = go.AddComponent<XinxiangBurstPulse>();
            pulse.Configure(duration, expandMul: 1.85f, fade: true);
        }

        static void CreateGroundRing(Transform parent, Color color, float radius)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "ShockRing";
            Object.Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = Vector3.down * (radius * 0.08f);
            ring.transform.localScale = new Vector3(radius * 0.55f, radius * 0.015f, radius * 0.55f);
            ApplyAdditive(ring, color, 1.8f);
            var ringPulse = ring.AddComponent<XinxiangBurstPulse>();
            ringPulse.Configure(0.7f, expandMul: 3.6f, fade: true);
        }

        static void CreateFadingLight(Transform parent, Color color, float radius)
        {
            var lightGo = new GameObject("HitLight");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = Vector3.up * (radius * 0.25f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = 2.2f;
            light.range = Mathf.Clamp(radius * 3.5f, 0.4f, 1.4f);
            var fade = lightGo.AddComponent<XinxiangLightFade>();
            fade.Configure(0.55f, light.intensity);
        }

        static void CreateWorldParticles(
            Transform parent,
            string name,
            Color color,
            float radius,
            int count = 36,
            float sizeMul = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.4f, radius * 3.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.22f * sizeMul, radius * 0.55f * sizeMul);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Color.Lerp(color, Color.white, 0.55f),
                color);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = 0f;
            main.maxParticles = Mathf.Max(64, count * 2);
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius * 0.1f;

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(color, 0.3f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.9f, 0.35f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLife.color = grad;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.05f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.maxParticleSize = 0.45f;
            renderer.minParticleSize = 0f;
            renderer.sharedMaterial = MakeAdditiveParticleMat(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            ps.Play(true);
        }

        static Material MakeAdditiveParticleMat(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader);
            // Prefer additive / transparent so flashes don't look like solid meshes.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // Transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);     // Additive
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            mat.color = color;
            mat.renderQueue = 3000;
            TryEnableTransparent(mat);
            return mat;
        }

        static void ApplyAdditive(GameObject go, Color color, float emission)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend == null) return;

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            Color c = color;
            c.a = 0.85f;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            mat.color = c;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * emission);
            }
            TryEnableTransparent(mat);
            mat.renderQueue = 3000;
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }

        static void TryEnableTransparent(Material mat)
        {
            if (mat == null) return;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One); // additive-ish
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }

        static ElementType ElementFor(XinxiangSkillId id)
        {
            switch (id)
            {
                case XinxiangSkillId.IceDamage: return ElementType.Water;
                case XinxiangSkillId.Buff: return ElementType.Light;
                case XinxiangSkillId.Heal: return ElementType.Light;
                default: return ElementType.Light;
            }
        }

        static string ElementFolder(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return "Fire";
                case ElementType.Water: return "Water";
                case ElementType.Grass: return "Grass";
                case ElementType.Light: return "Light";
                default: return "Fire";
            }
        }

        static bool LooksBroken(GameObject prefab)
        {
            if (prefab == null) return true;
            int total = 0;
            int broken = 0;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    total++;
                    if (IsBrokenMaterial(mats[i]))
                        broken++;
                }
            }
            return total > 0 && broken * 2 >= total;
        }

        static bool HasMissingMats(GameObject prefab)
        {
            if (prefab == null) return true;
            int missing = 0;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] == null) missing++;
            }
            return missing > 0;
        }

        static void SuppressMagentaRenderers(GameObject root)
        {
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                bool anyValid = false;
                if (mats != null)
                {
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (!IsBrokenMaterial(mats[i]))
                        {
                            anyValid = true;
                            break;
                        }
                    }
                }

                if (anyValid)
                    continue;

                renderer.enabled = false;
                var ps = renderer.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    var emission = ps.emission;
                    emission.enabled = false;
                }
            }
        }

        static bool IsBrokenMaterial(Material m)
        {
            if (m == null || m.shader == null) return true;
            if (!m.shader.isSupported) return true;
            string n = m.shader.name;
            if (string.IsNullOrEmpty(n)) return true;
            return n.Contains("InternalError");
        }
    }

    /// <summary>
    /// Keeps a world-space VFX glued to the spirit's DynamicGrid cell.
    /// </summary>
    public sealed class XinxiangVfxFollow : MonoBehaviour
    {
        SpiritUnit _unit;
        Vector3 _offset;

        public void Bind(SpiritUnit unit, Vector3 spawnAnchor)
        {
            _unit = unit;
            _offset = transform.position - spawnAnchor;
        }

        void LateUpdate()
        {
            if (_unit == null)
                return;
            Vector3 anchor;
            try { anchor = XinxiangVfxLibrary.AnchorOn(_unit); }
            catch
            {
                _unit = null;
                return;
            }
            transform.position = anchor + _offset;
        }
    }

    /// <summary>Spawns a second stock prefab after a short delay (Ice dome → burst).</summary>
    public sealed class XinxiangDelayedVfxSpawn : MonoBehaviour
    {
        GameObject _prefab;
        Vector3 _worldPos;
        float _scale;
        float _delay;
        float _t;
        bool _armed;

        public void Schedule(GameObject prefab, Vector3 worldPos, float scale, float delay)
        {
            _prefab = prefab;
            _worldPos = worldPos;
            _scale = scale;
            _delay = Mathf.Max(0.01f, delay);
            _t = 0f;
            _armed = prefab != null;
        }

        void Update()
        {
            if (!_armed)
                return;
            _t += Time.unscaledDeltaTime;
            if (_t < _delay)
                return;
            _armed = false;
            if (_prefab == null)
                return;
            var fx = Object.Instantiate(_prefab, transform);
            XinxiangVfxLibrary.PrepareDelayed(fx, _scale);
            if (fx != null)
            {
                foreach (var t in fx.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null)
                        t.gameObject.hideFlags = HideFlags.DontSave;
                }
            }
        }
    }

    /// <summary>
    /// Expands then destroys a flash mesh. Dims color/emission rather than relying on opaque alpha.
    /// </summary>
    public sealed class XinxiangBurstPulse : MonoBehaviour
    {
        float _duration = 0.5f;
        float _expandMul = 2.5f;
        bool _fade;
        Vector3 _baseScale;
        float _t;
        Material _mat;
        Color _baseColor;

        public void Configure(float duration, float expandMul = 2.5f, bool fade = true)
        {
            _duration = Mathf.Max(0.08f, duration);
            _expandMul = Mathf.Max(1.1f, expandMul);
            _fade = fade;
            _baseScale = transform.localScale;
            _t = 0f;
            var rend = GetComponent<Renderer>();
            if (rend != null)
            {
                _mat = Application.isPlaying ? rend.material : rend.sharedMaterial;
                if (_mat.HasProperty("_BaseColor"))
                    _baseColor = _mat.GetColor("_BaseColor");
                else if (_mat.HasProperty("_Color"))
                    _baseColor = _mat.GetColor("_Color");
                else
                    _baseColor = _mat.color;
            }
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(_t / _duration);
            float envelope = Mathf.Sin(u * Mathf.PI);
            float s = Mathf.Lerp(1f, _expandMul, envelope);
            transform.localScale = _baseScale * s;

            if (_fade && _mat != null)
            {
                Color c = _baseColor;
                c.a = _baseColor.a * (1f - u);
                c = Color.Lerp(c, Color.clear, u * 0.7f);
                if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
                if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", c);
                _mat.color = c;
                if (_mat.HasProperty("_EmissionColor"))
                    _mat.SetColor("_EmissionColor", c * (2.2f * (1f - u)));
            }

            if (u >= 1f)
                Destroy(gameObject);
        }
    }

    /// <summary>Fades a point light to zero then destroys itself.</summary>
    public sealed class XinxiangLightFade : MonoBehaviour
    {
        Light _light;
        float _duration = 0.55f;
        float _startIntensity = 2f;
        float _t;

        public void Configure(float duration, float startIntensity)
        {
            _duration = Mathf.Max(0.05f, duration);
            _startIntensity = startIntensity;
            _light = GetComponent<Light>();
            _t = 0f;
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(_t / _duration);
            if (_light != null)
                _light.intensity = Mathf.Lerp(_startIntensity, 0f, u * u);
            if (u >= 1f)
                Destroy(gameObject);
        }
    }

    /// <summary>Flings an ice crystal shard outward then fades/destroys.</summary>
    public sealed class XinxiangIceShardFly : MonoBehaviour
    {
        Vector3 _dir = Vector3.up;
        float _distance = 0.3f;
        float _duration = 0.55f;
        Vector3 _start;
        Vector3 _baseScale;
        float _t;
        Material _mat;
        Color _baseColor;

        public void Configure(Vector3 dir, float distance, float duration)
        {
            _dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.up;
            _distance = Mathf.Max(0.05f, distance);
            _duration = Mathf.Max(0.1f, duration);
            _start = transform.localPosition;
            _baseScale = transform.localScale;
            _t = 0f;
            var rend = GetComponent<Renderer>();
            if (rend != null)
            {
                _mat = Application.isPlaying ? rend.material : rend.sharedMaterial;
                if (_mat != null)
                {
                    if (_mat.HasProperty("_BaseColor"))
                        _baseColor = _mat.GetColor("_BaseColor");
                    else if (_mat.HasProperty("_Color"))
                        _baseColor = _mat.GetColor("_Color");
                    else
                        _baseColor = _mat.color;
                }
            }
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(_t / _duration);
            float ease = 1f - (1f - u) * (1f - u);
            transform.localPosition = _start + _dir * (_distance * ease);
            transform.localScale = _baseScale * Mathf.Lerp(1f, 0.15f, u * u);
            transform.Rotate(_dir, 420f * Time.unscaledDeltaTime, Space.World);

            if (_mat != null)
            {
                Color c = Color.Lerp(_baseColor, Color.clear, u * 0.85f);
                if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
                if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", c);
                _mat.color = c;
                if (_mat.HasProperty("_EmissionColor"))
                    _mat.SetColor("_EmissionColor", c * (2.4f * (1f - u)));
            }

            if (u >= 1f)
                Destroy(gameObject);
        }
    }
}
