using UnityEngine;

namespace NexusAnima
{
    public class GameBootstrap : MonoBehaviour
    {
        static bool _booted;

        [Header("Scene References")]
        [SerializeField] GameObject arenaRoot;
        [SerializeField] Transform gameRoot;
        [SerializeField] BoardGrid board;
        [SerializeField] BattleManager battle;
        [SerializeField] FateMasterController fateMaster;
        [SerializeField] DemoUI demoUi;
        [SerializeField] FormationInput formationInput;
        [SerializeField] SpiritUnit spiritUnitPrefab;
        [SerializeField] SpiritProjectile projectilePrefab;
        [SerializeField] XinxiangSkillController xinxiang;
        [SerializeField] XinxiangCastDirector xinxiangCast;
        [SerializeField] XinxiangPrankCallDirector xinxiangPrankCall;
        [SerializeField] BattleCameraFocus cameraFocus;

        [Header("Camera")]
        [Tooltip("Off = keep the authored Scene-view pose. On = auto FrameToBoard at boot.")]
        [SerializeField] bool frameCameraOnBoot = false;

        // Over the DJ mixer looking onto the dance floor. Near 0.02 keeps the mixer
        // in frame; 0.05 clips it. Occlusion stays off. FOV 42.
        static readonly Vector3 AuthoredCameraPos = new Vector3(0.123f, 0.775f, -2.118f);
        static readonly Vector3 AuthoredCameraEuler = new Vector3(28f, 0f, 0f);
        const float AuthoredCameraFov = 42f;
        const float AuthoredCameraNear = 0.02f;

        // Required when Enter Play Mode Options disables Domain Reload — statics otherwise stick.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _booted = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (FindAnyObjectByType<GameBootstrap>() == null)
                Debug.LogError("BattleDemo requires a GameBootstrap component saved in the scene.");
        }

        void Awake()
        {
            if (_booted)
            {
                Destroy(gameObject);
                return;
            }
            _booted = true;
            Application.targetFrameRate = 60;
            GameAudio.Ensure();
            BuildWorld();
            // ParallelStage prefers Resources/Audio/Bgm/Stage.wav; otherwise Formation.
            if (ArenaEnvironment.UseParallelStage && !ArenaEnvironment.IsLegacyArchiveScene())
                ParallelStageFx.TryPlayStageBgm();
            else
                GameAudio.I?.PlayFormationBgm();
        }

        void BuildWorld()
        {
            ResolveReferences();

            if (arenaRoot == null || gameRoot == null || board == null || battle == null ||
                fateMaster == null || demoUi == null || formationInput == null)
            {
                Debug.LogError("BattleDemo scene is incomplete. Assign GameBootstrap references in the Inspector.");
                return;
            }

            // Play boot only: bind the scene-saved DynamicGrid, then lock the calibrated
            // Board+DG pair. Do not rebuild-then-stamp — a small authored localScale
            // would shrink freshly painted tiles. CellToWorld reads painted Cell_*.
            bool legacyArchive = ArenaEnvironment.IsLegacyArchiveScene();
            if (legacyArchive)
            {
                if (!board.SyncFromDynamicGrid(rebuildVisual: true))
                    board.BuildVisual(forceRebuild: false);
            }
            else
            {
                if (!board.SyncFromDynamicGrid(rebuildVisual: false))
                    board.BuildVisual(forceRebuild: false);
                // Scene-authored Board / stage / camera are the source of truth.
                // Do not stamp hardcoded concert poses every Play — that made the
                // edit-mode Scene diverge, and would overwrite Play-mode tweaks
                // that PlayLayoutSync writes back on exit.
            }

            // Try PBR Stage Equipment skin (instant revert: ArenaEnvironment.UseParallelStage = false).
            // Arena fit uses the synced Board / DynamicGrid size.
            arenaRoot = ArenaEnvironment.ApplySelectedSkin(arenaRoot, gameRoot);

            if (spiritUnitPrefab != null)
                battle.SetSpiritPrefab(spiritUnitPrefab);
            if (projectilePrefab != null)
                battle.SetProjectilePrefab(projectilePrefab);
            if (!legacyArchive)
                ConfigureBattleLighting();

            var roster = DemoDataFactory.CreatePlayerRoster();
            var enemies = DemoDataFactory.CreateEnemyRoster();
            var masterDef = DemoDataFactory.CreateFateMaster();

            EnsureXinxiangComponents();

            fateMaster.Init(masterDef, board, battle);
            battle.Setup(board, fateMaster, roster, enemies, masterDef, demoUi);
            xinxiang.Init(battle, xinxiangCast, xinxiangPrankCall);
            battle.SetXinxiang(xinxiang);
            formationInput.Init(battle, board);
            // Build UI first — it clears Popup/Controls layers. Video overlay must be created after.
            demoUi.Build(battle);
            xinxiangCast.EnsureBuilt(cameraFocus);
            xinxiangPrankCall?.EnsureBuilt();
            // Spawn both sides from the shared pool placements (default enemy lineup included).
            battle.SpawnFormationPreviews();

            // Always keep the scene-authored Game pose (edit = Play).
            // Inspector may still have frameCameraOnBoot checked from an old
            // session — never re-frame.
            frameCameraOnBoot = false;
            KeepSceneAuthoredCamera();
            GridPlayAdjust.Ensure(board);
            PropPlayAdjust.Ensure();
        }

