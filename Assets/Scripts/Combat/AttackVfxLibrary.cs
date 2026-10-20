using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// Resolves melee slash / ranged projectile VFX by element.
    /// Prefers Resources prefabs linked from Piloto Super Realistic ARPG FX
    /// (menu: NexusAnima/Link Free VFX Packs); falls back to procedural FX.
    /// Expected Resources layout:
    ///   Resources/VFX/Melee/{Fire|Water|Grass|Light}
    ///   Resources/VFX/Ranged/{Fire|Water|Grass|Light}
    ///   Resources/VFX/Impact/{Fire|Water|Grass|Light}
    /// </summary>
    public static class AttackVfxLibrary
    {
        const string MeleeRoot = "VFX/Melee/";
        const string RangedRoot = "VFX/Ranged/";
        const string ImpactRoot = "VFX/Impact/";

        // Board cells are tiny (~0.24). Melee FX stay near the target cell but must still read clearly.
        const float PrefabMeleeScale = 0.52f;
        const float PrefabRangedScaleMul = 1.15f;
        // WaterSphere uses huge mesh domes; root-scale alone is ignored by screen-size clamps.
        // CompactWaterRangedParticles rewrites particle sizes — keep root near other projectiles.
        const float PrefabWaterRangedScaleMul = 1.0f;
        const float PrefabImpactScale = 0.38f;
        const float ProceduralMeleeScale = 0.32f;
        const float ProceduralImpactScale = 0.5f;
        const float PrefabDestroyDelay = 2.4f;
        // Allow slash streaks a bit past the cell center so the cut reads clearly.
        const float MeleeParticleReachMul = 0.85f;

        public static void SpawnMeleeHit(
            ElementType element,
            Vector3 from,
            Vector3 to,
            float cellSize,
            Team team)
        {
            // Anchor on the attacked cell, not the midpoint between units.
            Vector3 targetPos = to;
            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
                dir = Vector3.forward;
            dir.Normalize();

            var prefab = Resources.Load<GameObject>(MeleeRoot + ElementFolder(element));
            if (prefab != null)
            {
                var fx = Object.Instantiate(prefab, targetPos, Quaternion.LookRotation(dir, Vector3.up));
                PrepareSpawnedFx(fx, cellSize * PrefabMeleeScale);
                ConstrainMeleeParticlesToCell(fx, cellSize);
                BoostMeleeVisibility(fx);
                PinParticleMotion(fx, BoardParticleSpeedCap());
                Object.Destroy(fx, PrefabDestroyDelay);
                return;
            }

            SpawnProceduralMeleeSlash(element, targetPos, dir, cellSize * ProceduralMeleeScale, team);
        }

        public static GameObject SpawnRangedProjectileVisual(
            ElementType element,
            Vector3 start,
            float size)
        {
            var prefab = Resources.Load<GameObject>(RangedRoot + ElementFolder(element));
            if (prefab != null)
            {
                var fx = Object.Instantiate(prefab, start, Quaternion.identity);
                float mul = element == ElementType.Water
                    ? PrefabWaterRangedScaleMul
                    : PrefabRangedScaleMul;
                PrepareSpawnedFx(fx, size * mul);
                if (element == ElementType.Water)
                    CompactWaterRangedParticles(fx, size);
                PinParticleMotion(fx, BoardParticleSpeedCap());
                return fx;
            }

            return CreateProceduralProjectile(element, start, size);
        }

        public static void SpawnImpact(ElementType element, Vector3 position, float cellSize)
        {
            var prefab = Resources.Load<GameObject>(ImpactRoot + ElementFolder(element));
            if (prefab != null)
            {
                var fx = Object.Instantiate(prefab, position, Quaternion.identity);
                PrepareSpawnedFx(fx, cellSize * PrefabImpactScale);
                Object.Destroy(fx, PrefabDestroyDelay);
                return;
            }

            SpawnProceduralImpact(element, position, cellSize * ProceduralImpactScale);
        }

        static void PrepareSpawnedFx(GameObject fx, float worldScale)
        {
            if (fx == null) return;
            ScaleFx(fx.transform, worldScale);
            DisableColliders(fx);
            SuppressMagentaRenderers(fx);
            PinParticleMotion(fx, BoardParticleSpeedCap());
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (!ps.isPlaying)
                    ps.Play(true);
            }
        }

        /// <summary>
        /// Concert board cells are centimetres; Piloto FX is authored in metres.
        /// World simulation and velocity-over-lifetime Y (7–15) therefore fly into
        /// the ceiling. Clamp the spawned instance only — never edit stock prefabs.
        /// </summary>
        public static float BoardParticleSpeedCap()
        {
            float cell = 0.08f;
            var board = Object.FindAnyObjectByType<BoardGrid>();
            if (board != null && board.CellSize > 0.0001f)
                cell = board.CellSize;
            // ~3 cells/sec so a 0.5–1s burst stays on the tile, not the sky.
            return Mathf.Clamp(cell * 3.2f, 0.018f, 0.10f);
        }

        public static void PinParticleMotion(GameObject fx, float maxWorldSpeed)
        {
            if (fx == null) return;
            maxWorldSpeed = Mathf.Clamp(maxWorldSpeed, 0.018f, 0.12f);

            float rootScale = Mathf.Max(0.0008f, Mathf.Abs(fx.transform.lossyScale.x));
            float maxLocalSpeed = maxWorldSpeed / rootScale;

            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;

                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.gravityModifier = 0f;

                var inherit = ps.inheritVelocity;
                inherit.enabled = false;

                main.startSpeed = ClampUnsignedCurve(main.startSpeed, maxLocalSpeed);

                var vol = ps.velocityOverLifetime;
                if (vol.enabled)
                {
                    // VOL is often authored in world metres and ignores hierarchy scale.
                    vol.space = ParticleSystemSimulationSpace.Local;
                    vol.x = ClampSignedCurve(vol.x, maxWorldSpeed);
                    vol.y = ClampSignedCurve(vol.y, maxWorldSpeed);
                    vol.z = ClampSignedCurve(vol.z, maxWorldSpeed);
                }

                var force = ps.forceOverLifetime;
                if (force.enabled)
                {
                    force.space = ParticleSystemSimulationSpace.Local;
                    force.x = ClampSignedCurve(force.x, maxWorldSpeed);
                    force.y = ClampSignedCurve(force.y, maxWorldSpeed);
                    force.z = ClampSignedCurve(force.z, maxWorldSpeed);
                }

                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.separateAxes = false;
                limit.space = ParticleSystemSimulationSpace.World;
                limit.limit = maxWorldSpeed;
                limit.dampen = 1f;

                var ext = ps.externalForces;
                ext.enabled = false;
            }
        }

        static ParticleSystem.MinMaxCurve ClampUnsignedCurve(ParticleSystem.MinMaxCurve curve, float max)
        {
            max = Mathf.Max(0.001f, max);
            if (curve.mode == ParticleSystemCurveMode.TwoConstants)
            {
                float a = Mathf.Clamp(curve.constantMin, 0f, max);
                float b = Mathf.Clamp(curve.constantMax, 0f, max);
                return new ParticleSystem.MinMaxCurve(a, b);
            }

            if (curve.mode == ParticleSystemCurveMode.Constant)
                return new ParticleSystem.MinMaxCurve(Mathf.Clamp(curve.constant, 0f, max));

            float v = Mathf.Abs(curve.curveMultiplier);
            if (v < 0.0001f)
                v = Mathf.Abs(curve.constantMax);
            if (v < 0.0001f)
                v = max * 0.5f;
            return new ParticleSystem.MinMaxCurve(Mathf.Min(v, max));
        }

        static ParticleSystem.MinMaxCurve ClampSignedCurve(ParticleSystem.MinMaxCurve curve, float maxAbs)
        {
            maxAbs = Mathf.Max(0.001f, maxAbs);
            if (curve.mode == ParticleSystemCurveMode.TwoConstants)
            {
                return new ParticleSystem.MinMaxCurve(
                    Mathf.Clamp(curve.constantMin, -maxAbs, maxAbs),
                    Mathf.Clamp(curve.constantMax, -maxAbs, maxAbs));
            }

            if (curve.mode == ParticleSystemCurveMode.Constant)
                return new ParticleSystem.MinMaxCurve(Mathf.Clamp(curve.constant, -maxAbs, maxAbs));

            float v = curve.constant;
            if (Mathf.Abs(v) < 0.0001f)
                v = curve.curveMultiplier;
            return new ParticleSystem.MinMaxCurve(Mathf.Clamp(v, -maxAbs, maxAbs));
        }

        /// <summary>
        /// Particle systems with missing materials render as magenta under any RP.
        /// Disable those layers so only valid UberFXSG materials remain visible.
        /// </summary>
        static void SuppressMagentaRenderers(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                bool anyValid = false;
                if (mats != null)
                {
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m != null
                            && m.shader != null
                            && m.shader.isSupported
                            && (m.shader.name == null || !m.shader.name.Contains("InternalError")))
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

        static void ScaleFx(Transform root, float worldScale)
        {
            if (root == null) return;
            root.localScale = Vector3.one * Mathf.Max(0.02f, worldScale);
        }

        /// <summary>
        /// WaterSphere_Projectile is authored at cinematic scale. Shrinking the root
        /// barely changes the look because mesh domes + ParticleSystemRenderer.maxParticleSize
        /// (viewport fraction) keep the silhouette large. Disable the big shells and
        /// rewrite remaining particle sizes to board-projectile scale.
        /// </summary>
        static void CompactWaterRangedParticles(GameObject fx, float boardSize)
        {
            if (fx == null || boardSize <= 0.0001f) return;

            // Keep a soft core roughly the same world size as fire/grass orbs.
            float maxLocalSize = 1.15f;
            float maxSpeed = 0.8f;

            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                string n = ps.name.ToLowerInvariant();
                var renderer = ps.GetComponent<ParticleSystemRenderer>();

                // Giant water-sphere mesh shells / ripples / splash flipbooks dominate size.
                bool oversizedLayer = n.Contains("dome")
                    || n.Contains("distort")
                    || n.Contains("ripple")
                    || n.Contains("splash")
                    || n.Contains("panner")
                    || (renderer != null && renderer.renderMode == ParticleSystemRenderMode.Mesh);

                if (oversizedLayer)
                {
                    var emission = ps.emission;
                    emission.enabled = false;
                    if (renderer != null)
                        renderer.enabled = false;
                    continue;
                }

                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Local;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;

                var startSize = main.startSize;
                if (startSize.mode == ParticleSystemCurveMode.Constant)
                {
                    startSize.constant = Mathf.Min(startSize.constant, maxLocalSize);
                    main.startSize = startSize;
                }
                else if (startSize.mode == ParticleSystemCurveMode.TwoConstants)
                {
                    startSize.constantMin = Mathf.Min(startSize.constantMin, maxLocalSize * 0.55f);
                    startSize.constantMax = Mathf.Min(startSize.constantMax, maxLocalSize);
                    main.startSize = startSize;
                }
                else
                {
                    // Curves / two-curves: force a compact constant so authored ramps cannot explode.
                    main.startSize = maxLocalSize * 0.7f;
                }

                var startSpeed = main.startSpeed;
                if (startSpeed.mode == ParticleSystemCurveMode.Constant)
                {
                    startSpeed.constant = Mathf.Min(startSpeed.constant, maxSpeed);
                    main.startSpeed = startSpeed;
                }
                else if (startSpeed.mode == ParticleSystemCurveMode.TwoConstants)
                {
                    startSpeed.constantMin = Mathf.Min(startSpeed.constantMin, maxSpeed * 0.5f);
                    startSpeed.constantMax = Mathf.Min(startSpeed.constantMax, maxSpeed);
                    main.startSpeed = startSpeed;
                }

                var shape = ps.shape;
                if (shape.enabled)
                    shape.radius = Mathf.Min(shape.radius, 0.35f);

                if (renderer != null)
                {
                    // Critical: default maxParticleSize (~0.5 viewport) undoes tiny hierarchy scales.
                    renderer.maxParticleSize = 0.035f;
                    renderer.minParticleSize = 0f;
                }

                if (!ps.isPlaying)
                    ps.Play(true);
            }
        }

        /// <summary>
        /// Stab/Hit streaks can vanish under tiny hierarchy scales + low maxParticleSize.
        /// Nudge renderer clamps and ensure emitters are actually playing.
        /// </summary>
        static void BoostMeleeVisibility(GameObject fx)
        {
            if (fx == null) return;

            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var emission = ps.emission;
                if (!emission.enabled)
                    continue;

                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = true;
                    // Viewport fraction — high enough to read on a tiny board, not full-screen.
                    renderer.maxParticleSize = Mathf.Max(renderer.maxParticleSize, 0.12f);
                    renderer.minParticleSize = 0f;
                }

                if (!ps.isPlaying)
                    ps.Play(true);
            }
        }

        /// <summary>
        /// Piloto slash prefabs still spray sparks/trails past one board cell even when
        /// root scale is small. Cap start size / speed so the burst stays in-cell.
        /// </summary>
        static void ConstrainMeleeParticlesToCell(GameObject fx, float cellSize)
        {
            if (fx == null || cellSize <= 0.0001f) return;

            float maxReach = cellSize * MeleeParticleReachMul;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;

                // Hierarchy scale already shrinks values; further clamp absolute start speed
                // so fast trails cannot leave the target cell.
                float scale = Mathf.Max(0.001f, fx.transform.lossyScale.x);
                float maxLocalSpeed = maxReach / scale;
                var startSpeed = main.startSpeed;
                if (startSpeed.mode == ParticleSystemCurveMode.Constant)
                {
                    startSpeed.constant = Mathf.Min(startSpeed.constant, maxLocalSpeed);
                    main.startSpeed = startSpeed;
                }
                else if (startSpeed.mode == ParticleSystemCurveMode.TwoConstants)
                {
                    startSpeed.constantMin = Mathf.Min(startSpeed.constantMin, maxLocalSpeed);
                    startSpeed.constantMax = Mathf.Min(startSpeed.constantMax, maxLocalSpeed);
                    main.startSpeed = startSpeed;
                }

                var startSize = main.startSize;
                // Allow a punchier slash silhouette while still limiting cell overflow.
                float maxLocalSize = (cellSize * 1.15f) / scale;
                if (startSize.mode == ParticleSystemCurveMode.Constant)
                {
                    startSize.constant = Mathf.Min(startSize.constant, maxLocalSize);
                    main.startSize = startSize;
                }
                else if (startSize.mode == ParticleSystemCurveMode.TwoConstants)
                {
                    startSize.constantMin = Mathf.Min(startSize.constantMin, maxLocalSize);
                    startSize.constantMax = Mathf.Min(startSize.constantMax, maxLocalSize);
                    main.startSize = startSize;
                }

                // World-space speed cap is applied by PinParticleMotion so VOL Y
                // cannot still push sparks metres into the ceiling.
            }
        }

        static void DisableColliders(GameObject root)
        {
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
                Object.Destroy(c);
        }

        static Material MakeEmissiveMaterial(Color color, float emission = 2f)
        {
            // Prefer URP shaders — Built-in "Standard" turns magenta after URP switch.
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Standard");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            mat.color = color;
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * emission);
            }
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", 0.2f);
            return mat;
        }

        static void SpawnProceduralMeleeSlash(
            ElementType element,
            Vector3 mid,
            Vector3 dir,
            float cellSize,
            Team team)
        {
            Color color = DemoDataFactory.ElementVfxColor(element);
            if (team == Team.Enemy)
                color = Color.Lerp(color, new Color(1f, 0.25f, 0.15f), 0.25f);

            // Compact claw arc — sized for board cell units, not full-body cinematic FX.
            for (int i = 0; i < 3; i++)
            {
                float t = i / 2f;
                float side = Mathf.Lerp(-0.7f, 0.7f, t);
                var slash = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slash.name = "ElementMeleeSlash";
                Object.Destroy(slash.GetComponent<Collider>());

                Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
                slash.transform.position = mid
                    + right * (side * cellSize * 0.22f)
                    + dir * ((t - 0.5f) * cellSize * 0.08f)
                    + Vector3.up * (cellSize * 0.015f);
                slash.transform.rotation = Quaternion.LookRotation(dir, Vector3.up)
                    * Quaternion.Euler(0f, 0f, Mathf.Lerp(-40f, 40f, t));
                slash.transform.localScale = new Vector3(
                    cellSize * 0.055f,
                    cellSize * 0.02f,
                    cellSize * (0.42f + t * 0.12f));

                var renderer = slash.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = MakeEmissiveMaterial(color, 2.2f);
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                var fx = slash.AddComponent<MeleeClawEffect>();
                fx.Duration = 0.18f + i * 0.02f;
                fx.BaseScale = slash.transform.localScale;
            }

            // Core flash disc.
            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "MeleeFlash";
            Object.Destroy(flash.GetComponent<Collider>());
            flash.transform.position = mid;
            flash.transform.localScale = Vector3.one * (cellSize * 0.16f);
            var flashRenderer = flash.GetComponent<Renderer>();
            if (flashRenderer != null)
            {
                flashRenderer.material = MakeEmissiveMaterial(Color.Lerp(color, Color.white, 0.45f), 3f);
                flashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var flashFx = flash.AddComponent<MeleeClawEffect>();
            flashFx.Duration = 0.14f;
            flashFx.BaseScale = flash.transform.localScale;
        }

        static GameObject CreateProceduralProjectile(ElementType element, Vector3 start, float size)
        {
            Color color = DemoDataFactory.ElementVfxColor(element);
            var root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            root.name = "ElementProjectile";
            root.transform.position = start;
            root.transform.localScale = Vector3.one * size;
            Object.Destroy(root.GetComponent<Collider>());

            var renderer = root.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = MakeEmissiveMaterial(color, 2.6f);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Inner core for depth.
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Core";
            Object.Destroy(core.GetComponent<Collider>());
            core.transform.SetParent(root.transform, false);
            core.transform.localScale = Vector3.one * 0.45f;
            var coreRenderer = core.GetComponent<Renderer>();
            if (coreRenderer != null)
            {
                coreRenderer.material = MakeEmissiveMaterial(Color.Lerp(color, Color.white, 0.55f), 3.2f);
                coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var trail = root.AddComponent<TrailRenderer>();
            trail.time = 0.12f;
            trail.minVertexDistance = size * 0.1f;
            trail.startWidth = size * 0.7f;
            trail.endWidth = 0f;
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
            trail.material = new Material(
                Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color"));
            trail.numCapVertices = 4;

            // Element-shaped ring so fire/water/grass/light read differently at a glance.
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "ElementRing";
            Object.Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(root.transform, false);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = new Vector3(1.05f, 0.06f, 1.05f);
            var ringRenderer = ring.GetComponent<Renderer>();
            if (ringRenderer != null)
            {
                Color ringColor = color;
                ringColor.a = 0.85f;
                ringRenderer.material = MakeEmissiveMaterial(ringColor, 1.8f);
                ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            return root;
        }

        static void SpawnProceduralImpact(ElementType element, Vector3 position, float cellSize)
        {
            Color color = DemoDataFactory.ElementVfxColor(element);

            var burst = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            burst.name = "ElementImpact";
            Object.Destroy(burst.GetComponent<Collider>());
            burst.transform.position = position;
            burst.transform.localScale = Vector3.one * (cellSize * 0.14f);
            var renderer = burst.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = MakeEmissiveMaterial(color, 2.8f);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var fx = burst.AddComponent<MeleeClawEffect>();
            fx.Duration = 0.16f;
            fx.BaseScale = burst.transform.localScale;

            for (int i = 0; i < 4; i++)
            {
                float angle = i / 4f * Mathf.PI * 2f;
                var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shard.name = "ImpactShard";
                Object.Destroy(shard.GetComponent<Collider>());
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0.1f, Mathf.Sin(angle)) * (cellSize * 0.12f);
                shard.transform.position = position + offset;
                shard.transform.rotation = Quaternion.LookRotation(offset.normalized, Vector3.up);
                shard.transform.localScale = new Vector3(cellSize * 0.03f, cellSize * 0.03f, cellSize * 0.14f);
                var shardRenderer = shard.GetComponent<Renderer>();
                if (shardRenderer != null)
                {
                    shardRenderer.material = MakeEmissiveMaterial(color, 2f);
                    shardRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                var shardFx = shard.AddComponent<MeleeClawEffect>();
                shardFx.Duration = 0.2f + i * 0.015f;
                shardFx.BaseScale = shard.transform.localScale;
            }
        }
    }
}
