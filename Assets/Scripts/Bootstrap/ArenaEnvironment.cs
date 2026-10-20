using UnityEngine;

namespace NexusAnima
{
    public static class ArenaEnvironment
    {
        /// <summary>
        /// Default for BattleDemo. True = PBR Stage Equipment arena.
        /// The archived scene BattleDemo_AncientChineseArena always uses the old arena,
        /// regardless of this flag — so you can freely retune spirit positions on the new map.
        /// </summary>
        public static bool UseParallelStage = true;

        /// <summary>Standalone .unity archive of the original Ancient Chinese Arena layout.</summary>
        public const string LegacyArchiveSceneName = "BattleDemo_AncientChineseArena";

        public const string LegacyArenaName = "Ancient Chinese Arena";
        public const string ParallelArenaName = "PBRStageEquipment";
        static readonly Vector3 AuthoredConcertStagePos = new Vector3(0.1449653f, 0.7362906f, -2.073401f);
        const float AuthoredConcertStageScale = 0.006272694f;
        const string ParallelResource = "Arena/PBRStageEquipment";
        /// <summary>Permanent backup of the original Ancient Chinese Arena (created when ParallelStage became default).</summary>
        const string LegacyBackupResource = "Arena/LegacyAncientChineseArena";
        // Older Resources path (may be missing in this project).
        const string ArenaResource = "arena/source/arena";
        static readonly Color Grass = new Color(0.18f, 0.45f, 0.31f);
        static readonly Color Cliff = new Color(0.18f, 0.25f, 0.34f);
        static readonly Color Stone = new Color(0.35f, 0.43f, 0.5f);
        static readonly Color Gold = new Color(0.94f, 0.68f, 0.26f);
        static readonly Color CyanCrystal = new Color(0.22f, 0.86f, 0.9f);
        static readonly Color CoralCrystal = new Color(1f, 0.45f, 0.35f);

        public static void RevertToLegacy() => UseParallelStage = false;
        public static void EnableParallelStage() => UseParallelStage = true;

        /// <summary>True when the open scene is the frozen original-map archive.</summary>
        public static bool IsLegacyArchiveScene()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            return scene.IsValid() && scene.name == LegacyArchiveSceneName;
        }

        static bool ShouldUseParallelStage() => UseParallelStage && !IsLegacyArchiveScene();

        /// <summary>
        /// Swap the visible arena skin at boot. Keeps the legacy object in-scene (disabled)
        /// so flipping UseParallelStage + re-entering Play restores the original on BattleDemo.
        /// Opening BattleDemo_AncientChineseArena always forces the old arena.
        /// </summary>
        public static GameObject ApplySelectedSkin(GameObject currentArenaRoot, Transform parent = null)
        {
            var leftover = FindNamedIncludingInactive("ParallelStage");
            if (leftover != null && leftover.name != ParallelArenaName)
                leftover.SetActive(false);

            var legacy = FindLegacyArena(currentArenaRoot);
            var parallel = FindNamedIncludingInactive(ParallelArenaName);

            if (ShouldUseParallelStage())
            {
                if (legacy != null)
                    legacy.SetActive(false);

                bool spawned = false;
                if (parallel == null)
                {
                    parallel = SpawnParallelStage(parent != null
                        ? parent
                        : legacy != null ? legacy.transform.parent : null);
                    spawned = parallel != null;
                }

                if (parallel != null)
                {
                    parallel.name = ParallelArenaName;
                    parallel.SetActive(true);
                    // Fresh spawn still needs the calibrated concert pose.
                    // An already-placed scene instance keeps its transform so
                    // edit-mode Scene matches Play, and PlayLayoutSync can persist tweaks.
                    ConfigureArena(parallel, keepName: ParallelArenaName, applyAuthoredPose: spawned);
                    return parallel;
                }

                Debug.LogWarning("[Arena] ParallelStage missing — falling back to legacy arena.");
                if (legacy != null)
                {
                    legacy.SetActive(true);
                    return legacy;
                }
                return currentArenaRoot;
            }

            if (parallel != null)
                parallel.SetActive(false);
            if (legacy != null)
            {
                legacy.SetActive(true);
                return legacy;
            }

            // Scene copy missing — restore from permanent Resources backup.
            legacy = SpawnLegacyBackup(parent != null
                ? parent
                : parallel != null ? parallel.transform.parent : null);
            if (legacy != null)
                return legacy;
            return currentArenaRoot;
        }