        /// <summary>
        /// One-shot: write the calibrated concert layout into the open BattleDemo
        /// so edit-mode Scene matches Play. Do not call from Play boot.
        /// </summary>
        public static void BakeAuthoredConcertLayoutIntoOpenScene()
        {
            if (ArenaEnvironment.IsLegacyArchiveScene())
                return;
            var board = Object.FindFirstObjectByType<BoardGrid>();
            RestoreAuthoredBoardAndGrid(board);
            ApplyAuthoredCameraPose();
            StampConcertSun();
        }

        static void RestoreAuthoredBoardAndGrid(BoardGrid board)
        {
            if (board == null) return;
            // 0.723573 sits the painted tiles ~3mm above DemoRoom floor (0.741).
            // 0.7163 buried the checkerboard under the slab.
            board.transform.localPosition = new Vector3(0.12f, 0.723573f, -2.053265f);
            board.transform.localRotation = Quaternion.identity;
            board.transform.localScale = new Vector3(1f, 1f, 0.9502602f);
            board.CellSize = 0.02878833f;
            board.ApplyAuthoredDynamicGridPose();
            var dgLock = board.transform.Find("DynamicGrid");
            if (dgLock == null) return;
            dgLock.localPosition = new Vector3(0f, 0.0109f, 0.0065f);
            dgLock.localScale = new Vector3(-0.062f, 0.15147f, 0.0415f);
            dgLock.gameObject.tag = "Untagged";
            dgLock.gameObject.layer = 0;
            dgLock.gameObject.isStatic = false;
        }

        void EnsureXinxiangComponents()
        {
            if (gameRoot == null) return;

            if (cameraFocus == null)
            {
                var camGo = gameRoot.Find("BattleCameraFocus");
                if (camGo == null)
                {
                    var created = new GameObject("BattleCameraFocus");
                    created.transform.SetParent(gameRoot, false);
                    cameraFocus = created.AddComponent<BattleCameraFocus>();
                }
                else
                {
                    cameraFocus = camGo.GetComponent<BattleCameraFocus>()
                                  ?? camGo.gameObject.AddComponent<BattleCameraFocus>();
                }
            }

            if (xinxiangCast == null)
            {
                var castGo = gameRoot.Find("XinxiangCastDirector");
                if (castGo == null)
                {
                    var created = new GameObject("XinxiangCastDirector");
                    created.transform.SetParent(gameRoot, false);
                    xinxiangCast = created.AddComponent<XinxiangCastDirector>();
                }
                else
                {
                    xinxiangCast = castGo.GetComponent<XinxiangCastDirector>()
                                   ?? castGo.gameObject.AddComponent<XinxiangCastDirector>();
                }
            }

            if (xinxiang == null)
            {
                var skillGo = gameRoot.Find("XinxiangSkills");
                if (skillGo == null)
                {
                    var created = new GameObject("XinxiangSkills");
                    created.transform.SetParent(gameRoot, false);
                    xinxiang = created.AddComponent<XinxiangSkillController>();
                }
                else
                {
                    xinxiang = skillGo.GetComponent<XinxiangSkillController>()
                               ?? skillGo.gameObject.AddComponent<XinxiangSkillController>();
                }
            }

            if (xinxiangPrankCall == null)
            {
                var prankGo = gameRoot.Find("XinxiangPrankCall");
                if (prankGo == null)
                {
                    var created = new GameObject("XinxiangPrankCall");
                    created.transform.SetParent(gameRoot, false);
                    xinxiangPrankCall = created.AddComponent<XinxiangPrankCallDirector>();
                }
                else
                {
                    xinxiangPrankCall = prankGo.GetComponent<XinxiangPrankCallDirector>()
                                        ?? prankGo.gameObject.AddComponent<XinxiangPrankCallDirector>();
                }
            }
        }

        void ResolveReferences()
        {
            if (arenaRoot == null)
                arenaRoot = GameObject.Find("Ancient Chinese Arena");

            if (gameRoot == null)
            {
                var found = GameObject.Find("GameRoot");
                if (found != null) gameRoot = found.transform;
            }

            if (gameRoot == null) return;

            if (board == null)
                board = gameRoot.Find("Board")?.GetComponent<BoardGrid>();
            if (battle == null)
                battle = gameRoot.Find("BattleManager")?.GetComponent<BattleManager>();
            if (fateMaster == null)
                fateMaster = gameRoot.Find("FateMaster")?.GetComponent<FateMasterController>();
            if (demoUi == null)
                demoUi = gameRoot.Find("DemoUI")?.GetComponent<DemoUI>();
            if (formationInput == null && battle != null)
                formationInput = battle.GetComponent<FormationInput>();
        }

        static void ApplyAuthoredCameraPose()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.transform.SetPositionAndRotation(
                AuthoredCameraPos,
                Quaternion.Euler(AuthoredCameraEuler));
            cam.fieldOfView = AuthoredCameraFov;
            cam.nearClipPlane = AuthoredCameraNear;
            cam.useOcclusionCulling = false;
            BattleFlyCamera.EnsureOnMainCamera();

            var focus = Object.FindFirstObjectByType<BattleCameraFocus>();
            if (focus != null)
                focus.CaptureHome();
        }

        /// <summary>
        /// BattleDemo_AncientChineseArena keeps its own Main Camera. Do not copy
        /// the concert-stage pose (z≈-2.1) onto it.
        /// </summary>
        static void KeepSceneAuthoredCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.useOcclusionCulling = false;
            BattleFlyCamera.EnsureOnMainCamera();

            var focus = Object.FindFirstObjectByType<BattleCameraFocus>();
            if (focus != null)
                focus.CaptureHome();
        }

        /// <summary>
        /// Battle board is tiny (~10m). Default sun angle + full-strength soft shadows
        /// cast oversized, muddy character silhouettes. Raise the sun and fade shadows.
        /// </summary>
        static void ConfigureBattleLighting()
        {
            // Prefer tighter shadow maps on a small arena.
            if (QualitySettings.shadowDistance > 18f)
                QualitySettings.shadowDistance = 18f;

            bool concertStage = GameObject.Find(ArenaEnvironment.ParallelArenaName) != null;
            // Concert sun lives on the scene (edit = Play). Stamping it every boot
            // would fight PlayLayoutSync when the user tweaks intensity in Play.
            if (concertStage)
                return;

            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                if (light == null || light.type != LightType.Directional)
                    continue;

                light.transform.rotation = Quaternion.Euler(72f, 35f, 0f);
                light.intensity = Mathf.Clamp(light.intensity, 1.1f, 1.45f);
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.42f;
                light.shadowBias = 0.04f;
                light.shadowNormalBias = 0.65f;
                light.shadowNearPlane = 0.2f;
            }
        }

        static void StampConcertSun()
        {
            if (QualitySettings.shadowDistance > 18f)
                QualitySettings.shadowDistance = 18f;

            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                if (light == null || light.type != LightType.Directional)
                    continue;
                light.transform.rotation = Quaternion.Euler(72f, 35f, 0f);
                light.intensity = 0.95f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.32f;
                light.shadowBias = 0.04f;
                light.shadowNormalBias = 0.65f;
                light.shadowNearPlane = 0.2f;
            }
        }
    }
}