        static GameObject FindLegacyArena(GameObject current)
        {
            if (current != null && current.name.StartsWith("Ancient Chinese Arena"))
                return current;
            var found = FindNamedIncludingInactive(LegacyArenaName);
            if (found != null) return found;
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;
                if (!t.gameObject.scene.IsValid()) continue;
                if (t.name.StartsWith("Ancient Chinese Arena"))
                    return t.gameObject;
            }
            return null;
        }

        static GameObject SpawnLegacyBackup(Transform parent)
        {
            var prefab = Resources.Load<GameObject>(LegacyBackupResource);
            if (prefab == null)
            {
                Debug.LogWarning("[Arena] Legacy backup Resources/" + LegacyBackupResource + " not found.");
                return null;
            }

            var arena = Object.Instantiate(prefab);
            arena.name = LegacyArenaName;
            if (parent != null)
                arena.transform.SetParent(parent, false);
            arena.SetActive(true);
            Debug.Log("[Arena] Restored Ancient Chinese Arena from permanent backup.");
            return arena;
        }

        static GameObject FindNamedIncludingInactive(string name)
        {
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;
                if (!t.gameObject.scene.IsValid()) continue;
                if (t.name == name)
                    return t.gameObject;
            }
            return null;
        }

        static GameObject SpawnParallelStage(Transform parent)
        {
            var prefab = Resources.Load<GameObject>(ParallelResource);
            if (prefab == null)
            {
                Debug.LogWarning("[Arena] Resources/" + ParallelResource + " not found.");
                return null;
            }

            var arena = Object.Instantiate(prefab);
            arena.name = ParallelArenaName;
            if (parent != null)
                arena.transform.SetParent(parent, false);
            return arena;
        }

        public static void Create(Transform parent)
        {
            var sceneArena = GameObject.Find("arena")
                             ?? GameObject.Find("Arena")
                             ?? GameObject.Find(ParallelArenaName);
            if (sceneArena != null)
            {
                ConfigureArena(sceneArena);
                return;
            }

            if (TryCreateArena(parent)) return;

            CreateFallbackArena(parent);
        }

        static bool TryCreateArena(Transform parent)
        {
            var prefab = Resources.Load<GameObject>(ArenaResource);
            if (prefab == null) return false;

            var arena = Object.Instantiate(prefab, parent);
            arena.name = "Arena";
            ConfigureArena(arena);
            return true;
        }

        /// <summary>
        /// One-shot: make the open BattleDemo stage look like Play (pose, hidden
        /// LED/beams, darkened floor, KEEP lights). Uses instance materials.
        /// Do not call on Ancient Chinese Arena.
        /// </summary>
        public static void BakeEditModeToPlayLayout(GameObject arena)
        {
            if (arena == null) return;
            if (IsLegacyArchiveScene()) return;
            arena.name = ParallelArenaName;
            arena.SetActive(true);
            SilenceOversizedStageVfx(arena);
            DarkenBrightStageSurfaces(arena);
            ApplyAuthoredConcertStagePose(arena);
            ScaleStageLightsToFit(arena, AuthoredConcertStageScale);
            LiftCircleTruss(arena);
        }

        static void ConfigureArena(GameObject arena, string keepName = null, bool applyAuthoredPose = true)
        {
            if (!string.IsNullOrEmpty(keepName))
                arena.name = keepName;
            else if (arena.name != ParallelArenaName &&
                     arena.name != "ParallelStage" &&
                     !arena.name.StartsWith("Ancient Chinese Arena"))
                arena.name = "Arena";
            arena.SetActive(true);
            RemoveColliders(arena);
            if (arena.name == ParallelArenaName)
            {
                SilenceOversizedStageVfx(arena);
                ToneDownStageEmission(arena);
                DarkenBrightStageSurfaces(arena);
                if (applyAuthoredPose)
                {
                    ApplyAuthoredConcertStagePose(arena);
                    LiftCircleTruss(arena);
                }
                float fit = Mathf.Abs(arena.transform.localScale.x);
                if (fit < 0.0001f)
                    fit = AuthoredConcertStageScale;
                ScaleStageLightsToFit(arena, applyAuthoredPose ? AuthoredConcertStageScale : fit);
                return;
            }

            arena.transform.localPosition = Vector3.zero;
            arena.transform.localRotation = Quaternion.identity;
            arena.transform.localScale = Vector3.one;
            FitArenaToBoard(arena);
        }

        static void ApplyAuthoredConcertStagePose(GameObject arena)
        {
            if (arena == null) return;
            arena.transform.position = AuthoredConcertStagePos;
            arena.transform.rotation = Quaternion.identity;
            arena.transform.localScale = new Vector3(
                AuthoredConcertStageScale,
                AuthoredConcertStageScale,
                AuthoredConcertStageScale);
        }

        /// <summary>
        /// Kit authored Truss01_Circle5m at local Y 4.1, which sits ~8cm above the
        /// board after fit. Nudge it up in original-kit units (lossyScale ~0.02).
        /// </summary>
        const float CircleTrussLocalY = 6.1f;

        static void LiftCircleTruss(GameObject arena)
        {
            var transforms = arena.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null || t.name != "Truss01_Circle5m") continue;
                var lp = t.localPosition;
                lp.y = CircleTrussLocalY;
                t.localPosition = lp;
                return;
            }
        }

        /// <summary>
        /// Scale + center the arena so it matches the current Board / DynamicGrid footprint.
        /// Light.range is world-space (not scaled by Transform), so concert-kit lights
        /// must be retargeted after the mesh shrinks — otherwise Play floods the camera white.
        /// </summary>
        public static void FitArenaToBoard(GameObject arena)
        {
            if (arena == null) return;

            if (!TryCollectFitBounds(arena, out Bounds bounds))
                return;

            float largestHorizontalAxis = Mathf.Max(bounds.size.x, bounds.size.z);
            if (largestHorizontalAxis < 0.001f) return;

            // Prefer DynamicGrid / Board world size; fall back to legacy 9.6 if board missing.
            Vector3 targetCenter = Vector3.zero;
            float floorY = 0f;
            float scale = 9.6f / largestHorizontalAxis;
            var board = Object.FindFirstObjectByType<BoardGrid>();
            if (board != null)
            {
                Bounds boardBounds = board.GetWorldBounds();
                // Uniform scale must cover BOTH board axes (stage mesh is often non-square).
                const float apron = 1.35f;
                float needX = Mathf.Max(0.05f, boardBounds.size.x * apron);
                float needZ = Mathf.Max(0.05f, boardBounds.size.z * apron);
                float scaleX = needX / Mathf.Max(0.001f, bounds.size.x);
                float scaleZ = needZ / Mathf.Max(0.001f, bounds.size.z);
                scale = Mathf.Max(scaleX, scaleZ);
                targetCenter = new Vector3(boardBounds.center.x, 0f, boardBounds.center.z);
                floorY = board.GridSurfaceY;
            }

            arena.transform.localScale *= scale;
            ScaleStageLightsToFit(arena, scale);

            if (!TryCollectFitBounds(arena, out bounds))
                return;
            arena.transform.position += new Vector3(
                targetCenter.x - bounds.center.x,
                floorY - bounds.min.y,
                targetCenter.z - bounds.center.z);
        }

        static bool TryCollectFitBounds(GameObject arena, out Bounds bounds)
        {
            bounds = default;
            var renderers = arena.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!IncludeInFitBounds(renderers[i])) continue;
                if (!any)
                {
                    bounds = renderers[i].bounds;
                    any = true;
                }
                else bounds.Encapsulate(renderers[i].bounds);
            }
            return any;
        }

        static bool IncludeInFitBounds(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled) return false;
            if (renderer is ParticleSystemRenderer) return false;
            if (renderer.GetComponent<ParticleSystem>() != null) return false;
            var n = renderer.gameObject.name;
            return n != "Cone" && n != "Lines" && n != "FireBall"
                && n != "Flames" && n != "SmokeDark" && n != "Smoke"
                && n != "Smoke01" && n != "LaserBeams01";
        }

        /// <summary>
        /// Unity Light.range is world units and ignores Transform scale. PBR Stage Equipment
        /// authored ranges (10–50) at a ~0.1 fit would put the battle camera inside every lamp.
        /// </summary>
        static void ScaleStageLightsToFit(GameObject arena, float fitScale)
        {
            // Snapshot authored Light values first, then KEEP 04+03 at dark.
            var lights = arena.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                if (light == null || light.type == LightType.Directional) continue;
                light.cookie = null;
                light.shadows = LightShadows.None;
            }

            var animators = arena.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                var anim = animators[i];
                if (anim == null) continue;
                string n = anim.gameObject.name;
                // Keep disco fixtures spinning; MirrorBall must stay still so
                // Game-view posing can move the sphere without fighting the clip.
                anim.enabled = n.StartsWith("DiscoLight04")
                    || n.StartsWith("DiscoLight03")
                    || n.StartsWith("DiscoLight05");
                if (n.StartsWith("MirrorBall"))
                {
                    anim.enabled = false;
                    anim.speed = 0f;
                }
            }

            var guard = arena.GetComponent<ConcertLightGuard>();
            if (guard == null)
                guard = arena.AddComponent<ConcertLightGuard>();
            guard.ApplyKeepMix();
            guard.CaptureTargets(fitScale);
            ConcertLightPreviewHud.Hide();
        }

        static string StageFixtureKind(Light light)
        {
            Transform t = light.transform;
            int guard = 0;
            while (t != null && guard < 12)
            {
                string n = t.name;
                if (n.StartsWith("DiscoLight04")) return "disco04";
                if (n.StartsWith("DiscoLight03")) return "disco03";
                if (n.StartsWith("DiscoLight05")) return "disco05";
                if (n.StartsWith("MirrorBall")) return "mirror";
                if (n.StartsWith("FlameMachine")) return "flame";
                t = t.parent;
                guard++;
            }
            return "other";
        }

        static bool IsPreferredDisco03(Light light)
        {
            string n = light.gameObject.name;
            return n == "PointLight1" || n == "PointLight3";
        }

        static void ToneDownStageEmission(GameObject arena)
        {
            var renderers = arena.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var mats = renderers[i].sharedMaterials;
                if (mats == null) continue;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null || !mat.HasProperty("_EmissionColor")) continue;
                    Color e = mat.GetColor("_EmissionColor");
                    float max = Mathf.Max(e.r, e.g, e.b);
                    if (max <= 0.05f) continue;
                    bool nearWhite = e.r > 0.7f && e.g > 0.7f && e.b > 0.7f;
                    if (!nearWhite && max <= 1.05f) continue;
                    float target = nearWhite ? 0.08f : 0.35f;
                    mat.SetColor("_EmissionColor", e * (target / max));
                }
            }
        }

        /// <summary>
        /// Concert kit ships an HDR LED dance floor plus a DemoRoom shell. Hide only
        /// the LED floor. Keep DemoRoom as the stage floor — the too-light white cone
        /// is LightBeam*, not this mesh. Instance-darken room/panels so Play reads as
        /// a dark venue, not a blown-out studio.
        /// </summary>
        static void DarkenBrightStageSurfaces(GameObject arena)
        {
            var transforms = arena.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var go = transforms[i].gameObject;
                if (go.name.IndexOf("LedDanceFloor", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    go.SetActive(false);
            }

            var renderers = arena.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                int kind = ClassifyBrightStageSurface(renderer);
                if (kind == 0) continue;

                var mats = renderer.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;
                    if (mat.HasProperty("_EmissionColor"))
                        mat.SetColor("_EmissionColor", Color.black);
                    if (mat.HasProperty("_EmissionMap"))
                        mat.SetTexture("_EmissionMap", null);
                    mat.DisableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

                    Color target = kind == 1
                        ? new Color(0.07f, 0.06f, 0.09f, 1f)
                        : new Color(0.12f, 0.12f, 0.16f, 1f);
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", target);
                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", target);
                    if (mat.HasProperty("_Smoothness"))
                        mat.SetFloat("_Smoothness", kind == 1 ? 0.12f : 0.25f);
                }
            }
        }

        // 0 = skip, 1 = DemoRoom shell, 2 = LED panels / leftover dance floor
        static int ClassifyBrightStageSurface(Renderer renderer)
        {
            if (NameContains(renderer.gameObject.name, "DemoRoom")) return 1;
            if (NameContains(renderer.gameObject.name, "LedDanceFloor")) return 2;
            if (NameContains(renderer.gameObject.name, "PanelLed")) return 2;
            var mats = renderer.sharedMaterials;
            if (mats == null) return 0;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (NameContains(mats[i].name, "DemoRoom")) return 1;
                if (NameContains(mats[i].name, "LedDanceFloor")) return 2;
                if (NameContains(mats[i].name, "PanelLed")) return 2;
            }
            return 0;
        }

        static bool NameContains(string name, string token)
        {
            return !string.IsNullOrEmpty(name)
                && name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void SilenceOversizedStageVfx(GameObject arena)
        {
            var particles = arena.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                var ps = particles[i];
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.gameObject.SetActive(false);
            }

            var transforms = arena.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var n = transforms[i].gameObject.name;
                if (n == "Cone" || n == "Lines" || n == "LaserBeams01"
                    || n == "FireBall" || n == "Flames" || n == "SmokeDark"
                    || n == "LightBeam01" || n.StartsWith("LightBeam"))
                    transforms[i].gameObject.SetActive(false);
            }

            var lines = arena.GetComponentsInChildren<LineRenderer>(true);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] == null) continue;
                lines[i].enabled = false;
                lines[i].gameObject.SetActive(false);
            }
        }

        static void RemoveColliders(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                Object.Destroy(collider);
        }

        static void CreateFallbackArena(Transform parent)
        {
            var root = new GameObject("FantasyArena");
            root.transform.SetParent(parent, false);

            var cliff = CreatePrimitive("FloatingCliff", PrimitiveType.Cylinder, root.transform, Cliff);
            cliff.transform.position = new Vector3(0f, -0.9f, 0f);
            cliff.transform.localScale = new Vector3(7.2f, 0.55f, 7.2f);

            var rim = CreatePrimitive("MossyPlatform", PrimitiveType.Cylinder, root.transform, Grass);
            rim.transform.position = new Vector3(0f, -0.34f, 0f);
            rim.transform.localScale = new Vector3(6.65f, 0.18f, 6.65f);

            var battlefield = CreatePrimitive("BattlefieldFoundation", PrimitiveType.Cube, root.transform, new Color(0.14f, 0.2f, 0.28f));
            battlefield.transform.position = new Vector3(0f, -0.13f, 0f);
            battlefield.transform.localScale = new Vector3(9.5f, 0.22f, 9.5f);

            CreateRunicCorners(root.transform);
            CreateCrystalClusters(root.transform);
            CreateFloatingRocks(root.transform);
        }

        static void CreateRunicCorners(Transform parent)
        {
            Vector3[] positions =
            {
                new Vector3(-4.75f, 0f, -4.75f),
                new Vector3(-4.75f, 0f, 4.75f),
                new Vector3(4.75f, 0f, -4.75f),
                new Vector3(4.75f, 0f, 4.75f)
            };

            for (int i = 0; i < positions.Length; i++)
            {
                var baseStone = CreatePrimitive("RunicStone", PrimitiveType.Cylinder, parent, Stone);
                baseStone.transform.position = positions[i] + Vector3.up * 0.18f;
                baseStone.transform.localScale = new Vector3(0.42f, 0.18f, 0.42f);

                var rune = CreatePrimitive("RuneCore", PrimitiveType.Sphere, baseStone.transform, i < 2 ? CyanCrystal : CoralCrystal);
                rune.transform.localPosition = Vector3.up * 1.1f;
                rune.transform.localScale = Vector3.one * 0.55f;
                SetEmission(rune.GetComponent<Renderer>(), i < 2 ? CyanCrystal : CoralCrystal);

                var spire = CreatePrimitive("RuneSpire", PrimitiveType.Cylinder, baseStone.transform, Gold);
                spire.transform.localPosition = Vector3.up * 0.72f;
                spire.transform.localScale = new Vector3(0.12f, 0.4f, 0.12f);
            }
        }

        static void CreateCrystalClusters(Transform parent)
        {
            CreateCrystalCluster(parent, new Vector3(-5.1f, 0f, -1.8f), CyanCrystal, 0.35f);
            CreateCrystalCluster(parent, new Vector3(-5.05f, 0f, 2.15f), CyanCrystal, -0.6f);
            CreateCrystalCluster(parent, new Vector3(5.05f, 0f, -2.15f), CoralCrystal, -0.45f);
            CreateCrystalCluster(parent, new Vector3(5.1f, 0f, 1.8f), CoralCrystal, 0.4f);
        }

        static void CreateCrystalCluster(Transform parent, Vector3 position, Color color, float rotation)
        {
            var cluster = new GameObject("ElementCrystalCluster");
            cluster.transform.SetParent(parent, false);
            cluster.transform.position = position;
            cluster.transform.rotation = Quaternion.Euler(0f, rotation * Mathf.Rad2Deg, 0f);

            for (int i = 0; i < 3; i++)
            {
                float angle = i * 2.1f;
                var crystal = CreatePrimitive("Crystal", PrimitiveType.Cylinder, cluster.transform, color);
                crystal.transform.localPosition = new Vector3(Mathf.Cos(angle) * 0.22f, 0.32f + i * 0.08f, Mathf.Sin(angle) * 0.22f);
                crystal.transform.localScale = new Vector3(0.16f, 0.42f + i * 0.08f, 0.16f);
                crystal.transform.localRotation = Quaternion.Euler(12f, i * 47f, 8f);
                SetEmission(crystal.GetComponent<Renderer>(), color);
            }
        }

        static void CreateFloatingRocks(Transform parent)
        {
            Vector3[] positions =
            {
                new Vector3(-5.55f, -1.25f, -3.3f), new Vector3(-4.7f, -1.65f, 5.2f),
                new Vector3(5.35f, -1.35f, 3.65f), new Vector3(4.8f, -1.7f, -5.1f)
            };

            for (int i = 0; i < positions.Length; i++)
            {
                var rock = CreatePrimitive("FloatingRock", PrimitiveType.Sphere, parent, Cliff);
                rock.transform.position = positions[i];
                rock.transform.localScale = new Vector3(1.1f, 0.42f, 0.9f);
                rock.transform.rotation = Quaternion.Euler(14f * i, 24f * i, 18f * i);
            }
        }

        static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            Object.Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateMaterial(color);
            return go;
        }

        static Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                ?? Shader.Find("Standard");
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            material.color = color;
            material.enableInstancing = true;
            return material;
        }

        static void SetEmission(Renderer renderer, Color color)
        {
            var material = renderer.sharedMaterial;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 0.55f);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : color);
        }
    }
}
